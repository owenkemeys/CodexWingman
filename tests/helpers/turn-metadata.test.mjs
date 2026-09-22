import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/turn-metadata/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

test('Turn Metadata package declares the target-scoped readable contract', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'turn-metadata',
    name: 'Turn Metadata',
    version: '1.0.7',
    description: 'Shows recorded model and generation details for each completed assistant turn.',
    refreshSeconds: 5,
    capabilities: ['files.codexTargetSession'],
    entrypoints: { backend: 'backend.js', apply: 'apply.js', remove: 'remove.js' },
  })
  const info = await requiredFile('HELPER_INFO.md')
  assert.match(info, /^# Turn Metadata/m)
  assert.match(info, /## Purpose and behavior/)
  assert.match(info, /## Sharp edges and failure behavior/)
  assert.match(info, /files\.codexTargetSession/)
})

test('backend returns only target-scoped structured metadata and fails closed when unavailable', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { JSON, Object })
  const snapshot = {
    threadId: '019f61c0-3333-7333-8333-333333333333',
    records: {
      '019f61c0-4444-7444-8444-444444444444': {
        turnId: '019f61c0-4444-7444-8444-444444444444',
        model: 'gpt-5.6-sol',
        providerId: 'openai',
        providerLabel: 'OpenAI',
        completeness: 'partial',
        subagents: [],
      },
    },
  }
  const available = refresh({
    files: { codexTargetSession: { turnMetadata: () => snapshot } },
  })
  assert.deepEqual(JSON.parse(JSON.stringify(available)), snapshot)
  assert.deepEqual(JSON.parse(JSON.stringify(refresh({
    files: { codexTargetSession: { turnMetadata: () => null } },
  }))), { threadId: null, records: {} })
})

function makeTurn(document, turnId, { completed = true } = {}) {
  const surface = document.createElement('section')
  surface.dataset.requestUserInputAutoResolutionConversationId = '019f61c0-3333-7333-8333-333333333333'
  const turn = document.createElement('div')
  turn.dataset.turnKey = turnId
  const finalAssistant = document.createElement('div')
  if (completed) finalAssistant.dataset.localConversationFinalAssistant = 'true'
  const annotation = document.createElement('div')
  annotation.dataset.responseAnnotationTarget = `item-${turnId}`
  const content = document.createElement('div')
  content.textContent = `response for ${turnId}`
  const toolbar = document.createElement('div')
  toolbar.dataset.fixtureResponseActions = 'true'
  const footer = document.createElement('div')
  const copyWrapper = document.createElement('span')
  copyWrapper.dataset.state = 'closed'
  const copy = document.createElement('button')
  copy.setAttribute('aria-label', 'Copy')
  copyWrapper.append(copy)
  const forkWrapper = document.createElement('span')
  const fork = document.createElement('button')
  fork.setAttribute('aria-label', 'Continue in new task from here')
  forkWrapper.append(fork)
  toolbar.append(copyWrapper, forkWrapper)
  const timestamp = document.createElement('span')
  timestamp.dataset.assistantMessageSentTime = 'true'
  timestamp.setAttribute('class', 'ml-1.5 flex h-full items-center opacity-0 group-focus-within:opacity-100 group-hover:opacity-100')
  timestamp.textContent = '18:27'
  footer.append(toolbar, timestamp)
  annotation.append(content, footer)
  finalAssistant.append(annotation)
  turn.append(finalAssistant)
  surface.append(turn)
  document.body.append(surface)
  return { surface, turn, finalAssistant, annotation, footer, toolbar, timestamp, copy, copyWrapper }
}

function completeRecord(turnId) {
  return {
    turnId,
    model: 'gpt-5.6-sol',
    providerId: 'openai',
    providerLabel: 'OpenAI',
    startedAt: Date.parse('2026-07-14T16:01:00Z'),
    completedAt: Date.parse('2026-07-14T16:01:06Z'),
    durationMs: 6000,
    tokenUsage: {
      inputTokens: 101,
      cachedInputTokens: 11,
      outputTokens: 17,
      reasoningOutputTokens: 3,
      totalTokens: 121,
    },
    subagents: [{
      threadId: '019f61c0-6666-7666-8666-666666666666',
      model: 'gpt-5.6-luna',
      reasoningEffort: 'high',
      status: 'completed',
    }],
    completeness: 'complete',
  }
}

