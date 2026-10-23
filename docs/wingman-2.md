# Wingman 2.0

Wingman is the user-facing name. Existing `CodexWingman` executable, settings, and Windows shell IDs remain compatibility identifiers during migration; a rename must migrate installed shortcuts and updater paths together. Wingman is a Windows tray host for several desktop apps. Each app owns its launcher, discovery, status, repair, and window-open implementation. The Helper package format and enable/remove lifecycle remain common; renderer scripts are app-specific.

## Tray contract

```text
Codex: OK (2/2) >
    Open a hooked window
    Repair Codex
    View status details...
    ──────────────────────
    [x] Open on launch
T3 Code: Problem (1/3) >
    Open a hooked window
    Repair T3 Code
    View status details...
    ──────────────────────
    [ ] Open on launch
T3 web: OK (1/1) >
    Open T3 web with Wingman
    View status details...
    Check T3 sign-in now
Grok Bot: OK (1/1) >
    Open Grok Bot with Wingman
    View status details...
    ──────────────────────
    [ ] Taskbar pin opens Grok Bot through Wingman
──────────────────────────
[x] Helpers enabled
Manage helpers >
──────────────────────────
About
Check for Updates
Close Wingman
```

The counts are *hooked visible windows / visible windows for that app*, never CDP endpoints or background pages. The T3 denominator comes from visible, titled, top-level windows belonging to recognized T3 executables in the current session; the numerator is capped at that count using only renderer connections that answer a CDP evaluation. A discovered page whose connection fails does not count. `OK` requires equal counts and current Helper reconciliation without failures. `Problem` covers partial coverage, failed Helpers, or a window that cannot be connected. A closed app displays `Closed (0/0)`. Unknown discovery displays `Checking` or `Status unavailable`, not `OK`. The detail view names the affected app and a concrete recovery action without exposing CDP IDs. Keep the menu rows stable during passive polling so a click cannot move under the pointer. The T3 renderer-to-native-window correspondence still requires a live Windows trial before the counts can be treated as verified coverage.

`Open on launch` is per app. Existing Codex startup behavior migrates to checked; T3 Code defaults unchecked until its launcher has passed a Windows trial. Starting Wingman opens checked apps only. Unchecking a target does not close an already open window. `Open a hooked window` opens or reuses that app's own window route and verifies a new or reused visible target; it does not merely open an ordinary unhooked window. `Repair` can propose a graceful target restart when needed, with an app-specific explanation, and must not stop the other target. The global Helpers switch removes or reapplies Wingman-owned UI in both apps while target discovery and status remain available.

## Host boundary

An `AppTarget` contract owns a stable app ID, process identity, loopback CDP endpoint, page URL predicate, open-window route, repair route, and health calculation. Codex's existing package-family checks and `app://-/index.html` predicate become its adapter. T3 Code needs its own executable identity and `t3code://app` page predicate; a Codex predicate must never select a T3 page. Ports and discovered targets are kept per app, not in one global `activePort`. A target's Helper Host uses only pages selected for that target.

The `wingman.json` manifest and apply/remove lifecycle stay recognizable. Schema v1 remains Codex-only. Schema v2 replaces top-level `entrypoints` with a `targets` object whose keys are app IDs (`codex`, `t3-code`) and whose values contain each app's `apply`, `remove`, and optional `backend` paths. A package may target one app or provide separate scripts for both. The catalog validates every declared script, and each Helper Host selects its own app's package projection and renderer pages before apply, Host Action drain, or cleanup. Codex session access and native new-window actions remain Codex-only. The tray owns a separate T3 Host and target source. T3 open and repair handlers use only a recognized T3 executable and its own loopback debugging port; restart first requests a graceful close of exact-path T3 processes in the current session, and fails closed if they remain. The T3 startup checkbox is separately persisted and defaults off. Without a verified T3 debugging endpoint, visible windows show `Problem (0/N)`. Launcher and coverage source tests do not establish a working installed or rendered T3 integration; keep that trial as an acceptance gate.

