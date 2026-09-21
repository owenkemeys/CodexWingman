# Hook Trace

- Helper generation: v16
- Manifest version: 1.3.6
- Helper ID: `hook-trace`

## Purpose and behavior

Hook Trace joins two Codex data sources: verified live hook runs from the active turn and complete saved model context from the task transcript. Saved developer and system messages are included even when Codex recorded them before the visible user message or without a recoverable hook event. While the Helper is enabled, every retained insertion appears automatically as a collapsed fold at its exact recorded position. Turns and sides without retained data receive no Hook Trace control. Expanded entries preserve complete recorded text without an internal scroll area.

Hovering the Hook control shows a compact effect-first `Hooks` panel. Lifecycle names remain on the left; the right column now distinguishes `Affected model`, `Failed`, `Diagnostics only`, and `No recorded model effect`, followed by the recorded source and run count. Retained transcript-only context is labelled `Retained` instead of being presented as a verified hook run. The panel groups by displayed lifecycle, source, and effect, so silent repetitions become one count without hiding a model-affecting run behind them. It dismisses a surviving native Hooks hover before opening, so only one Hooks panel is visible. Its four-side placement reserves a gap around the trigger, constrains itself to the usable viewport, and keeps the trigger-to-popup hover transition stable. Clicking the Hook control first opens any collapsed native work folds that contain Hook Trace rows, then expands all Hook Trace folds for that side. Clicking again when all are expanded collapses the Hook Trace folds. Opening or closing an individual header affects only that fold. A stationary background click in an expanded body collapses that fold, while text selection, pointer movement, modifiers, links, controls, and `pre` or `code` content remain safe.

Every user-triggered fold or bulk change uses bounded multi-frame post-layout scroll compensation against a single conversation scroll owner, allowing one later browser or virtualized-layout adjustment to settle before acceptance. The owner is selected after layout from the nearest usable overflow ancestor and never hands residual movement to the document or another outer viewport. The initiating pointer position and associated content stay at the same viewport point when that owner has room; keyboard activation falls back to the focused control. When a body disappears, its surviving fold header becomes the compensated affordance.

Verified live rows are effect-first: `Hook affected the model`, `Hook failed`, `Hook emitted diagnostics`, or `Hook ran silently`, followed by the native activity they surrounded when Codex exposes that correlation. A continuous sequence of otherwise-identical runs becomes one disclosure with a compact count such as `(24x)`, even when Codex assigned a different hidden execution target ID to each run. Rows are mounted beside native activity controls rather than inside them, so native labels never absorb Hook Trace text. Saved developer or system messages retain `Added model context: <position>` because Codex did not preserve hook provenance for them. `Before your message` rows precede the authored message inside its bubble; prompt-submission rows follow it. Live rows show Codex's configuration-layer source category, resolved source path, run status, status message, and non-context diagnostic output. Transcript-only rows read `Source not recorded by Codex` rather than guessing.

When Codex also renders its native Hooks control, Hook Trace mounts inside that control's exact wrapper and suppresses the native button continuously only there. This also works while Codex's final response is folded and its ordinary Copy control is unavailable. Reconciliation does not expose a still-owned native control between frames, and removing or disabling Hook Trace restores the native control in its original order.

## Capabilities

- `files.codexTargetSession` reads only the session that Wingman has matched to the exact visible Codex task.
- Only a rollout whose `session_meta.id` exactly equals the visible task ID is read. Misleading filenames and linked subagent rollouts are rejected.
- Live hooks around tool calls are matched to the smallest native tool surface exposing Codex's execution ID. A native surface can still group several commands. Saved context uses its transcript ordinal only when no live ID exists.

## Sharp edges and failure behavior

- A completed silent run with empty entries means only that Codex recorded the lifecycle, source, path, and outcome. Hook Trace explicitly says that Codex recorded no model-visible text, command, or diagnostic output and that Hook Trace cannot determine what the hook did from the task data. The source `hooks.json` path remains visible for manual investigation. Exact commands and diagnostics are shown only when Codex records them; Hook Trace never infers or re-runs a command. The current Helper capability cannot read the referenced hook configuration. Safely showing configured executable or script basenames requires a future Host capability that reads only the exact recorded source and strips potentially secret arguments.
- Codex persists model-visible developer/system text but does not persist hook-run metadata in task history. Historical turns therefore retain the complete text the model received. Past silent hook executions cannot be assigned to a task or turn, because Codex's SQLite log keeps only anonymous start/completion events. Silent runs remain visible while Codex still exposes their live run data.
- An unreadable or missing target session is labelled unavailable rather than as a genuine empty turn.
- Developer-role text is not treated as proof of a hook or of an OpenAI/provider source.
- A `System hook` means Codex's system configuration layer; it is not presented as an OpenAI/provider attribution. Source paths are displayed as recorded paths, not as proof of authorship or user-openable locations.
- If the exact task, turn, side, text, or a safe rendered anchor cannot be validated, Hook Trace fails closed for that entry.
- Cleanup removes only Hook Trace-owned controls, summaries, and inline rows, and restores native action-tray classes.

## Personal copy and updates

Copy the complete Helper folder before customizing it. Keep the stable Helper ID only when the personal copy is intended to override the bundled package. Backups must live under `Helpers\_backups\<backup name>`, never as an immediate sibling package in `Helpers`, because Wingman scans immediate package folders and rejects duplicate Helper IDs. After changing files, use `Reload helpers` and verify automatic collapsed folds, effect-first count summary placement, native-parent expansion, individual and bulk folding, body-click guards, interaction-point scroll preservation, chronological placement, full expanded text, and cleanup in a dedicated Codex test window.
