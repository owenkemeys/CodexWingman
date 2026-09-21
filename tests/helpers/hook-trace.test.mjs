import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/hook-trace/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function makeTurn(document, turnId) {
  const turn = document.createElement('section')
  turn.dataset.turnKey = turnId
  const stack = document.createElement('div')

  const userAnchor = document.createElement('div')
  userAnchor.dataset.localConversationUserAnchor = 'true'
  userAnchor.dataset.contentSearchUnitKey = `${turnId}:0:user`
  const userShell = document.createElement('div')
  userShell.dataset.fixtureUserShell = 'true'
  const userBubble = document.createElement('div')
  userBubble.dataset.userMessageBubble = 'true'
  userBubble.setAttribute('role', 'button')
  const userBubbleBody = document.createElement('div')
  const userText = document.createElement('div')
  userText.setAttribute('class', 'text-size-chat')
  userText.textContent = 'Your message'
  userBubbleBody.append(userText)
  userBubble.append(userBubbleBody)
  const userActionRow = document.createElement('div')
  userActionRow.setAttribute('class', 'flex flex-row-reverse')
  const userVisibilityTray = document.createElement('div')
  userVisibilityTray.setAttribute('class', 'mr-1 ms-1 flex items-center gap-2 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100')
  const timestamp = document.createElement('span')
  timestamp.textContent = '12:31'
  const userActions = document.createElement('div')
  userActions.setAttribute('class', 'flex items-center gap-0.5')
  const copyMessageWrapper = document.createElement('span')
  const copyMessage = document.createElement('button')
  copyMessage.setAttribute('aria-label', 'Copy message')
  copyMessageWrapper.append(copyMessage)
  userActions.append(copyMessageWrapper)
  userVisibilityTray.append(timestamp, userActions)
  userActionRow.append(userVisibilityTray)
  userShell.append(userBubble, userActionRow)
  userAnchor.append(userShell)

  const activity = document.createElement('div')
  activity.dataset.fixtureActivity = 'true'
  activity.dataset.localConversationItemTargetIds = 'exec-fixture-one'
  const activityButton = document.createElement('button')
  activityButton.setAttribute('class', 'group/activity-header')
  activityButton.textContent = 'Ran commands'
  const activityBody = document.createElement('div')
  activityBody.textContent = 'command output'
  activity.append(activityButton, activityBody)

  const finalAssistant = document.createElement('div')
  finalAssistant.dataset.localConversationFinalAssistant = 'true'
  const assistantUnit = document.createElement('div')
  assistantUnit.setAttribute('class', 'text-size-chat')
  const annotation = document.createElement('div')
  annotation.dataset.responseAnnotationTarget = `item-${turnId}`
  const answer = document.createElement('div')
  answer.textContent = 'Codex final answer'
  const responseActions = document.createElement('div')
  responseActions.setAttribute('class', 'opacity-0 group-hover:opacity-100')
  const copyWrapper = document.createElement('span')
  const copy = document.createElement('button')
  copy.setAttribute('aria-label', 'Copy')
  copyWrapper.append(copy)
  responseActions.append(copyWrapper)
  annotation.append(answer, responseActions)
  assistantUnit.append(annotation)
  finalAssistant.append(assistantUnit)

  stack.append(userAnchor, activity, finalAssistant)
  turn.append(stack)
  document.body.append(turn)
  return { turn, stack, userAnchor, userShell, userBubble, userBubbleBody, userText, userActionRow, userVisibilityTray, timestamp, userActions, activity, activityButton, activityBody, finalAssistant, assistantUnit, annotation, responseActions, copyMessage, copy }
}

function snapshot(turnId) {
  return {
    threadId: '019f70bd-1577-7813-a834-945e67aeb367',
    status: 'available',
    records: {
      [turnId]: [
        { sequence: 0, kind: 'modelContext', side: 'user', label: 'Prompt-submission context', text: 'line one\nline two\n  exact indentation', sourceLabel: 'Source not recorded by Codex' },
        { sequence: 1, kind: 'modelContext', side: 'response', label: 'Before tool use', activityIndex: 0, text: 'before exact text', sourceLabel: 'Source not recorded by Codex' },
        { sequence: 2, kind: 'modelContext', side: 'response', label: 'After tool use', activityIndex: 0, text: 'after exact text', sourceLabel: 'Source not recorded by Codex' },
        { sequence: 3, kind: 'modelContext', side: 'response', label: 'Stop context', text: 'stop exact text', sourceLabel: 'Source not recorded by Codex' },
      ],
    },
  }
}

test('Hook Trace package declares the target-scoped contract and backend fails closed', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'hook-trace',
    name: 'Hook Trace',
    version: '1.3.6',
    description: 'Shows recorded hook activity and retained model context as automatic collapsed folds at exact turn lifecycle positions.',
    refreshSeconds: 2,
    capabilities: ['files.codexTargetSession'],
    entrypoints: { backend: 'backend.js', apply: 'apply.js', remove: 'remove.js' },
  })
  const info = await requiredFile('HELPER_INFO.md')
  assert.match(info, /complete recorded text/i)
  assert.match(info, /Source not recorded by Codex/)
  assert.match(info, /live hook runs/i)
  assert.match(info, /Exact commands and diagnostics/i)
  assert.match(info, /automatic collapsed folds/i)
  assert.match(info, /count summary/i)
  assert.match(info, /background click/i)
  assert.match(info, /scroll compensation/i)
  assert.match(info, /single conversation scroll owner/i)
  assert.match(info, /native work folds/i)
  assert.match(info, /cannot determine what the hook did/i)
  assert.match(info, /Helpers\\_backups/i)
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { JSON, Object, Array })
  const state = snapshot('019f71d0-2d51-7592-9069-61056ecd3a9c')
  assert.deepEqual(JSON.parse(JSON.stringify(refresh({ files: { codexTargetSession: { hookTrace: () => state } } }))), state)
  assert.deepEqual(JSON.parse(JSON.stringify(refresh({ files: { codexTargetSession: { hookTrace: () => null } } }))), { threadId: null, status: 'session-unavailable', records: {} })
})

test('renderer adds independent native-style user and response Hook controls', async () => {
  const fixture = createBrowserFixture()
  try {
    const source = await requiredFile('apply.js')
    const turnId = '019f71d0-2d51-7592-9069-61056ecd3a9c'
    const rendered = makeTurn(fixture.document, turnId)
    executeRenderer(source, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const controls = fixture.document.querySelectorAll('[data-hook-trace-trigger]')
    assert.equal(controls.length, 2)
    assert.equal(controls[0].getAttribute('data-hook-side'), 'user')
    assert.equal(controls[1].getAttribute('data-hook-side'), 'response')
    assert.equal(controls[0].textContent, '')
    assert.equal(controls[0].querySelectorAll('svg').length, 1)
    const icon = controls[0].querySelector('svg')
    assert.equal(icon.getAttribute('viewBox'), '0 0 18 18')
    assert.equal(icon.querySelector('circle')?.getAttribute('cx'), '8.99805')
    assert.deepEqual([...icon.querySelectorAll('path')].map((path) => path.getAttribute('d')), [
      'M9 6.75V12C9 13.6569 10.3431 15 12 15C13.6569 15 15 13.6569 15 12V9.75L13.5 11.25',
      'M9 6.75V12C9 13.6569 7.65685 15 6 15C4.34315 15 3 13.6569 3 12V9.75L4.5 11.25',
    ])
    assert.equal(controls[0].parentElement.parentElement, rendered.userActions)
    assert.equal(controls[1].parentElement.parentElement, rendered.responseActions)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline]').length, 4)
  } finally {
    fixture.dispose()
  }
})

