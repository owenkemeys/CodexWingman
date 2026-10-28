import importlib.util, json, sys, tempfile, time, unittest
from datetime import datetime, timezone
from pathlib import Path

spec = importlib.util.spec_from_file_location('bridge', Path(__file__).parents[2] / 'tools/t3_pairing_bridge.py')
bridge_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge_module)

FAKE_T3 = r'''
import json, sys
from pathlib import Path
log = Path(sys.argv[1]); args = sys.argv[2:]
with log.open('a') as f: f.write(json.dumps(args) + '\n')
if args[:3] == ['auth', 'pairing', 'create']:
    print('note: created'); print(json.dumps({'pairUrl': 'https://t3.example.net:8443/pair#token=one-time'}))
elif args[:3] == ['auth', 'session', 'list']:
    print(json.dumps([
        {'sessionId': 'old', 'method': 'browser-session-cookie', 'issuedAt': '2026-09-01T00:00:00Z', 'client': {'label': 'Wingman T3 web (PC)'}},
        {'sessionId': 'new', 'method': 'browser-session-cookie', 'issuedAt': '2026-10-01T00:00:00Z', 'client': {'label': 'Wingman T3 web (PC)'}},
        {'sessionId': 'other', 'method': 'browser-session-cookie', 'issuedAt': '2026-08-01T00:00:00Z', 'client': {'label': 'Laptop'}},
    ]))
elif args[:3] == ['auth', 'session', 'revoke']:
    print('{}')
else:
    sys.exit(3)
'''


def stamp(offset=0.0):
    return datetime.fromtimestamp(time.time() + offset, timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


class T3PairingBridge(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.folder = root / 'pairing'; self.folder.mkdir()
        self.calls = root / 'calls.log'
        fake = root / 'fake_t3.py'; fake.write_text(FAKE_T3)
        self.logs = []
        self.bridge = bridge_module.Bridge(self.folder, 'https://T3.example.net:8443/', root / 'state',
                                           [sys.executable, str(fake), str(self.calls)], log=self.logs.append)

    def tearDown(self):
        self.temp.cleanup()

    def request(self, rid='0f1e2d3c4b5a69788796a5b4c3d2e1f0', **overrides):
        body = {'schema_version': 1, 'kind': 'pair', 'id': rid, 'instance': 'Wingman T3 web (PC)',
                'label': 'Wingman T3 web (PC)', 'reason': 'signed-out', 'origin': None, 'created_at_utc': stamp()}
        body.update(overrides)
        (self.folder / f'{rid}.request.json').write_text(json.dumps(body))
        return rid

    def response(self, rid):
        return json.loads((self.folder / f'{rid}.response.json').read_text())

    def calls_made(self):
        return [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []

    def test_pair_answers_once_with_the_link_and_never_logs_it(self):
        rid = self.request(origin='https://t3.example.net:8443')
        self.assertEqual(self.bridge.process(), 1)
        self.assertEqual(self.response(rid), {'id': rid, 'status': 'ok',
                                              'pairing_url': 'https://t3.example.net:8443/pair#token=one-time'})
        create = self.calls_made()[0]
        self.assertEqual(create[:5], ['auth', 'pairing', 'create', '--ttl', '2m'])
        self.assertIn('https://t3.example.net:8443', create)
        self.assertNotIn('--scope', create)
        self.assertNotIn('one-time', '\n'.join(self.logs))
        (self.folder / f'{rid}.response.json').unlink()
        self.assertEqual(self.bridge.process(), 0)
        self.assertFalse((self.folder / f'{rid}.response.json').exists())

    def test_revoke_previous_keeps_the_newest_session_with_that_label(self):
        rid = self.request(kind='revoke-previous', reason='renewed')
        self.bridge.process()
        self.assertEqual(self.response(rid), {'id': rid, 'status': 'ok', 'revoked': 1})
        self.assertEqual(self.calls_made()[-1], ['auth', 'session', 'revoke', 'old'])

    def test_refuses_foreign_labels_origins_and_mismatched_ids(self):
        a = self.request('aaaaaaaa1', label='My laptop')
        b = self.request('bbbbbbbb2', origin='https://elsewhere.example.net')
        c = self.request('cccccccc3', id='something-else')
        self.bridge.process()
        self.assertEqual(self.response(a)['message'], "label must start with 'Wingman '")
        self.assertEqual(self.response(b)['message'], 'origin is not served here')
        self.assertEqual(self.response(c)['message'], 'id mismatch')
        self.assertEqual(self.calls_made(), [])

    def test_stale_requests_are_dropped_unanswered(self):
        rid = self.request(created_at_utc=stamp(-3600))
        self.assertEqual(self.bridge.process(), 0)
        self.assertFalse((self.folder / f'{rid}.request.json').exists())
        self.assertFalse((self.folder / f'{rid}.response.json').exists())

    def test_base_url_must_be_https(self):
        with self.assertRaises(ValueError):
            bridge_module.Bridge(self.folder, 'http://t3.example.net', self.folder, ['t3'])


if __name__ == '__main__':
    unittest.main()
