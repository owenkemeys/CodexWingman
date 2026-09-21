# Agent Derangement Risk UI test card

The dial describes only the task rendered in its own Codex window. The unavailable `?` state is correct whenever visible task identity or matching telemetry cannot be established.

## Identity contract

The Host and renderer accept only these visible DOM attributes:

- `data-above-composer-conversation-id`
- `data-request-user-input-auto-resolution-conversation-id`

They normalize an optional `local:` prefix and require exactly one unique task ID. The launch URL, `initialRoute`, sidebar state, cached state, newest session, and other windows are not identity sources. A native New Task screen returns `?` even if a stale identity node lingers during the transition.

## Acceptance program

Use one designated test target and three existing tasks with distinct current dial signatures. Run `A -> B -> A -> C -> B -> C -> A -> New Task`. At every step:

1. Confirm the visible task title or content and the supported DOM identity agree.
2. Read the dial or `?` marker from the rendered UI.
3. Hover the dial and record the visible compaction count and other tooltip fields.
4. Capture a screenshot that shows the task and dial state together.
5. Fail immediately if the source task's full visible signature survives the switch.

Backend snapshots and unit tests are diagnostic support only. They do not pass this program.

## Current evidence

The 2026-07-14 run passed all eight scenarios. Its machine-readable record and screenshots are under `Codex_Working/agent-derangement-ui-acceptance-2026-07-14/`. The design contract is in `docs/superpowers/specs/2026-07-14-agent-derangement-visible-conversation-design.md`.

Use `docs/testing/rendered-state-ui-acceptance.md` for the reusable loop and CDP guardrails.
