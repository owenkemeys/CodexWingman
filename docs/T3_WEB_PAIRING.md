# T3 web pairing

Wingman's T3 Code (web) window signs in with T3's own one-time pairing links, never by copying browser cookies. T3 browser sessions end a fixed 30 days after pairing, so Wingman pairs a signed-out window and renews about three days before the end. It gets each link from a **pairing folder**: Wingman writes a request file there, and a small bridge on the T3 host answers it. This page documents the file format and the reference bridge.

Without a pairing folder, T3 web still works: sign in inside its window with a pairing link from your T3 server, and repeat that when the session ends.

## Setup

1. Pick a folder both machines can reach, for example a share on the T3 host mapped on the PC. Only the PC user and the bridge should be able to write it: a response holds a sign-in link for up to two minutes.
2. On the T3 host, run the reference bridge (Python 3.10+, the `t3` CLI of the running T3 server on `PATH` or given with `--t3`):

   ```text
   python tools/t3_pairing_bridge.py --folder /srv/share/t3-pairing --base-url https://t3.example.net:8443 --watch 2
   ```

   `--watch N` polls every N seconds; without it the bridge processes the folder once and exits, which suits a file-watch trigger such as a systemd path unit. State (answered request ids) goes to `--state-dir`, by default `~/.local/state/t3-pairing-bridge`, outside the shared folder.
3. On the PC, point Wingman at the same folder: `python tools/configure_local.py --t3-web-url https://t3.example.net:8443 --t3-pairing-folder "\\server\share\t3-pairing" --apply`, or set `t3Web.pairing.folder` in `%LOCALAPPDATA%\CodexWingman\settings.json`.

An explicit `t3Web.pairing.folder` always wins. Wingman can also find the folder from one optional sync tool's receipt on the PC; that lookup does nothing when the receipt does not exist.

## Request file

Wingman writes `.<id>.request.tmp` and renames it to `<id>.request.json`, so a reader never sees a half-written request. `<id>` is 32 lowercase hex characters.

```json
{
  "schema_version": 1,
  "kind": "pair",
  "id": "0f1e2d3c4b5a69788796a5b4c3d2e1f0",
  "instance": "Wingman T3 web (MY-PC)",
  "label": "Wingman T3 web (MY-PC)",
  "reason": "signed-out",
  "origin": "https://t3.example.net:8443",
  "created_at_utc": "2026-10-28T10:00:00Z"
}
```

- `kind` is `pair` (create a one-time pairing link) or `revoke-previous` (after a renewal, revoke older sessions with the same label).
- `label` always starts with `Wingman `; it names the PC only, so older Wingman sessions can be found and revoked.
- `origin` is the T3 web origin Wingman knows, or `null` on a first pairing; the bridge then answers for its configured origin.
- `reason` is informational (`signed-out`, `renewal`, `renewed`, …).

## Response file

The bridge writes `<id>.response.json` (also via a temporary name and rename):

```json
{ "id": "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "status": "ok", "pairing_url": "https://t3.example.net:8443/pair#token=…" }
```

- `status` is `ok` or `error`; an error carries a short `message`, which Wingman shows in Status details.
- `pair` answers carry `pairing_url`: an https link on the same origin, path `/pair`, fragment `token=…`, valid for two minutes and single-use. Wingman exchanges it inside its own window exactly like T3's `/pair` page does.
- `revoke-previous` answers may carry `revoked` (a count).
- `id` must equal the request id, or Wingman rejects the answer.

Wingman waits up to 150 seconds, then deletes the request and the response as soon as it has read the answer. The reference bridge answers each id once, drops requests older than ten minutes, refuses labels that do not start with `Wingman ` and origins other than its configured one, and deletes unread responses after five minutes.

## Reference bridge

`tools/t3_pairing_bridge.py` uses the T3 server's own CLI:

- `pair`: `t3 auth pairing create --ttl 2m --label <label> --base-url <origin> --json`, plus any `--scope` values you pass to the bridge (by default T3's own default scopes).
- `revoke-previous`: `t3 auth session list --json`, then `t3 auth session revoke <id>` for every browser session with exactly that label except the newest. Sessions whose label does not start with `Wingman ` are never touched.

It prints one line per request and never prints a link or token. Adapt it freely; Wingman depends only on the file format above.
