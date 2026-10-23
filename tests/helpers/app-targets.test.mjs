import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

// Synthetic T3 web origin with the real URL shapes (tailnet host, :8443, /<environment>/<thread>).
const t3WebOrigin = 'https://t3.example-tailnet.ts.net:8443'
const threadPath = '/2f0c7a4e-1b6d-4c1e-9a55-0d3c2b1a9e77/7d1e5b2c-3a4f-4e6d-8c9b-1a2b3c4d5e6f'

const read = (path) => fs.readFile(new URL(`../../helpers/${path}`, import.meta.url), 'utf8')

function makeT3Composer(fixture, provider) {
  const form = fixture.document.createElement('form')
  const surface = fixture.document.createElement('div')
  surface.setAttribute('data-chat-composer-main-surface', 'true')
  const editor = fixture.document.createElement('div')
  editor.setAttribute('data-testid', 'composer-editor')
  const controls = fixture.document.createElement('div')
  controls._rect = { left: 100, top: 650, width: 200, height: 28 }
  const picker = fixture.document.createElement('button')
  picker.setAttribute('data-chat-provider-model-picker', 'true')
  picker._rect = { left: 100, top: 650, width: 120, height: 28 }
  controls.appendChild(picker)
  surface.append(editor, controls)
  form.appendChild(surface)
  fixture.document.body.appendChild(form)
  const props = { activeThreadId: 'thread-one', activeThread: { id: 'thread-one', environmentId: 'environment-one' },
    providerStatuses: [provider] }
  const root = { stateNode: {} }
  root.stateNode.current = root
  const composer = { memoizedProps: props, return: root }
  surface.__reactFiber$trial = { return: composer }
  picker.__reactFiber$trial = { return: { memoizedProps: { isComposerOwned: true, activeInstanceId: provider.instanceId }, return: composer } }
  return { controls, picker, surface, props }
}

const provider = (used = 42) => ({
  instanceId: 'codex-personal', enabled: true, installed: true,
  usageLimits: { checkedAt: new Date().toISOString(), windows: [
    { kind: 'session', label: '5-hour usage', usedPercent: used, windowDurationMins: 300, resetsAt: new Date(Date.now() + 2 * 3600000).toISOString() },
    { kind: 'weekly', label: 'Weekly usage', usedPercent: 20, windowDurationMins: 10080, resetsAt: new Date(Date.now() + 4 * 86400000).toISOString() },
  ] },
})

function useT3Web(fixture) {
  fixture.window.location = { protocol: 'https:', origin: t3WebOrigin, pathname: threadPath, href: t3WebOrigin + threadPath }
}

test('T3 web: Usage Dials render beside the model picker exactly like native T3', async () => {
  const fixture = createBrowserFixture()
  useT3Web(fixture)
  fixture.setRectResolver(() => ({ left: 80, top: 650, width: 40, height: 28 }))
  const composer = makeT3Composer(fixture, provider())
  const health = []
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { app: 't3-web', request() {}, reportRenderHealth: value => health.push(value) } })
    const controller = fixture.window.__codexHelperUsageDials
    assert.equal(controller.status().attachedCount, 1)
    assert.equal(controller.status().primary.usedPercent, 42)
    assert.equal(controller.status().secondary.usedPercent, 20)
    assert.equal(composer.picker.previousElementSibling.getAttribute('data-codex-helper'), 'usage-dials')
    const dial = composer.controls.querySelector('[data-codex-helper-dial="primary"]')
    assert.equal(dial.getAttribute('data-wingman-app'), 't3-code', 'same palette as native T3')
    assert.match(dial.getAttribute('aria-label'), /42%/)
    assert.equal(health.at(-1).state, 'ready')
    executeRenderer(await read('usage-dials/remove.js'), fixture)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 0)
  } finally { fixture.dispose() }
})

test('T3 web: a plain https page without the Wingman app marker is not treated as T3', async () => {
  const fixture = createBrowserFixture()
  useT3Web(fixture)
  makeT3Composer(fixture, provider())
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { request() {} } })
    assert.equal(fixture.window.__codexHelperUsageDials.status().attachedCount, 0)
    assert.equal(fixture.window.__codexHelperUsageDials.status().renderHealth, undefined)
  } finally { fixture.dispose() }
})

