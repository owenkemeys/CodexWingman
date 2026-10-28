#!/usr/bin/env python3
"""Fails when words from the maintainer's own setup appear in the source tree or a release package.

The words are stored only as salted SHA-256 hashes (tools/release-hygiene-terms.txt), so the list
itself does not publish them. Every file is split into words (also camelCase parts), adjacent word
pairs and dotted/dashed names; each is hashed and looked up. Binaries are scanned through their
ASCII and UTF-16 strings, so text embedded in a DLL is caught too. A finding prints the file, the
line (or byte offset) and the term's hash prefix, never the word, because CI logs are public.

Deliberate exceptions are listed per file, with a maximum count, in tools/release-hygiene-allow.json.
Extra plaintext terms can come from the RELEASE_HYGIENE_EXTRA_TERMS environment variable (one per
line), for example a repository secret.

  python tools/release_hygiene.py tree               scan the files git tracks
  python tools/release_hygiene.py zip PACKAGE.zip    scan every file in a release zip
  python tools/release_hygiene.py dir FOLDER         scan a folder
  python tools/release_hygiene.py hash TERM...       print the hash line for new terms
"""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TERMS_FILE = ROOT / "tools" / "release-hygiene-terms.txt"
ALLOW_FILE = ROOT / "tools" / "release-hygiene-allow.json"
SALT = b"codexwingman-release-hygiene-v1:"
WORD = re.compile(r"[A-Za-z0-9]+")
NAME = re.compile(r"[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?")
CAMEL = re.compile(r"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")
ASCII_RUN = re.compile(rb"[\x20-\x7e\t]{4,}")
UTF16_RUN = re.compile(rb"(?:[\x20-\x7e]\x00){4,}")
BINARY_SUFFIXES = {".dll", ".exe", ".pdb", ".ico", ".png", ".jpg", ".gif", ".zip", ".bin", ".woff", ".woff2"}


def term_hash(term: str) -> str:
    normalized = " ".join(term.lower().split())
    return hashlib.sha256(SALT + normalized.encode("utf-8")).hexdigest()


def load_terms() -> set[str]:
    hashes = {line.split()[0] for line in TERMS_FILE.read_text(encoding="utf-8").splitlines()
              if line.strip() and not line.lstrip().startswith("#")}
    for term in os.environ.get("RELEASE_HYGIENE_EXTRA_TERMS", "").splitlines():
        if term.strip():
            hashes.add(term_hash(term))
    return hashes


def load_allow() -> dict[str, dict[str, int]]:
    data = json.loads(ALLOW_FILE.read_text(encoding="utf-8"))
    return {pattern: dict(entry["max"]) for pattern, entry in data["files"].items()}


def is_binary(name: str, data: bytes) -> bool:
    return Path(name).suffix.lower() in BINARY_SUFFIXES or b"\x00" in data[:8192]


def text_lines(name: str, data: bytes):
    """(location, text) pairs: line numbers for text, byte offsets for strings inside binaries."""
    if not is_binary(name, data):
        for number, line in enumerate(data.decode("utf-8", errors="replace").splitlines(), 1):
            yield f"line {number}", line
        return
    for match in ASCII_RUN.finditer(data):
        yield f"offset {match.start()}", match.group().decode("ascii")
    for match in UTF16_RUN.finditer(data):
        yield f"offset {match.start()}", match.group().decode("utf-16-le")


def candidates(text: str):
    words = WORD.findall(text)
    lower = [w.lower() for w in words]
    yield from lower
    for word in words:
        parts = CAMEL.findall(word)
        if len(parts) > 1:
            yield from (p.lower() for p in parts)
    for first, second in zip(lower, lower[1:]):
        yield f"{first} {second}"
    for name in NAME.findall(text):
        yield name.lower()


def scan_bytes(name: str, data: bytes, terms: set[str], show: bool) -> list[tuple[str, str, str, str]]:
    findings = []
    for location, line in text_lines(name, data):
        seen = set()
        for candidate in candidates(line):
            digest = term_hash(candidate)
            if digest in terms and digest not in seen:
                seen.add(digest)
                findings.append((name, location, digest, candidate if show else ""))
    return findings


def apply_allow(findings, allow):
    counts: dict[tuple[str, str], int] = {}
    for name, _, digest, _ in findings:
        counts[(name, digest)] = counts.get((name, digest), 0) + 1
    errors = []
    for name, location, digest, word in findings:
        limit = next((rules.get(digest, 0) for pattern, rules in allow.items()
                      if fnmatch.fnmatchcase(name, pattern)), 0)
        if counts[(name, digest)] > limit:
            errors.append((name, location, digest, word))
    return errors


def iter_tree(root: Path):
    listed = subprocess.run(["git", "ls-files", "-z"], cwd=root, capture_output=True, check=True).stdout
    for name in filter(None, listed.decode("utf-8").split("\0")):
        path = root / name
        if path.is_file():
            yield name, path.read_bytes()


def iter_dir(folder: Path):
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            yield path.relative_to(folder).as_posix(), path.read_bytes()


def iter_zip(package: Path):
    with zipfile.ZipFile(package) as archive:
        for info in archive.infolist():
            if not info.is_dir():
                yield info.filename.replace("\\", "/"), archive.read(info)


def scan(files, show: bool = False) -> tuple[int, list]:
    terms, allow = load_terms(), load_allow()
    findings, count = [], 0
    for name, data in files:
        count += 1
        findings.extend(scan_bytes(name, data, terms, show))
    return count, apply_allow(findings, allow)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("mode", choices=["tree", "zip", "dir", "hash"])
    parser.add_argument("target", nargs="*")
    parser.add_argument("--show", action="store_true", help="print the matched word (local use only)")
    args = parser.parse_args(argv)
    if args.mode == "hash":
        for term in args.target:
            print(term_hash(term))
        return 0
    if args.mode == "tree":
        files = iter_tree(Path(args.target[0]) if args.target else ROOT)
    elif args.mode == "zip":
        files = iter_zip(Path(args.target[0]))
    else:
        files = iter_dir(Path(args.target[0]))
    count, errors = scan(files, args.show)
    for name, location, digest, word in errors:
        print(f"::error file={name}::setup-specific term {digest[:12]} at {location}" + (f" ({word})" if word else ""))
    print(f"release hygiene: {count} files scanned, {len(errors)} finding(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
