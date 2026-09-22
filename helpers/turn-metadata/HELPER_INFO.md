# Turn Metadata

- Helper generation: v8
- Manifest version: 1.0.7
- Helper ID: `turn-metadata`

## Purpose and behavior

Turn Metadata adds a small model label to the action toolbar of each completed assistant turn. Hover, keyboard focus, or tap opens a floating panel with the generation details recorded for that exact turn.

The Helper associates records only through Codex's exact turn ID. It uses `files.codexTargetSession` to receive structured metadata for the conversation visible in each renderer. It does not receive raw session text, activate providers, send messages, or change conversation history.

## Files and entrypoints

- `wingman.json`: package identity, refresh cadence, and the target-scoped capability.
- `backend.js`: requests structured metadata for the visible conversation.
- `apply.js`: owns toolbar triggers, panels, styles, interaction, and bounded reconciliation.
- `remove.js`: removes only Turn Metadata ownership and runtime state.

## Sharp edges and failure behavior

The renderer depends on Codex's semantic `data-turn-key`, final-assistant marker, response annotation, and native Copy action. Current virtualized history uses display keys, so the helper reads the committed React entry bound to that exact row and verifies its conversation and turn IDs against the session record. It does not infer identity from text, ordering, timestamps, alternate fibers, or conversation arrays. It recognizes both current and earlier native response action labels. If any exact identity or anchor is absent, the Helper shows nothing for that turn. Missing historical fields remain absent and produce a partial-data notice. Malformed or unavailable session data fails closed.

Disable the Helper or choose `Reload helpers` to run its cleanup. Cleanup leaves Codex's native response actions, tooltips, messages, and conversation state unchanged.

## Personal copy and updates

Copy the complete Helper folder before customizing it. Keep the stable Helper ID only when the personal copy is intended to override the bundled package. After changing files, use `Reload helpers` and verify exact turn ownership, current and historical conversations, panel bounds, keyboard dismissal, and cleanup in a scratch Codex window.
