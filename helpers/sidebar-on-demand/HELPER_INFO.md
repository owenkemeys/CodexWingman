# Sidebar on Demand

- Helper generation: v1
- Manifest version: 1.1.0
- Helper ID: `sidebar-on-demand`

## Purpose and behavior

This renderer-only helper gates Codex's live floating sidebar so passive edge hover cannot show it. Deliberate primary-pointer, click, Enter, and Space actions on Codex's semantic sidebar trigger remain available. A deliberate action grants an immediate two-animation-frame exemption: if the floating panel appears, the exemption lasts until it closes; if Codex opens only its docked sidebar, the gate re-arms after the second frame. The gate also returns when the window blurs or the pointer leaves the window. No Host capabilities or periodic refresh are used.

## Codex contracts

The helper depends on `[data-app-shell-sidebar-trigger="true"]` for deliberate actions and `[data-pip-obstacle="app-shell-floating-left-panel"]` for the floating panel. If Codex changes either attribute, update the selectors in `apply.js`.

## Sharp edges and failure behavior

If Codex changes either semantic attribute, the helper fails closed: it leaves the native sidebar behavior unchanged rather than guessing from screen position. Deliberate actions are recognized only on the primary pointer or the documented keyboard keys. A window blur or pointer exit clears any temporary exemption so passive hover does not remain enabled.

## Disable or restore

Uncheck the Helper under `Manage helpers` to remove its stylesheet, listeners, observer, markers, and explicit-intent state. Ordinary sidebar toggle and close controls are not intercepted. A complete personal package with the same `sidebar-on-demand` ID can override this bundled copy.

## Personal copy and updates

Copy the complete Helper folder before customizing it. Keep the stable Helper ID only when the personal copy is intended to override the bundled package. After changing files, use `Reload helpers` and verify passive edge hover, deliberate pointer actions, keyboard actions, window blur, and cleanup in a scratch Codex window.