test('T3 web: provider refresh loads only same-origin T3 modules', async () => {
  const apply = await read('usage-dials/apply.js')
  const fixture = createBrowserFixture()
  useT3Web(fixture)
  const stale = provider()
  stale.usageLimits.checkedAt = new Date(Date.now() - 20 * 60000).toISOString()
  const composer = makeT3Composer(fixture, stale)
  const registry = { '~effect/reactivity/AtomRegistry': true, get() {}, set() {} }
  composer.surface.__reactFiber$trial.return = { memoizedProps: { value: registry }, return: composer.surface.__reactFiber$trial.return }
  const urls = [`${t3WebOrigin}/assets/runtime-native.js`, 'https://elsewhere.example/assets/runtime-evil.js', 't3code://app/assets/server-desktop.js']
  for (const url of urls) {
    const link = fixture.document.createElement('link')
    link.setAttribute('rel', 'modulepreload')
    link.href = url
    fixture.document.body.appendChild(link)
  }
  const imported = []
  const calls = []
  fixture.context.importNativeModule = async url => {
    imported.push(url)
    return { native: { refreshProviders: { label: 'environment-data:server:refresh-providers', run: async (_registry, request) => { calls.push(request) } } } }
  }
  try {
    executeRenderer(apply.replace('import(url)', 'importNativeModule(url)'), fixture, { state: {}, wingman: { app: 't3-web', request() {} } })
    await fixture.flush()
    assert.deepEqual(imported, [urls[0]])
    assert.deepEqual(JSON.parse(JSON.stringify(calls)), [{ environmentId: 'environment-one', input: { instanceId: 'codex-personal' } }])
  } finally { fixture.dispose() }
})

function makeT3Timeline(fixture, href) {
  const timeline = fixture.document.createElement('div')
  timeline.setAttribute('data-timeline-root', 'true')
  fixture.document.body.appendChild(timeline)
  const message = fixture.document.createElement('div')
  message.setAttribute('data-message-id', 't3-assistant-1')
  message.setAttribute('data-message-role', 'assistant')
  timeline.appendChild(message)
  const markdown = fixture.document.createElement('div')
  markdown.setAttribute('class', 'chat-markdown')
  message.appendChild(markdown)
  const anchor = fixture.document.createElement('a')
  anchor.setAttribute('class', 'chat-markdown-file-link')
  anchor.setAttribute('href', href)
  anchor.textContent = 'link'
  markdown.appendChild(anchor)
  return { timeline, message, anchor }
}

test('T3 web: clickable file links run only with the t3-web marker and open through the Host', async () => {
  const source = await read('clickable-file-links/apply.t3.js')
  const config = { mappings: [{ sourcePrefix: '/mnt/example-data/', windowsPrefix: 'R:/' }], excludePrefixes: [] }
  const plain = createBrowserFixture()
  useT3Web(plain)
  makeT3Timeline(plain, '/mnt/example-data/projects/a.txt')
  try {
    executeRenderer(source, plain, { wingman: { request() {} }, helperConfig: config })
    assert.equal(plain.window.__codexWingmanT3ClickableFileLinks, undefined)
  } finally { plain.dispose() }
  const web = createBrowserFixture()
  useT3Web(web)
  const { anchor } = makeT3Timeline(web, '/mnt/example-data/projects/a.txt')
  const requests = []
  try {
    executeRenderer(source, web, { wingman: { app: 't3-web', request: (...args) => requests.push(args) }, helperConfig: config })
    const button = anchor.nextElementSibling
    assert.equal(button.getAttribute('data-clickable-file-links-t3-open'), 'true')
    button.dispatchEvent(new web.context.Event('click', { bubbles: true, cancelable: true }))
    assert.deepEqual(requests.map(([action, body]) => [action, body.path]), [['system.openFilePath', 'R:/projects/a.txt']])
  } finally { web.dispose() }
})