test('current virtualized history resolves exact entry identity and current native action labels', async () => {
  const fixture = createBrowserFixture()
  try {
    const id = '019f61c0-4444-7444-8444-444444444444'
    const threadId = '019f61c0-3333-7333-8333-333333333333'
    const key = 'history-content:tail:0:local:11111111-2222-4333-8444-555555555555'
    const matching = makeTurn(fixture.document, key)
    matching.annotation.setAttribute('data-response-annotation-conversation', threadId)
    matching.toolbar.querySelector('button[aria-label="Continue in new task from here"]').setAttribute('aria-label', 'Fork chat from here')
    const entry = { turnKey: key, conversationId: threadId, turnId: id, turn: { turnId: id } }
    matching.turn.__reactFiber$fixture = { memoizedProps: {}, return: { memoizedProps: { entry }, return: null } }
    executeRenderer(await requiredFile('apply.js'), fixture, { state: { threadId, records: { [id]: completeRecord(id) } } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 1)
    assert.equal(matching.toolbar.querySelector('[data-turn-metadata-trigger]').getAttribute('data-turn-id'), id)
    assert.deepEqual(Array.from(fixture.window.__codexHelperTurnMetadata.status().visibleRecordIds), [id])
    entry.conversationId = '019f61c0-7777-7777-8777-777777777777'
    fixture.window.__codexHelperTurnMetadata.reconcile()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 0)
    entry.conversationId = threadId
    entry.turnKey = 'different-row'
    fixture.window.__codexHelperTurnMetadata.reconcile()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 0)
    entry.turnKey = key
    entry.turn.turnId = '019f61c0-7777-7777-8777-777777777777'
    fixture.window.__codexHelperTurnMetadata.reconcile()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 0)
  } finally { fixture.dispose() }
})

test('renderer places metadata and timestamp inside the native action holder with one shared visibility owner', async () => {
  const fixture = createBrowserFixture()
  try {
    const source = await requiredFile('apply.js')
    const matchingId = '019f61c0-4444-7444-8444-444444444444'
    const missingId = '019f61c0-5555-7555-8555-555555555555'
    const matching = makeTurn(fixture.document, matchingId)
    const unrelatedCopy = fixture.document.createElement('button')
    unrelatedCopy.setAttribute('aria-label', 'Copy')
    matching.annotation.children[0].appendChild(unrelatedCopy)
    makeTurn(fixture.document, missingId)
    makeTurn(fixture.document, '019f61c0-7777-7777-8777-777777777777', { completed: false })
    executeRenderer(source, fixture, {
      state: { threadId: '019f61c0-3333-7333-8333-333333333333', records: { [matchingId]: completeRecord(matchingId) } },
    })
    await fixture.flush()

    const controls = fixture.document.querySelectorAll('[data-turn-metadata-trigger]')
    const trigger = controls[0]
    assert.equal(controls.length, 1)
    assert.equal(controls[0].textContent, 'gpt-5.6-sol')
    assert.equal(controls[0].getAttribute('aria-label'), 'Show response metadata for gpt-5.6-sol')
    assert.equal(controls[0].closest('[data-turn-key]').getAttribute('data-turn-key'), matchingId)
    assert.equal(trigger.parentElement.getAttribute('data-codex-wingman-owner'), 'turn-metadata')
    assert.equal(matching.toolbar.children[0] === matching.copyWrapper, true)
    assert.equal(matching.toolbar.children[2] === trigger.parentElement, true)
    assert.equal(matching.toolbar.children[3] === matching.timestamp, true)
    assert.equal(matching.footer.children.length, 1)
    assert.equal(trigger.parentElement.getAttribute('class') || '', '')
    assert.equal(matching.timestamp.getAttribute('data-turn-metadata-relocated'), 'true')
    assert.doesNotMatch(matching.timestamp.getAttribute('class') || '', /(?:^|:)opacity-/)
    const style = fixture.document.querySelector('[data-turn-metadata-style]')
    assert.match(style.textContent, /color:\s*var\(--color-token-text-tertiary/)
  } finally {
    fixture.dispose()
  }
})

test('renderer panel shows recorded sections, remains idempotent, and cleanup preserves native actions', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const remove = await requiredFile('remove.js')
    const turnId = '019f61c0-4444-7444-8444-444444444444'
    const rendered = makeTurn(fixture.document, turnId)
    const options = {
      state: { threadId: '019f61c0-3333-7333-8333-333333333333', records: { [turnId]: completeRecord(turnId) } },
    }
    executeRenderer(apply, fixture, options)
    executeRenderer(apply, fixture, options)
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 1)

    const trigger = fixture.document.querySelector('[data-turn-metadata-trigger]')
    trigger.click()
    await fixture.flush()
    const panel = fixture.document.querySelector('[data-turn-metadata-panel]')
    assert.ok(panel)
    assert.match(panel.textContent, /gpt-5\.6-sol/)
    assert.match(panel.textContent, /OpenAI/)
    assert.match(panel.textContent, /6 seconds/)
    assert.match(panel.textContent, /Cached input/)
    assert.match(panel.textContent, /Reasoning/)
    assert.match(panel.textContent, /019f61c0-6666-7666-8666-666666666666/)

    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelectorAll('[data-codex-wingman-owner="turn-metadata"]').length, 0)
    assert.equal(rendered.toolbar.querySelectorAll('button[aria-label="Copy"]').length, 1)
    assert.equal(rendered.copy.isConnected, true)
    assert.equal(rendered.footer.children[0] === rendered.toolbar, true)
    assert.equal(rendered.footer.children[1] === rendered.timestamp, true)
    assert.match(rendered.timestamp.getAttribute('class') || '', /(?:^|\s)opacity-0(?:\s|$)/)
    assert.equal(rendered.timestamp.getAttribute('data-turn-metadata-relocated'), null)
  } finally {
    fixture.dispose()
  }
})

