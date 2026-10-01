import argparse
import importlib.util
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools'))
sys.path.insert(0, str(ROOT / 'il2cpp_tools'))
import update_triage as triage
import metadata_layout as layout
spec = importlib.util.spec_from_file_location('research_snapshot', ROOT / 'il2cpp_tools/snapshot.py')
snapshot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(snapshot)


class PreferencesTests(unittest.TestCase):
    def test_preserves_comments_encoding_and_saved_state(self):
        raw = '\ufeff[Mod]\r\n  DebugLog = false # explanation\r\nSaved = "R=Crowbar"\r\n'.encode('utf-8')
        prefs = triage.Preferences(raw)
        prefs.set('Mod', 'DebugLog', 'true')
        self.assertEqual(prefs.encode(), raw.replace(b'false', b'true'))
        prefs.set('Mod', 'DebugLog', 'false')
        self.assertEqual(prefs.encode(), raw)

    def test_missing_entries_and_sections_do_not_merge_lines(self):
        prefs = triage.Preferences(b'[Old]\nValue = 2')
        prefs.set('Old', 'DebugLog', 'true')
        prefs.set('New', 'Enabled', 'true')
        parsed = triage.Preferences(prefs.encode())
        self.assertEqual(parsed.values[('Old', 'Value')], '2')
        self.assertEqual(parsed.values[('New', 'Enabled')], 'true')
        parsed.set('New', 'Enabled', None)
        self.assertNotIn(('New', 'Enabled'), parsed.values)

    def test_duplicates_refused(self):
        for text in [b'[M]\nD = true\nD = false', b'[M]\nD = true\n[M]\nE = false']:
            with self.assertRaises(ValueError):
                triage.Preferences(text)

    def test_utf16(self):
        raw = '[M]\r\nDebugLog = false\r\n'.encode('utf-16')
        p = triage.Preferences(raw)
        p.set('M', 'DebugLog', 'true')
        self.assertEqual(p.encode(), '[M]\r\nDebugLog = true\r\n'.encode('utf-16'))


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.game = self.base / 'game'
        (self.game / 'Mods').mkdir(parents=True)
        (self.game / 'UserData').mkdir()
        (self.game / 'MelonLoader').mkdir()
        (self.game / 'Mods/BetterBow.dll').write_bytes(b'fake')
        self.cfg = self.game / 'UserData/MelonPreferences.cfg'
        self.cfg.write_bytes(b'[BetterBow]\nDebugLog = false\nTuning = 3\n')
        self.closed = patch.object(triage, 'require_closed')
        self.closed.start()

    def tearDown(self):
        self.closed.stop()
        self.temp.cleanup()

    def args(self, **kwargs):
        return argparse.Namespace(dry_run=False, with_frame_probe=False, verbose_throws=False,
                                  output=str(self.base / 'session'), log_path=None, session=str(self.base / 'session'), **kwargs)

    def test_roundtrip_preserves_testing_tuning(self):
        args = self.args()
        triage.prepare(args, self.game)
        self.cfg.write_bytes(self.cfg.read_bytes().replace(b'Tuning = 3', b'Tuning = 7'))
        triage.restore(args, self.game)
        self.assertEqual(self.cfg.read_bytes(), b'[BetterBow]\nDebugLog = false\nTuning = 7\n')

    def test_conflict_causes_no_partial_restore(self):
        args = self.args()
        triage.prepare(args, self.game)
        self.cfg.write_bytes(self.cfg.read_bytes().replace(b'true', b'false'))
        before = self.cfg.read_bytes()
        with self.assertRaisesRegex(ValueError, 'Restore conflict'):
            triage.restore(args, self.game)
        self.assertEqual(self.cfg.read_bytes(), before)

    def test_stale_log_rejected_and_new_capture_copies_sidecar(self):
        args = self.args()
        log = self.game / 'MelonLoader/Latest.log'
        log.write_text('[01:00:00.000] old log\n')
        triage.prepare(args, self.game)
        args.output = str(self.base / 'capture')
        with self.assertRaises(ValueError):
            triage.collect(args, self.game)
        log.write_text('[02:00:00.000] [ERROR] MissingMethodException\n   at BetterBow.Mod.Run()\n')
        plan = json.loads((Path(args.session) / 'session.json').read_text())
        os.utime(log, (plan['started_epoch']+1, plan['started_epoch']+1))
        sidecar = self.game / 'UserData/GrabLog/session-test.jsonl'
        sidecar.parent.mkdir()
        sidecar.write_text('{"kind":"test"}\n')
        os.utime(sidecar, (plan['started_epoch']+1, plan['started_epoch']+1))
        triage.collect(args, self.game)
        self.assertEqual((Path(args.output) / 'UserData/GrabLog/session-test.jsonl').read_text(), sidecar.read_text())
        issue = json.loads((Path(args.output) / 'issues.json').read_text())['groups'][0]
        self.assertIn('BetterBow', issue['candidate_mods'])
        self.assertIn('at BetterBow.Mod.Run()', issue['first_block'])

    def test_process_check_failure_stops_prepare(self):
        self.closed.stop()
        with patch.object(triage, 'require_closed', side_effect=ValueError('Close game')):
            with self.assertRaises(ValueError):
                triage.prepare(self.args(), self.game)
        self.closed.start()
        self.assertIn(b'false', self.cfg.read_bytes())

    def test_manifest_covers_projects_and_real_debug_keys(self):
        inventory = triage.source_inventory()
        self.assertEqual(set(inventory), set(triage.MANIFEST))
        for name, source in inventory.items():
            declared = set()
            categories, keys = set(), set()
            for p in (ROOT / source['project']).parent.rglob('*.cs'):
                if set(p.parts) & {'bin', 'obj', 'Tests', 'Example'}: continue
                import re
                code = p.read_text(encoding='utf-8-sig')
                declared.update(re.findall(r'CreateEntry\("(DebugLog)"', code))
                categories.update(re.findall(r'CreateCategory\("([^"]+)"', code))
                keys.update(re.findall(r'CreateEntry\("([^"]+)"', code))
            self.assertEqual(bool(declared), any(key == 'DebugLog' for _, key in triage.MANIFEST[name]['debug']), name)
            for category, key in triage.MANIFEST[name]['debug']:
                self.assertIn(category, categories, name)
                self.assertIn(key, keys, name)


