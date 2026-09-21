# Writing a CodexWingman Helper

A Helper is a folder of plain JSON and JavaScript files. You can read the whole package in a text editor before installing it. No Helper needs its own executable, PowerShell script, Node installation, or administrator prompt.

Put Helpers in the `Helpers` folder beside `CodexWingman.exe`, one folder per Helper. This is the only folder Wingman scans. Wingman shows the folder name in its tray menu, so a readable name such as `Example label v1` works well. `Open Helpers folder` opens that directory and never changes its contents. Choose `Reload helpers` after installing or editing a package.

Wingman treats installed Helpers as trusted code. Renderer scripts run inside Codex and can read or change that page. Backend scripts receive only the APIs described below, but a granted file capability can expose private chat data. The user installing a Helper is responsible for reviewing it.

## Package files

Every package contains:

- `wingman.json`, the manifest.
- `HELPER_INFO.md`, the human and agent-facing explanation of purpose, version, files, capabilities, customization points, and sharp edges.
- `apply.js`, which adds or updates the Helper in each Codex window.
- `remove.js`, which removes only that Helper's nodes, styles, listeners, and globals.

`backend.js` is optional. It reads data outside the renderer and returns JSON state for `apply.js`.

Here is a v1 manifest:

```json
{
  "schemaVersion": 1,
  "id": "example-label",
  "name": "Example label",
  "version": "1.0.0",
  "description": "Adds a small reviewable label to Codex.",
  "refreshSeconds": 0,
  "capabilities": [],
  "config": {
    "label": "Wingman active"
  },
  "entrypoints": {
    "apply": "apply.js",
    "remove": "remove.js"
  }
}
```

IDs contain lowercase ASCII letters, digits, and hyphens. Entrypoint paths are relative to the package folder and cannot escape it. `refreshSeconds` controls periodic reconciliation; use `0` when renderer observation is enough. Unknown manifest versions, invalid paths, missing files, and duplicate IDs within one package root are rejected and shown as diagnostics.

## Minimal renderer Helper

This `apply.js` is safe to run more than once:

```javascript
const id = `codex-wingman-${helperId}`;
let label = document.getElementById(id);
if (!label) {
  label = document.createElement('span');
  label.id = id;
  label.dataset.codexWingmanOwner = helperId;
  label.textContent = 'Wingman active';
  document.body.append(label);
}
```

Its `remove.js` cleans up only what the Helper owns:

```javascript
document
  .querySelectorAll(`[data-codex-wingman-owner="${helperId}"]`)
  .forEach(node => node.remove());
```

`config` is optional and must be a JSON object. Wingman supplies it to both renderer scripts as a top-level frozen `helperConfig` object. A missing, `null`, array, scalar, or otherwise non-object config becomes an empty frozen object. Use `helperConfig` for package settings that do not need backend code.

Wingman supplies `helperId`, `state`, and `wingman` to both renderer scripts. `state` is the JSON value returned by the backend, or an empty object when there is no backend. `wingman.request(action, payload)` asks the Host to perform a declared Host Action. Repeated apply, remove, reload, and rerender calls must be harmless.

## Optional backend

A backend defines one function:

```javascript
function refresh(wingman) {
  wingman.log(`Refresh at ${wingman.currentTime()}`);
  return { label: 'Wingman active' };
}
```

The return value must be JSON-compatible. Wingman limits each backend run by time and statement count. Direct .NET access is not available.

All backends receive:

- `wingman.log(message)`
- `wingman.currentTime()`
- `wingman.expandEnvironmentVariables(value)`

The `files.codexSessions` capability adds `wingman.files.codexSessions.roots()`, `listFiles(root)`, and `readText(path)`. Reads are limited to configured Codex session roots. Declare it only when the Helper needs chat-session data:

```json
"capabilities": ["files.codexSessions"]
```

For per-window or historic-thread metrics, use `files.codexTargetSession` instead. The Host binds `wingman.files.codexTargetSession.snapshot()` to the current Codex target's validated `/local/<thread-id>` route and performs the bounded session lookup outside the backend runtime. It returns `null` when the target has no thread or its session cannot be read. This keeps multiple windows isolated and avoids loading the entire session corpus into one backend run.

```json
"capabilities": ["files.codexTargetSession"]
```

The same capability exposes `wingman.files.codexTargetSession.turnMetadata()`. It returns `{ threadId, records }`, where `records` is keyed by exact Codex turn ID and contains only structured model, provider, timing, token-usage, subagent, and completeness fields. It never returns raw session lines, messages, encrypted content, or paths. Use this interface for response-level metadata; do not infer provenance from rendered text or visual order.

It also exposes `wingman.files.codexTargetSession.hookTrace()`. It returns `{ threadId, records }`, where each exact turn record contains every saved developer/system message, including context recorded before the visible user message. Text is preserved in full. Precise lifecycle placement is used only when transcript ordering proves it; otherwise use a generic model-context label. Saved transcripts do not retain hook provenance, so these records must be labelled `Source not recorded by Codex` and must not be claimed as verified hooks. Renderer helpers may separately join Codex's live `turn.hookRuns` data to recover verified hook source, status, silent executions, and tool execution IDs while that live state remains available. Codex's durable logs do not retain enough task/turn identity to reconstruct historical silent runs.

The `codex.openNewChatWindow` capability permits this renderer request:

```javascript
wingman.request('codex.openNewChatWindow', { path: '/' });
```

The Host checks that the requesting Helper declared the action before it calls Codex's fixed native new-window route. Other backend or Host powers are unavailable until a later Wingman version explicitly adds them.

## Testing and installation

Test renderer scripts against a small local HTML fixture and verify that apply is idempotent and remove leaves unrelated UI alone. A backend test should cover malformed or unavailable input as well as its normal result.

When a Helper shows data for the task currently on screen, the rendered task identity is the only authority. The Host and renderer must share the same resolver, reject conflicting identity signals, and show an unavailable state while the UI is between tasks. Do not recover identity from the launch URL, sidebar, cached state, newest record, or another window.

Fixture tests are supporting evidence. Complete UI-affecting work with the bounded live loop in `docs/testing/rendered-state-ui-acceptance.md`. The Agent Derangement Risk implementation has a project-specific card at `docs/testing/agent-derangement-risk-test-card.md`.

Copy the finished folder into the `Helpers` folder beside `CodexWingman.exe`, choose `Reload helpers`, then enable it under `Helpers`. If its ID matches a bundled Helper, the personal package takes effect until it is removed and Helpers are reloaded.