test('renderer omits Hook controls when a turn has no recorded hook context', async () => {
  const fixture = createBrowserFixture()
  try {
    const source = await requiredFile('apply.js')
    const turnId = '019f71d0-7777-7777-8777-777777777777'
    makeTurn(fixture.document, turnId)
    executeRenderer(source, fixture, { state: { threadId: '019f70bd-1577-7813-a834-945e67aeb367', status: 'available', records: {} } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline]').length, 0)
    executeRenderer(source, fixture, { state: { threadId: null, status: 'session-unavailable', records: {} } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 0)
  } finally {
    fixture.dispose()
  }
})

test('click toggles each side independently and renders full disclosure rows around tool work', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-2d51-7592-9069-61056ecd3a9c'
    const rendered = makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const user = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]')
    const response = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')

    user.click()
    await fixture.flush()
    assert.equal(user.getAttribute('aria-pressed'), 'true')
    assert.equal(response.getAttribute('aria-pressed'), 'false')
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="user"]').length, 1)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]').length, 3)
    assert.equal(rendered.userVisibilityTray.hasAttribute('data-hook-trace-visibility-active'), true)
    assert.match(rendered.userVisibilityTray.getAttribute('class'), /opacity-0/)
    assert.equal(rendered.userVisibilityTray.contains(user.closest('[data-hook-trace-slot]')), true)
    assert.equal(rendered.userBubble.contains(fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')), true)

    response.click()
    await fixture.flush()
    const responseRows = fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(responseRows.length, 3)
    assert.equal(rendered.activity.contains(responseRows[0]), true)
    assert.equal(rendered.activity.children[0], responseRows[0])
    assert.equal(rendered.activity.contains(responseRows[1]), true)
    assert.equal(rendered.activity.children.at(-1), responseRows[1])
    assert.equal(rendered.finalAssistant.contains(responseRows[2]), true)
    assert.equal(responseRows[2].previousElementSibling === rendered.annotation, true)
    assert.match(responseRows[0].textContent, /Added model context: Before tool use/)
    assert.match(responseRows[1].textContent, /Added model context: After tool use/)
    assert.match(responseRows[2].textContent, /Added model context: Stop context/)
    for (const row of [fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]'), ...responseRows]) {
      const fold = row.querySelector('[data-hook-trace-row-trigger]')
      assert.equal(row.getAttribute('class'), 'text-size-chat text-token-text-secondary')
      assert.equal(fold.getAttribute('class'), 'text-size-chat hover:bg-token-bg-subtle inline-flex items-center gap-1 rounded-md border border-transparent focus-visible:ring-2 focus-visible:ring-token-focus-border focus-visible:outline-none')
      assert.match(fold.querySelector('[data-hook-trace-chevron]').getAttribute('class'), /rotate-90/)
    }

    const userRow = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    const body = userRow.querySelector('[data-hook-trace-row-body]')
    assert.equal(body.textContent, 'line one\nline two\n  exact indentationSource not recorded by Codex')
    assert.equal(body.querySelector('pre').textContent, 'line one\nline two\n  exact indentation')
    assert.equal(body.style.maxHeight ?? '', '')
    assert.equal(body.style.overflow ?? '', '')
    assert.match(userRow.querySelector('[data-hook-trace-chevron]').getAttribute('class') || '', /rotate-90/)
  } finally {
    fixture.dispose()
  }
})

test('tool hook rows use their exact activity ordinal instead of collapsing onto the first and last activity', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-9999-7999-8999-999999999999'
    const rendered = makeTurn(fixture.document, turnId)
    const secondActivity = fixture.document.createElement('div')
    secondActivity.dataset.fixtureActivity = 'true'
    const secondButton = fixture.document.createElement('button')
    secondButton.textContent = 'Ran second commands'
    const secondBody = fixture.document.createElement('div')
    secondBody.textContent = 'second command output'
    secondActivity.append(secondButton, secondBody)
    rendered.activity.insertAdjacentElement('afterend', secondActivity)
    const state = snapshot(turnId)
    state.records[turnId][1].activityIndex = 1
    state.records[turnId][2].activityIndex = 1
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]').click()
    await fixture.flush()
    const rows = fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(secondActivity.children[0] === rows[0], true)
    assert.equal(secondActivity.children.at(-1) === rows[1], true)
    assert.equal(rendered.activity.contains(rows[0]), false)
    assert.equal(rendered.activity.contains(rows[1]), false)
  } finally {
    fixture.dispose()
  }
})

test('hover summary shares a stable region, clamps to viewport, and cleanup restores native trays', async () => {
  const fixture = createBrowserFixture()
  try {
    fixture.window.innerWidth = 360
    fixture.window.innerHeight = 640
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger')) return { left: 334, right: 358, top: 610, bottom: 634, width: 24, height: 24 }
      if (node.hasAttribute?.('data-hook-trace-summary')) return { width: 320, height: 120 }
      return { width: 20, height: 20 }
    })
    const apply = await requiredFile('apply.js')
    const remove = await requiredFile('remove.js')
    const turnId = '019f71d0-2d51-7592-9069-61056ecd3a9c'
    const rendered = makeTurn(fixture.document, turnId)
    const originalUserClass = rendered.userActions.getAttribute('class')
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 2)
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const summary = fixture.document.querySelector('[data-hook-trace-summary]')
    assert.ok(summary)
    assert.equal(summary.querySelector('[data-hook-trace-summary-title]').textContent, 'Hooks')
    assert.equal(summary.querySelector('[data-hook-trace-summary-row]').textContent, 'Prompt-submission contextRetained')
    assert.equal(trigger.getAttribute('data-state'), 'open')
    assert.ok(Number.parseFloat(summary.style.left) >= 8)
    assert.ok(Number.parseFloat(summary.style.top) >= 8)
    summary.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    trigger.dispatchEvent(new fixture.PointerEvent('pointerout', { bubbles: true, pointerType: 'mouse', relatedTarget: summary }))
    await new Promise((resolve) => setTimeout(resolve, 180))
    assert.ok(fixture.document.querySelector('[data-hook-trace-summary]'), 'summary remains while pointer crosses from trigger to panel')

    trigger.click()
    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelectorAll('[data-codex-wingman-owner="hook-trace"]').length, 0)
    assert.equal(rendered.userActions.getAttribute('class'), originalUserClass)
    assert.equal(rendered.copyMessage.isConnected, true)
    assert.equal(rendered.copy.isConnected, true)
  } finally {
    fixture.dispose()
  }
})

test('state refresh removes stale rows while preserving the active Hook view controls', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-2d51-7592-9069-61056ecd3a9c'
    const rendered = makeTurn(fixture.document, turnId)
    const originalClass = rendered.userActions.getAttribute('class')
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]').click()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline]').length, 4)
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline]').length, 0)
    assert.equal(fixture.window.__codexHelperHookTrace.status().expandedCount, 0)
    assert.equal(rendered.userActions.getAttribute('class'), originalClass)
    assert.equal(rendered.userVisibilityTray.hasAttribute('data-hook-trace-visibility-active'), false)
  } finally {
    fixture.dispose()
  }
})