class ResearchTests(unittest.TestCase):
    def valid_metadata(self):
        size = 8 + 8 * len(layout.TABLES)
        header = bytearray(size)
        struct.pack_into('<II', header, 0, 0xFAB11BAF, 31)
        payload = bytearray()
        for name, stride in {'string': 1, 'typeDefinitions': 88, 'methods': 36, 'images': 40}.items():
            struct.pack_into('<II', header, 8 + layout.TABLES.index(name)*8, size + len(payload), stride)
            payload.extend(bytes(stride))
        return header + payload

    def test_guard_rejects_unsupported_and_corrupt_layout(self):
        raw = self.valid_metadata()
        self.assertEqual(layout.header(raw)['methods'][1], 36)
        for mutation in [b'x', bytes(8), raw[:100]]:
            with self.assertRaises(ValueError): layout.header(mutation)
        struct.pack_into('<I', raw, 4, 32)
        with self.assertRaisesRegex(ValueError, 'Unsupported metadata v32'):
            layout.header(raw)

    def test_cache_key_uses_content_and_schema(self):
        self.assertNotEqual(layout.cache_key(b'AAA', b'M'), layout.cache_key(b'BBB', b'M'))
        self.assertNotEqual(layout.cache_key(b'AAA', b'M'), layout.cache_key(b'AAA', b'N'))

    def test_diff_keeps_overloads_and_ignores_address_only_moves(self):
        value = {'schema': 1, 'type_scope': 'test', 'file_sha256': {}, 'mod_patch_inventory': {},
                 'types': {'Game:T': {'fields': [], 'methods': [{'signature': 'void M(int x)', 'rva': 1}, {'signature': 'void M(float x)', 'rva': 2}]}}}
        newer = json.loads(json.dumps(value))
        newer['types']['Game:T']['methods'][0]['rva'] = 99
        self.assertEqual(snapshot.diff(value, newer)['changes'], {})
        newer['types']['Game:T']['methods'].pop()
        self.assertEqual(snapshot.diff(value, newer)['changes']['Game:T']['removed_signatures'], ['void M(float x)'])


if __name__ == '__main__':
    unittest.main()