test('T3 web: Obsidian links use the T3 timeline rules under the t3-web marker', async () => {
  const source = await read('obsidian-links/apply.js')
  const web = createBrowserFixture()
  useT3Web(web)
  const raw = '/mnt/example-data/obsidian/Library/Notes/T3 Trial.md'
  const { anchor, timeline } = makeT3Timeline(web, raw)
  const requests = []
  let nativeClicks = 0
  timeline.addEventListener('click', () => nativeClicks++)
  try {
    executeRenderer(source, web, { wingman: { app: 't3-web', request: (...args) => requests.push(args) }, helperConfig: { mappings: [{ sourcePrefix: '/mnt/example-data/obsidian/' }] } })
    const event = new web.context.Event('click', { bubbles: true, cancelable: true })
    anchor.dispatchEvent(event)
    assert.equal(event.defaultPrevented, true)
    assert.equal(nativeClicks, 0, 'T3 capture-phase handling, as on desktop T3')
    assert.equal(requests[0][0], 'system.openObsidianUri')
  } finally { web.dispose() }
})

// Grok Bot composer, using the semantic class markers its renderer emits next to hashed StyleX classes.
function makeGrokComposer(fixture) {
  fixture.window.location = { protocol: 'file:', href: 'file:///C:/Users/example/AppData/Local/Programs/Grok%20Bot/resources/app.asar/dist/renderer/index.html' }
  const dock = fixture.document.createElement('div')
  dock.setAttribute('class', 'sand-chat-input-dock x1abc')
  const form = fixture.document.createElement('form')
  form.setAttribute('class', 'sand-prompt-form x9f8e')
  const shell = fixture.document.createElement('div')
  shell.setAttribute('class', 'sand-prompt-shell')
  const host = fixture.document.createElement('div')
  host.setAttribute('class', 'sand-prompt-field-host')
  const field = fixture.document.createElement('div')
  field.setAttribute('class', 'sand-prompt-field')
  host.appendChild(field)
  const row = fixture.document.createElement('div')
  row.setAttribute('class', 'sand-prompt-actions-row')
  const trailing = fixture.document.createElement('div')
  trailing.setAttribute('class', 'sand-prompt-actions-trailing x77aa')
  trailing._rect = { left: 600, top: 700, width: 120, height: 32 }
  const attach = fixture.document.createElement('button')
  attach.setAttribute('class', 'sand-prompt-attach')
  const cluster = fixture.document.createElement('div')
  cluster.setAttribute('class', 'sand-prompt-cta-cluster x55')
  const mic = fixture.document.createElement('button')
  mic.setAttribute('class', 'sand-prompt-mic')
  const send = fixture.document.createElement('button')
  send.setAttribute('class', 'sand-prompt-send')
  cluster.append(mic, send)
  trailing.append(attach, cluster)
  row.appendChild(trailing)
  shell.append(host, row)
  form.appendChild(shell)
  dock.appendChild(form)
  fixture.document.body.appendChild(dock)
  return { trailing, cluster, attach }
}

