import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'
import { createBrowserFixture, executeRenderer, makeComposer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/agent-derangement-risk/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function metricEvent(timestamp, metrics) {
  return JSON.stringify({ timestamp, payload: { type: 'token_count', ...metrics } })
}

function makeWingman(line) {
  return {
    currentTime: () => '2026-07-12T18:00:00.000Z',
    log() {},
    files: { codexSessions: {
      roots: () => ['C:/sessions'],
      listFiles: () => ['C:/sessions/current.jsonl'],
      readText: () => line,
    } },
  }
}

function loadBackend(source) {
  return vm.runInNewContext(`${source}\n({ refresh, calculateDerangementRisk, bandForRisk, DERANGEMENT_RISK_ALGORITHM, DERANGEMENT_RISK_ALGORITHMS })`, { Date, JSON, Math, Number, String })
}

test('unknown algorithm names fail closed without prototype lookup', async () => {
  const backend = loadBackend(await requiredFile('backend.js'))
  const inputs = { fill: 0.5, compactions: 1, cumulativeRatio: 1, freshRatio: 1 }
  for (const name of ['', null, '__proto__', 'toString', 'missing']) assert.equal(backend.calculateDerangementRisk(inputs, name), null)
})

test('existing adapter accepts null fresh ratio and preserves legacy cumulative fallback', async () => {
  const backend = loadBackend(await requiredFile('backend.js'))
  const value = backend.calculateDerangementRisk({ fill: 0.5, compactions: 1, cumulativeRatio: 2, freshRatio: null }, 'existing')
  assert.ok(Math.abs(value - (0.55 * 0.5 + 0.25 / 3 + 0.20 * Math.min(Math.log(3) / Math.log(5), 1))) < 1e-12)
  const source = await requiredFile('backend.js')
  const legacy = vm.runInNewContext(`${source.replace("var DERANGEMENT_RISK_ALGORITHM = 'smooth-age'", "var DERANGEMENT_RISK_ALGORITHM = 'existing'")}\nrefresh`, { Date, JSON, Math, Number, String })
  const result = legacy(makeWingman(metricEvent('2026-07-12T17:59:00Z', { context: { usedTokens: 100, maxTokens: 1000 }, compactions: 1 })))
  assert.equal(result.cumTokens, 100)
})

test('exact total and cache telemetry are both required for new algorithms', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const event = { info: { last_token_usage: { input_tokens: 100 }, total_token_usage: { cached_input_tokens: 20 }, model_context_window: 1000 }, total_tokens: 900 }
  assert.equal(refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', event))).risk, null)
})

test('invalid Host compaction override fails closed and valid live-info smooth-age is exact', async () => {
  const backend = loadBackend(await requiredFile('backend.js'))
  for (const override of [-1, 1.5, '2']) {
    const result = backend.refresh({ log() {}, files: { codexSessions: {}, codexTargetSession: { snapshot: () => ({ compactions: override, event: { payload: { info: { last_token_usage: { input_tokens: 300 }, total_token_usage: { total_tokens: 5000, cached_input_tokens: 1000 } , model_context_window: 1000 } } } }) } } })
    assert.equal(result.risk, null)
  }
  const result = backend.refresh({ log() {}, files: { codexSessions: {}, codexTargetSession: { snapshot: () => ({ compactions: 4, event: { payload: { info: { last_token_usage: { input_tokens: 300 }, total_token_usage: { total_tokens: 5000, cached_input_tokens: 1000 } , model_context_window: 1000 } } } }) } } })
  assert.ok(Math.abs(result.risk - (0.5 * 0.5 + 0.4 * (4 / 6) + 0.1 * 0.3)) < 1e-12)
})

test('shared bandForRisk returns exact boundaries', async () => {
  const backend = loadBackend(await requiredFile('backend.js'))
  assert.equal(backend.bandForRisk(.59), 'healthy')
  assert.equal(backend.bandForRisk(.60), 'watch')
  assert.equal(backend.bandForRisk(.849999), 'watch')
  assert.equal(backend.bandForRisk(.85), 'handoff')
  assert.equal(backend.bandForRisk(null), 'unavailable')
})

test('formula registry exposes the selected smooth-age algorithm and exact keys', async () => {
  const source = await requiredFile('backend.js')
  const backend = loadBackend(source)
  assert.equal(backend.DERANGEMENT_RISK_ALGORITHM, 'smooth-age')
  assert.deepEqual(Object.keys(backend.DERANGEMENT_RISK_ALGORITHMS), ['existing', 'milestones', 'smooth-age', 'paired'])
  for (const key of Object.keys(backend.DERANGEMENT_RISK_ALGORITHMS)) {
    assert.equal(typeof backend.DERANGEMENT_RISK_ALGORITHMS[key].description, 'string')
    assert.equal(typeof backend.DERANGEMENT_RISK_ALGORITHMS[key].calculate, 'function')
  }
})

test('formula registry calculates all four formulas exactly', async () => {
  const backend = loadBackend(await requiredFile('backend.js'))
  const inputs = { fill: 0.75, compactions: 10, cumulativeRatio: 7, freshRatio: 5 }
  const expected = {
    existing: Math.max(0, Math.min(1, 0.55 * 0.75 + 0.25 * 1 + 0.20 * Math.min(Math.log(8) / Math.log(5), 1))),
    milestones: 0.9,
    'smooth-age': 0.5 * 1 + 0.4 * (5 / 6) + 0.1 * 0.75,
    paired: 0.85 * Math.sqrt(1 * (5 / 6)) + 0.15 * 0.75,
  }
  for (const [name, value] of Object.entries(expected)) assert.ok(Math.abs(backend.calculateDerangementRisk(inputs, name) - value) < 1e-12, name)
})

test('smooth-age requires same-event cache and explicit compaction telemetry', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const base = { info: { last_token_usage: { input_tokens: 24000 }, total_token_usage: { total_tokens: 192000 }, model_context_window: 128000 } }
  assert.equal(refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', base))).risk, null)
  const withCache = { info: { last_token_usage: { input_tokens: 24000 }, total_token_usage: { total_tokens: 192000, cached_input_tokens: 128000 }, model_context_window: 128000 } }
  assert.equal(refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', withCache))).risk, null)
  const host = { files: { codexTargetSession: { snapshot: () => ({ threadId: 't', compactions: 2, event: { payload: { type: 'token_count', ...withCache } } }) } } }
  assert.ok(refresh(host).risk !== null)
})

function exposeVisibleConversation(document, threadId, signal = 'above-composer') {
  const node = document.createElement('div')
  if (signal === 'above-composer') node.dataset.aboveComposerConversationId = threadId
  else if (signal === 'request-user-input') node.dataset.requestUserInputAutoResolutionConversationId = threadId
  else throw new Error(`Unsupported visible-conversation signal: ${signal}`)
  document.body.appendChild(node)
  return node
}

test('Agent Derangement Risk package declares its separate Helper contract', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'agent-derangement-risk',
    name: 'Agent Derangement Risk',
    version: '1.0.2',
    description: 'Shows the estimated risk that a chat has become deranged by context history.',
    refreshSeconds: 2,
    capabilities: ['files.codexTargetSession'],
    entrypoints: { backend: 'backend.js', apply: 'apply.js', remove: 'remove.js' },
  })
})