## T3 first proof

Before treating the T3 menu's repair or hook claims as verified, identify one installed T3 Code executable on Windows and launch one dedicated test window with a loopback CDP endpoint. Verify exact process/window ownership, the `t3code://app` renderer URL, injected Helper behavior, removal, reload, and survival after an ordinary upstream T3 update. Keep the owner's other T3 windows and foreground focus untouched. T3's remote server does not make the Windows renderer's local paths or Codex session files available; any Hook Trace or Turn Metadata port must prove an exact authorized server-side thread mapping separately.

The first T3 renderer loadout is **Usage dials**, **Obsidian links**, and **Clickable file links**. The T3 Usage dials adapter reads the exact selected provider from committed composer props and shows its session and weekly quota windows using the existing dial appearance. Ambiguous, stale, expired, or malformed data remains unavailable. Its source tests are development evidence; the real composer still requires live verification. Obsidian links scans only rendered timeline messages and uses the same validated Wingman action as Codex. Clickable file links adds an Open in Windows control beside T3's file chips without replacing their native click or context menu. Both were applied and removed on one hidden fixture in the live PC-1 Nightly renderer. The Codex scripts and settings behavior remain separate. The remaining bundled Helpers stay Codex-only: their data comes from Codex sessions, their UI behavior has no matching T3 surface, or T3 already owns the feature. A T3-specific replacement requires its own observed behavior and tests before enabling it.

| Helper | T3 decision |
| --- | --- |
| Obsidian links | Supported in timeline messages. |
| Clickable file links | Supported for file-link chips in timeline messages. |
| Agent derangement risk, Hook Trace, Turn Metadata | Require a verified T3 thread and data contract before accessing session data. |
| Turn render recovery | Codex render workaround; no matching T3 failure has been observed. |
| Sidebar on demand | Codex floating-panel behavior does not match T3's sidebar. |
| New chat window | Needs a T3-specific window action rather than Codex's native action. |
| Usage Dials | T3 source adapter implemented; real composer and quota display still require live verification. |
| JSON Debug | Codex-specific diagnostics; a T3 version needs its own evidence and design. |

## T3 web and Grok Bot targets

Two more app targets follow the same `AppTarget` contract, each with its own app ID, Helper Host, loopback port, page predicate and tray row (`T3 web: …`, `Grok Bot: …`). Both default to `Open on launch` off. Native Codex and native T3 desktop behavior is unchanged.

**T3 web (`t3-web`).** Wingman opens the T3 web client as a Chrome app window (`--app=<origin>`) with Wingman's own Chrome data folder (`%LOCALAPPDATA%\CodexWingman\t3-web\chrome-data`) and `--remote-debugging-address=127.0.0.1`. Pages are selected only on the exact https origin from local settings or a validated pairing link; no host name ships in the package. The windows get Wingman's `…T3Web` AppUserModelID and a Start shortcut `Wingman T3 Code (web)` is created when T3 web can work on the PC. Launching that shortcut while a T3 web window already exists brings that window to the front (restoring it if minimized) through Wingman's own Chrome DevTools, like one click on a pinned app; it never opens a second window. Usage dials, Obsidian links and Clickable file links run there with their T3 scripts, gated on `wingman.app === 't3-web'`; the dial imports T3's runtime module only from the same origin.

**T3 web sign-in.** T3 browser sessions end a fixed 30 days after pairing and are not replaced automatically. Wingman pairs a signed-out window and renews three days before the end with a one-time, two-minute pairing link from a pluggable provider, exchanged from inside the page exactly as T3's `/pair` page does, then asks the host to revoke the previous Wingman session with the same label. The shipped provider drops a request file in a folder the T3 host answers (found from the the sync share receipt, or set as `t3Web.pairing.folder`). Wingman never reads, decrypts or copies browser cookies and stores no credential. The expiry Wingman acts on is the one the page reports (`/api/auth/session`), with the remembered expiry only as a fallback. Development packages (with `development.json`, never stable releases) accept `CodexWingman.exe --t3-web-renew-now` to run that renewal immediately and `--close` to close the running Wingman like its tray Close item; both only signal an already running development Wingman and never start one.

