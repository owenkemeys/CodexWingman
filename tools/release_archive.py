"""Verify a GitHub download ZIP against the enclosed exact package manifest."""

import argparse
import hashlib
import json
import stat
import sys
import zipfile
from pathlib import Path


def verify_archive(archive_path: Path, root_name: str, repository: str, version: str) -> int:
    with zipfile.ZipFile(archive_path) as archive:
        entries = {}
        for entry in archive.infolist():
            name = entry.filename
            parts = name.rstrip("/").split("/")
            if (not name.startswith(root_name + "/") or "\\" in name
                    or any(part in ("", ".", "..") for part in parts)
                    or stat.S_IFMT(entry.external_attr >> 16) == stat.S_IFLNK):
                raise ValueError(f"unsafe archive entry: {name}")
            if entry.is_dir():
                continue
            relative = name[len(root_name) + 1:]
            if relative in entries:
                raise ValueError(f"duplicate archive entry: {relative}")
            entries[relative] = entry
        if "release.json" not in entries:
            raise ValueError("release manifest is missing")
        manifest = json.loads(archive.read(entries["release.json"]))
        if (manifest.get("schema") != "codexwingman.release.v1"
                or manifest.get("repository") != repository
                or manifest.get("version") != version):
            raise ValueError("release identity differs from archive selection")
        expected = manifest.get("files")
        if not isinstance(expected, dict) or set(entries) - {"release.json"} != set(expected):
            raise ValueError("archive file list differs from release manifest")
        for name, digest in expected.items():
            if hashlib.sha256(archive.read(entries[name])).hexdigest() != digest:
                raise ValueError(f"archive file did not verify: {name}")
        return len(expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--root-name", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try:
        print(verify_archive(args.archive, args.root_name, args.repository, args.version))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        print(f"Release archive failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
