"""Offline update evidence and reversible diagnostics; Python stdlib only."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = json.loads(Path(__file__).with_name('update-triage.json').read_text())
METADATA = Path('GunmanContracts_Data/il2cpp_data/Metadata/global-metadata.dat')
SECTION = re.compile(r'^\s*\[([^\]]+)\]\s*(?:#.*)?$')
ENTRY = re.compile(r'^(\s*([\w]+)\s*=\s*)(.*?)(\s*(?:#.*)?)(\r?\n)?$')


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def write_preferences(path, raw):
    temporary = path.with_name(f'{path.name}.triage-{time.time_ns()}.tmp')
    try:
        with temporary.open('xb') as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def resolve_game(explicit=None):
    if explicit:
        return Path(explicit).resolve()
    if os.environ.get('GUNMAN_CONTRACTS_DIR'):
        return Path(os.environ['GUNMAN_CONTRACTS_DIR']).resolve()
    paths = set()
    for prop in ROOT.glob('*/GameDir.local.props'):
        value = ET.parse(prop).findtext('.//GameDir')
        if value:
            paths.add(str(Path(value.strip()).resolve()))
    if len(paths) > 1:
        raise ValueError('Local projects point at different games; specify --game-dir.')
    if paths:
        return Path(paths.pop())
    local = ROOT / 'il2cpp_tools/game_dir.txt'
    if local.exists():
        return Path(local.read_text(encoding='utf-8').strip()).resolve()
    raise ValueError('Game path unavailable. Supply --game-dir or GameDir.local.props.')


def require_closed():
    if os.name != 'nt':
        raise ValueError('Live game operations require Windows process checks.')
    result = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq GunmanContracts.exe', '/FO', 'CSV', '/NH'],
                            capture_output=True, text=True, check=True)
    if 'gunmancontracts.exe' in result.stdout.lower():
        raise ValueError('Close GunmanContracts first; MelonLoader saves preferences on exit.')


class Preferences:
    """Edit only known scalar entries; preserve comments, encoding and other settings."""
    def __init__(self, raw):
        self.encoding = ('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else
                         'utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8')
        self.text = raw.decode(self.encoding)
        self.newline = '\r\n' if '\r\n' in self.text else '\n'
        self.lines = self.text.splitlines(keepends=True)
        self.index()

    def index(self):
        self.values, self.locations, self.sections = {}, {}, {}
        section = None
        for i, line in enumerate(self.lines):
            header = SECTION.match(line.strip())
            if header:
                section = header[1]
                if section in self.sections:
                    raise ValueError(f'Duplicate preference category [{section}].')
                self.sections[section] = i
            else:
                entry = ENTRY.match(line)
                if entry and section:
                    key = (section, entry[2])
                    if key in self.values:
                        raise ValueError(f'Duplicate preference {key}.')
                    self.values[key] = entry[3].strip()
                    self.locations[key] = i

    def set(self, section, key, value):
        identity = (section, key)
        if identity in self.locations:
            i = self.locations[identity]
            if value is None:
                del self.lines[i]
            else:
                m = ENTRY.match(self.lines[i])
                self.lines[i] = m[1] + value + m[4] + (m[5] or '')
        elif value is not None:
            if section not in self.sections:
                if self.lines and not self.lines[-1].endswith('\n'):
                    self.lines[-1] += self.newline
                self.lines.extend([self.newline, f'[{section}]{self.newline}', f'{key} = {value}{self.newline}'])
            else:
                i = self.sections[section] + 1
                while i < len(self.lines) and not SECTION.match(self.lines[i].strip()):
                    i += 1
                if i and not self.lines[i - 1].endswith('\n'):
                    self.lines[i - 1] += self.newline
                self.lines.insert(i, f'{key} = {value}{self.newline}')
        self.index()

    def encode(self):
        return ''.join(self.lines).encode(self.encoding)


def source_inventory():
    rows = {}
    for project in sorted(ROOT.glob('*/*.csproj')):
        tree = ET.parse(project)
        assembly = tree.findtext('.//AssemblyName') or project.stem
        sources = [p for p in project.parent.rglob('*.cs') if not set(p.relative_to(project.parent).parts) & {'bin', 'obj', 'Tests', 'Example'}]
        code = '\n'.join(p.read_text(encoding='utf-8-sig') for p in sources)
        targets = sorted(set(re.findall(r'HarmonyPatch\(typeof\(([^)]+)\),\s*nameof\(([^)]+)\)', code)))
        hard = []
        for deps in re.findall(r'MelonAdditionalDependencies\(([^)]+)\)', code):
            hard.extend(re.findall(r'"([^"]+)"', deps))
        rows[assembly] = {
            'project': str(project.relative_to(ROOT)),
            'hard_dependencies': sorted(set(hard)),
            'project_references': [e.attrib['Include'] for e in tree.findall('.//ProjectReference')],
            'patch_targets': [{'type': t, 'member': m.rsplit('.', 1)[-1]} for t, m in targets],
            'dynamic_patch_files': [str(p.relative_to(ROOT)) for p in sources if re.search(r'TargetMethods?\s*\(', p.read_text(encoding='utf-8-sig'))],
            'source_sha256': {str(p.relative_to(ROOT)): sha(p) for p in sources + [project]},
        }
    return rows


def fingerprints(game):
    files = [game / name for name in ['GunmanContracts.exe', 'GameAssembly.dll', 'UnityPlayer.dll',
             str(METADATA), 'GunmanContracts_Data/globalgamemanagers', 'GunmanContracts_Data/boot.config']]
    for directory in ['Mods', 'Plugins', 'UserLibs', 'MelonLoader/Il2CppAssemblies', 'MelonLoader/net6']:
        files.extend(sorted((game / directory).glob('*.dll')))
    return {str(p.relative_to(game)): {'sha256': sha(p), 'size': p.stat().st_size}
            for p in files if p.is_file()}


def inventory(game):
    sources = source_inventory()
    installed = sorted(p.stem for p in (game / 'Mods').glob('*.dll'))
    cfg = game / 'UserData/MelonPreferences.cfg'
    prefs = Preferences(cfg.read_bytes()) if cfg.exists() else Preferences(b'')
    metadata_version = None
    if (game / METADATA).exists():
        with open(game / METADATA, 'rb') as stream:
            metadata_version = int.from_bytes(stream.read(8)[4:], 'little')
    return {'schema': 1, 'utc': datetime.now(timezone.utc).isoformat(), 'game_dir': str(game),
            'metadata_version': metadata_version, 'files': fingerprints(game), 'sources': sources,
            'installed': installed, 'third_party': sorted(set(installed) - set(sources)),
            'preferences': {f'{s}.{k}': v for (s, k), v in prefs.values.items()},
            'manifest_gaps': sorted(set(sources) - set(MANIFEST)),
            'missing_dependencies': {name: [d for d in sources[name]['hard_dependencies'] if d not in installed]
                                     for name in installed if name in sources},
            'missing_companions': {name: [d for d in MANIFEST.get(name, {}).get('companions', []) if d not in installed]
                                   for name in installed},
            'legacy_dlls': sorted(set(installed) & {'BillyClubs', 'RadarSense', 'ArrowGrabAssist', 'ArrowQuiver'})}


STAMP = re.compile(r'^\[\d\d:\d\d:\d\d\.\d+\]')
SIGNAL = re.compile(r'\[(?:ERROR|WARNING)\]|\b\w*Exception\b|failed|unable|could not|method not found|missing (?:method|field|dependency)|ambiguous', re.I)


def analyze_log(path, sources):
    """Keep complete blocks and conservative candidate ownership, including untagged stacks."""
    text = path.read_text(encoding='utf-8-sig', errors='replace')
    blocks = []
    for line in text.splitlines():
        if STAMP.match(line) or not blocks:
            blocks.append([])
        blocks[-1].append(line)
    groups = {}
    for block in blocks:
        body = '\n'.join(block)
        if not SIGNAL.search(body):
            continue
        normalized = STAMP.sub('', body).strip()
        normalized = re.sub(r'0x[0-9a-fA-F]+', '<address>', normalized)
        if normalized not in groups:
            candidates = []
            for name, source in sources.items():
                aliases = {name, re.sub(r'(?<!^)(?=[A-Z])', ' ', name)}
                if name == 'Daredevil':
                    aliases |= {'BillyClubs', 'RadarSense', 'Radar Sense'}
                # Match MelonLoader's spaces-to-underscores logger names as well.
                aliases |= {s.replace(' ', '_') for s in list(aliases)}
                if any(re.search(r'\b' + re.escape(alias) + r'\b', body) for alias in aliases):
                    candidates.append(name)
                elif any(t['type'].rsplit('.', 1)[-1] in body and t['member'] in body for t in source['patch_targets']):
                    candidates.append(name)
            groups[normalized] = {'count': 0, 'candidate_mods': candidates, 'first_block': body}
        groups[normalized]['count'] += 1
    loaded = re.findall(r"Melon Assembly loaded:.*?Mods[\\/](.*?\.dll)'", text)
    hashes = dict(re.findall(r"Melon Assembly loaded:.*?Mods[\\/](.*?\.dll)'\s*\n[^\n]*SHA256 Hash: '([0-9A-Fa-f]+)'", text))
    version = {k: (re.search(rf'{k} Version:\s*([^\r\n]+)', text).group(1)
                   if re.search(rf'{k} Version:\s*([^\r\n]+)', text) else None) for k in ['Game', 'Unity']}
    return {'version_from_log': version, 'assemblies_seen': sorted(set(loaded)), 'assembly_sha256_from_log': {k: v.lower() for k, v in hashes.items()},
            'mod_count_line': re.findall(r'\b\d+ Mods loaded\.', text),
            'groups': sorted(groups.values(), key=lambda x: -x['count'])}


def report(out, data, log=None, changes=None):
    lines = ['# Game update evidence', '', f"Captured UTC: {data['utc']}",
             f"Metadata version: {data['metadata_version']}", '',
             'No automatic gameplay pass/fail verdict. Installed, loaded, enabled and exercised are separate facts.', '',
             '## Inventory', '', '| DLL | Diagnostics / disabled settings | Missing dependencies / expected companions |', '|---|---|---|']
    for name in data['installed']:
        spec = MANIFEST.get(name, {})
        flags = [f'{s}.{k}={data["preferences"].get(s + "." + k, "absent")}' for s, k in spec.get('debug', [])]
        flags += [f'{s}.Enabled=false (feature untested unless enabled)' for s in spec.get('categories', []) if data['preferences'].get(s + '.Enabled', '').lower() == 'false' and [s, 'Enabled'] not in spec.get('debug', [])]
        missing = data['missing_dependencies'].get(name, [])
        companions = [d for d in data['missing_companions'].get(name, []) if d not in missing]
        dependency_text = ', '.join(missing) + ('; companions: ' + ', '.join(companions) if companions else '')
        lines.append(f'| {name} | {"; ".join(flags) or "No configured debug switch; observe behavior/startup"} | {dependency_text} |')
    lines.extend(['', f'Third-party DLLs (no automatic preference edits): {", ".join(data["third_party"]) or "none"}.',
                  f'Legacy duplicate-risk DLLs: {", ".join(data["legacy_dlls"]) or "none"}.',
                  f'Manifest gaps: {", ".join(data["manifest_gaps"]) or "none"}.', '',
                  '## Shared patch targets', '', 'These suggest isolation groups; they do not prove a conflict.', ''])
    owners = {}
    for name in data['installed']:
        for target in data['sources'].get(name, {}).get('patch_targets', []):
            key = target['type'].rsplit('.', 1)[-1] + '::' + target['member']
            owners.setdefault(key, []).append(name)
    lines += [f'- {key}: {", ".join(names)}' for key, names in sorted(owners.items()) if len(names) > 1]
    if changes is not None:
        lines += ['', '## File changes since preparation', '', 'Changed references require review; stale interop cannot be ruled out by timestamps.', '']
        lines += [f'- {name}: {state}' for name, state in changes.items()] or ['None.']
    if log:
        lines += ['', '## Log observations', '', f'Bootstrap versions: {log["version_from_log"]}',
                  f'Assembly load lines: {len(log["assemblies_seen"])}. Assembly loading alone does not prove mod initialization.',
                  '', 'Candidates come from tags/stack symbols/shared hooks. Attribution requires reproduction.', '']
        mismatches = [name for name, digest in log['assembly_sha256_from_log'].items()
                      if data['files'].get(str(Path('Mods') / name), {}).get('sha256') != digest]
        if mismatches:
            lines += ['The log DLL hashes differ from the current folder (historical run or DLLs changed after play): ' + ', '.join(mismatches), '']
        for group in log['groups']:
            lines += [f'### {group["count"]} occurrence(s); candidates: {", ".join(group["candidate_mods"]) or "loader / game / unknown"}',
                      '', '```text', group['first_block'], '```', '']
        if not log['groups']:
            lines.append('No matching warnings/errors. Silent regressions remain untested.')
    lines += ['', '## Next checks', '', 'Follow the smoke checklist in Tools/UPDATE_RECOVERY.md. Record actions and outcomes in playtest.md.',
              'Fix loader/generation failures before mod failures; isolate shared dependencies before consumers.']
    (out / 'report.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def changed_files(before, after):
    return {key: ('added' if key not in before else 'removed' if key not in after else 'changed')
            for key in sorted(before.keys() | after.keys()) if before.get(key) != after.get(key)}


def new_output(action, explicit=None):
    out = Path(explicit).resolve() if explicit else ROOT / 'feature/update-triage' / f'{action}-{datetime.now():%Y%m%d-%H%M%S-%f}'
    out.mkdir(parents=True, exist_ok=False)
    return out


def evidence(out, game, log_path=None):
    data = inventory(game)
    save(out / 'inventory.json', data)
    cfg = game / 'UserData/MelonPreferences.cfg'
    if cfg.exists():
        shutil.copy2(cfg, out / 'MelonPreferences.cfg')
    log = None
    path = Path(log_path) if log_path else game / 'MelonLoader/Latest.log'
    if path.exists():
        shutil.copy2(path, out / 'Latest.log')
        log = analyze_log(out / 'Latest.log', data['sources'])
        save(out / 'issues.json', log)
    report(out, data, log)
    return data


def prepare(args, game):
    require_closed()
    cfg = game / 'UserData/MelonPreferences.cfg'
    raw = cfg.read_bytes()
    prefs = Preferences(raw)
    installed = {p.stem for p in (game / 'Mods').glob('*.dll')}
    keys = [tuple(key) for name, spec in MANIFEST.items() if name in installed for key in spec['debug']]
    if args.with_frame_probe and 'FrameProbe' in installed:
        keys.append(('FrameProbe', 'Enabled'))
    if args.verbose_throws and 'Daredevil' in installed:
        keys.append(('BillyClubs', 'FollowThroughVerboseLog'))
    edits = []
    for section, key in keys:
        before = prefs.values.get((section, key))
        if before is not None and before.lower() not in {'true', 'false'}:
            raise ValueError(f'Expected boolean [{section}] {key}, found {before!r}.')
        if before is None or before.lower() != 'true':
            edits.append({'section': section, 'key': key, 'before': before, 'applied': 'true'})
            prefs.set(section, key, 'true')
    print(json.dumps(edits, indent=2))
    if args.dry_run:
        print('Dry run: no files written.')
        return
    if not edits:
        raise ValueError('Diagnostics already enabled. Reuse the existing session or take Snapshot.')
    out = new_output('prepare', args.output)
    evidence(out, game)
    shutil.copy2(cfg, out / 'preferences-before.cfg')
    # Save a durable recovery plan before touching the live cfg.
    previous_log = game / 'MelonLoader/Latest.log'
    plan = {'schema': 1, 'game_dir': str(game), 'started_epoch': time.time(), 'edits': edits,
            'previous_log_sha256': sha(previous_log) if previous_log.exists() else None,
            'before_sha256': sha(cfg), 'prepared_sha256': hashlib.sha256(prefs.encode()).hexdigest()}
    save(out / 'session.json', plan)
    require_closed()
    if cfg.read_bytes() != raw:
        raise ValueError('Preferences changed during preparation; no live write performed.')
    write_preferences(cfg, prefs.encode())
    if sha(cfg) != plan['prepared_sha256']:
        raise ValueError('Live preference verification failed; backup is in session folder.')
    save(out / 'prepared-inventory.json', inventory(game))
    (out / 'playtest.md').write_text('# Playtest actions and results\n\nUse Tools/UPDATE_RECOVERY.md. Record scene, action, UTC/time in log, and expected/observed result.\n', encoding='utf-8')
    print(f'Prepared: {out}\nStart the game, exercise the checklist, quit, then Collect with this -Session. Restore afterwards.')


def read_session(path, game):
    if not path:
        raise ValueError('--session is required.')
    session = Path(path).resolve()
    plan = json.loads((session / 'session.json').read_text(encoding='utf-8'))
    if Path(plan['game_dir']).resolve() != game:
        raise ValueError('Session belongs to a different game installation.')
    return session, plan


def collect(args, game):
    require_closed()
    session, plan = read_session(args.session, game)
    path = Path(args.log_path) if args.log_path else game / 'MelonLoader/Latest.log'
    if not path.exists() or path.stat().st_mtime < plan['started_epoch']:
        raise ValueError('No new session log since Prepare; refusing to attribute an older log to this test.')
    if sha(path) == plan.get('previous_log_sha256'):
        raise ValueError('Log content is unchanged since Prepare; refusing stale evidence.')
    out = new_output('collect', args.output)
    data = evidence(out, game, path)
    before = json.loads((session / 'prepared-inventory.json').read_text(encoding='utf-8'))
    changes = changed_files(before['files'], data['files'])
    log = json.loads((out / 'issues.json').read_text(encoding='utf-8'))
    # Preserve ancillary logs created/updated since Prepare, with original relative names.
    for directory in ['GrabLog', 'EnemyAwarenessLog', 'FrameProbe']:
        for src in (game / 'UserData' / directory).glob('session-*'):
            if src.is_file() and src.stat().st_mtime >= plan['started_epoch']:
                dest = out / 'UserData' / directory / src.name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, dest)
    shutil.copy2(session / 'playtest.md', out / 'playtest.md')
    save(out / 'file-changes.json', changes)
    save(out / 'capture.json', {'session': str(session), 'log_source': str(path), 'started_epoch': plan['started_epoch']})
    report(out, data, log, changes)
    print(f'Collected: {out}\nRead report.md. No gameplay compatibility verdict inferred from log silence.')


def restore(args, game):
    require_closed()
    session, plan = read_session(args.session, game)
    if plan.get('restored_utc'):
        raise ValueError('This session has already been restored.')
    cfg = game / 'UserData/MelonPreferences.cfg'
    raw = cfg.read_bytes()
    prefs = Preferences(raw)
    # Validate every owned key first. Keep unrelated tuning/loadouts made during testing.
    for edit in plan['edits']:
        current = prefs.values.get((edit['section'], edit['key']))
        if current is None or current.lower() != edit['applied']:
            raise ValueError(f"Restore conflict [{edit['section']}] {edit['key']}={current}; live cfg unchanged. Review session backup.")
    for edit in plan['edits']:
        prefs.set(edit['section'], edit['key'], edit['before'])
    if args.dry_run:
        print('Restore dry run: no conflicts, no files written.')
        return
    backup = session / f'preferences-before-restore-{time.time_ns()}.cfg'
    backup.write_bytes(raw)
    require_closed()
    if cfg.read_bytes() != raw:
        raise ValueError('Preferences changed during restore; no live write performed.')
    write_preferences(cfg, prefs.encode())
    if cfg.read_bytes() != prefs.encode():
        raise ValueError('Restored preference verification failed.')
    plan['restored_utc'] = datetime.now(timezone.utc).isoformat()
    save(session / 'session.json', plan)
    print('Restored only diagnostics changed by Prepare; other saved settings preserved.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['snapshot', 'prepare', 'collect', 'restore'])
    parser.add_argument('--game-dir')
    parser.add_argument('--session')
    parser.add_argument('--output')
    parser.add_argument('--log-path')
    parser.add_argument('--with-frame-probe', action='store_true')
    parser.add_argument('--verbose-throws', action='store_true')
    parser.add_argument('--archive-native', action='store_true', help='Snapshot: preserve GameAssembly and metadata locally for later disassembly.')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if args.archive_native and args.action != 'snapshot':
        parser.error('--archive-native applies to snapshot only.')
    if args.dry_run and args.action not in {'prepare', 'restore'}:
        parser.error('--dry-run applies to prepare/restore only.')
    if (args.with_frame_probe or args.verbose_throws) and args.action != 'prepare':
        parser.error('Diagnostic options apply to prepare only.')
    game = resolve_game(args.game_dir)
    if not (game / 'GunmanContracts.exe').is_file():
        parser.error(f'Game installation unavailable: {game}')
    if args.action == 'snapshot':
        require_closed()
        out = new_output('snapshot', args.output)
        data = evidence(out, game, args.log_path)
        if args.archive_native:
            for relative in [Path('GameAssembly.dll'), METADATA]:
                dest = out / 'native-baseline' / relative
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(game / relative, dest)
                if sha(dest) != data['files'][str(relative)]['sha256']:
                    raise ValueError('Native baseline changed during copy; snapshot is inconsistent.')
        print(f'Snapshot: {out}\nLatest.log here is historical evidence, not a newly exercised test.')
    else:
        {'prepare': prepare, 'collect': collect, 'restore': restore}[args.action](args, game)


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
