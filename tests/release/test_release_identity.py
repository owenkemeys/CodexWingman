import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("release_identity", ROOT / "tools/release_identity.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ReleaseIdentityTests(unittest.TestCase):
    def test_current_identity_matches_binary(self):
        identity = MODULE.load_identity(ROOT, "v1.0.0")
        self.assertIn("What's in this release", MODULE.release_notes(identity))

    def test_mismatched_tag_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "Release tag"):
            MODULE.load_identity(ROOT, "v2.0.0")

    def test_mismatched_about_content_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "src/CodexWingman").mkdir(parents=True)
            (root / "src/CodexWingman/CodexWingman.csproj").write_text(
                "<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>",
                encoding="utf-8",
            )
            (root / "release-content.json").write_text(
                json.dumps({"version": "1.0.0", "highlights": ["A visible change."]}),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "About version"):
                MODULE.load_identity(root)
