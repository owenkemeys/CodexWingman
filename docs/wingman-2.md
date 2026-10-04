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

The first T3 renderer loadout is **Obsidian links** and **Clickable file links**. Obsidian links scans only rendered timeline messages and uses the same validated Wingman action as Codex. Clickable file links adds an Open in Windows control beside T3's file chips without replacing their native click or context menu. Both were applied and removed on one hidden fixture in the live PersonalOS Nightly renderer. The Codex scripts and settings behavior remain separate. The other bundled Helpers stay Codex-only: their data comes from Codex sessions, their UI behavior has no matching T3 surface, or T3 already owns the feature. A T3-specific replacement requires its own observed behavior and tests before enabling it.

| Helper | T3 decision |
| --- | --- |
| Obsidian links | Supported in timeline messages. |
| Clickable file links | Supported for file-link chips in timeline messages. |
| Agent derangement risk, Hook Trace, Turn Metadata | Require a verified T3 thread and data contract before accessing session data. |
| Turn render recovery | Codex render workaround; no matching T3 failure has been observed. |
| Sidebar on demand | Codex floating-panel behavior does not match T3's sidebar. |
| New chat window | Needs a T3-specific window action rather than Codex's native action. |
| Usage Dials | Requires T3 usage data and a T3-specific display. |
| JSON Debug | Codex-specific diagnostics; a T3 version needs its own evidence and design. |

## Acceptance

Automated tests cover each app's discovery isolation, menu counts, repair scope, Helper selection, and global toggle. A Windows trial then verifies two Codex windows plus two T3 windows, one deliberately unhooked window in each app, the per-app submenu wording and counts, startup checkbox persistence, repair that touches only the chosen app, and cleanup on Wingman exit. Report source/package evidence separately from installed and rendered behavior.

The release and update path is specified in [Codex app release contract](codex-app-release-contract.md).