test('renderer joins genuine live hook runs to exact tool surfaces and exposes recorded source', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-aaaa-7aaa-8aaa-aaaaaaaaaaaa'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceTest = {
      child: {
        memoizedProps: {
          turnId,
          turn: {
            turnId,
            hookRuns: [
              {
                id: 'user-prompt-live',
                run: {
                  id: 'user-prompt-live',
                  eventName: 'userPromptSubmit',
                  source: 'user',
                  sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json',
                  status: 'completed',
                  entries: [{ kind: 'context', text: 'live prompt context' }],
                },
              },
              {
                id: 'pre-tool-live:exec-fixture-one',
                run: {
                  id: 'pre-tool-live:exec-fixture-one',
                  eventName: 'preToolUse',
                  source: 'user',
                  sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json',
                  status: 'failed',
                  statusMessage: 'Hook process exited with code 7',
                  entries: [
                    { kind: 'command', text: 'dcg.exe --example' },
                    { kind: 'warning', text: 'diagnostic output' },
                  ],
                },
              },
              {
                id: 'post-tool-live:exec-fixture-one',
                run: {
                  id: 'post-tool-live:exec-fixture-one',
                  eventName: 'postToolUse',
                  source: 'project',
                  sourcePath: 'C:\\repo\\.codex\\hooks.json',
                  status: 'completed',
                  entries: [{ kind: 'context', text: 'live post-tool context' }],
                },
              },
            ],
          },
        },
      },
    }
    executeRenderer(apply, fixture, { state: { threadId: '019f70bd-1577-7813-a834-945e67aeb367', status: 'available', records: {} } })
    await fixture.flush()
    const user = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]')
    const response = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    assert.equal(user.hasAttribute('aria-disabled'), false)
    assert.equal(response.hasAttribute('aria-disabled'), false)
    response.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const summaryRows = [...fixture.document.querySelectorAll('[data-hook-trace-summary-row]')]
    assert.deepEqual(summaryRows.map((row) => row.textContent), [
      'Before tool useFailed · Userhook exited with code 7',
      'After tool useAffected model · Project',
    ])
    assert.equal(summaryRows[0].querySelector('[data-hook-trace-summary-status]').textContent, 'hook exited with code 7')
    assert.equal(summaryRows[0].querySelector('[data-hook-trace-summary-event]').textContent, 'Before tool use')
    assert.equal(summaryRows[0].querySelector('[data-hook-trace-summary-meta]').textContent, 'Failed · User')
    user.click()
    response.click()
    await fixture.flush()
    const userRow = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(userRow.textContent, /Hook affected the model at prompt-submission context/i)
    assert.match(userRow.querySelector('[data-hook-trace-source]').textContent, /User hook.*hooks\.json/)
    const responseRows = fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(responseRows.length, 2)
    assert.equal(rendered.activity.children[0], responseRows[0])
    assert.equal(rendered.activity.children.at(-1), responseRows[1])
    assert.match(responseRows[0].querySelector('[data-hook-trace-row-trigger]').textContent, /Hook failed before Ran commands/i)
    assert.match(responseRows[1].querySelector('[data-hook-trace-row-trigger]').textContent, /Hook affected the model after Ran commands/i)
    assert.match(responseRows[0].querySelector('pre').textContent, /No model-visible text inserted/)
    assert.match(responseRows[0].querySelector('[data-hook-trace-source]').textContent, /User hook.*hooks\.json/)
    assert.match(responseRows[0].querySelector('[data-hook-trace-status]').textContent, /Failed.*Hook process exited with code 7/i)
    assert.match(responseRows[0].querySelector('[data-hook-trace-output]').textContent, /command.*dcg\.exe --example/i)
    assert.match(responseRows[0].querySelector('[data-hook-trace-output]').textContent, /warning.*diagnostic output/i)
    assert.match(responseRows[1].querySelector('[data-hook-trace-source]').textContent, /Project hook.*hooks\.json/)
  } finally {
    fixture.dispose()
  }
})

test('silent hook runs are grouped and framed honestly around their recorded effect', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-aaab-7aab-8aab-aaaaaaaaaaab'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceEffectTest = {
      child: {
        memoizedProps: {
          turnId,
          turn: {
            turnId,
            hookRuns: [
              { run: { id: 'silent-one:exec-fixture-one', eventName: 'postToolUse', source: 'user', sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json', status: 'completed', entries: [] } },
              { run: { id: 'silent-two:exec-fixture-one', eventName: 'postToolUse', source: 'user', sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json', status: 'completed', entries: [] } },
              { run: { id: 'context:exec-fixture-one', eventName: 'postToolUse', source: 'project', sourcePath: 'C:\\repo\\.codex\\hooks.json', status: 'completed', entries: [{ kind: 'context', text: 'context that changed the model input' }] } },
              { run: { id: 'failed:exec-fixture-one', eventName: 'preToolUse', source: 'user', sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json', status: 'failed', statusMessage: 'Hook process exited with code 7', entries: [{ kind: 'warning', text: 'blocked by policy' }] } },
            ],
          },
        },
      },
    }

    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    assert.deepEqual([...fixture.document.querySelectorAll('[data-hook-trace-summary-row]')].map((row) => row.textContent), [
      'Before tool useFailed · Userhook exited with code 7',
      'After tool useAffected model · Project',
      'After tool useNo recorded model effect · User · 2 runs',
    ])

    trigger.click()
    await fixture.flush()
    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.equal(rows.length, 3, 'repeated identical silent runs around one activity should become one useful disclosure')
    assert.match(rows[0].querySelector('[data-hook-trace-row-trigger]').textContent, /Hook failed before Ran commands/i)
    assert.match(rows[1].querySelector('[data-hook-trace-row-trigger]').textContent, /Hook affected the model after Ran commands/i)
    assert.match(rows[2].querySelector('[data-hook-trace-row-trigger]').textContent, /Hook ran silently after Ran commands \(2x\)/i)
    const silentBody = rows[2].querySelector('[data-hook-trace-row-body]')
    assert.match(silentBody.querySelector('pre').textContent, /Codex recorded no model-visible text, command, or diagnostic output/i)
    assert.match(silentBody.querySelector('pre').textContent, /Hook Trace cannot determine what the hook did/i)
    assert.match(silentBody.querySelector('[data-hook-trace-source]').textContent, /hooks\.json/i)
  } finally {
    fixture.dispose()
  }
})

test('continuous display-identical silent runs batch across hidden execution target IDs', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-aaac-7aac-8aac-aaaaaaaaaaac'
    const rendered = makeTurn(fixture.document, turnId)
    const targets = Array.from({ length: 24 }, (_, index) => `exec-batch-${index + 1}`)
    rendered.activity.dataset.localConversationItemTargetIds = targets.join(' ')
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceBatchTest = {
      child: {
        memoizedProps: {
          turnId,
          turn: {
            turnId,
            hookRuns: targets.map((target, index) => ({
              run: {
                id: `silent-${index + 1}:${target}`,
                eventName: 'postToolUse',
                source: 'user',
                sourcePath: 'C:\\Users\\tester\\.codex\\hooks.json',
                status: 'completed',
                entries: [],
              },
            })),
          },
        },
      },
    }

    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()

    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.equal(rows.length, 1)
    assert.equal(rows[0].querySelector('[data-hook-trace-row-trigger]').textContent, 'Hook ran silently after Ran commands (24x)')
  } finally {
    fixture.dispose()
  }
})

test('activity controls remain native-only text when the execution target is the button itself', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-aaad-7aad-8aad-aaaaaaaaaaad'
    const rendered = makeTurn(fixture.document, turnId)
    delete rendered.activity.dataset.localConversationItemTargetIds
    rendered.activityButton.dataset.localConversationItemTargetIds = 'exec-fixture-one'

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()

    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.equal(rows.length, 3)
    assert.equal(rendered.activityButton.textContent, 'Ran commands')
    assert.equal(rows.every((row) => !rendered.activityButton.contains(row)), true)
    assert.equal(rows.every((row) => (row.textContent.match(/Hook|Added model context/g) || []).length === 1), true)
  } finally {
    fixture.dispose()
  }
})

