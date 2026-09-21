# JSON UI debug

- Helper generation: v1
- Manifest version: 1.0.0
- Helper ID: `json-debug`
- Default state: disabled

## Purpose and behavior

This developer aid displays a fixed, renderer-local diagnostics panel. It checks semantic Codex UI surfaces, reports FOUND or MISSING counts, outlines matching elements, and records the page URL, title, apply count, refresh count, and last refresh time. It refreshes after relevant DOM changes and on a two-second interval. It does not request Host capabilities or send actions.

## Files and entrypoints

- `wingman.json`: package metadata, default state, cadence, and entrypoints.
- `apply.js`: creates the overlay, observes the page, refreshes canaries, and restores changed outlines during cleanup.
- `remove.js`: removes the overlay, style, outlines, observer, timer, and renderer global.

## Customization

Edit the canary list, selectors, colors, or panel styles in `apply.js`. Keep selectors semantic and keep all created nodes and styles marked as helper-owned so cleanup remains safe.

## Sharp edges and failure behavior

This helper is tied to Codex's current DOM and may report MISSING after a Codex UI change. Broad selectors can outline unrelated elements or make the page noisy. Invalid selectors are treated as no matches. Each renderer has its own overlay and controller. The Host isolates script errors and reports the helper diagnostic instead of granting extra access.

## Personal copy and updates

This is a standard bundled Helper. `Open Helpers folder` only opens the personal package directory; it does not copy this Helper there. To customize it, install a complete personal copy with the same `json-debug` ID. Wingman never changes personal package contents.

## Disable or restore

Uncheck the Helper under `Manage helpers` to remove its UI. Removing a personal override and choosing `Reload helpers` returns to the bundled copy.
