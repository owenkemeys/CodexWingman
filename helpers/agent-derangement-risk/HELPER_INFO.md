# Agent Derangement Risk

Helper ID: `agent-derangement-risk`

Manifest version: 1.0.2

## Purpose and behavior

Shows a small circular estimate of how degraded the current chat may become from context pressure, compactions, and accumulated thread history. The ring is display-only and is placed after Usage Dials when that Helper is present.

Risk below 60% is Healthy and inherits the current text color. Risk from 60% to below 85% is Watch using Usage Dials warning amber (`#d89614`). Risk at or above 85% is Handoff using Usage Dials danger red (`#e5484d`). Missing risk data shows a visible `?` in the same anchored slot while retaining the unavailable accessible label and tooltip.

## Files and entrypoints

- `wingman.json` — Helper declaration and refresh interval.
- `backend.js` — Session JSONL metric discovery and risk calculation.
- `apply.js` — Owned ring rendering and placement.
- `remove.js` — Owned cleanup.

## Sharp edges and failure behavior

The active `smooth-age` algorithm requires an explicit compaction count plus exact same-event cumulative total and cached-input telemetry. Missing required telemetry shows the existing `?` unavailable marker instead of substituting raw cumulative load. The retained `existing` algorithm preserves the old fallback from missing cumulative totals to current used tokens. The Helper does not start a new chat or take any automatic action.

## Deferred visual alignment

The next time this renderer is changed, align its Healthy used arc and background with Usage Dials and Codex's native context donut: use `text-token-description-foreground`, a fully opaque used arc, and the same color at `16%` opacity for the complete background track. Preserve the existing amber Watch and red Handoff colors. This is a note for the next scoped change, not a separate immediate redesign.

## Algorithm selection

The one selector parameter is `DERANGEMENT_RISK_ALGORITHM` at the top of `backend.js`. Supported keys are `existing`, `milestones`, `smooth-age`, and `paired`; the active selection is Option B, `smooth-age`. The retained `existing` adapter uses raw cumulative load and does not require fresh-processing telemetry. The three new algorithms calculate fresh processing only from the selected event's exact `info.total_token_usage.total_tokens` and `cached_input_tokens`; missing same-event values or an explicit compaction count fails closed to unavailable.

## Personal copy and updates

The bundled package is the reference implementation. A personal copy can customize the renderer or formula while retaining the same ownership ID and cleanup contract. The target-session capability reads only the current thread’s configured session file.

The popup uses a muted heading, separates Risk from Compactions with a divider, and shows cumulative input plus output as Total tokens. Context-window occupancy stays in the native context popup.