**Grok Bot (`grok-bot`).** Wingman relaunches Grok Bot once, after asking, with only `--remote-debugging-address=127.0.0.1 --remote-debugging-port=<p>`; Grok Bot keeps its own user data and sign-in. A running Grok Bot is closed gracefully and never killed. Wingman changes no Grok Bot files. The composer dial reads Grok Bot's live in-app usage store (the value its account menu shows) and redraws when it changes. While Grok Bot has focus or the dial's popup is open, and at once when the Grok Bot window gains focus or becomes visible, it asks Grok Bot's own throttled `refresh()` when the reading is over 5 minutes old, at most every 5 minutes. Grok Bot's local weekly-usage cache (read-only) is only a fallback and is labelled as a saved, not-live reading; with neither, or a stale (over 24 hours), expired or malformed cache, the dial shows `?`. Wingman itself makes no network calls to Grok Bot's servers and holds no Grok Bot credentials. An existing Grok Bot taskbar pin can be pointed at Wingman (`--launch=grok-bot`) from the tray as an opt-in step, off by default, with a backup and a restore item; Wingman never adds pins.

### Decisions

- 2026-10-22: T3 web uses Wingman's own Chrome data folder. This is deliberate and applies to T3 web only; the Codex rule to keep the normal-profile hook-ready design and not create a disposable profile is unchanged. Chrome refuses a debugging port on its default data folder, and copying the user's Chrome cookies is not allowed. Do not "fix" this back to the normal Chrome profile.
- 2026-10-22: T3 web sign-in is renewed by one-time pairing links before the fixed 30-day expiry, never by cookie copying; the previous Wingman session is revoked by label after a renewal.
- 2026-10-22: Grok Bot gets a relaunch with a localhost-only port and a composer dial; no patching, no extension, no floating window, no server calls of Wingman's own. Pins are never forced; retargeting the existing Grok Bot pin is opt-in with a backup. Autostart stays off.
- 2026-10-23: the Grok Bot dial reads Grok Bot's live in-app store (`weeklyUsage`, what its account menu shows), with the saved cache only as a labelled fallback, and nudges Grok Bot's own throttled `refresh()` only while its window has focus or the dial popup is open (lead decision under the owner's earlier decision on technical tradeoffs). From `native-slot-v32` it also asks at once when the window gains focus or becomes visible (focus / visibilitychange), so a glance shows a current number; the 5-minute cap is unchanged. Settings > Usage uses a separate store that can lag by a few points; the dial follows the account menu.

These targets are source- and fixture-tested only until a Windows trial shows the real windows grouping under their identities, Grok Bot keeping its sign-in after the relaunch, the dial in the real Grok Bot and T3 web composers, and pairing end to end.

## Acceptance

The acceptance bar is visible, working composer dials, file links, and Obsidian links on real T3 messages. About version and a healthy tray row are insufficient. Hidden fixtures and fake action launchers establish development stages only.

Automated tests cover each app's discovery isolation, menu counts, repair scope, Helper selection, and global toggle. A Windows trial then verifies two Codex windows plus two T3 windows, one deliberately unhooked window in each app, the per-app submenu wording and counts, startup checkbox persistence, repair that touches only the chosen app, and cleanup on Wingman exit. Report source/package evidence separately from installed and rendered behavior.

The release and update path is specified in [Codex app release contract](codex-app-release-contract.md).

### Live T3 composer compatibility

The Lexical editable DOM node has no React fiber. Usage Dials resolves the nearest React-owned DOM ancestor within the same main composer surface, then validates the committed root and exact selected provider before reading quota. The regression fixture preserves that observed separation.
