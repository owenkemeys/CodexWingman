# CodexWingman

CodexWingman is a Windows tray companion for the official Codex desktop app, to add small quality-of-life improvements by modifying the UI in realtime. It loads small, readable Helper packages and applies them to every connected Codex window. Wingman does not replace Codex or modify its chat history.

Start here: [Agent setup and customization](docs/AGENT_SETUP.md). Give this repository link to your agent and ask it to install Wingman, configure local integrations, and report unresolved choices. Windows only.

**Launch Codex through the Codex Wingman shortcut.** Wingman automatically launches Codex with the connection its helpers need. Installing agents must create that shortcut, launch it, verify that Codex opens through Wingman, and tell the user to use it for normal launches. Installing Wingman without launching it does not activate the helpers.

> Unofficial community project. CodexWingman is not affiliated with, endorsed by, or sponsored by OpenAI. OpenAI, ChatGPT, and Codex are trademarks of OpenAI.

MIT licensed. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).


## Bundled Helpers

- Usage dials adds five-hour and weekly account-usage rings beside native context-window dials. Its independent popup shows 76px ring previews, usage, remaining allowance, elapsed time, and reset information; the context-window tooltip stays native.
- Agent Derangement Risk adds a display-only ring after Usage dials that estimates context degradation from current tokens, compactions, and cumulative thread history.
- Sidebar on Demand stops the floating sidebar from opening when the pointer merely crosses the left edge. Intentional sidebar controls still work.
- New chat in new window adds that command to the context menu of verified New Chat controls and uses Codex's native window-opening route.
- Clickable file links opens local Windows file references in their registered applications. Remote path mappings are optional and empty by default.
- Obsidian links opens configured local vault notes using standard Obsidian links. The setup tool discovers local vaults and reports missing configuration.
- Turn Metadata shows available turn timing and metadata.
- Turn Render Recovery handles supported render stalls without changing stored chat history.
- Hook Trace adds independent Hook controls to user and response action trays. Historical turns expose every saved developer/system message in full as `Added model context`, including context recorded before the visible user message. Verified live hook runs additionally show configuration source, outcome, diagnostics, and silent runs. Codex does not retain enough identity to place past silent executions after a restart. Empty controls remain hoverable but cannot be toggled.
- JSON UI debug adds a renderer-local canary legend and colored outlines for composer, model, context, permissions, thread, popover, and window-action surfaces.

The checked `Helpers enabled` item is the master injection switch: uncheck it to remove Wingman-owned UI, and check it again to refresh or reinject. Individual Helpers can be enabled or disabled from the `Manage helpers` submenu. `Reload helpers` rescans package folders without restarting Wingman. Exit cleans up before Wingman closes.

Use the Codex Wingman shortcut whenever you want to launch Codex through Codex Wingman. The first launch starts Wingman and a Helper-ready Codex process. Launching the same shortcut again asks the running Wingman instance to open Codex, repair it, or open another native window as needed.

`Repair Codex and Helpers...` is always available. It checks the current Codex windows and reapplies Helpers without restarting Wingman. If Codex is already running without a usable connection, Wingman asks before gracefully closing and relaunching Codex with the normal profile; cancelling leaves every window untouched. `Open another Codex window` remains a separate action.

The tray begins with two native text rows: a health label such as `Status: OK`, followed by coverage such as `Altering 2 of 4 windows`. The first number is the usable Codex windows receiving Helpers; the second is the total visible Codex windows. Other states include `Status: Needs attention`, `Status: Needs repair`, `Status: Codex closed`, and `Status: Helpers paused`. `View status details...` shows the current plain-language diagnostic and next action.

The injected controls use semantic attributes and Codex's existing layout rather than screen coordinates. Wingman reconciles rerenders, side chats, and multiple windows.

## Requirements

- Windows with the .NET 9 Desktop Runtime.
- The official Codex desktop app.
- A normal signed-in Codex installation. Wingman enables localhost debugging only when it launches or restarts Codex.

After confirming a repair restart, Wingman closes Codex through its normal window-close path, waits for the entire Codex process set to exit, selects the first available localhost port starting at `9223`, and launches the packaged Codex app with:

```text
--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223
```

The port shown above is illustrative; it is not a fixed requirement. If `9223` is occupied or stale, Wingman advances to the next available port. The active port is recorded in `%LOCALAPPDATA%\CodexWingman\runtime.json` so a later Wingman launch can reconnect without guessing. The debugging endpoint is loopback-only. Wingman does not expose it to the network.

If Codex is already running without a usable endpoint, Wingman leaves it alone and reports that the restart action is required. It never force-kills an ambiguous Codex process tree. An explicit `--cdp-port=<port>` argument is available for managed or diagnostic launches; ordinary users do not need it.

## Install and run

Build an exact package into `dist\CodexWingman-verified`:

```powershell
.\scripts\Publish-CodexWingman.ps1
```

The publisher builds in a new sibling staging folder, verifies the staged package, then replaces the previous package. This prevents removed or renamed bundled Helpers from surviving an update. If the replacement cannot complete, the previous package is restored.

Run `dist\CodexWingman-verified\CodexWingman.exe` when using the stable published package, or add the published build to your per-user Start Menu:

```powershell
.\scripts\Install-CodexWingmanStartMenu.ps1
```

The installer creates `Codex Wingman.lnk` in the current user's Start Menu. This is the primary Codex launcher while using Wingman. It does not install a service or request administrator access.
When `dist\CodexWingman-verified` exists, the installer selects it automatically; use `-PackageDirectory` to choose another published folder explicitly.

## Helper packages

All Helpers live in `Helpers` beside `CodexWingman.exe`. This is the only folder Wingman scans, and `Open Helpers folder` opens that exact directory without changing its contents. The user owns every folder and file placed there.

Helpers are plain JavaScript, JSON, and Markdown. Each bundled package includes `HELPER_INFO.md` with its purpose, files, capabilities, customization guidance, and sharp edges. Wingman treats installed Helpers as trusted code. Read a Helper before installing it. See [Helper authoring](docs/helper-authoring.md) for the package format and the capabilities available to scripts.

The tray menu includes an `About` command. Its dialog shows `CodexWingman v<installed version>`, taken from the executable version set once in the project file.

Settings live under `%LOCALAPPDATA%\CodexWingman\settings.json`. On first launch, Wingman imports an existing `%LOCALAPPDATA%\CodexHelper\settings.json` without deleting the old file. Optional remote or shared session roots are configuration, never compiled defaults. See [configuration](docs/configuration.md).

## Build and verification

Use the exact-source verification and installation steps in [Agent setup](docs/AGENT_SETUP.md). The agent owns dependency checks, build, backup, launch and live verification. Settings and vault paths belong to the current user.

This is an unofficial, unsigned community tool. Helpers are trusted local code; the loopback debugging endpoint provides access to the signed-in app. Do not expose it to a network or install untrusted helpers. Codex updates can change internal UI contracts; disable a failing helper and inspect status rather than assuming compatibility.