test('live capture keeps a completed hook snapshot over stale pending props', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-eeee-7eee-8eee-eeeeeeeeeeee'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    const completed = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', completedAt: '2026-07-17T22:00:00Z', entries: [{ kind: 'context', text: 'completed context' }] } }] }
    const started = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'inProgress', entries: [] } }] }
    fixture.document.documentElement.__reactContainer$hookTraceStale = { child: { memoizedProps: { turnId, turn: completed }, pendingProps: { turnId, turn: started } } }
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    const user = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]')
    user.click()
    const row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(row.querySelector('pre').textContent, /completed context/)
    assert.match(row.querySelector('[data-hook-trace-status]').textContent, /Completed/i)
  } finally {
    fixture.dispose()
  }
})

test('an open live row refreshes when the same hook run completes', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-a111-7111-8111-111111111111'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    const run = { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'inProgress', entries: [] }
    fixture.document.documentElement.__reactContainer$hookTraceRefresh = { child: { memoizedProps: { turnId, turn: { turnId, hookRuns: [{ run }] } } } }
    const state = { threadId: snapshot(turnId).threadId, status: 'available', records: {} }
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]').click()
    let row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(row.querySelector('pre').textContent, /Codex recorded no model-visible text, command, or diagnostic output/)

    Object.assign(run, { status: 'completed', completedAt: '2026-07-17T22:05:00Z', entries: [{ kind: 'context', text: 'arrived on completion' }] })
    fixture.window.__codexHelperHookTrace.update(state)
    await fixture.flush()
    row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.equal(row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded'), 'true')
    assert.match(row.querySelector('pre').textContent, /arrived on completion/)
    assert.match(row.querySelector('[data-hook-trace-status]').textContent, /Completed/i)
  } finally {
    fixture.dispose()
  }
})

test('equal-scoring stale alternate props cannot overwrite current live hook content', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-a222-7222-8222-222222222222'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    const current = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'failed', statusMessage: 'new result', completedAt: '2026-07-17T22:10:00Z', entries: [{ kind: 'warning', text: 'new diagnostic' }] } }] }
    const stale = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'failed', statusMessage: 'old result', completedAt: '2026-07-17T22:10:00Z', entries: [{ kind: 'warning', text: 'old diagnostic' }] } }] }
    fixture.document.documentElement.__reactContainer$hookTraceTie = { child: { memoizedProps: { turnId, turn: current }, alternate: { memoizedProps: { turnId, turn: stale } } } }
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]').click()
    const row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(row.querySelector('[data-hook-trace-status]').textContent, /new result/)
    assert.doesNotMatch(row.querySelector('[data-hook-trace-status]').textContent, /old result/)

    current.hookRuns[0].run.statusMessage = 'newest result'
    current.hookRuns[0].run.entries = [{ kind: 'warning', text: 'newest diagnostic' }]
    fixture.window.__codexHelperHookTrace.update({ threadId: snapshot(turnId).threadId, status: 'available', records: {} })
    const refreshed = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(refreshed.querySelector('[data-hook-trace-status]').textContent, /newest result/)
  } finally {
    fixture.dispose()
  }
})

test('equal-scoring current alternate replaces a cached stale live hook snapshot', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-a223-7223-8223-222222222223'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    const stale = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'failed', statusMessage: 'old result', completedAt: '2026-07-17T22:10:00Z', entries: [{ kind: 'warning', text: 'old diagnostic' }] } }] }
    const current = { turnId, hookRuns: [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', status: 'failed', statusMessage: 'new result', completedAt: '2026-07-17T22:10:00Z', entries: [{ kind: 'warning', text: 'new diagnostic' }] } }] }
    const alternate = { memoizedProps: { turnId, turn: stale } }
    const cached = { memoizedProps: { turnId, turn: stale }, alternate }
    fixture.document.documentElement.__reactContainer$hookTraceReverseTie = { child: cached }
    const state = { threadId: snapshot(turnId).threadId, status: 'available', records: {} }
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]').click()
    let row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(row.querySelector('[data-hook-trace-status]').textContent, /old result/)

    alternate.memoizedProps = { turnId, turn: current }
    fixture.window.__codexHelperHookTrace.update(state)
    await fixture.flush()
    row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    assert.match(row.querySelector('[data-hook-trace-status]').textContent, /new result/)
    assert.doesNotMatch(row.querySelector('[data-hook-trace-status]').textContent, /old result/)
  } finally {
    fixture.dispose()
  }
})

test('user-side lifecycle rows surround the authored message at their exact insertion boundary', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-b111-7111-8111-111111111111'
    const rendered = makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, {
      state: {
        threadId: snapshot(turnId).threadId,
        status: 'available',
        records: {
          [turnId]: [
            { sequence: 0, kind: 'modelContext', side: 'user', label: 'Before your message', text: 'before text' },
            { sequence: 1, kind: 'modelContext', side: 'user', label: 'Prompt-submission context', text: 'after text' },
          ],
        },
      },
    })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]').click()
    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="user"]')]
    assert.equal(rows.length, 2)
    assert.equal(rows[0].nextElementSibling === rendered.userText, true)
    assert.equal(rendered.userText.nextElementSibling === rows[1], true)
    assert.match(rows[0].textContent, /Added model context: Before your message/)
    assert.match(rows[1].textContent, /Added model context: Prompt-submission context/)
  } finally {
    fixture.dispose()
  }
})

test('tool hook run IDs match colon-bearing target IDs on the smallest native surface', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-ffff-7fff-8fff-ffffffffffff'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.activity.dataset.localConversationItemTargetIds = 'group-one unrelated'
    const leaf = fixture.document.createElement('div')
    leaf.dataset.localConversationItemTargetIds = 'exec:fixture:one'
    rendered.activityButton.remove()
    leaf.append(rendered.activityButton)
    rendered.activityBody.remove()
    rendered.activity.append(leaf, rendered.activityBody)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceColonId = { child: { memoizedProps: { turnId, turn: { turnId, hookRuns: [{ run: { id: 'pre-base:exec:fixture:one', eventName: 'preToolUse', source: 'project', status: 'completed', entries: [] } }] } } } }
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]').click()
    const row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(leaf.children[0] === row, true)
  } finally {
    fixture.dispose()
  }
})