test('standalone remove restores a relocated native timestamp when the controller global is missing', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const remove = await requiredFile('remove.js')
    const turnId = '019f61c0-4444-7444-8444-444444444444'
    const rendered = makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, {
      state: { threadId: '019f61c0-3333-7333-8333-333333333333', records: { [turnId]: completeRecord(turnId) } },
    })
    await fixture.flush()
    const orphanedController = fixture.window.__codexHelperTurnMetadata
    delete fixture.window.__codexHelperTurnMetadata

    executeRenderer(remove, fixture)

    assert.equal(rendered.footer.children[0] === rendered.toolbar, true)
    assert.equal(rendered.footer.children[1] === rendered.timestamp, true)
    assert.match(rendered.timestamp.getAttribute('class') || '', /(?:^|\s)group-hover:opacity-100(?:\s|$)/)
    assert.equal(rendered.timestamp.getAttribute('data-turn-metadata-relocated'), null)
    orphanedController.cleanup()
  } finally {
    fixture.dispose()
  }
})

test('renderer supports hover delay, focus, Escape, coarse-style tap, and viewport-clamped positioning', async () => {
  const fixture = createBrowserFixture()
  try {
    fixture.window.innerWidth = 375
    fixture.window.innerHeight = 812
    fixture.setRectResolver((node) => {
      if (node.hasAttribute?.('data-turn-metadata-trigger')) return { left: 340, right: 364, top: 740, bottom: 764, width: 24, height: 24 }
      if (node.hasAttribute?.('data-turn-metadata-panel')) return { width: 351, height: 350 }
      return { width: 20, height: 20 }
    })
    const apply = await requiredFile('apply.js')
    const turnId = '019f61c0-4444-7444-8444-444444444444'
    const nativeFocusTarget = fixture.document.createElement('div')
    nativeFocusTarget.tabIndex = -1
    fixture.document.body.append(nativeFocusTarget)
    fixture.window.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') setTimeout(() => nativeFocusTarget.focus(), 0)
    }, true)
    makeTurn(fixture.document, turnId)
    executeRenderer(apply, fixture, {
      state: { records: { [turnId]: completeRecord(turnId) } },
    })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-turn-metadata-trigger]')

    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    let panel = fixture.document.querySelector('[data-turn-metadata-panel]')
    assert.ok(panel)
    assert.ok(Number.parseFloat(panel.style.left) >= 12)
    assert.ok(Number.parseFloat(panel.style.left) + Number.parseFloat(panel.style.width) <= 363)
    assert.ok(Number.parseFloat(panel.style.top) >= 12)
    assert.ok(Number.parseFloat(panel.style.maxHeight) <= 420)
    assert.ok(Number.parseFloat(panel.style.maxHeight) <= 812 * 0.55)

    trigger.dispatchEvent(new fixture.PointerEvent('pointerout', { bubbles: true, pointerType: 'mouse' }))
    await new Promise((resolve) => setTimeout(resolve, 180))
    assert.equal(fixture.document.querySelector('[data-turn-metadata-panel]'), null)

    trigger.dispatchEvent(new fixture.Event('focusin', { bubbles: true }))
    await fixture.flush()
    assert.ok(fixture.document.querySelector('[data-turn-metadata-panel]'))
    let downstreamEscapeCount = 0
    trigger.addEventListener('keydown', () => { downstreamEscapeCount += 1 })
    trigger.dispatchEvent(new fixture.KeyboardEvent('keydown', { bubbles: true, key: 'Escape' }))
    await new Promise((resolve) => setTimeout(resolve, 10))
    assert.equal(fixture.document.querySelector('[data-turn-metadata-panel]'), null)
    assert.equal(fixture.document.activeElement, trigger)
    assert.equal(downstreamEscapeCount, 0, 'owned Escape handling prevents native toolbar focus theft')

    trigger.click()
    assert.ok(fixture.document.querySelector('[data-turn-metadata-panel]'))
    trigger.click()
    assert.equal(fixture.document.querySelector('[data-turn-metadata-panel]'), null)

    trigger.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }))
    trigger.dispatchEvent(new fixture.Event('focusin', { bubbles: true }))
    trigger.click()
    assert.ok(fixture.document.querySelector('[data-turn-metadata-panel]'), 'touch focus plus click opens once')
    trigger.dispatchEvent(new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }))
    trigger.dispatchEvent(new fixture.Event('focusin', { bubbles: true }))
    trigger.click()
    assert.equal(fixture.document.querySelector('[data-turn-metadata-panel]'), null, 'second touch toggles closed once')
  } finally {
    fixture.dispose()
  }
})

