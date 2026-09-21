# New chat in new window

- Helper generation: v1
- Manifest version: 1.0.0
- Helper ID: `new-chat-window`

## Purpose and behavior

This helper adds right-click behavior to verified native New Chat and New Task controls. It asks the Host to open one correlated Codex child window, then uses the matching native control in that child so project-specific context is preserved. Generic controls are distinguished by their UI shape and clear any remembered project selection. Ordinary left-click behavior remains native.

## Files and entrypoints

- `wingman.json`: package metadata, refresh cadence, entrypoints, and the `codex.openNewChatWindow` capability declaration.
- `apply.js`: finds and marks supported controls, handles right-click, requests the child window, and consumes one-shot child bootstrap data.
- `remove.js`: removes listeners, ownership markers, old menu/style remnants, and the renderer controller.

## Capabilities

`codex.openNewChatWindow` permits the declared Host action used by the child-window workflow. The renderer cannot choose or inspect CDP target identities.

## Customization

Control recognition and label rules live in `apply.js`. Keep matching narrow, require one unambiguous native control, retain exact-child correlation, and keep project-specific completion acknowledgement if adding another Codex control shape.

## Sharp edges and failure behavior

The helper depends on current Codex labels, glyphs, and DOM structure. Unknown controls are ignored. Ambiguous project-specific matches fail closed; the Host may close the correlated child after failure or timeout. Generic children intentionally fail open after correlation because clearing Codex's remembered project rebuilds the composer before a reliable acknowledgement can survive. If Codex is not hook-ready, the Host cannot create the child and reports the failure.

## Personal copy and updates

This is a standard bundled Helper. `Open Helpers folder` only opens the personal package directory; it does not copy this Helper there. To customize it, install a complete personal copy with the same `new-chat-window` ID. Wingman never changes personal package contents.

## Disable or restore

Uncheck the Helper under `Manage helpers` to remove its listeners and markers. Check it again to restore it. Removing a personal override and choosing `Reload helpers` returns to the bundled copy.