test('live hook capture refreshes as a turn accumulates later hook runs', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-bbbb-7bbb-8bbb-bbbbbbbbbbbb'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    const hookRuns = [{ run: { id: 'prompt', eventName: 'userPromptSubmit', source: 'user', entries: [{ kind: 'context', text: 'prompt context' }] } }]
    fixture.document.documentElement.__reactContainer$hookTraceIncremental = {
      child: { memoizedProps: { turnId, turn: { turnId, hookRuns } } },
    }
    const state = { threadId: snapshot(turnId).threadId, status: 'available', records: {} }
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    assert.equal(fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]'), null)

    hookRuns.push({ run: { id: 'pre:exec-fixture-one', eventName: 'preToolUse', source: 'project', entries: [] } })
    fixture.window.__codexHelperHookTrace.update(state)
    await fixture.flush()
    const response = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    assert.equal(response.hasAttribute('aria-disabled'), false)
    response.click()
    assert.match(fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')?.textContent || '', /Hook ran silently before Ran commands/)
  } finally {
    fixture.dispose()
  }
})

test('DOM mutation reconciliation does not repeatedly rescan an empty React hook tree', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-cccc-7ccc-8ccc-cccccccccccc'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    let reads = 0
    const fiber = { child: null }
    Object.defineProperty(fiber, 'memoizedProps', { get() { reads += 1; return { turnId, turn: { turnId, hookRuns: [] } } } })
    fixture.document.documentElement.__reactContainer$hookTraceEmpty = { child: fiber }
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()
    const readsAfterUpdate = reads
    for (let index = 0; index < 8; index += 1) rendered.answer?.append?.(fixture.document.createElement('span'))
    rendered.annotation.append(fixture.document.createElement('span'))
    await fixture.flush()
    assert.equal(reads, readsAfterUpdate)
  } finally {
    fixture.dispose()
  }
})

test('before-tool rows tolerate a native activity header button nested below the activity root', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-dddd-7ddd-8ddd-dddddddddddd'
    const rendered = makeTurn(fixture.document, turnId)
    const header = fixture.document.createElement('div')
    rendered.activityButton.remove()
    header.append(rendered.activityButton)
    rendered.activityBody.remove()
    rendered.activity.append(header, rendered.activityBody)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    assert.doesNotThrow(() => fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]').click())
    const first = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(rendered.activity.children[0] === first, true)
    assert.equal(rendered.activity.contains(rendered.activityButton), true)
  } finally {
    fixture.dispose()
  }
})

test('enabled Hook Trace renders only retained insertions as automatic collapsed folds', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c111-7111-8111-111111111111'
    makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()

    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline]')]
    assert.equal(rows.length, 4)
    assert.deepEqual(rows.map((row) => row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded')), ['false', 'false', 'false', 'false'])
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 2)

    const emptyFixture = createBrowserFixture()
    try {
      const emptyTurnId = '019f71d0-c112-7112-8112-111111111112'
      makeTurn(emptyFixture.document, emptyTurnId)
      executeRenderer(apply, emptyFixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
      await emptyFixture.flush()
      assert.equal(emptyFixture.document.querySelectorAll('[data-hook-trace-inline]').length, 0)
      assert.equal(emptyFixture.document.querySelectorAll('[data-hook-trace-trigger]').length, 0)
    } finally {
      emptyFixture.dispose()
    }
  } finally {
    fixture.dispose()
  }
})

test('hover groups lifecycle counts and reserves a non-overlapping gap around its trigger', async () => {
  const fixture = createBrowserFixture()
  try {
    fixture.window.innerWidth = 360
    fixture.window.innerHeight = 300
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger')) return { left: 168, right: 192, top: 130, bottom: 154, width: 24, height: 24 }
      if (node.hasAttribute?.('data-hook-trace-summary')) return { width: 320, height: 260 }
      return { width: 20, height: 20 }
    })
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c222-7222-8222-222222222222'
    makeTurn(fixture.document, turnId)
    const records = []
    for (let index = 0; index < 14; index++) records.push({ sequence: index, kind: 'modelContext', side: 'response', label: 'During Codex work', text: `during ${index}` })
    for (let index = 0; index < 8; index++) records.push({ sequence: 14 + index, kind: 'modelContext', side: 'response', label: 'After tool use', text: `after ${index}` })
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: { [turnId]: records } } })
    await fixture.flush()

    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const summary = fixture.document.querySelector('[data-hook-trace-summary]')
    assert.deepEqual([...summary.querySelectorAll('[data-hook-trace-summary-row]')].map((row) => row.textContent), [
      'After tool useRetained · 8 entries',
      'During Codex workRetained · 14 entries',
    ])
    const left = Number.parseFloat(summary.style.left)
    const top = Number.parseFloat(summary.style.top)
    const width = Number.parseFloat(summary.style.width)
    const height = Number.parseFloat(summary.style.maxHeight)
    const separated = top + height <= 124 || top >= 160 || left + width <= 162 || left >= 198
    assert.equal(separated, true, `summary must not overlap trigger: ${JSON.stringify({ left, top, width, height })}`)
  } finally {
    fixture.dispose()
  }
})

test('hover aggregates runs by displayed lifecycle and source instead of hidden source path', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c223-7223-8223-222222222223'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceGroupedRuns = {
      child: {
        memoizedProps: {
          turnId,
          turn: {
            turnId,
            hookRuns: [
              { run: { id: 'pre-one:exec-fixture-one', eventName: 'preToolUse', source: 'user', sourcePath: 'C:\\one\\hooks.json', entries: [] } },
              { run: { id: 'pre-two:exec-fixture-one', eventName: 'preToolUse', source: 'user', sourcePath: 'C:\\two\\hooks.json', entries: [] } },
              { run: { id: 'post-one:exec-fixture-one', eventName: 'postToolUse', source: 'user', sourcePath: 'C:\\one\\hooks.json', entries: [] } },
            ],
          },
        },
      },
    }
    executeRenderer(apply, fixture, { state: { threadId: snapshot(turnId).threadId, status: 'available', records: {} } })
    await fixture.flush()

    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()

    assert.deepEqual([...fixture.document.querySelectorAll('[data-hook-trace-summary-row]')].map((row) => row.textContent), [
      'Before tool useNo recorded model effect · User · 2 runs',
      'After tool useNo recorded model effect · User',
    ])
  } finally {
    fixture.dispose()
  }
})

test('opening Hook Trace dismisses a surviving native Hooks panel before showing its own', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c224-7224-8224-222222222224'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    const nativePanel = fixture.document.createElement('div')
    nativePanel.setAttribute('role', 'tooltip')
    nativePanel.dataset.nativeHooksPanel = 'true'
    nativePanel.textContent = 'HooksPreToolUseUser'
    nativeHookControl.addEventListener('pointerout', () => nativePanel.remove())
    rendered.responseActions.append(nativeHookControl)
    fixture.document.body.append(nativePanel)

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()

    assert.equal(nativePanel.isConnected, false)
    assert.equal(fixture.document.querySelectorAll('[role="tooltip"]').length, 1)
    assert.ok(fixture.document.querySelector('[data-hook-trace-summary]'))
  } finally {
    fixture.dispose()
  }
})