test('Grok Bot: one weekly dial sits before the send cluster and its percentage equals the cache value', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  const composer = makeGrokComposer(fixture)
  const nextResetMs = Date.now() + 4 * 86400000
  const health = []
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 22.58, nextResetMs, readAtMs: Date.now() - 60000 } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth: value => health.push(value) },
    })
    const controller = fixture.window.__codexHelperUsageDials
    assert.equal(controller.status().attachedCount, 1)
    assert.equal(controller.status().secondary.usedPercent, 23, 'whole percent, as Grok Bot\'s own menu shows it')
    assert.equal(controller.status().primary, null)
    const bank = composer.trailing.querySelector('[data-codex-helper="usage-dials"]')
    assert.equal(bank.nextElementSibling, composer.cluster, 'dial sits immediately before mic/send')
    assert.equal(bank.getAttribute('aria-label'), 'Grok Bot usage')
    const weekly = bank.querySelector('[data-codex-helper-dial="secondary"]')
    assert.equal(weekly.getAttribute('data-wingman-app'), 'grok-bot')
    assert.match(weekly.getAttribute('aria-label'), /^Weekly: 23%\./)
    assert.equal(weekly.querySelector('[data-segment="background"]').getAttribute('data-end-percent'), '100')
    assert.equal(weekly.querySelector('[data-segment="unused"]').getAttribute('data-start-percent'), '23', 'drawn usage arc ends at the shown percentage')
    assert.equal(bank.querySelector('[data-codex-helper-dial="primary"]').style.display, 'none')
    assert.equal(health.at(-1).state, 'saved-reading', 'no live store here, so health says it is the saved reading')
    assert.match(health.at(-1).detail, /saved reading, not the live value: Grok Bot's page is not ready/)
    assert.equal(weekly.dataset.usageSource, 'saved')
    assert.equal(weekly.style.opacity, '0.5', 'a saved reading is visibly dimmed')
    assert.match(weekly.getAttribute('aria-label'), /Saved reading, not live\.$/)
    weekly.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    const tip = fixture.document.querySelector('[role="tooltip"]').textContent
    assert.match(tip, /Saved reading, .* \(not live\)/)
    assert.match(tip, /Weekly:/, 'same heading as the shared popup')
    assert.doesNotMatch(tip, /Weekly usage/)
    assert.match(tip, /23% used \(77% left\)/)
    assert.doesNotMatch(tip, /Resets unavailable/, 'no Codex reset-credit line for Grok Bot')
    controller.update({ grokBot: { available: true, percentUsed: 61, nextResetMs, readAtMs: Date.now() } })
    assert.equal(controller.status().secondary.usedPercent, 61)
    executeRenderer(await read('usage-dials/remove.js'), fixture)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 0)
    assert.equal(fixture.window.__codexHelperUsageDials, undefined)
  } finally { fixture.dispose() }
})

test('Grok Bot: missing or out-of-range cache shows a question mark, never a guessed number', async () => {
  for (const grokBot of [{ available: false }, undefined, { available: true, percentUsed: 140, readAtMs: Date.now() }]) {
    const fixture = createBrowserFixture()
    const composer = makeGrokComposer(fixture)
    const health = []
    try {
      executeRenderer(await read('usage-dials/apply.js'), fixture, { state: grokBot ? { grokBot } : {}, wingman: { app: 'grok-bot', request() {}, reportRenderHealth: value => health.push(value) } })
      const status = fixture.window.__codexHelperUsageDials.status()
      assert.equal(status.secondary, null)
      const primary = composer.trailing.querySelector('[data-codex-helper-dial="primary"]')
      assert.equal(primary.style.display, 'inline-flex')
      assert.match(primary.getAttribute('aria-label'), /Usage unavailable\. Grok Bot has not saved a current usage reading yet\./)
      assert.equal(health.at(-1).state, 'waiting')
    } finally { fixture.dispose() }
  }
})

test('Grok Bot: no composer markers means no dial and not-applicable health', async () => {
  const fixture = createBrowserFixture()
  fixture.window.location = { protocol: 'file:' }
  const health = []
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: { grokBot: { available: true, percentUsed: 5, readAtMs: Date.now() } }, wingman: { app: 'grok-bot', request() {}, reportRenderHealth: value => health.push(value) } })
    assert.equal(fixture.window.__codexHelperUsageDials.status().attachedCount, 0)
    assert.equal(health.at(-1).state, 'not-applicable')
  } finally { fixture.dispose() }
})

test('Grok Bot backend returns only the typed Wingman projection', async () => {
  const source = await read('usage-dials/backend.grok.js')
  const projection = { available: true, percentUsed: 22.58, nextResetMs: 1, readAtMs: 2 }
  const refresh = new Function(`${source}; return refresh`)()
  assert.deepEqual(refresh({ files: { grokBotUsage: { read: () => projection } } }), { grokBot: projection })
})

const grokStore = (initial) => {
  let current = initial
  const listeners = new Set()
  const calls = { refresh: 0 }
  return {
    calls,
    set(next) { current = next; for (const listener of [...listeners]) listener() },
    listenerCount: () => listeners.size,
    weeklyUsage: {
      snapshots: { get: () => current, subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener) }, set() { throw new Error('helper must not write') }, update() { throw new Error('helper must not write') } },
      presence: { get: () => 'present' },
      refresh() { calls.refresh += 1 },
      refreshNow() { throw new Error('helper uses the throttled refresh only') },
    },
  }
}