test('backend calculates risk from current, compaction, and cumulative context metrics', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const result = refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', {
    context: { usedTokens: 500, maxTokens: 1000 },
    compactions: 1,
    cumTokens: 2000,
  })))
  const expected = Math.max(0, Math.min(1,
    0.55 * 0.5 + 0.25 * (1 / 3) + 0.20 * (Math.log(3) / Math.log(5)),
  ))
  assert.equal(result.usedTokens, 500)
  assert.equal(result.maxTokens, 1000)
  assert.equal(result.compactions, null)
  assert.equal(result.cumTokens, 2000)
  assert.equal(result.risk, null)
  assert.equal(result.band, 'unavailable')
})

test('backend reads the live Codex token_count info shape', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const result = refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', {
    info: {
      last_token_usage: { input_tokens: 24000, total_tokens: 25000 },
      total_token_usage: { input_tokens: 180000, output_tokens: 12000, total_tokens: 192000 },
      model_context_window: 128000,
    },
  })))
  assert.equal(result.usedTokens, 24000)
  assert.equal(result.maxTokens, 128000)
  assert.equal(result.cumTokens, 192000)
  assert.equal(result.risk, null)
})

test('backend fails closed when legacy-shaped telemetry lacks smooth-age inputs', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const result = refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', {
    used_tokens: 1000,
    max_tokens: 1000,
    compactions: 9,
  })))
  assert.equal(result.usedTokens, 1000)
  assert.equal(result.cumTokens, 1000)
  assert.equal(result.band, 'unavailable')
  assert.equal(refresh(makeWingman('{"bad":true}')).risk, null)
})

