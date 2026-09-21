import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/json-debug/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function control(document, tag, id, attributes = {}) {
  const node = document.createElement(tag)
  node.id = id
  for (const [name, value] of Object.entries(attributes)) node.setAttribute(name, value)
  document.body.appendChild(node)
  return node
}

function fixtureWithSurfaces(fixture) {
  control(fixture.document, 'button', 'sidebar-trigger', { 'data-app-shell-sidebar-trigger': 'true', 'aria-label': 'Show sidebar' })
  control(fixture.document, 'nav', 'composer-navigation', { 'aria-label': 'Conversation navigation' })
  control(fixture.document, 'button', 'model-picker', { 'aria-label': 'Choose model' })
  control(fixture.document, 'span', 'context-anchor', { role: 'img', 'aria-label': 'Context usage: 42%' })
  control(fixture.document, 'button', 'new-chat', { 'aria-label': 'New chat' })
  control(fixture.document, 'div', 'tooltip', { role: 'tooltip' })
  return fixture
}

test('manifest declares the renderer-only JSON debug helper', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'json-debug',
    name: 'JSON UI debug',
    version: '1.0.0',
    description: 'Shows semantic UI canaries and refresh diagnostics in a renderer-local overlay.',
    enabledByDefault: false,
    refreshSeconds: 2,
    capabilities: [],
    entrypoints: { apply: 'apply.js', remove: 'remove.js' },
  })
})

test('apply is idempotent, reports canaries, and keeps the overlay readable and non-interactive', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = fixtureWithSurfaces(createBrowserFixture())
  try {
    executeRenderer(apply, fixture)
    const first = fixture.window.__codexJsonDebug
    executeRenderer(apply, fixture)
    const root = fixture.document.querySelector('[data-codex-json-debug-root]')
    assert.ok(root)
    assert.equal(fixture.document.querySelectorAll('[data-codex-json-debug-root]').length, 1)
    assert.equal(fixture.document.querySelectorAll('[data-codex-json-debug-legend]').length, 1)
    assert.equal(root.dataset.codexHelper, 'json-debug')
    assert.equal(root.style.pointerEvents, 'none')
    assert.equal(root.style.position, 'fixed')
    assert.match(root.textContent, /URL:/)
    assert.match(root.textContent, /Title:/)
    assert.match(root.textContent, /sidebar trigger.*FOUND/i)
    assert.match(root.textContent, /new-chat controls.*FOUND/i)
    assert.match(root.textContent, /refreshes:\s*2/i)
    const canaryKeys = first.getResults().map((entry) => entry.key)
    assert.ok(canaryKeys.includes('composer-input'))
    assert.ok(canaryKeys.includes('permissions-controls'))
    assert.equal(first.instanceId, root.dataset.codexJsonDebugInstance)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('legend and API expose the injected host target ID instead of the instance ID', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  const targetId = 'fixture-cdp-target-id'
  try {
    executeRenderer(apply, fixture, { wingman: { target: { id: targetId }, request() {} } })
    const root = fixture.document.querySelector('[data-codex-json-debug-root]')
    const api = fixture.window.__codexJsonDebug
    assert.equal(api.targetId, targetId)
    assert.match(root.textContent, new RegExp(`CDP target: ${targetId}`))
    assert.doesNotMatch(root.textContent, /\| Target:/)
    assert.notEqual(api.targetId, api.instanceId)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('missing selectors are represented as MISSING without throwing', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  try {
    executeRenderer(apply, fixture)
    const result = fixture.window.__codexJsonDebug
    assert.equal(result.getResults().every((entry) => entry.status === 'MISSING' || entry.key === 'body-root'), true)
    assert.match(fixture.document.querySelector('[data-codex-json-debug-root]').textContent, /MISSING/)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('refresh handles rerendered attributes and multiple matches', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  try {
    executeRenderer(apply, fixture)
    const api = fixture.window.__codexJsonDebug
    const first = control(fixture.document, 'button', 'one', { 'aria-label': 'New chat' })
    const second = control(fixture.document, 'button', 'two', { 'aria-label': 'New chat' })
    await fixture.flush()
    assert.equal(api.getResults().find((entry) => entry.key === 'new-chat').count, 2)
    first.setAttribute('aria-label', 'Settings')
    second.setAttribute('aria-label', 'Settings')
    await fixture.flush()
    assert.equal(api.getResults().find((entry) => entry.key === 'new-chat').status, 'MISSING')
    assert.ok(api.getResults().find((entry) => entry.key === 'new-chat').refreshes >= 2)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('instances are distinct and cleanup removes only owned nodes, styles, and attributes', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const a = fixtureWithSurfaces(createBrowserFixture())
  const b = fixtureWithSurfaces(createBrowserFixture())
  const preserved = control(a.document, 'button', 'preserved', { 'data-codex-json-debug-target': 'native' })
  try {
    executeRenderer(apply, a)
    executeRenderer(apply, b)
    const resultA = a.window.__codexJsonDebug
    const resultB = b.window.__codexJsonDebug
    assert.notEqual(resultA.instanceId, resultB.instanceId)
    executeRenderer(remove, a)
    assert.equal(a.document.querySelector('[data-codex-json-debug-root]'), null)
    assert.equal(a.document.querySelector('[data-codex-json-debug-style]'), null)
    assert.equal(a.window.__codexJsonDebug, undefined)
    assert.equal(preserved.getAttribute('data-codex-json-debug-target'), 'native')
    assert.ok(b.document.querySelector('[data-codex-json-debug-root]'))
  } finally {
    executeRenderer(remove, b)
    a.dispose()
    b.dispose()
  }
})

test('overlay mutations do not create an observer refresh loop', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  try {
    executeRenderer(apply, fixture)
    const api = fixture.window.__codexJsonDebug
    const before = api.getResults().find((entry) => entry.key === 'body-root').refreshes
    await fixture.flush()
    const after = api.getResults().find((entry) => entry.key === 'body-root').refreshes
    assert.equal(after, before, 'self-rendering the legend must not schedule another refresh')
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})
