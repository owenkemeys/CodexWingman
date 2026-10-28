# Configuration

For an agent-managed installation without shared infrastructure, start with [Agent setup](AGENT_SETUP.md).

## Personal helper configuration

`helperConfig` overrides a helper's entire manifest `config` object by helper ID. It is kept outside the sealed installation and survives package updates and tray toggles. Omit a helper ID to retain its bundled configuration; an empty object explicitly clears it. Reload helpers after changing settings.

```json
{
  "helperConfig": {
    "obsidian-links": {
      "mappings": [{ "sourcePrefix": "C:/Notes/My Notes/", "vault": "My Notes" }],
      "uriAction": "open"
    },
    "clickable-file-links": { "mappings": [], "excludePrefixes": [] }
  }
}
```

Merge these fields into existing settings; do not replace unrelated preferences. `python tools/configure_local.py` reports detected vaults and missing customization. Add `--apply` to save with backup. The public distribution starts with no remote paths or vault mappings; Obsidian integration requires this setup or explicit local configuration. Standard `open` needs no community plugin. `wait-for-note` is only for users who have separately installed that plugin.

## General settings

CodexWingman stores per-user settings at:

```text
%LOCALAPPDATA%\CodexWingman\settings.json
```

Missing or malformed settings fall back to safe defaults: usage dials enabled and no additional session roots.

```json
{
  "usageDialsEnabled": true,
  "forceHighPerformanceGpu": false,
  "helperEnabled": {
    "usage-dials": true,
    "sidebar-on-demand": true,
    "new-chat-window": true
  },
  "additionalSessionRoots": [
    "\\\\server\\shared-codex\\sessions",
    "\\\\server\\shared-codex\\archived_sessions"
  ]
}
```

Set `forceHighPerformanceGpu` to `true` on a hybrid-graphics Windows machine to add Chromium's high-performance-GPU override whenever Wingman launches Codex. It is disabled by default so other installations retain their existing graphics behavior.

Environment variables in additional roots are expanded when the helper starts. Duplicate roots are ignored. The helper always includes the current user's standard local roots:

```text
%USERPROFILE%\.codex\sessions
%USERPROFILE%\.codex\archived_sessions
```

On first launch, CodexWingman imports an existing `%LOCALAPPDATA%\CodexHelper\settings.json` only when the new settings file does not yet exist. It leaves the legacy file untouched.

The tray menu persists every Helper's checked state in `helperEnabled`. `usageDialsEnabled` remains for compatibility with older settings. Edit the JSON only to add or remove nonstandard session stores, then restart CodexWingman.

## T3 web and Grok Bot

T3 web needs the https address of your T3 server and, for automatic sign-in and renewal, a pairing folder that a small bridge on the T3 host answers (request/response format and the reference bridge `tools/t3_pairing_bridge.py` are in [T3 web pairing](T3_WEB_PAIRING.md)). Set them with `python tools/configure_local.py --t3-web-url https://host:port --t3-pairing-folder <folder> --apply`, or in settings:

```json
{
  "t3Web": {
    "url": "https://t3.example.net:8443",
    "pairing": { "provider": "shared-folder", "folder": "\\\\server\\share\\t3-pairing" },
    "openOnLaunch": false
  },
  "openGrokBotOnLaunch": false
}
```

`t3Web.chromePath` overrides Chrome discovery. `t3Web.pairing.label` overrides the session label (default `Wingman T3 web (<PC name>)`). Only https addresses are accepted. Without a pairing folder, T3 web still opens; sign in inside its window with a pairing link from your T3 server, and repeat that when the 30-day session ends. Wingman's T3 web Chrome data stays in `%LOCALAPPDATA%\CodexWingman\t3-web\chrome-data` and its pairing record in `t3-web\pairing.json` (expiry only, no credential).

Grok Bot needs no settings. Wingman uses the standard install (`%LOCALAPPDATA%\Programs\Grok Bot\Grok Bot.exe`) and reads only its local weekly-usage cache under `%APPDATA%\Grok Bot\sand-client-persistence`.