test('bulk folding resolves mixed state before collapsing all', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c333-7333-8333-333333333333'
    makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')

    rows[0].querySelector('[data-hook-trace-row-trigger]').click()
    assert.deepEqual(rows.map((row) => row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded')), ['true', 'false', 'false'])
    trigger.click()
    assert.deepEqual(rows.map((row) => row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded')), ['true', 'true', 'true'])
    assert.equal(trigger.getAttribute('aria-pressed'), 'true')
    trigger.click()
    assert.deepEqual(rows.map((row) => row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded')), ['false', 'false', 'false'])
    assert.equal(trigger.getAttribute('aria-pressed'), 'false')
  } finally {
    fixture.dispose()
  }
})

test('expanded body collapses only on a safe stationary background click', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c444-7444-8444-444444444444'
    makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="user"]')
    const fold = row.querySelector('[data-hook-trace-row-trigger]')
    fold.click()
    let body = row.querySelector('[data-hook-trace-row-body]')
    const pre = body.querySelector('pre')
    const source = body.querySelector('[data-hook-trace-source]')

    pre.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse', clientX: 40, clientY: 80 }))
    pre.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 40, clientY: 80 }))
    assert.equal(fold.getAttribute('aria-expanded'), 'true')

    source.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse', clientX: 40, clientY: 90 }))
    source.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 40, clientY: 90 }))
    assert.equal(fold.getAttribute('aria-expanded'), 'true')

    fixture.window.getSelection = () => ({ isCollapsed: false, toString: () => 'selected text' })
    body.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse', clientX: 40, clientY: 80 }))
    body.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 40, clientY: 80 }))
    assert.equal(fold.getAttribute('aria-expanded'), 'true')

    fixture.window.getSelection = () => ({ isCollapsed: true, toString: () => '' })
    body.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse', clientX: 40, clientY: 80 }))
    body.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 70, clientY: 110 }))
    assert.equal(fold.getAttribute('aria-expanded'), 'true')

    body.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse', clientX: 40, clientY: 80 }))
    body.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 42, clientY: 82 }))
    assert.equal(fold.getAttribute('aria-expanded'), 'false')
    assert.equal(row.querySelector('[data-hook-trace-row-body]'), null)
  } finally {
    fixture.dispose()
  }
})

test('bulk layout changes preserve the initiating interaction point in the nearest scroll container', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c555-7555-8555-555555555555'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.stack.style.overflowY = 'auto'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 1200
    rendered.stack.scrollTop = 300
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        const top = 200 + (expanded ? 80 : 0) - (rendered.stack.scrollTop - 300)
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    assert.equal(trigger.getBoundingClientRect().top, 200, 'the interacted control must not move before the next paint')
    await fixture.flush()
    assert.equal(rendered.stack.scrollTop, 380)
    assert.equal(trigger.getBoundingClientRect().top, 200)
  } finally {
    fixture.dispose()
  }
})

test('clamped conversation scrolling never cascades into an unrelated window scroll', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c557-7557-8557-555555555557'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.stack.style.overflowY = 'auto'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 1200
    rendered.stack.scrollTop = 800
    const documentScroller = fixture.document.documentElement
    fixture.document.scrollingElement = documentScroller
    documentScroller.style.overflowY = 'auto'
    documentScroller.clientHeight = 600
    documentScroller.scrollHeight = 2400
    documentScroller.scrollTop = 300
    let windowScrollTop = 300
    let globalScrollCalls = 0
    fixture.window.scrollBy = (_x, y) => {
      globalScrollCalls++
      windowScrollTop = Math.min(1200, windowScrollTop + y)
    }
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        const top = 200 + (expanded ? 80 : 0) - (rendered.stack.scrollTop - 800)
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    await fixture.flush()

    assert.equal(rendered.stack.scrollTop, 800, 'the real conversation owner is clamped at its boundary')
    assert.equal(documentScroller.scrollTop, 300, 'residual movement must never cascade into the document scroller')
    assert.equal(windowScrollTop, 300, 'an unrelated document viewport must not be pushed toward its end')
    assert.equal(globalScrollCalls, 0)
  } finally {
    fixture.dispose()
  }
})

test('a programmatically scrollable conversation owner is used even when its CSS overflow is hidden', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c55a-755a-855a-55555555555a'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.stack.style.overflowY = 'hidden'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 1200
    rendered.stack.scrollTop = 300
    const documentScroller = fixture.document.documentElement
    fixture.document.scrollingElement = documentScroller
    documentScroller.style.overflowY = 'auto'
    documentScroller.clientHeight = 600
    documentScroller.scrollHeight = 2400
    documentScroller.scrollTop = 300
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        const top = 200 + (expanded ? 80 : 0) - (rendered.stack.scrollTop - 300)
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    await fixture.flush()

    assert.equal(rendered.stack.scrollTop, 380)
    assert.equal(documentScroller.scrollTop, 300)
    assert.equal(trigger.getBoundingClientRect().top, 200)
  } finally {
    fixture.dispose()
  }
})

test('bulk expansion opens a collapsed native work fold before opening the Hook Trace rows inside it', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c559-7559-8559-555555555559'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeFold = fixture.document.createElement('div')
    nativeFold.dataset.fixtureNativeWorkFold = 'true'
    const nativeTrigger = fixture.document.createElement('button')
    nativeTrigger.textContent = 'Worked for 2m 46s'
    nativeTrigger.setAttribute('aria-expanded', 'false')
    const unrelatedCollapsedControl = fixture.document.createElement('button')
    unrelatedCollapsedControl.textContent = 'Other collapsed work'
    unrelatedCollapsedControl.setAttribute('aria-expanded', 'false')
    const nativeBody = fixture.document.createElement('div')
    nativeBody.dataset.fixtureNativeWorkFoldBody = 'true'
    nativeBody.style.display = 'none'
    rendered.activityButton.setAttribute('aria-expanded', 'false')
    rendered.stack.append(nativeFold)
    nativeFold.append(nativeTrigger, unrelatedCollapsedControl, nativeBody)
    nativeBody.append(rendered.activity)
    nativeTrigger.addEventListener('click', () => {
      nativeTrigger.setAttribute('aria-expanded', 'true')
      nativeBody.style.display = 'block'
    })

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const bulk = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    const nestedRows = [...nativeBody.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.ok(nestedRows.length > 0)
    assert.equal(nativeTrigger.getAttribute('aria-expanded'), 'false')

    bulk.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 12, clientY: 12 }))
    await fixture.flush()

    assert.equal(nativeTrigger.getAttribute('aria-expanded'), 'true', 'the owning native work fold must open first')
    assert.deepEqual(nestedRows.map(rowIsOpen), nestedRows.map(() => true), 'all nested Hook Trace rows must then open')
  } finally {
    fixture.dispose()
  }
})

test('bulk expansion recognizes a collapsed native work control that is itself an ancestor of Hook Trace rows', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c560-7560-8560-555555555560'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeFold = fixture.document.createElement('div')
    nativeFold.setAttribute('role', 'button')
    nativeFold.setAttribute('aria-expanded', 'false')
    nativeFold.dataset.fixtureNativeWorkFold = 'true'
    const nativeLabel = fixture.document.createElement('span')
    nativeLabel.textContent = 'Worked for 2m 46s'
    const nativeBody = fixture.document.createElement('div')
    nativeBody.dataset.fixtureNativeWorkFoldBody = 'true'
    rendered.stack.append(nativeFold)
    nativeFold.append(nativeLabel, nativeBody)
    nativeBody.append(rendered.activity)
    nativeFold.addEventListener('click', () => nativeFold.setAttribute('aria-expanded', 'true'))

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const bulk = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    const nestedRows = [...nativeBody.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.ok(nestedRows.length > 0)

    bulk.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 12, clientY: 12 }))
    await fixture.flush()

    assert.equal(nativeFold.getAttribute('aria-expanded'), 'true')
    assert.deepEqual(nestedRows.map(rowIsOpen), nestedRows.map(() => true))
  } finally {
    fixture.dispose()
  }
})