const mountGrokRoot = (fixture, store) => {
  const root = fixture.document.createElement('div')
  root.id = 'root'
  const provider = { memoizedProps: { value: { weeklyUsage: store.weeklyUsage, usageSummary: {} } }, child: null, sibling: null }
  root['__reactContainer$test'] = { memoizedProps: {}, child: { memoizedProps: {}, child: provider, sibling: null }, sibling: null }
  fixture.document.body.appendChild(root)
}

test('Grok Bot: the dial follows the live client store, not the stale cache, and redraws on store change', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  const composer = makeGrokComposer(fixture)
  const nextResetMs = Date.now() + 4 * 86400000
  const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 42.14, nextResetMs }, readAtMs: Date.now() - 30000 } })
  mountGrokRoot(fixture, store)
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 33.7, nextResetMs, readAtMs: Date.now() - 5 * 3600000 } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} },
    })
    const controller = fixture.window.__codexHelperUsageDials
    assert.equal(controller.status().secondary.usedPercent, 42, 'live store wins over the cache (42.14 shows as 42%, like the menu)')
    assert.equal(controller.status().usageSource, 'live')
    assert.equal(store.listenerCount(), 1)
    assert.equal(store.calls.refresh, 0, 'a fresh live reading needs no refresh')
    store.set({ status: 'ready', value: { usage: { percentUsed: 47.5, nextResetMs }, readAtMs: Date.now() } })
    fixture.flushAnimationFrames?.()
    controller.update({ grokBot: { available: true, percentUsed: 33.7, nextResetMs, readAtMs: Date.now() - 5 * 3600000 } })
    assert.equal(controller.status().secondary.usedPercent, 48)
    store.set({ status: 'ready', value: { usage: { percentUsed: 0.4, nextResetMs }, readAtMs: Date.now() } })
    await fixture.flush()
    assert.equal(controller.status().secondary.usedPercent, 1, 'store change alone redraws; menu shows 1% for anything between 0 and 1')
    store.set({ status: 'ready', value: { usage: { percentUsed: 47.5, nextResetMs }, readAtMs: Date.now() } })
    await fixture.flush()
    assert.equal(controller.status().secondary.usedPercent, 48, 'store change alone redraws')
    const weekly = composer.trailing.querySelector('[data-codex-helper-dial="secondary"]')
    assert.match(weekly.getAttribute('aria-label'), /^Weekly: 48%\./)
    weekly.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.match(fixture.document.querySelector('[role="tooltip"]').textContent, /Updated just now/)
    executeRenderer(await read('usage-dials/remove.js'), fixture)
    assert.equal(store.listenerCount(), 0, 'cleanup unsubscribes')
  } finally { fixture.dispose() }
})

test('Grok Bot: a stale live reading asks the app\'s own throttled refresh at most once per 5 minutes', async () => {
  const fixture = createBrowserFixture()
  makeGrokComposer(fixture)
  const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 10, nextResetMs: Date.now() + 86400000 }, readAtMs: Date.now() - 10 * 60000 } })
  mountGrokRoot(fixture, store)
  try {
    fixture.document.hasFocus = () => true
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} } })
    const controller = fixture.window.__codexHelperUsageDials
    controller.update({})
    controller.update({})
    assert.equal(store.calls.refresh, 1)
    assert.equal(controller.status().secondary.usedPercent, 10)
  } finally { fixture.dispose() }
})

test('Grok Bot: no refresh request while the window lacks focus and the popup is closed; opening the popup asks once', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  makeGrokComposer(fixture)
  const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 10, nextResetMs: Date.now() + 86400000 }, readAtMs: Date.now() - 10 * 60000 } })
  mountGrokRoot(fixture, store)
  let focused = false
  fixture.document.hasFocus = () => focused
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} } })
    const controller = fixture.window.__codexHelperUsageDials
    controller.update({})
    assert.equal(store.calls.refresh, 0, 'visible but unfocused, popup closed: no request')
    const weekly = fixture.document.querySelector('[data-codex-helper-dial="secondary"]')
    weekly.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.equal(store.calls.refresh, 1, 'opening the popup asks once')
    weekly.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    focused = true
    controller.update({})
    assert.equal(store.calls.refresh, 1, 'still at most once per 5 minutes')
  } finally { fixture.dispose() }
})

