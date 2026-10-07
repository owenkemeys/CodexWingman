# Usage dials

- Helper generation: native-slot-v26
- Manifest version: 1.5.5
- Helper ID: `usage-dials`

## Purpose and behavior

This helper shows separate 5-hour and weekly account-usage dials beside Codex's context control. It reads the signed-in account's native rate-limit cache and classifies limits by their duration, including accounts with only a weekly window. Each dial compares usage with elapsed time while retaining a complete neutral background ring: native context-dial grey shows usage within pace, a middle-grey outline with the background showing through marks elapsed capacity not yet used, and usage ahead of pace is orange until usage reaches 1.5 times elapsed time, when it becomes red. At 95% usage the excess arc is red; usage within elapsed time stays neutral. For 98% usage with 60% elapsed, 0–60% is neutral and 60–98% is red in both the composer and hover preview. Without an elapsed-time estimate, the used arc stays neutral rather than inventing overuse. The renderer uses Codex's native context slot when available, or creates a small temporary slot beside the model control until the native slot appears. Hover or keyboard focus exposes usage, time elapsed, reset details, and available reset credits.

## Files and entrypoints

- `wingman.json`: package metadata, 60-second refresh cadence, entrypoints, and capability declaration.
- `apply.js`: subscribes to native account state, normalizes current quota windows, and manages the dials and tooltip content in each renderer.
- `remove.js`: removes owned dials, styles, slots, tooltip sections, and the renderer controller.

## Capabilities

Codex uses its existing in-memory account cache without a backend. The optional T3 backend declares `files.providerUsage` to read one explicitly configured, typed quota snapshot. It has no access to credentials or session files.

## Customization

Change account parsing, warning rules, placement, labels, colors, or tooltip presentation in `apply.js`, and keep the embedded renderer synchronized. Preserve duration-based mapping: 300 minutes is the 5-hour window and 10080 minutes is the weekly window. Keep missing windows unavailable instead of copying the other value into their place.

## Sharp edges and failure behavior

Missing, malformed, expired, or failed native account data produces unavailable state rather than a guessed value. Partial input leaves only the missing window unavailable. The legacy embedded renderer still accepts explicit snapshot input when no native account data exists. Codex DOM changes can prevent placement; the helper then waits for a verified native or fallback anchor. Semantically equivalent context indicators and model controls with changed footer spacing remain supported. If Codex renames the account-cache query, one unambiguous account-data query may supply the same windows; multiple candidates fail closed. It replaces stale renderer controllers by version and removes its temporary slot when Codex creates the native one. Native usage updates refresh the dials even when the reset-credit count stays unchanged.

## Personal copy and updates

Copy the complete Helper folder before customizing it. Keep the stable Helper ID only when the personal copy is intended to override the bundled package. After changing files, use `Reload helpers` and verify five-hour and weekly mapping, missing streams, tooltip content, warning colors, native-slot takeover, and cleanup in a scratch Codex window.

## Available rate-limit resets

The hover and keyboard-focus pop-ups show the signed-in account's available reset-credit count from Codex's native account query cache. The count follows account-cache updates without extra network polling and is distinct from credits applicable right now. Missing, failed, or malformed account data displays unavailable, while a confirmed zero displays 0. Disabling the helper removes its cache subscription. This compatibility adapter depends on Codex's React query provider and fails closed when that interface changes.

Usage hover is independent of the native context-window tooltip. Each usage dial opens its own 76px preview and window details, followed by available resets. Five-hour usage appears to the left of weekly usage when the account reports both. The shared reset-credit balance is labelled when both windows exist. The popup retains its 2px hollow outline; the miniature retains the original filled-circle segment rendering from before the outline changes.

Available reset credits are separated by a divider and shown count-first, for example "2 resets available". Unknown balances display "Resets unavailable".

Plan names never create or suppress a quota window. Use the durations and reset timestamps reported by the native account cache, including weekly-only and five-hour-only accounts. Reset credits are account-level and distinct from each window's scheduled reset time. See `docs/USAGE_LIMITS.md` for the dated policy research and its limitations.

## T3 Code

The T3 target follows the provider instance selected in the model picker, including before that provider runs a turn. Dials sit immediately to the picker's left and preserve native context actions. Session, weekly, monthly and other subscription allowances retain their reported labels and reset times. Calendar allowances without a duration use conventional arcs instead of guessed pacing.

The helper invokes T3's existing instance-scoped provider refresh at most once per minute, through the current app's native command and registry. Native authentication and account requests remain on the owning T3 server. It does not copy credentials, activate providers, rediscover models or send prompts. Hashed asset names are discovered per release; a missing native interface leaves usage explicitly unavailable. Source tests do not establish live T3 rendering or owner acceptance.

T3 compatibility: reads committed composer quota through the nearest React-owned ancestor of Lexical’s editable node, bounded to the same composer surface.

T3 dials retain Codex’s black/white neutral palette in light/dark themes. Muted composer text colours must not dim either the miniature or its separate hover preview; geometry, warning colours, and segment opacity remain shared.

Missing, ambiguous, stale, expired or malformed quota displays a question mark with a provider-specific explanation. A successful quota read containing no percentages displays a dash and explains that the provider has not reported usage yet. Neither state invents zero usage or borrows another provider's quota. Renderer readiness reports waiting to the Host; valid data restores the dials automatically.

T3 refresh discovers the native provider command across up to eight preloaded server modules, including unrelated modules loaded by lazy routes. It selects one unambiguous command by contract and retains the per-provider refresh cooldown.

The composer-owned picker supplies its committed `activeInstanceId`, including unsent draft changes. The saved conversation model does not override that choice.

Resolve the refresh registry over the full committed editor ancestry; it can be nested inside the owning composer. Multiple distinct registries leave refresh unavailable.

An optional external Grok companion (`tools/provider_usage_bridge.py`) retains the valid billing config that T3 discards when its percentage field is absent. Grok's own `credit_balance_from_config` and real `/usage` screen interpret that config as 0%. The companion applies this interpretation only to a successful unified-billing response with a valid current weekly/monthly period. Missing configs, invalid percentages and failed requests remain unavailable. The renderer accepts a snapshot only for the exact environment, provider instance and signed-in account, at most three minutes old; native nonempty quota wins. The configured `providerUsageFile` contains quota and its account identity, never an authentication key. Removing the companion or configuration restores native-only behavior. This keeps the fallback outside T3's release files.