test('Hook Trace action clicks do not bubble into Codex action-surface handlers', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c558-7558-8558-555555555558'
    const rendered = makeTurn(fixture.document, turnId)
    let owningSurfaceClicks = 0
    rendered.responseActions.addEventListener('click', () => owningSurfaceClicks++)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()

    const bulk = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    bulk.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 12, clientY: 12 }))
    const fold = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
      .querySelector('[data-hook-trace-row-trigger]')
    fold.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 12, clientY: 12 }))

    assert.equal(owningSurfaceClicks, 0)
  } finally {
    fixture.dispose()
  }
})

test('interaction-point compensation settles after one delayed native scroll adjustment', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c556-7555-8555-555555555555'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.stack.style.overflowY = 'auto'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 1200
    let scrollTop = 300
    let delayedAdjustmentScheduled = false
    Object.defineProperty(rendered.stack, 'scrollTop', {
      configurable: true,
      get: () => scrollTop,
      set: (value) => {
        scrollTop = value
        if (value !== 300 && !delayedAdjustmentScheduled) {
          delayedAdjustmentScheduled = true
          setTimeout(() => { scrollTop += 18 }, 0)
        }
      },
    })
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        const top = 200 + (expanded ? 80 : 0) - (scrollTop - 300)
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    await fixture.flush()
    assert.equal(scrollTop, 380)
    assert.equal(trigger.getBoundingClientRect().top, 200)
  } finally {
    fixture.dispose()
  }
})

test('streaming reconciliation preserves only the exact expanded record when sequences collide', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c666-7666-8666-666666666666'
    makeTurn(fixture.document, turnId)
    const state = {
      threadId: snapshot(turnId).threadId,
      status: 'available',
      records: {
        [turnId]: [
          { sequence: 0, kind: 'modelContext', side: 'user', label: 'Before your message', text: 'first collision' },
          { sequence: 0, kind: 'modelContext', side: 'user', label: 'Prompt-submission context', text: 'second collision' },
        ],
      },
    }
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    let rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="user"]')]
    rows[1].querySelector('[data-hook-trace-row-trigger]').click()
    assert.deepEqual(rows.map(rowIsOpen), [false, true])

    fixture.window.__codexHelperHookTrace.update({
      ...state,
      records: {
        [turnId]: [...state.records[turnId], { sequence: 1, kind: 'modelContext', side: 'user', label: 'During Codex work', text: 'streamed later' }],
      },
    })
    await fixture.flush()
    rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="user"]')]
    assert.deepEqual(rows.map(rowIsOpen), [false, true, false])
  } finally {
    fixture.dispose()
  }
})

test('live hook data upgrades only its matching saved occurrence and preserves lifecycle summary order', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c777-7777-8777-777777777777'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceMerge = {
      child: {
        memoizedProps: {
          turnId,
          turn: {
            turnId,
            hookRuns: [{ run: { id: 'post:exec-fixture-one', eventName: 'postToolUse', source: 'project', entries: [{ kind: 'context', text: 'same retained text' }] } }],
          },
        },
      },
    }
    executeRenderer(apply, fixture, {
      state: {
        threadId: snapshot(turnId).threadId,
        status: 'available',
        records: {
          [turnId]: [
            { sequence: 9, kind: 'modelContext', side: 'response', label: 'Before tool use', activityIndex: 0, text: 'same retained text' },
            { sequence: 0, kind: 'modelContext', side: 'response', label: 'After tool use', activityIndex: 0, text: 'same retained text' },
          ],
        },
      },
    })
    await fixture.flush()

    const rows = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
    assert.equal(rows.length, 2)
    assert.match(rows[0].textContent, /Added model context: Before tool use/)
    assert.match(rows[1].textContent, /Hook affected the model after Ran commands/)

    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    assert.deepEqual([...fixture.document.querySelectorAll('[data-hook-trace-summary-row]')].map((row) => row.textContent), [
      'Before tool useRetained',
      'After tool useAffected model · Project',
    ])
  } finally {
    fixture.dispose()
  }
})

test('native Hooks controls stay suppressed only while Hook Trace safely replaces them', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c778-7778-8778-777777777778'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    assert.equal(nativeHookControl.getAttribute('data-hook-trace-native-suppressed'), 'true')
    assert.match(fixture.document.querySelector('[data-hook-trace-style]').textContent, /\[data-hook-trace-native-suppressed="true"\]\s*\{[^}]*display\s*:\s*none\s*!important/i)

    let suppressionRemovalCount = 0
    const removeAttribute = nativeHookControl.removeAttribute.bind(nativeHookControl)
    nativeHookControl.removeAttribute = (name) => {
      if (name === 'data-hook-trace-native-suppressed') suppressionRemovalCount++
      return removeAttribute(name)
    }
    fixture.window.__codexHelperHookTrace.reconcile()
    assert.equal(suppressionRemovalCount, 0)
    assert.equal(nativeHookControl.getAttribute('data-hook-trace-native-suppressed'), 'true')

    const replacement = fixture.document.createElement('button')
    replacement.setAttribute('aria-label', 'Hooks')
    nativeHookControl.remove()
    rendered.responseActions.append(replacement)
    await fixture.flush()
    assert.equal(replacement.getAttribute('data-hook-trace-native-suppressed'), 'true')

    fixture.window.__codexHelperHookTrace.update({ threadId: snapshot(turnId).threadId, status: 'available', records: {} })
    await fixture.flush()
    assert.equal(replacement.hasAttribute('data-hook-trace-native-suppressed'), false)

    fixture.window.__codexHelperHookTrace.update(snapshot(turnId))
    await fixture.flush()
    fixture.window.__codexHelperHookTrace.cleanup()
    assert.equal(replacement.hasAttribute('data-hook-trace-native-suppressed'), false)
  } finally {
    fixture.dispose()
  }
})

test('Hook Trace occupies the native Hooks wrapper position instead of appending after metadata', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c780-7780-8780-777777777780'
    const rendered = makeTurn(fixture.document, turnId)
    const nativeWrapper = fixture.document.createElement('span')
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    nativeWrapper.append(nativeHookControl)
    const copyWrapper = rendered.copy.parentElement
    copyWrapper.remove()
    rendered.responseActions.append(nativeWrapper, copyWrapper)

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()

    const slot = fixture.document.querySelector('[data-hook-trace-slot][data-hook-side="response"]')
    assert.equal(slot.parentElement, nativeWrapper)
    assert.equal(slot.nextElementSibling, nativeHookControl)
    assert.equal(nativeHookControl.getAttribute('data-hook-trace-native-suppressed'), 'true')
    assert.equal(rendered.responseActions.children[0], nativeWrapper)
  } finally {
    fixture.dispose()
  }
})

test('a collapsed response can mount Hook Trace from the native Hooks control without a visible Copy control', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c781-7781-8781-777777777781'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.responseActions.replaceChildren()
    const nativeWrapper = fixture.document.createElement('span')
    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    nativeWrapper.append(nativeHookControl)
    rendered.responseActions.append(nativeWrapper)

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()

    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    assert.ok(trigger)
    assert.equal(trigger.closest('[data-hook-trace-slot]').parentElement, nativeWrapper)
    assert.equal(nativeHookControl.getAttribute('data-hook-trace-native-suppressed'), 'true')
  } finally {
    fixture.dispose()
  }
})

