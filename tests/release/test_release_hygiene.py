import importlib.util, os, tempfile, unittest, zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location('hygiene', ROOT / 'tools/release_hygiene.py')
hygiene = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hygiene)


class ReleaseHygiene(unittest.TestCase):
    def scan(self, files):
        with mock.patch.dict(os.environ, {'RELEASE_HYGIENE_EXTRA_TERMS': 'zorblax\nquiet harbor\nhost-q7'}):
            return hygiene.scan(files)[1]

    def test_the_source_tree_is_clean(self):
        count, errors = hygiene.scan(hygiene.iter_tree(ROOT))
        self.assertGreater(count, 50)
        self.assertEqual(errors, [], 'setup-specific terms in tracked files; run tools/release_hygiene.py tree --show')

    def test_terms_are_stored_as_hashes_only(self):
        for line in hygiene.TERMS_FILE.read_text().splitlines():
            if line.strip() and not line.startswith('#'):
                self.assertRegex(line, r'^[0-9a-f]{64}$')

    def test_finds_words_camel_case_pairs_and_names_without_printing_them(self):
        errors = self.scan([
            ('a.md', b'Notes from Zorblax.\n'),
            ('b.cs', b'var zorblaxSync = 1;\n'),
            ('c.txt', b'The Quiet  Harbor rules\n'),
            ('d.json', b'{"host": "host-q7"}\n'),
            ('e.md', b'zorblaxian and host-q70 are different words\n'),
        ])
        self.assertEqual(sorted(e[0] for e in errors), ['a.md', 'b.cs', 'c.txt', 'd.json'])
        self.assertTrue(all(e[3] == '' for e in errors))

    def test_reads_ascii_and_utf16_strings_inside_binaries_and_zips(self):
        binary = b'MZ\x00\x00' + 'Zorblax'.encode('utf-16-le') + b'\x00\x01'
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / 'p.zip'
            with zipfile.ZipFile(package, 'w') as archive:
                archive.writestr('Root/App.dll', binary)
                archive.writestr('Root/readme.txt', 'nothing here')
            errors = self.scan(hygiene.iter_zip(package))
        self.assertEqual([e[0] for e in errors], ['Root/App.dll'])

    def test_allowlist_is_per_file_and_bounded(self):
        digest = hygiene.term_hash('zorblax')
        with mock.patch.object(hygiene, 'load_allow', return_value={'*Allowed.dll': {digest: 1}}):
            self.assertEqual(self.scan([('x/Allowed.dll', b'\x00zorblax\x00')]), [])
            self.assertEqual(len(self.scan([('x/Allowed.dll', b'\x00zorblax\x00zorblax\x00')])), 2)
            self.assertEqual(len(self.scan([('x/Other.dll', b'\x00zorblax\x00')])), 1)


if __name__ == '__main__':
    unittest.main()
