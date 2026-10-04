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

The counts are *hooked visible windows / visible windows for that app*, never CDP endpoints or background pages. `OK` requires all visible windows hooked and current Helper reconciliation without failures. `Problem` covers partial coverage, failed Helpers, or a window that cannot be connected. A closed app displays `Closed (0/0)`. Unknown discovery displays `Checking` or `Status unavailable`, not `OK`. The detail view names the affected app and a concrete recovery action without exposing CDP IDs. Keep the menu rows stable during passive polling so a click cannot move under the pointer.

`Open on launch` is per app. Existing Codex startup behavior migrates to checked; T3 Code defaults unchecked until its launcher has passed a Windows trial. Starting Wingman opens checked apps only. Unchecking a target does not close an already open window. `Open a hooked window` opens or reuses that app's own window route and verifies a new or reused visible target; it does not merely open an ordinary unhooked window. `Repair` can propose a graceful target restart when needed, with an app-specific explanation, and must not stop the other target. The global Helpers switch removes or reapplies Wingman-owned UI in both apps while target discovery and status remain available.

## Host boundary

An `AppTarget` contract owns a stable app ID, process identity, loopback CDP endpoint, page URL predicate, open-window route, repair route, and health calculation. Codex's existing package-family checks and `app://-/index.html` predicate become its adapter. T3 Code needs its own executable identity and `t3code://app` page predicate; a Codex predicate must never select a T3 page. Ports and discovered targets are kept per app, not in one global `activePort`. A target's Helper Host uses only pages selected for that target.

The `wingman.json` manifest and apply/remove lifecycle stay recognizable. A future manifest revision identifies supported apps and maps each to its own `apply.js` and `remove.js`; the default for existing packages is Codex only. T3 scripts cannot be inferred from Codex scripts. Host Actions remain capability-gated and target-scoped. An app-specific Helper failure affects only that app's health and never silently disables the other.

## T3 first proof

Before enabling the T3 menu's repair or hook claims, identify one installed stable T3 Code executable on Windows and launch one dedicated test window with a loopback CDP endpoint. Verify exact process/window ownership, the `t3code://app` renderer URL, injected Obsidian Links behavior, removal, reload, and survival after an ordinary upstream T3 update. Keep the owner's other T3 windows and foreground focus untouched. T3's remote server does not make the Windows renderer's local paths or Codex session files available; any Hook Trace or Turn Metadata port must prove an exact authorized server-side thread mapping separately.

## Acceptance

Automated tests cover each app's discovery isolation, menu counts, repair scope, Helper selection, and global toggle. A Windows trial then verifies two Codex windows plus two T3 windows, one deliberately unhooked window in each app, the per-app submenu wording and counts, startup checkbox persistence, repair that touches only the chosen app, and cleanup on Wingman exit. Report source/package evidence separately from installed and rendered behavior.

The release and update path is specified in [Codex app release contract](codex-app-release-contract.md).