test('backend returns unavailable state for malformed or incomplete metrics', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const result = refresh(makeWingman(metricEvent('2026-07-12T17:59:00Z', {
    context: { usedTokens: 20 },
    compactions: '1',
  })))
  assert.deepEqual(JSON.parse(JSON.stringify(result)), { usedTokens: null, maxTokens: null, compactions: null, cumTokens: null, risk: null, band: 'unavailable' })
})

test('backend reads at most sixteen JSONL files without traversing a filtered copy', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const files = Array.from({ length: 20000 }, (_, index) => `C:/sessions/ignored-${index}.txt`)
  files.push(...Array.from({ length: 20 }, (_, index) => `C:/sessions/metric-${index}.jsonl`))
  const reads = []
  const result = refresh({
    currentTime: () => '2026-07-12T18:00:00.000Z',
    log() {},
    files: { codexSessions: {
      roots: () => ['C:/sessions'],
      listFiles: () => files,
      readText: (file) => { reads.push(file); return metricEvent('2026-07-12T17:59:00Z', { context: { usedTokens: 10, maxTokens: 100 } }) },
    } },
  })
  assert.equal(result.usedTokens, 10)
  assert.equal(reads.length, 16)
})

test('backend fails closed when a session root returns a non-array file list', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const logs = []
  const result = refresh({
    log(message) { logs.push(message) },
    files: { codexSessions: {
      roots: () => ['C:/sessions'],
      listFiles: () => null,
      readText: () => { throw new Error('readText should not run') },
    } },
  })
  assert.deepEqual(JSON.parse(JSON.stringify(result)), { usedTokens: null, maxTokens: null, compactions: null, cumTokens: null, risk: null, band: 'unavailable' })
  assert.match(logs.join('\n'), /non-array.*file list/i)
})

test('backend keeps metrics keyed by their session IDs instead of sharing one newest snapshot', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const files = new Map([
    ['C:/sessions/one.jsonl', [
      JSON.stringify({ type: 'session_meta', payload: { id: 'thread-one' } }),
      metricEvent('2026-07-12T17:59:00Z', { context: { usedTokens: 100, maxTokens: 1000 }, cumTokens: 100 }),
    ].join('\n')],
    ['C:/sessions/two.jsonl', [
      JSON.stringify({ type: 'session_meta', payload: { id: 'thread-two' } }),
      metricEvent('2026-07-12T18:00:00Z', { context: { usedTokens: 900, maxTokens: 1000 }, cumTokens: 900 }),
    ].join('\n')],
  ])
  const result = refresh({
    currentTime: () => '2026-07-12T18:00:01.000Z',
    log() {},
    files: { codexSessions: {
      roots: () => ['C:/sessions'],
      listFiles: () => [...files.keys()],
      readText: (file) => files.get(file),
    } },
  })
  assert.equal(result.byThreadId['thread-one'].usedTokens, 100)
  assert.equal(result.byThreadId['thread-two'].usedTokens, 900)
})

test('backend prefers compact Host snapshots for historic-session coverage', async () => {
  const source = await requiredFile('backend.js')
  const refresh = vm.runInNewContext(`${source}\nrefresh`, { Date, JSON, Math, Number, String })
  const event = (usedTokens, totalTokens, cachedInputTokens) => ({
    timestamp: '2026-07-12T18:00:00Z',
    payload: { type: 'token_count', info: {
      last_token_usage: { input_tokens: usedTokens },
      total_token_usage: { total_tokens: totalTokens, cached_input_tokens: cachedInputTokens },
      model_context_window: 1000,
    } },
  })
  const result = refresh({
    currentTime: () => '2026-07-12T18:00:01.000Z',
    log() {},
    files: { codexSessions: {
      snapshots: () => ({
        'historic-one': { event: event(100, 2600, 600), compactions: 2 },
        'historic-two': { event: event(900, 2000, 1000), compactions: 0 },
      }),
      roots: () => { throw new Error('filesystem fallback should not run') },
      listFiles: () => { throw new Error('filesystem fallback should not run') },
      readText: () => { throw new Error('filesystem fallback should not run') },
    } },
  })
  assert.equal(result.byThreadId['historic-one'].usedTokens, 100)
  assert.equal(result.byThreadId['historic-one'].compactions, 2)
  assert.ok(Math.abs(result.byThreadId['historic-one'].risk - (0.50 * (2 / 8) + 0.40 * (2 / 6) + 0.10 * 0.1)) < 1e-12)
  assert.equal(result.byThreadId['historic-one'].band, 'healthy')
  assert.equal(result.byThreadId['historic-two'].usedTokens, 900)
})

