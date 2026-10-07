import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("release_archive", ROOT / "tools/release_archive.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ReleaseArchiveTests(unittest.TestCase):
    def test_manifest_and_archive_must_match(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "release.zip"
            payload = b"exe"
            import hashlib
            manifest = {
                "schema": "codexwingman.release.v1",
                "repository": "https://github.com/example/Wingman",
                "version": "2.0.0",
                "files": {"CodexWingman.exe": hashlib.sha256(payload).hexdigest()},
            }
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("CodexWingman-verified/CodexWingman.exe", payload)
                output.writestr("CodexWingman-verified/release.json", json.dumps(manifest))
            self.assertEqual(1, MODULE.verify_archive(
                archive, "CodexWingman-verified", "https://github.com/example/Wingman", "2.0.0"))
            with zipfile.ZipFile(archive, "a") as output:
                output.writestr("CodexWingman-verified/extra.txt", "extra")
            with self.assertRaisesRegex(ValueError, "file list"):
                MODULE.verify_archive(archive, "CodexWingman-verified",
                                      "https://github.com/example/Wingman", "2.0.0")

    def test_archive_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("CodexWingman-verified/../escape.txt", "bad")
            with self.assertRaisesRegex(ValueError, "unsafe archive entry"):
                MODULE.verify_archive(archive, "CodexWingman-verified",
                                      "https://github.com/example/Wingman", "2.0.0")
