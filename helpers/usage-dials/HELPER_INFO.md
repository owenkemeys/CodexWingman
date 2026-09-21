# Usage dials

- Helper generation: native-slot-v15
- Manifest version: 1.3.2
- Helper ID: `usage-dials`

## Purpose and behavior

This helper shows separate 5-hour and weekly account-usage dials beside Codex's context control. It reads the signed-in account's native rate-limit cache and classifies limits by their duration, including accounts with only a weekly window. Each dial compares usage with elapsed time while retaining a complete background ring: native context-dial grey shows usage within pace, a middle-grey outline with the background showing through marks elapsed capacity not yet used, and usage ahead of pace is orange until usage reaches 1.5 times elapsed time, when it becomes red. At 95% usage the dial becomes fully red. The renderer uses Codex's native context slot when available, or creates a small temporary slot beside the model control until the native slot appears. Hover or keyboard focus exposes usage, time elapsed, reset details, and available reset credits.

## Files and entrypoints

- `wingman.json`: package metadata, 60-second refresh cadence, entrypoints, and capability declaration.
- `apply.js`: subscribes to native account state, normalizes current quota windows, and manages the dials and tooltip content in each renderer.
- `remove.js`: removes owned dials, styles, slots, tooltip sections, and the renderer controller.

## Capabilities

No Host capabilities or session backend are required. The helper reads Codex's existing in-memory account cache without filesystem access or additional network requests. Shared-session latency cannot block its activation.

## Customization

Change account parsing, warning rules, placement, labels, colors, or tooltip presentation in `apply.js`, and keep the embedded renderer synchronized. Preserve duration-based mapping: 300 minutes is the 5-hour window and 10080 minutes is the weekly window. Keep missing windows unavailable instead of copying the other value into their place.

## Sharp edges and failure behavior

Missing, malformed, expired, or failed native account data produces unavailable state rather than a guessed value. Partial input leaves only the missing window unavailable. The legacy embedded renderer still accepts explicit snapshot input when no native account data exists. Codex DOM changes can prevent placement; the helper then waits for a verified native or fallback anchor. It replaces stale renderer controllers by version and removes its temporary slot when Codex creates the native one. Native usage updates refresh the dials even when the reset-credit count stays unchanged.

## Personal copy and updates

Copy the complete Helper folder before customizing it. Keep the stable Helper ID only when the personal copy is intended to override the bundled package. After changing files, use `Reload helpers` and verify five-hour and weekly mapping, missing streams, tooltip content, warning colors, native-slot takeover, and cleanup in a scratch Codex window.

## Available rate-limit resets

The hover and keyboard-focus pop-ups show the signed-in account's available reset-credit count from Codex's native account query cache. The count follows account-cache updates without extra network polling and is distinct from credits applicable right now. Missing, failed, or malformed account data displays unavailable, while a confirmed zero displays 0. Disabling the helper removes its cache subscription. This compatibility adapter depends on Codex's React query provider and fails closed when that interface changes.

Usage hover is independent of the native context-window tooltip. Each usage dial opens its own 76px preview and window details, followed by available resets. Five-hour usage appears to the left of weekly usage when the account reports both. The shared reset-credit balance is labelled when both windows exist. The popup retains its 2px hollow outline; the miniature retains the original filled-circle segment rendering from before the outline changes.

Available reset credits are separated by a divider and shown count-first, for example "2 resets available". Unknown balances display "Resets unavailable".

Plan names never create or suppress a quota window. Use the durations and reset timestamps reported by the native account cache, including weekly-only and five-hour-only accounts. Reset credits are account-level and distinct from each window's scheduled reset time. See `docs/USAGE_LIMITS.md` for the dated policy research and its limitations.
