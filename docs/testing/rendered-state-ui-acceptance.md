# Rendered-state UI acceptance

Use this loop when an Electron UI shows data that belongs to the page, document, task, account, or other entity currently visible in one renderer. It is meant for bugs where the screen changes but a badge, dial, tooltip, or status panel keeps data from the previous screen.

## Authority

The rendered UI is the acceptance surface. Backend state, parser output, logs, controller globals, and fixture tests can explain a failure, but they cannot prove the user sees the right result.

Resolve identity from the rendered content in the same renderer. Require supported identity signals to agree. Do not fall back to the launch URL, initial route, sidebar selection, cached identity, newest record, or another window. When identity is absent, conflicting, or still changing, show the product's unavailable state and keep checking.

## The loop

1. Observe fresh state in one exact test target. Record its target ID, visible page identity, visible metric or marker, tooltip details, and a screenshot.
2. Choose one transition from a finite scenario list. Use several source pages with visibly distinct current signatures so stale retention is obvious.
3. Act once through the real UI control. Keep the action inside the designated target.
4. Verify the destination's visible identity first, then the metric, tooltip, and screenshot. Reject any result that retains the source page's full visible signature.
5. Record the transition, evidence, and outcome before continuing.
6. Stop successfully only after every scenario passes. Stop failed-closed on an identity mismatch, action outside the target, lost target, or ambiguous UI. Stop stagnated when two consecutive attempts add no evidence.

A useful eight-scenario program is `A -> B -> A -> C -> B -> C -> A -> unavailable`. Include direct loads, repeated switches, revisits, and the product's real blank or unavailable state. Capture current first-visit signatures at the start of the run. Do not hardcode old percentages for live telemetry. If a value legitimately changes during the run, refresh that page's baseline and record the drift instead of accepting a silent mismatch.

## CDP field notes

- Prefer an exact user-designated target over guessing which renderer is safe. Never substitute a user window when the test guardrail requires isolation.
- Treat a new browser target as untrusted until it has the app preload bridge, app URL, visible window bounds, and real rendered UI. A hidden `about:blank` target is not a test window.
- Put timeouts and close/error handlers around every CDP call. Navigation can destroy an execution context and leave a request waiting forever.
- When a click will navigate, schedule it with `setTimeout(() => node.click(), 0)` so the evaluation call can return before the context disappears.
- Virtualized rows may exist outside the viewport. Call `scrollIntoView`, wait for layout, then recheck connection, dimensions, viewport intersection, and hidden ancestors before clicking.
- Normalize control text with whitespace collapsing. Native labels often contain newlines or hidden option text.
- Check escaping when JavaScript is injected through a template string. To deliver `/\s+/` to the evaluated source, the outer template needs `/\\s+/`.
- A visible glyph can have no useful layout rectangle. For small markers such as `?`, combine computed display, visibility, and opacity with screenshot review instead of relying on one rectangle predicate.

## Evidence record

For each scenario, store the source and destination labels, exact target ID, visible identity, visible metric or marker, tooltip fields, timestamp, screenshot path, and pass/fail reason. Keep diagnostic backend values in a separate section so they cannot be mistaken for acceptance evidence.

The loop may inspect any in-scope renderer, but it may act only in the designated test target. It does not authorize creating windows through user-owned renderers, closing windows, changing profiles, or publishing a build.