test('renderer places one risk ring after Usage Dials and cleans up only its ownership', async () => {
  const apply = await requiredFile('apply.js')
  const remove = await requiredFile('remove.js')
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'main', 60)
  exposeVisibleConversation(fixture.document, 'main-thread')
  const usage = fixture.document.createElement('span')
  usage.dataset.codexHelper = 'usage-dials'
  composer.wrapper.insertAdjacentElement('afterend', usage)
  const state = { threadId: 'main-thread', usedTokens: 700, maxTokens: 1000, compactions: 0, cumTokens: 700, risk: 0.385, band: 'healthy' }
  try {
    executeRenderer(apply, fixture, { state })
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.ok(ring)
    assert.equal(ring.previousElementSibling, usage)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 1)
    assert.match(ring.getAttribute('aria-label'), /Agent derangement risk: 39%.*healthy/i)
    assert.equal(ring.dataset.state, 'healthy')
    ring.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const tooltip = fixture.document.querySelector('[role="tooltip"][data-codex-helper-tooltip="agent-derangement-risk"]')
    assert.ok(tooltip)
    assert.match(tooltip.textContent, /Risk: 39% \(healthy\)/)
    assert.doesNotMatch(tooltip.textContent, /Current context:/)
    assert.equal(tooltip.querySelector('.codex-helper-risk-heading').textContent, 'Agent derangement risk')
    assert.match(tooltip.querySelector('.codex-helper-risk-details').textContent, /Compactions:/)
    assert.match(tooltip.textContent, /Compactions: 0/)
    assert.match(tooltip.textContent, /Total tokens: 700/)
    fixture.window.dispatchEvent(new fixture.Event('blur'))
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'), null)
    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-style="agent-derangement-risk"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'), null)
    assert.ok(fixture.document.querySelector('[data-codex-helper="usage-dials"]'))
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer prefers one Usage Dials anchor when the same row also has a native context anchor', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'live-mixed-row', 60)
  const nativeAfter = composer.wrapper.nextElementSibling
  const usageSlot = fixture.document.createElement('span')
  usageSlot.dataset.codexHelperContextSlot = 'usage-dials'
  const usage = fixture.document.createElement('span')
  usage.dataset.codexHelper = 'usage-dials'
  composer.row.replaceChildren(usageSlot, usage, composer.wrapper, nativeAfter)
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    await fixture.flush()
    const rings = composer.row.querySelectorAll('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(rings.length, 1)
    assert.equal(rings[0].previousElementSibling, usage)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer replaces stale owned rings with exactly one ring per composer', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const composers = [
    makeComposer(fixture.document, 'duplicate-one', 40),
    makeComposer(fixture.document, 'duplicate-two', 60),
  ]
  for (const composer of composers) {
    const stale = fixture.document.createElement('span')
    stale.dataset.codexHelper = 'agent-derangement-risk'
    composer.wrapper.insertAdjacentElement('afterend', stale)
  }
  const staleStyle = fixture.document.createElement('style')
  staleStyle.dataset.codexHelperStyle = 'agent-derangement-risk'
  fixture.document.documentElement.appendChild(staleStyle)
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    await fixture.flush()
    const rings = fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(rings.length, 2)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-style="agent-derangement-risk"]').length, 1)
    for (const composer of composers) {
      assert.equal(composer.row.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 1)
    }
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer dismisses its tooltip on pointer leave and window blur', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'tooltip-dismissal', 60)
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    ring.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.ok(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'))
    ring.dispatchEvent(new fixture.PointerEvent('pointerleave', { pointerType: 'mouse' }))
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'), null)

    ring.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.ok(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'))
    fixture.window.dispatchEvent(new fixture.Event('blur'))
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'), null)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer cleanup removes fallback ownership and repeated reapply stays idempotent', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'reapply-native', 60)
  const fallbackSlot = fixture.document.createElement('span')
  fallbackSlot.dataset.codexHelperContextSlot = 'agent-derangement-risk'
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    await fixture.flush()
    const firstController = fixture.window.__codexHelperAgentDerangementRisk
    fixture.document.body.appendChild(fallbackSlot)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-context-slot="agent-derangement-risk"]').length, 1)
    assert.equal(firstController.cleanup(), true)
    assert.equal(firstController.cleanup(), true)
    assert.equal(fixture.document.querySelector('[data-codex-helper-context-slot="agent-derangement-risk"]'), null)

    executeRenderer(apply, fixture, { state: { risk: 0.3, band: 'healthy' } })
    executeRenderer(apply, fixture, { state: { risk: 0.4, band: 'healthy' } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 1)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-context-slot="agent-derangement-risk"]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-style="agent-derangement-risk"]').length, 1)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer watchdog leaves direct and legacy usage active when no host lease exists', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'lease-absent', 60)
  const intervals = []
  fixture.context.setInterval = (callback, delay) => {
    const token = { callback, delay }
    intervals.push(token)
    return token
  }
  fixture.context.clearInterval = () => {}
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    const watchdog = intervals.find((interval) => interval.delay === 1000)
    assert.ok(watchdog)
    watchdog.callback()
    assert.ok(fixture.window.__codexHelperAgentDerangementRisk)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 1)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer watchdog consumes refreshed host leases without duplicating controllers', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'lease-refresh', 60)
  let now = 1000
  class ControlledDate extends Date {
    static now() { return now }
  }
  const intervals = new Set()
  fixture.context.Date = ControlledDate
  fixture.context.setInterval = (callback, delay) => {
    const token = { callback, delay }
    intervals.add(token)
    return token
  }
  fixture.context.clearInterval = (token) => intervals.delete(token)
  fixture.window.__codexHelperAgentDerangementRiskLeaseExpiresAt = now + 6000
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy' } })
    const controller = fixture.window.__codexHelperAgentDerangementRisk
    const watchdog = [...intervals].find((interval) => interval.delay === 1000)
    assert.ok(watchdog)

    now = 5000
    fixture.window.__codexHelperAgentDerangementRiskLeaseExpiresAt = now + 6000
    executeRenderer(apply, fixture, { state: { risk: 0.3, band: 'healthy' } })
    assert.equal(fixture.window.__codexHelperAgentDerangementRisk, controller)
    assert.equal([...intervals].filter((interval) => interval.delay === 1000).length, 1)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 1)

    now = 7000
    watchdog.callback()
    assert.equal(fixture.window.__codexHelperAgentDerangementRisk, controller)

    now = 11000
    watchdog.callback()
    assert.equal(fixture.window.__codexHelperAgentDerangementRisk, undefined)
    assert.equal(fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]'), null)
    assert.equal(intervals.size, 0)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer uses the approved healthy, watch, and handoff boundaries', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'fallback', 60)
  exposeVisibleConversation(fixture.document, 'boundary-thread')
  try {
    for (const [risk, band] of [[0.59, 'healthy'], [0.60, 'watch'], [0.849999, 'watch'], [0.85, 'handoff'], [1, 'handoff']]) {
      executeRenderer(apply, fixture, { state: { threadId: 'boundary-thread', risk } })
      await fixture.flush()
      const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
      assert.equal(ring.previousElementSibling, composer.wrapper)
      assert.equal(ring.dataset.state, band)
      assert.doesNotMatch(ring.getAttribute('aria-label'), /act/i)
    }
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer matches the Usage Dials palette without an Act state', async () => {
  const apply = await requiredFile('apply.js')
  assert.match(apply, /\[data-state="watch"\]\s*\{\s*color:\s*#d89614;\s*\}/i)
  assert.match(apply, /\[data-state="handoff"\]\s*\{\s*color:\s*#e5484d;\s*\}/i)
  assert.doesNotMatch(apply, /\[data-state="healthy"\][^{]*\{[^}]*color\s*:/i)
  assert.doesNotMatch(apply, /data-state="act"|#f08c00/i)
})

test('renderer follows the Usage Dials fallback bank before the native context anchor exists', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'fallback-bank', 60)
  composer.dial.remove()
  const usage = fixture.document.createElement('span')
  usage.dataset.codexHelper = 'usage-dials'
  composer.wrapper.insertAdjacentElement('afterend', usage)
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy', usedTokens: 200, maxTokens: 1000, compactions: 0, cumTokens: 200 } })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.ok(ring)
    assert.equal(ring.previousElementSibling, usage)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer keeps a ring on each surface when another surface already has a native anchor', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const first = makeComposer(fixture.document, 'mixed-native', 60)
  const second = makeComposer(fixture.document, 'mixed-fallback', 40)
  second.dial.remove()
  const usage = fixture.document.createElement('span')
  usage.dataset.codexHelper = 'usage-dials'
  second.wrapper.insertAdjacentElement('afterend', usage)
  try {
    executeRenderer(apply, fixture, { state: { risk: 0.2, band: 'healthy', usedTokens: 200, maxTokens: 1000, compactions: 0, cumTokens: 200 } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="agent-derangement-risk"]').length, 2)
    assert.equal(first.wrapper.nextElementSibling?.getAttribute('data-codex-helper'), 'agent-derangement-risk')
    assert.equal(second.wrapper.nextElementSibling?.getAttribute('data-codex-helper'), 'usage-dials')
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer keeps an anchored unavailable ring visible while metrics are pending', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'pending', 20)
  try {
    executeRenderer(apply, fixture, { state: {} })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.ok(ring)
    assert.equal(ring.style.display, 'inline-flex')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('svg').style.display, 'none')
    const marker = ring.querySelector('.codex-helper-unavailable-marker')
    assert.equal(marker.textContent, '?')
    assert.equal(marker.style.display, 'inline')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
    ring.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.match(fixture.document.querySelector('[role="tooltip"][data-codex-helper-tooltip="agent-derangement-risk"]').textContent, /risk: unavailable/i)
    ring.dispatchEvent(new fixture.PointerEvent('pointerleave', { pointerType: 'mouse' }))
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]'), null)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer uses the rendered conversation instead of stale URL and sidebar identities', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'scoped', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const active = fixture.document.createElement('div')
  active.dataset.appActionSidebarThreadId = 'local:old-thread'
  active.dataset.appActionSidebarThreadActive = 'true'
  fixture.document.body.appendChild(active)
  exposeVisibleConversation(fixture.document, 'local:visible-thread')
  const state = {
    byThreadId: {
      'old-thread': { usedTokens: 100, maxTokens: 1000, compactions: 0, cumTokens: 100, risk: 0.04, band: 'healthy' },
      'visible-thread': { usedTokens: 900, maxTokens: 1000, compactions: 1, cumTokens: 900, risk: 0.39, band: 'healthy' },
    },
  }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    assert.match(fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]').getAttribute('aria-label'), /39%/)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer is unavailable when no rendered conversation identity exists', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'missing-visible-id', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const active = fixture.document.createElement('div')
  active.dataset.appActionSidebarThreadId = 'local:old-thread'
  active.dataset.appActionSidebarThreadActive = 'true'
  fixture.document.body.appendChild(active)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 0, cumTokens: 900, risk: 0.39, band: 'healthy' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').style.display, 'inline')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer is unavailable when the trusted rendered identity has no matching telemetry', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'unmatched-visible-id', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const active = fixture.document.createElement('div')
  active.dataset.appActionSidebarThreadId = 'local:old-thread'
  active.dataset.appActionSidebarThreadActive = 'true'
  fixture.document.body.appendChild(active)
  exposeVisibleConversation(fixture.document, 'visible-thread')
  const state = {
    byThreadId: {
      'old-thread': { usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' },
    },
  }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').style.display, 'inline')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer is unavailable when supported rendered conversation signals disagree', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'conflicting-visible-id', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  exposeVisibleConversation(fixture.document, 'visible-thread-one')
  exposeVisibleConversation(fixture.document, 'local:visible-thread-two', 'request-user-input')
  const state = {
    byThreadId: {
      'old-thread': { usedTokens: 900, maxTokens: 1000, compactions: 0, cumTokens: 900, risk: 0.39, band: 'healthy' },
      'visible-thread-one': { usedTokens: 800, maxTokens: 1000, compactions: 1, cumTokens: 800, risk: 0.44, band: 'healthy' },
      'visible-thread-two': { usedTokens: 700, maxTokens: 1000, compactions: 2, cumTokens: 700, risk: 0.49, band: 'healthy' },
    },
  }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').style.display, 'inline')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer switches percentage and compaction tooltip with the rendered conversation identity', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'visible-switch', 60)
  const visible = exposeVisibleConversation(fixture.document, 'thread-one', 'request-user-input')
  const state = {
    byThreadId: {
      'thread-one': { usedTokens: 100, maxTokens: 1000, compactions: 1, cumTokens: 100, risk: 0.04, band: 'healthy' },
      'thread-two': { usedTokens: 900, maxTokens: 1000, compactions: 3, cumTokens: 900, risk: 0.39, band: 'healthy' },
    },
  }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.match(ring.getAttribute('aria-label'), /4%/)
    ring.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.match(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]').textContent, /Compactions: 1/)

    visible.dataset.requestUserInputAutoResolutionConversationId = 'local:thread-two'
    await fixture.flush()
    assert.match(ring.getAttribute('aria-label'), /39%/)
    assert.match(fixture.document.querySelector('[data-codex-helper-tooltip="agent-derangement-risk"]').textContent, /Compactions: 3/)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer retains thread risk for a standalone Do anything composer without native home markers', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'new-task', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fthread-one' }
  exposeVisibleConversation(fixture.document, 'thread-one')
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  const placeholder = fixture.document.createElement('p')
  placeholder.dataset.placeholder = 'Do anything'
  composer.appendChild(placeholder)
  fixture.document.body.appendChild(composer)
  const state = { threadId: 'thread-one', usedTokens: 900, maxTokens: 1000, compactions: 0, cumTokens: 900, risk: 0.39, band: 'healthy' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'healthy')
    assert.match(ring.getAttribute('aria-label'), /Agent derangement risk: 39%, healthy/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer resets risk on the native New task home screen despite a stale thread route', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'new-task-home', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we build?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  composer.textContent = 'TEST HERE'
  const projectButton = fixture.document.createElement('button')
  projectButton.setAttribute('aria-label', 'Choose project')
  main.append(heading, composer, projectButton, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer is unavailable when native New task visibly retains a stale supported identity', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'new-task-stale-supported-id', 60)
  exposeVisibleConversation(fixture.document, 'old-thread')
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we build?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  main.append(heading, composer, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer resets risk on a selected-project New task screen without a Choose project button', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'selected-project-home', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we build?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  composer.textContent = 'TEST HERE'
  const selectedProject = fixture.document.createElement('button')
  selectedProject.textContent = 'Example Project'
  main.append(heading, composer, selectedProject, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer resets risk on a project-specific New task heading', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'project-specific-home', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we work on in Example Project?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  composer.textContent = 'TEST HERE'
  main.append(heading, composer, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer resets risk on the native git-project New task heading', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'git-project-home', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  heading.dataset.feature = 'game-source'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we build in Example Project?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  main.append(heading, composer, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})

test('renderer resets risk on the native work-mode New task heading without a project name', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const anchor = makeComposer(fixture.document, 'work-mode-home', 60)
  fixture.context.location = { href: 'app://-/index.html?initialRoute=%2Flocal%2Fold-thread' }
  const main = fixture.document.createElement('main')
  const heading = fixture.document.createElement('div')
  heading.className = 'heading-xl flex items-end justify-center'
  heading.dataset.feature = 'game-source'
  const headingText = fixture.document.createElement('span')
  headingText.textContent = 'What should we work on?'
  heading.appendChild(headingText)
  const composer = fixture.document.createElement('div')
  composer.dataset.codexComposer = 'true'
  composer.setAttribute('contenteditable', 'true')
  main.append(heading, composer, anchor.row)
  fixture.document.body.appendChild(main)
  const state = { threadId: 'old-thread', usedTokens: 900, maxTokens: 1000, compactions: 2, cumTokens: 5000, risk: 0.92, band: 'handoff' }
  try {
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    const ring = fixture.document.querySelector('[data-codex-helper="agent-derangement-risk"]')
    assert.equal(ring.dataset.state, 'unavailable')
    assert.equal(ring.querySelector('.codex-helper-unavailable-marker').textContent, '?')
    assert.match(ring.getAttribute('aria-label'), /unavailable/i)
  } finally {
    fixture.window.__codexHelperAgentDerangementRisk?.cleanup?.()
    fixture.dispose()
  }
})