test('native Hooks suppression never crosses from a replaced side through a shared visibility holder', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c779-7779-8779-777777777779'
    const rendered = makeTurn(fixture.document, turnId)
    const sharedVisibilityHolder = fixture.document.createElement('div')
    sharedVisibilityHolder.setAttribute('class', 'opacity-0 group-hover:opacity-100')
    sharedVisibilityHolder.append(rendered.userActions, rendered.responseActions)
    rendered.turn.append(sharedVisibilityHolder)

    const userNativeHookControl = fixture.document.createElement('button')
    userNativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.userActions.append(userNativeHookControl)
    const responseNativeHookControl = fixture.document.createElement('button')
    responseNativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(responseNativeHookControl)

    const state = snapshot(turnId)
    state.records[turnId] = state.records[turnId].filter((record) => record.side === 'user')
    executeRenderer(apply, fixture, { state })
    await fixture.flush()

    assert.equal(userNativeHookControl.getAttribute('data-hook-trace-native-suppressed'), 'true')
    assert.equal(responseNativeHookControl.hasAttribute('data-hook-trace-native-suppressed'), false)
  } finally {
    fixture.dispose()
  }
})

test('collapsing one side keeps a shared visibility holder active while the other side remains expanded', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c782-7782-8782-777777777782'
    const rendered = makeTurn(fixture.document, turnId)
    const sharedVisibilityHolder = fixture.document.createElement('div')
    sharedVisibilityHolder.setAttribute('class', 'opacity-0 group-hover:opacity-100')
    sharedVisibilityHolder.append(rendered.userActions, rendered.responseActions)
    rendered.turn.append(sharedVisibilityHolder)

    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const user = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]')
    const response = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    user.click()
    response.click()
    assert.equal(sharedVisibilityHolder.getAttribute('data-hook-trace-visibility-active'), 'true')

    user.click()
    assert.equal(response.getAttribute('aria-pressed'), 'true')
    assert.equal(rendered.responseActions.getAttribute('data-hook-trace-actions-active'), 'true')
    assert.equal(sharedVisibilityHolder.getAttribute('data-hook-trace-visibility-active'), 'true')
  } finally {
    fixture.dispose()
  }
})

test('removing a hovered Hook Trace trigger also removes its owned summary', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c783-7783-8783-777777777783'
    makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="user"]')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    assert.ok(fixture.document.querySelector('[data-hook-trace-summary]'))

    fixture.window.__codexHelperHookTrace.update({ threadId: snapshot(turnId).threadId, status: 'available', records: {} })
    await fixture.flush()
    assert.equal(fixture.document.querySelector('[data-hook-trace-summary]'), null)
  } finally {
    fixture.dispose()
  }
})

test('partially unanchorable records do not rebuild the rows that were placed safely', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c784-7784-8784-777777777784'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.finalAssistant.remove()
    const state = snapshot(turnId)
    state.records[turnId] = state.records[turnId].filter((record) => record.side === 'response' && ['Before tool use', 'Stop context'].includes(record.label))
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const before = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    assert.ok(before)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]').length, 1)

    fixture.window.__codexHelperHookTrace.reconcile()
    const after = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(after, before)
  } finally {
    fixture.dispose()
  }
})

test('scroll compensation uses the nearest overflow container even when expansion makes it scrollable', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c888-7888-8888-888888888888'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.stack.style.overflowY = 'auto'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 400
    rendered.stack.scrollTop = 0
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        if (expanded) rendered.stack.scrollHeight = 1200
        const top = 200 + (expanded ? 80 : 0) - rendered.stack.scrollTop
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    await fixture.flush()
    assert.equal(rendered.stack.scrollTop, 80)
    assert.equal(trigger.getBoundingClientRect().top, 200)
  } finally {
    fixture.dispose()
  }
})

test('records without a safe inline anchor do not leave a dead Hook control', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-c999-7999-8999-999999999999'
    const rendered = makeTurn(fixture.document, turnId)
    rendered.userBubble.remove()
    executeRenderer(apply, fixture, {
      state: {
        threadId: snapshot(turnId).threadId,
        status: 'available',
        records: { [turnId]: [{ sequence: 0, kind: 'modelContext', side: 'user', label: 'Prompt-submission context', text: 'orphaned' }] },
      },
    })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="user"]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-hook-trace-trigger][data-hook-side="user"]').length, 0)
  } finally {
    fixture.dispose()
  }
})

test('a saved fold stays expanded when matching live provenance arrives', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-ca11-7a11-8a11-111111111111'
    const rendered = makeTurn(fixture.document, turnId)
    const state = {
      threadId: snapshot(turnId).threadId,
      status: 'available',
      records: { [turnId]: [{ sequence: 0, kind: 'modelContext', side: 'response', label: 'After tool use', activityIndex: 0, text: 'upgraded context' }] },
    }
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    let row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    row.querySelector('[data-hook-trace-row-trigger]').click()
    assert.equal(rowIsOpen(row), true)

    const nativeHookControl = fixture.document.createElement('button')
    nativeHookControl.setAttribute('aria-label', 'Hooks')
    rendered.responseActions.append(nativeHookControl)
    fixture.document.documentElement.__reactContainer$hookTraceUpgrade = {
      child: { memoizedProps: { turnId, turn: { turnId, hookRuns: [{ run: { id: 'post:exec-fixture-one', eventName: 'postToolUse', source: 'project', entries: [{ kind: 'context', text: 'upgraded context' }] } }] } } },
    }
    fixture.window.__codexHelperHookTrace.update(state)
    await fixture.flush()
    row = fixture.document.querySelector('[data-hook-trace-inline][data-hook-side="response"]')
    assert.equal(rowIsOpen(row), true)
    assert.match(row.textContent, /Hook affected the model after Ran commands/)
  } finally {
    fixture.dispose()
  }
})

test('scroll compensation skips an inert inner overflow wrapper for the usable outer scroller', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f71d0-ca22-7a22-8a22-222222222222'
    const rendered = makeTurn(fixture.document, turnId)
    const outer = fixture.document.createElement('div')
    outer.style.overflowY = 'auto'
    outer.clientHeight = 400
    outer.scrollHeight = 1200
    outer.scrollTop = 300
    rendered.turn.remove()
    fixture.document.body.append(outer)
    outer.append(rendered.turn)
    rendered.stack.style.overflowY = 'auto'
    rendered.stack.clientHeight = 400
    rendered.stack.scrollHeight = 400
    rendered.stack.scrollTop = 0
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-hook-trace-trigger') && node.getAttribute('data-hook-side') === 'response') {
        const expanded = [...fixture.document.querySelectorAll('[data-hook-trace-inline][data-hook-side="response"]')]
          .some((row) => row.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
        const top = 200 + (expanded ? 80 : 0) - (outer.scrollTop - 300)
        return { left: 500, right: 524, top, bottom: top + 24, width: 24, height: 24 }
      }
      return { left: 0, right: 20, top: 0, bottom: 20, width: 20, height: 20 }
    })
    executeRenderer(apply, fixture, { state: snapshot(turnId) })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-hook-trace-trigger][data-hook-side="response"]')
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true, clientX: 512, clientY: 212 }))
    await fixture.flush()
    assert.equal(rendered.stack.scrollTop, 0)
    assert.equal(outer.scrollTop, 380)
    assert.equal(trigger.getBoundingClientRect().top, 200)
  } finally {
    fixture.dispose()
  }
})

function rowIsOpen(row) {
  return row.querySelector('[data-hook-trace-row-trigger]').getAttribute('aria-expanded') === 'true'
}