test('bounded observation attaches metadata when a pending turn receives its final assistant surface', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f61c0-4444-7444-8444-444444444444'
    const pending = makeTurn(fixture.document, turnId, { completed: false })
    executeRenderer(apply, fixture, { state: { records: { [turnId]: completeRecord(turnId) } } })
    await fixture.flush()
    assert.equal(fixture.document.querySelector('[data-turn-metadata-trigger]'), null)

    const completed = makeTurn(fixture.document, turnId)
    completed.finalAssistant.remove()
    completed.surface.remove()
    pending.finalAssistant.remove()
    pending.turn.appendChild(completed.finalAssistant)
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-turn-metadata-trigger]').length, 1)
  } finally {
    fixture.dispose()
  }
})

test('renderer marks partial records, removes stale ownership on state refresh, and keeps missing fields absent', async () => {
  const fixture = createBrowserFixture()
  try {
    const apply = await requiredFile('apply.js')
    const turnId = '019f61c0-4444-7444-8444-444444444444'
    makeTurn(fixture.document, turnId)
    const partial = {
      turnId,
      model: null,
      providerId: 'legacy-provider',
      subagents: [],
      completeness: 'partial',
    }
    executeRenderer(apply, fixture, { state: { records: { [turnId]: partial } } })
    await fixture.flush()
    const trigger = fixture.document.querySelector('[data-turn-metadata-trigger]')
    assert.equal(trigger.textContent, 'Model details')
    trigger.click()
    const panel = fixture.document.querySelector('[data-turn-metadata-panel]')
    assert.match(panel.textContent, /historical record is partial/i)
    assert.doesNotMatch(panel.textContent, /Cached input/)
    assert.doesNotMatch(panel.textContent, /legacy-provider\s+legacy-provider/)

    executeRenderer(apply, fixture, { state: { records: {} } })
    await fixture.flush()
    assert.equal(fixture.document.querySelector('[data-turn-metadata-trigger]'), null)
    assert.equal(fixture.document.querySelector('[data-turn-metadata-panel]'), null)
  } finally {
    fixture.dispose()
  }
})
