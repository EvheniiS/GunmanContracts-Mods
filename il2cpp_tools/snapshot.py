"""Save/diff a v31 research map, preserving overloads rather than choosing address [0].

python il2cpp_tools/snapshot.py save --output feature/game-before.json
python il2cpp_tools/snapshot.py diff feature/game-before.json feature/game-after.json --output feature/game-diff.json
Set GUNMAN_CONTRACTS_DIR as for the other tools. Save requires pefile; diff is stdlib only.
"""
import argparse
from contextlib import redirect_stdout
from datetime import datetime, timezone
import json
from pathlib import Path
import sys


def capture(type_names=None):
    with redirect_stdout(sys.stderr):
        import il2
    import importlib.util
    root = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('update_triage', root / 'Tools/update_triage.py')
    triage = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(triage)
    sources = triage.source_inventory()
    if type_names is None:
        # Broad ANB/HVR scope also covers indirect calls/fields not in Harmony attributes.
        selected = [t for t in range(il2.NT) if il2.types[t]['name'].startswith(('ANB', 'HVR', 'GD_'))]
        # Include nested coroutine/state-machine types used by the selected classes.
        chosen = set(selected)
        while True:
            nested = {t for t in range(il2.NT) if il2.types[t]['decl'] in chosen}
            if nested <= chosen: break
            chosen |= nested
        selected = sorted(chosen)
    else:
        selected = sorted({t for name in type_names for t in il2.find_type(name)})
        absent = [name for name in type_names if not il2.find_type(name)]
        if absent:
            raise ValueError(f'Requested types not found: {absent}')
    result = {}
    for t in selected:
        ty = il2.types[t]
        # Image + namespace + nested declaration chain prevents simple-name collisions.
        image = next(n for n, start, count in il2.images if start <= t < start + count)
        declaring, chain, seen = ty['decl'], [ty['name']], {t}
        while 0 <= declaring < il2.NT and declaring not in seen:
            seen.add(declaring)
            chain.insert(0, il2.types[declaring]['name'])
            declaring = il2.types[declaring]['decl']
        identity = image + ':' + (ty['ns'] + '.' if ty['ns'] else '') + '+'.join(chain)
        methods = []
        # The mapper's name2addr combines overloads. Recover each token's exact pointer.
        for mi in range(ty['mStart'], ty['mStart'] + ty['mc']):
            m = il2.method(mi)
            address = il2.method_addrs.get(mi)
            methods.append({'signature': il2.sig(mi), 'token': m['token'],
                            'rva': address-il2.base if address else None})
        result[identity] = {'fields': [{'name': name, 'type': kind, 'offset': offset} for name, kind, offset in il2.fields_of(t)],
                            'methods': methods}
    return {'schema': 1, 'utc': datetime.now(timezone.utc).isoformat(), 'game_dir': il2.G,
            'metadata_version': 31, 'file_sha256': {'GameAssembly.dll': il2.KEY[1], 'global-metadata.dat': il2.KEY[2]},
            'type_scope': sorted(type_names) if type_names else 'ANB*, HVR*, GD_* and nested types', 'types': result,
            'mod_patch_inventory': {name: value['patch_targets'] for name, value in sources.items()},
            'limitations': ['Type strings do not fully resolve generics/arrays/byref. Compare interop signatures when needed.',
                           'RVA/offset changes are research navigation, not evidence a mod broke.',
                           'Same signatures do not establish unchanged behavior; assets and dynamic hooks require separate checks.']}


def diff(before, after):
    if before['schema'] != 1 or after['schema'] != 1 or before['type_scope'] != after['type_scope']:
        raise ValueError('Snapshots must have schema 1 and the same type scope.')
    changes = {}
    a, b = before['types'], after['types']
    for name in sorted(a.keys() | b.keys()):
        if name not in a or name not in b:
            changes[name] = {'status': 'added' if name not in a else 'removed'}
            continue
        fa = {f['name']: (f['type'], f['offset']) for f in a[name]['fields']}
        fb = {f['name']: (f['type'], f['offset']) for f in b[name]['fields']}
        ma, mb = Counter(m['signature'] for m in a[name]['methods']), Counter(m['signature'] for m in b[name]['methods'])
        if fa != fb or ma != mb:
            changes[name] = {'fields': {k: {'before': fa.get(k), 'after': fb.get(k)} for k in sorted(fa.keys() | fb.keys()) if fa.get(k) != fb.get(k)},
                             'removed_signatures': list((ma-mb).elements()), 'added_signatures': list((mb-ma).elements())}
    impacted = {}
    for mod, targets in after['mod_patch_inventory'].items():
        impacted[mod] = [name for name in changes if any(name.split(':', 1)[1].split('+')[-1].rsplit('.', 1)[-1] == t['type'].rsplit('.', 1)[-1] for t in targets)]
    return {'before_sha256': before['file_sha256'], 'after_sha256': after['file_sha256'],
            'changes': changes, 'candidate_mods': {m: t for m, t in impacted.items() if t},
            'note': 'Candidates require inspection, not automatic repairs. Field offsets may move harmlessly. Native logic and assets can change without a signature delta.'}


from collections import Counter


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    capture_parser = sub.add_parser('save')
    capture_parser.add_argument('--output', required=True)
    capture_parser.add_argument('--type', action='append', dest='types')
    diff_parser = sub.add_parser('diff')
    diff_parser.add_argument('before')
    diff_parser.add_argument('after')
    diff_parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():
        parser.error('Output already exists; preserve the earlier snapshot.')
    value = capture(args.types) if args.action == 'save' else diff(json.loads(Path(args.before).read_text()), json.loads(Path(args.after).read_text()))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    print(output.resolve())


if __name__ == '__main__':
    main()