test('Grok Bot: the dial follows weeklyUsage (what the account menu renders), not usageSummary (Settings > Usage)', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  makeGrokComposer(fixture)
  const nextResetMs = Date.now() + 4 * 86400000
  const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 44.95, nextResetMs }, readAtMs: Date.now() - 1000 } })
  const root = fixture.document.createElement('div')
  root.id = 'root'
  // Live 0.75.3 shape: one context holds both stores, with different numbers.
  const usageSummary = { snapshots: { get: () => ({ status: 'ready', value: { sandUsagePercent: 42.8, sandUsageResetTimestampMs: nextResetMs } }), subscribe: () => () => {} }, refresh() { throw new Error('helper must not refresh usageSummary') } }
  root['__reactContainer$test'] = { memoizedProps: {}, child: { memoizedProps: { value: { weeklyUsage: store.weeklyUsage, usageSummary } }, child: null, sibling: null }, sibling: null }
  fixture.document.body.appendChild(root)
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} } })
    const status = fixture.window.__codexHelperUsageDials.status()
    assert.equal(status.usageSource, 'live')
    assert.equal(status.secondary.usedPercent, 45, 'menu shows Math.round(44.95) = 45%, Settings would show 43%')
    assert.match(fixture.document.querySelector('[data-codex-helper-dial="secondary"]').getAttribute('aria-label'), /^Weekly: 45%\./)
  } finally { fixture.dispose() }
})

test('Grok Bot: without a live store the saved cache is used and labelled with its age', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  const composer = makeGrokComposer(fixture)
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 33.7, nextResetMs: Date.now() + 86400000, readAtMs: Date.now() - 3 * 3600000 } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} },
    })
    assert.equal(fixture.window.__codexHelperUsageDials.status().usageSource, 'cache')
    const weekly = composer.trailing.querySelector('[data-codex-helper-dial="secondary"]')
    weekly.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    assert.match(fixture.document.querySelector('[role="tooltip"]').textContent, /Saved reading, 3 h ago/)
  } finally { fixture.dispose() }
})

test('Grok Bot: two candidate stores fail closed to the cache', async () => {
  const fixture = createBrowserFixture()
  makeGrokComposer(fixture)
  const a = grokStore({ status: 'ready', value: { usage: { percentUsed: 1 }, readAtMs: Date.now() } })
  const b = grokStore({ status: 'ready', value: { usage: { percentUsed: 2 }, readAtMs: Date.now() } })
  const root = fixture.document.createElement('div')
  root.id = 'root'
  root['__reactContainer$test'] = { memoizedProps: { value: { weeklyUsage: a.weeklyUsage } }, child: null,
    sibling: { memoizedProps: { value: { weeklyUsage: b.weeklyUsage } }, child: null, sibling: null } }
  fixture.document.body.appendChild(root)
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, { state: {}, wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} } })
    assert.equal(fixture.window.__codexHelperUsageDials.status().usageSource, null)
    assert.equal(a.calls.refresh + b.calls.refresh, 0)
  } finally { fixture.dispose() }
})

test('Grok Bot: the live store is found however deep or late it sits in the tree (no element budget)', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  makeGrokComposer(fixture)
  const nextResetMs = Date.now() + 4 * 86400000
  const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 57.4, nextResetMs }, readAtMs: Date.now() - 1000 } })
  const root = fixture.document.createElement('div')
  root.id = 'root'
  // 5,000 unrelated siblings first, then a 3,000-deep chain, then the provider:
  // far beyond the old 400-fiber walk.
  let deep = { memoizedProps: { value: { weeklyUsage: store.weeklyUsage } }, child: null, sibling: null }
  for (let i = 0; i < 3000; i++) deep = { memoizedProps: { value: i % 7 ? {} : { theme: 'dark' } }, child: deep, sibling: null }
  let siblings = deep
  for (let i = 0; i < 5000; i++) siblings = { memoizedProps: {}, child: null, sibling: siblings }
  root['__reactContainer$test'] = { memoizedProps: {}, child: siblings, sibling: null }
  fixture.document.body.appendChild(root)
  const health = []
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 12, nextResetMs, readAtMs: Date.now() - 3600000 } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth: value => health.push(value) },
    })
    const status = fixture.window.__codexHelperUsageDials.status()
    assert.equal(status.usageSource, 'live')
    assert.equal(status.secondary.usedPercent, 57)
    assert.equal(health.at(-1).state, 'ready')
    const weekly = fixture.document.querySelector('[data-codex-helper-dial="secondary"]')
    assert.equal(weekly.dataset.usageSource, 'live')
    assert.equal(weekly.style.opacity, '')
    assert.doesNotMatch(weekly.getAttribute('aria-label'), /Saved reading/)
  } finally { fixture.dispose() }
})

