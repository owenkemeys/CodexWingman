#!/usr/bin/env python3
"""Reference bridge that answers Wingman's T3 web pairing requests on the T3 host.

Wingman writes <id>.request.json into a pairing folder and waits for <id>.response.json; the file
format is in docs/T3_WEB_PAIRING.md. This bridge answers with the T3 server's own CLI:

  pair             t3 auth pairing create --ttl 2m --label <label> --base-url <origin> [--scope ...] --json
  revoke-previous  of the browser sessions whose label is exactly <label>, keep the newest and revoke
                   the others (t3 auth session revoke <id>); labels not starting "Wingman " are refused.

Each request id is answered once (state in --state-dir); requests older than ten minutes are dropped;
the origin must equal --base-url; unread responses are deleted after five minutes; no link or token
is ever printed.

  python tools/t3_pairing_bridge.py --folder DIR --base-url https://host:port [--watch SECONDS]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

STALE_SECONDS = 10 * 60
RESPONSE_TTL_SECONDS = 5 * 60
PROCESSED_KEEP_SECONDS = 7 * 24 * 3600
CLI_TIMEOUT = 60
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9-]{7,63}$")
LABEL = re.compile(r"^Wingman [ -~]{1,72}$")
KINDS = {"pair", "revoke-previous"}


def write_json(path: Path, data: dict) -> None:
    tmp = path.with_name("." + path.name + ".tmp")
    tmp.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def origin_of(url: str) -> str | None:
    try:
        parts = urlsplit(str(url).strip())
        port = parts.port
    except ValueError:
        return None
    if parts.scheme != "https" or not parts.hostname or parts.username or parts.password:
        return None
    host = parts.hostname.lower()
    return f"https://{host}:{port}" if port and port != 443 else f"https://{host}"


def first_json(text: str):
    decoder = json.JSONDecoder()
    for match in re.finditer(r"[\[{]", text):
        try:
            return decoder.raw_decode(text[match.start():])[0]
        except ValueError:
            continue
    raise ValueError("no JSON in t3 output")


def parse_time(value) -> float | None:
    try:
        return datetime.strptime(str(value), "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp()
    except ValueError:
        return None


class Bridge:
    def __init__(self, folder: Path, base_url: str, state_dir: Path, t3_command: list[str],
                 scopes: tuple[str, ...] = (), log=print):
        origin = origin_of(base_url)
        if origin is None:
            raise ValueError("--base-url must be an https origin")
        self.folder = Path(folder)
        self.origin = origin
        self.state_dir = Path(state_dir)
        self.t3_command = list(t3_command)
        self.scopes = tuple(scopes)
        self.log = log

    def run_t3(self, args: list[str]) -> str:
        result = subprocess.run([*self.t3_command, *args], capture_output=True, text=True, timeout=CLI_TIMEOUT)
        if result.returncode != 0:
            # Keep only the first line of the error; it never carries a credential.
            first = (result.stderr or result.stdout or "").strip().splitlines()[:1]
            raise RuntimeError(f"t3 {' '.join(args[:3])} failed ({result.returncode}): "
                               + (first[0][:160] if first else "no output"))
        return result.stdout

    def pair(self, request: dict) -> dict:
        scopes = [flag for scope in self.scopes for flag in ("--scope", scope)]
        out = first_json(self.run_t3(["auth", "pairing", "create", "--ttl", "2m", "--label", request["label"],
                                      "--base-url", self.origin, *scopes, "--json"]))
        url = out.get("pairUrl") if isinstance(out, dict) else None
        if not isinstance(url, str) or origin_of(url) != self.origin or not urlsplit(url).fragment.startswith("token="):
            raise RuntimeError("t3 did not return a pairing link for the configured origin")
        return {"pairing_url": url}

    def revoke_previous(self, request: dict) -> dict:
        sessions = first_json(self.run_t3(["auth", "session", "list", "--json"]))
        if not isinstance(sessions, list):
            raise RuntimeError("unexpected t3 session list")
        mine = [s for s in sessions if isinstance(s, dict)
                and s.get("method") == "browser-session-cookie"
                and isinstance(s.get("client"), dict) and s["client"].get("label") == request["label"]
                and isinstance(s.get("sessionId"), str) and isinstance(s.get("issuedAt"), str)]
        mine.sort(key=lambda s: s["issuedAt"])
        for session in mine[:-1]:
            self.run_t3(["auth", "session", "revoke", session["sessionId"]])
        return {"revoked": max(len(mine) - 1, 0)}

    @staticmethod
    def refusal(request: dict) -> str | None:
        if request.get("schema_version") != 1:
            return "unsupported request"
        if request.get("kind") not in KINDS:
            return "unknown request kind"
        if not isinstance(request.get("label"), str) or not LABEL.match(request["label"]):
            return "label must start with 'Wingman '"
        return None

    def _load_state(self) -> dict:
        path = self.state_dir / "state.json"
        try:
            return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {"processed": {}}
        except ValueError:
            return {"processed": {}}

    def process(self, now: float | None = None) -> int:
        now = time.time() if now is None else now
        if not self.folder.is_dir():
            return 0
        self.state_dir.mkdir(parents=True, exist_ok=True)
        state = self._load_state()
        processed = state.setdefault("processed", {})
        handled = 0
        for path in sorted(self.folder.glob("*.request.json")):
            rid = path.name[: -len(".request.json")]
            if not ID.match(rid) or rid in processed:
                continue
            try:
                request = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue  # half-written or unreadable; the next pass retries
            created = parse_time(request.get("created_at_utc")) if isinstance(request, dict) else None
            if created is None or now - created > STALE_SECONDS or created - now > 300:
                path.unlink(missing_ok=True)
                processed[rid] = {"at": now, "outcome": "stale"}
                self.log(f"{rid}: stale request dropped")
                continue
            if request.get("id") != rid:
                response = {"id": rid, "status": "error", "message": "id mismatch"}
                outcome = "refused: id mismatch"
            else:
                refusal = self.refusal(request)
                # No origin means a first pairing that does not know the address yet: the link tells it.
                if refusal is None and request.get("origin") is not None and origin_of(request["origin"]) != self.origin:
                    refusal = "origin is not served here"
                if refusal is not None:
                    response = {"id": rid, "status": "error", "message": refusal}
                    outcome = "refused: " + refusal
                else:
                    try:
                        extra = self.pair(request) if request["kind"] == "pair" else self.revoke_previous(request)
                        response = {"id": rid, "status": "ok", **extra}
                        outcome = "ok" + (f" revoked {extra['revoked']}" if "revoked" in extra else "")
                    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
                        response = {"id": rid, "status": "error", "message": str(error)[:200] or type(error).__name__}
                        outcome = "error: " + response["message"]
            write_json(self.folder / f"{rid}.response.json", response)
            processed[rid] = {"at": now, "kind": request.get("kind"), "outcome": outcome}
            self.log(f"{rid}: {request.get('kind')} from {str(request.get('instance', '?'))[:40]}: {outcome}")
            handled += 1
        for path in self.folder.glob("*.response.json"):
            try:
                if now - path.stat().st_mtime > RESPONSE_TTL_SECONDS:
                    path.unlink(missing_ok=True)
            except OSError:
                pass
        for rid in [k for k, v in processed.items() if now - float(v.get("at", 0)) > PROCESSED_KEEP_SECONDS]:
            del processed[rid]
        write_json(self.state_dir / "state.json", state)
        return handled


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Answer Wingman's T3 web pairing requests (docs/T3_WEB_PAIRING.md).")
    parser.add_argument("--folder", required=True, help="pairing folder Wingman writes requests into")
    parser.add_argument("--base-url", required=True, help="https origin of the T3 server, e.g. https://t3.example.net:8443")
    parser.add_argument("--state-dir", default=str(Path.home() / ".local" / "state" / "t3-pairing-bridge"),
                        help="where answered request ids are kept (outside the shared folder)")
    parser.add_argument("--t3", default="t3", help="path of the T3 server's CLI (default: t3 on PATH)")
    parser.add_argument("--scope", action="append", default=[],
                        help="pairing scope to grant (repeatable); default is T3's own default set")
    parser.add_argument("--watch", type=float, metavar="SECONDS", help="keep polling every SECONDS instead of one pass")
    args = parser.parse_args(argv)
    bridge = Bridge(Path(args.folder), args.base_url, Path(args.state_dir), [args.t3], tuple(args.scope))
    if args.watch is None:
        bridge.process()
        return 0
    interval = max(args.watch, 0.5)
    while True:
        bridge.process()
        time.sleep(interval)


if __name__ == "__main__":
    sys.exit(main())
