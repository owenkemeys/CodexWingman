"""Validate the installed About identity and render short public release notes."""

import argparse
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree


def load_identity(root: Path, tag: str | None = None) -> dict:
    content = json.loads((root / "release-content.json").read_text(encoding="utf-8"))
    project = ElementTree.parse(root / "src/CodexWingman/CodexWingman.csproj")
    project_version = project.findtext(".//Version")
    version = content.get("version")
    highlights = content.get("highlights")
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("About version must be a stable three-part version")
    if version != project_version:
        raise ValueError("About version and executable version differ")
    if tag is not None and tag != "v" + version:
        raise ValueError("Release tag and executable version differ")
    if not isinstance(highlights, list) or not 1 <= len(highlights) <= 5:
        raise ValueError("About needs one to five user-facing highlights")
    if any(not isinstance(item, str) or not item.strip() or "\n" in item for item in highlights):
        raise ValueError("About highlights must be single nonempty lines")
    return content


def release_notes(content: dict) -> str:
    return "## What's in this release\n\n" + "\n".join(
        "- " + line.strip() for line in content["highlights"]
    ) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--tag")
    parser.add_argument("--notes-output", type=Path)
    args = parser.parse_args()
    try:
        content = load_identity(args.root, args.tag)
        if args.notes_output:
            args.notes_output.write_text(release_notes(content), encoding="utf-8")
        print(content["version"])
    except (ValueError, OSError, KeyError) as error:
        print(f"Release identity failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