test('Grok Bot: a store that appears after the first look is picked up on a later pass', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  makeGrokComposer(fixture)
  const nextResetMs = Date.now() + 4 * 86400000
  const realNow = Date.now
  let clock = realNow()
  Date.now = () => clock
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 12, nextResetMs, readAtMs: clock - 3600000 } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} },
    })
    const controller = fixture.window.__codexHelperUsageDials
    assert.equal(controller.status().usageSource, 'cache')
    const store = grokStore({ status: 'ready', value: { usage: { percentUsed: 30, nextResetMs }, readAtMs: clock } })
    mountGrokRoot(fixture, store)
    controller.update({ grokBot: { available: true, percentUsed: 12, nextResetMs, readAtMs: clock - 3600000 } })
    assert.equal(controller.status().usageSource, 'cache', 'searches are spaced, not run on every reconcile')
    clock += 2500
    controller.update({ grokBot: { available: true, percentUsed: 12, nextResetMs, readAtMs: clock - 3600000 } })
    assert.equal(controller.status().usageSource, 'live')
    assert.equal(controller.status().secondary.usedPercent, 30)
  } finally { Date.now = realNow; fixture.dispose() }
})

test('Usage popup owns its layout so a host without utility classes cannot run lines together', async () => {
  const fixture = createBrowserFixture()
  fixture.setRectResolver(() => ({ left: 560, top: 700, width: 32, height: 32 }))
  const composer = makeGrokComposer(fixture)
  try {
    executeRenderer(await read('usage-dials/apply.js'), fixture, {
      state: { grokBot: { available: true, percentUsed: 33.7, nextResetMs: Date.now() + 86400000, readAtMs: Date.now() } },
      wingman: { app: 'grok-bot', request() {}, reportRenderHealth() {} },
    })
    composer.trailing.querySelector('[data-codex-helper-dial="secondary"]')
      .dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    const tip = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
    const stack = tip.querySelector('[data-codex-helper-usage-stack]')
    assert.ok(stack, 'stack is marked for the scoped rules')
    assert.ok(stack.children.length >= 4, 'each readout line is its own element')
    const css = fixture.document.querySelector('[data-codex-helper-style="usage-dials"]').textContent
    assert.match(css, /\[data-codex-helper-tooltip="usage-fallback"\] > \[data-codex-helper-usage-stack\] \{[^}]*display: flex !important;[^}]*flex-direction: column !important;/)
    assert.match(css, /\[data-codex-helper-usage-stack\] > span \{[^}]*display: block !important;/)
    assert.match(css, /gap: calc\(var\(--spacing, 0\.25rem\) \* 0\.5\)/, 'same gap as gap-0.5')
    assert.match(css, /width: calc\(var\(--spacing, 0\.25rem\) \* 38\)/, 'same width as w-38')
    // Inline layout outranks any host rule, so hostile host CSS cannot collapse it.
    assert.equal(stack.style.display, 'flex')
    assert.equal(stack.style.flexDirection, 'column')
    assert.equal(stack.style.gap, 'calc(var(--spacing, 0.25rem) * 0.5)')
    for (const line of [...stack.children].filter(child => child.tagName === 'SPAN')) {
      assert.equal(line.style.display, 'block')
      assert.equal(line.style.whiteSpace, 'pre-line')
    }
  } finally { fixture.dispose() }
})
