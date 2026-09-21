# Turn Render Recovery

Helper ID: `turn-render-recovery`

Manifest version: 1.5.1

## Purpose and behavior

Turn Render Recovery has two fail-closed repair paths:

1. If a completed final assistant item is already present in Codex's native React state but its virtualized row is clipped or stale, the Helper removes only that row's cached height. Codex remeasures and rerenders it.
2. If Codex's history state omitted turns—or remains falsely active on a finished activity/outcome row—the Helper reads that task's exact `rolloutPath` through the same host-specific Codex app-server connection, parses native turn items, passes them through Codex's own thread-to-conversation mapper, and updates Codex's native conversation store. When a newer turn is genuinely active, its saved prompt, commentary, reasoning summaries, and completed tool rows are restored as `inProgress` while the completed prefix is restored without clearing the active runtime state. A falsely active native state is cleared only when the rollout's latest started turn has both a saved `task_complete` event and a non-empty `final_answer`. Codex's existing components perform the rendering.

The Helper does not insert message HTML, render Markdown, write to the rollout, inject duplicate thread items, scan other tasks, or require a Helper backend/file capability. The active rendered conversation identity and its own native `rolloutPath` are the only recovery authority.

After either repair path changes a turn, the Helper adds a small `Turn restored in UI by Wingman` provenance note immediately after Codex's native response timestamp. Untouched turns receive no note. The note follows the timestamp's existing visibility behavior and is removed when the Helper is disabled or removed.

## Sharp edges and failure behavior

This Helper intentionally uses private Codex React/store boundaries because the desktop app exposes no public row-remeasurement or history-repair command. Both paths are compatibility-gated. If an app update changes the expected manager, mapper, canonical-history, or virtual-list shapes, the relevant path becomes a no-op and records a diagnostic instead of mutating unknown state.

For host-routed tasks, manager discovery first performs a bounded shallow scan of renderer update queues for the manager that owns the visible task. This keeps remote-task recovery below the existing search ceiling even when unrelated local state makes the wider React graph substantially larger.

Turn mapping supports both Codex's earlier thread-store policy mapper and the current canonical-history `mapThreadTurns` mapper. Both routes still use Codex's own native mapping code and fail closed when neither compatible mapper is available.

Genuinely active or streaming turns are never marked complete and their runtime state is never cleared. Their saved UI prefix may be refreshed no more than once every ten seconds; incomplete output is kept `inProgress`. A failed rollout read waits ten seconds before retrying and doubles that delay for consecutive failures up to one minute. Once an idle task's cached rollout and native history agree, later DOM mutations reuse that healthy result without rereading the full rollout; if Codex later hydrates a stale snapshot, the cached native recovery is reapplied without another file request. Clearing the active state still requires the rollout's latest started turn to have both a saved `task_complete` event and a non-empty `final_answer`; an outcome/tool row alone is never enough. The rollout's declared task ID must exactly match the rendered task. Recovery is in-memory and read-only with respect to Codex session files.

Tool calls saved as `custom_tool_call`, `function_call`, or `mcp_tool_call` are represented as Codex `dynamicToolCall` items with their saved arguments and content items. Codex owns their display.

## Personal copy and updates

Keep personal overrides under Wingman's user Helper folder only when testing a newer local copy. A bundled update with the same Helper ID replaces the personal copy after the override is removed.

## Files

- `wingman.json` declares a renderer-only Helper with no file or Host capabilities.
- `apply.js` performs version-gated clipping repair and task-scoped native history rehydration.
- `remove.js` disconnects the observer, clears in-memory recovery state and provenance notes, and removes the Helper global.
