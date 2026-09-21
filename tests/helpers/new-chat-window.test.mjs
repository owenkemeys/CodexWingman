import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/new-chat-window/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  assert.equal(await fs.access(file).then(() => true, () => false), true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function prepare(fixture, bootstrap = null) {
  fixture.context.location = { search: '?initialRoute=%2F' }
  fixture.window.location = fixture.context.location
  fixture.context.bootstrap = bootstrap
  fixture.context.setTimeout = setTimeout
  fixture.context.clearTimeout = clearTimeout
}

function control(fixture, label, id = 'new-chat', append = true) {
  const button = fixture.document.createElement('button')
  button.id = id
  button.setAttribute('aria-label', label)
  if (append) fixture.document.body.appendChild(button)
  return button
}

function newTaskGlyph(fixture, button) {
  const svg = fixture.document.createElement('svg')
  const path = fixture.document.createElement('path')
  path.setAttribute('d', 'M6.33325 1.88379C6.58178 1.5')
  svg.appendChild(path)
  button.appendChild(svg)
}

test('manifest declares the exact helper identity and Host capability', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.equal(manifest.id, 'new-chat-window')
  assert.deepEqual(manifest.capabilities, ['codex.openNewChatWindow'])
})

test('project-specific right-click opens root child with exact semantic label and leaves source unchanged', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const exactLabel = 'Start new task in Example Project'
  const button = control(fixture, exactLabel)
  let nativeClicks = 0
  button.addEventListener('click', () => nativeClicks++)
  const opens = []
  try {
    executeRenderer(apply, fixture, { wingman: { openChild: (path, bootstrap) => opens.push({ path, bootstrap }) } })
    const event = new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    button.dispatchEvent(event)
    assert.equal(event.defaultPrevented, true)
    assert.deepEqual(JSON.parse(JSON.stringify(opens)), [{
      path: '/',
      bootstrap: { mode: 'native-new-chat', controlLabel: exactLabel },
    }])
    assert.equal(nativeClicks, 0)
    assert.equal(fixture.document.querySelector('[data-codex-helper-menu]'), null)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('generic New Chat right-click opens exact root child while left-click remains native', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  for (const label of ['New chat', 'Create a new chat']) {
    const source = createBrowserFixture()
    prepare(source)
    const button = control(source, label)
    const opens = []
    let nativeClicks = 0
    button.addEventListener('click', () => nativeClicks++)
    executeRenderer(apply, source, { wingman: { openChild: (path, bootstrap) => opens.push({ path, bootstrap }) } })
    button.click()
    assert.equal(nativeClicks, 1, `${label} left-click remains native`)
    assert.equal(opens.length, 0)
    const event = new source.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    button.dispatchEvent(event)
    assert.equal(event.defaultPrevented, true, label)
    assert.deepEqual(JSON.parse(JSON.stringify(opens)), [{ path: '/', bootstrap: { mode: 'native-new-chat', controlLabel: label, controlKind: 'semantic' } }])
    assert.equal(nativeClicks, 1, `${label} source is unchanged by right-click`)

    const child = createBrowserFixture()
    prepare(child, opens[0].bootstrap)
    const childButton = control(child, label, 'generic-child', false)
    let childClicks = 0
    let acknowledgements = 0
    const completionOrder = []
    childButton.addEventListener('click', () => childClicks++)
    executeRenderer(apply, child, { wingman: { openChild() {}, completeChild: () => { acknowledgements++; completionOrder.push('ack') } } })
    child.document.body.appendChild(childButton)
    await child.flush()
    assert.equal(childClicks, 1)
    assert.equal(acknowledgements, 0, `${label} waits to clear inherited project context`)
    const clearProject = control(child, "Don't work in a project", 'clear-project', false)
    clearProject.setAttribute('aria-label', "Don't work in a project")
    let clearClicks = 0
    clearProject.addEventListener('click', () => { clearClicks++; completionOrder.push('clear') })
    child.document.body.appendChild(clearProject)
    await child.flush()
    assert.equal(clearClicks, 1)
    assert.equal(acknowledgements, 0, 'generic navigation does not use an acknowledgement that its own rebuild can erase')
    assert.deepEqual(completionOrder, ['clear'])
    executeRenderer(remove, source)
    executeRenderer(remove, child)
    source.dispose()
    child.dispose()
  }
})

test('sidebar New task row ignores its keyboard shortcut when opening the child', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const nav = fixture.document.createElement('nav')
  nav.setAttribute('aria-label', 'Scheduled task folders')
  const button = fixture.document.createElement('button')
  button.textContent = 'New task Ctrl+N'
  nav.appendChild(button)
  fixture.document.body.appendChild(nav)
  const opens = []
  try {
    executeRenderer(apply, fixture, { wingman: { openChild: (path, bootstrap) => opens.push({ path, bootstrap }) } })
    const event = new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    button.dispatchEvent(event)
    assert.equal(event.defaultPrevented, true)
    assert.deepEqual(JSON.parse(JSON.stringify(opens)), [{ path: '/', bootstrap: { mode: 'native-new-chat', controlLabel: 'New task', controlKind: 'sidebar-row' } }])
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('visible collapsed-header New task icon is recognized while its invisible duplicate is ignored', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const header = fixture.document.createElement('header')
  const invisible = fixture.document.createElement('div')
  invisible.className = 'invisible pointer-events-none fixed top-0 left-0'
  const invisibleButton = fixture.document.createElement('button')
  newTaskGlyph(fixture, invisibleButton)
  invisible.appendChild(invisibleButton)
  const collapsed = fixture.document.createElement('div')
  collapsed.className = 'pointer-events-none relative h-full shrink-0 [container-type:inline-size]'
  const button = fixture.document.createElement('button')
  newTaskGlyph(fixture, button)
  collapsed.appendChild(button)
  header.appendChild(invisible)
  header.appendChild(collapsed)
  const unrelated = fixture.document.createElement('div')
  unrelated.className = 'pointer-events-none relative h-full shrink-0'
  const unrelatedButton = fixture.document.createElement('button')
  newTaskGlyph(fixture, unrelatedButton)
  unrelated.appendChild(unrelatedButton)
  header.appendChild(unrelated)
  fixture.document.body.appendChild(header)
  const opens = []
  try {
    executeRenderer(apply, fixture, { wingman: { openChild: (path, bootstrap) => opens.push({ path, bootstrap }) } })
    invisibleButton.dispatchEvent(new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
    assert.equal(opens.length, 0)
    unrelatedButton.dispatchEvent(new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
    assert.equal(opens.length, 0)
    const event = new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    button.dispatchEvent(event)
    assert.equal(event.defaultPrevented, true)
    assert.deepEqual(JSON.parse(JSON.stringify(opens)), [{ path: '/', bootstrap: { mode: 'native-new-chat', controlLabel: 'New task', controlKind: 'collapsed-header' } }])
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('U+2019 project labels round-trip exactly and acknowledge after the native click', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const exactLabel = 'Start new task in O’Reilly'
  const source = createBrowserFixture()
  prepare(source)
  const sourceButton = control(source, exactLabel)
  const opens = []
  executeRenderer(apply, source, { wingman: { openChild: (path, bootstrap) => opens.push({ path, bootstrap }) } })
  sourceButton.dispatchEvent(new source.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
  assert.deepEqual(JSON.parse(JSON.stringify(opens)), [{ path: '/', bootstrap: { mode: 'native-new-chat', controlLabel: exactLabel } }])

  const child = createBrowserFixture()
  prepare(child, opens[0].bootstrap)
  const childButton = control(child, exactLabel, 'oreilly-child', false)
  let clicks = 0
  let acknowledgements = 0
  childButton.addEventListener('click', () => clicks++)
  executeRenderer(apply, child, { wingman: { openChild() {}, completeChild: () => acknowledgements++ } })
  child.document.body.appendChild(childButton)
  await child.flush()
  try {
    assert.equal(clicks, 1)
    assert.equal(acknowledgements, 1)
  } finally {
    executeRenderer(remove, source)
    executeRenderer(remove, child)
    source.dispose()
    child.dispose()
  }
})

test('exact child waits for one exact matching native control and clicks it once', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const exactLabel = 'Start new task in Example Project'
  const fixture = createBrowserFixture()
  prepare(fixture, { mode: 'native-new-chat', controlLabel: exactLabel })
  let acknowledgements = 0
  const wingman = { openChild() {}, completeChild: () => acknowledgements++ }
  executeRenderer(apply, fixture, { wingman })
  const wrong = control(fixture, 'Start new task in Other Project')
  let wrongClicks = 0
  wrong.addEventListener('click', () => wrongClicks++)
  const matching = control(fixture, exactLabel, 'late-control', false)
  let matchingClicks = 0
  matching.addEventListener('click', () => matchingClicks++)
  fixture.document.body.appendChild(matching)
  await fixture.flush()
  try {
    assert.equal(matchingClicks, 1)
    assert.equal(wrongClicks, 0)
    assert.equal(acknowledgements, 1)
    executeRenderer(apply, fixture, { wingman })
    assert.equal(matchingClicks, 1)
    assert.equal(acknowledgements, 1)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('duplicate, missing, and malformed bootstrap labels never guess a control', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const exactLabel = 'Start new task in Example Project'
  for (const bootstrap of [
    { mode: 'native-new-chat', controlLabel: exactLabel },
    { mode: 'native-new-chat', controlLabel: 'Start new task in Missing Project' },
    { mode: 'native-new-chat', controlLabel: '' },
    { mode: 'native-new-chat', controlLabel: ' Start new task in Example Project' },
    { mode: 'native-new-chat', controlLabel: 'Start  new task in Example Project' },
    { mode: 'native-new-chat', controlLabel: 'Start new task in Example Project ' },
    { mode: 'native-new-chat' },
  ]) {
    const fixture = createBrowserFixture()
    prepare(fixture, bootstrap)
    let clicks = 0
    if (bootstrap.controlLabel === exactLabel) {
      control(fixture, exactLabel, 'duplicate-a').addEventListener('click', () => clicks++)
      control(fixture, exactLabel, 'duplicate-b').addEventListener('click', () => clicks++)
    } else {
      control(fixture, exactLabel).addEventListener('click', () => clicks++)
    }
    let acknowledgements = 0
    executeRenderer(apply, fixture, { wingman: { openChild() {}, completeChild: () => acknowledgements++ } })
    assert.equal(clicks, 0, JSON.stringify(bootstrap))
    assert.equal(acknowledgements, 0, JSON.stringify(bootstrap))
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('project matcher rejects action-like suffixes and cleanup removes listeners', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const falsePositive = control(fixture, 'Start new task in Example Project: open settings')
  const valid = control(fixture, 'Start new task in Real Project', 'valid')
  const opens = []
  executeRenderer(apply, fixture, { wingman: { openChild: (...args) => opens.push(args) } })
  falsePositive.dispatchEvent(new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
  assert.equal(opens.length, 0)
  executeRenderer(remove, fixture)
  valid.dispatchEvent(new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
  assert.equal(opens.length, 0)
  fixture.dispose()
})

test('a published helper upgrade replaces a stale renderer controller', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const button = control(fixture, 'New task')
  let staleReconciles = 0
  let staleCleanups = 0
  fixture.window.__codexHelperNewChatWindow = {
    version: 'old',
    reconcile: () => staleReconciles++,
    cleanup: () => { staleCleanups++; delete fixture.window.__codexHelperNewChatWindow },
  }
  try {
    executeRenderer(apply, fixture, { wingman: { openChild() {} } })
    assert.equal(staleReconciles, 0)
    assert.equal(staleCleanups, 1)
    assert.equal(button.dataset.codexHelperContextMenu, 'new-chat-window')
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('same-turn DOM mutation bursts coalesce to one full control scan', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const originalQuerySelectorAll = fixture.document.querySelectorAll.bind(fixture.document)
  let fullControlScans = 0
  fixture.document.querySelectorAll = (selector) => {
    if (selector === 'button,[role="button"],a[href]') fullControlScans++
    return originalQuerySelectorAll(selector)
  }
  try {
    executeRenderer(apply, fixture, { wingman: { openChild() {} } })
    const scansAfterInitialApply = fullControlScans
    for (let index = 0; index < 100; index++) {
      const inert = fixture.document.createElement('div')
      inert.textContent = `irrelevant mutation ${index}`
      fixture.document.body.appendChild(inert)
    }
    await fixture.flush()
    assert.ok(fullControlScans - scansAfterInitialApply <= 1)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})

test('cleanup cancels a queued mutation reconciliation', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const originalQuerySelectorAll = fixture.document.querySelectorAll.bind(fixture.document)
  let fullControlScans = 0
  fixture.document.querySelectorAll = (selector) => {
    if (selector === 'button,[role="button"],a[href]') fullControlScans++
    return originalQuerySelectorAll(selector)
  }
  executeRenderer(apply, fixture, { wingman: { openChild() {} } })
  const scansAfterInitialApply = fullControlScans
  fixture.document.body.appendChild(fixture.document.createElement('div'))
  executeRenderer(remove, fixture)
  await fixture.flush()
  assert.equal(fullControlScans, scansAfterInitialApply)
  fixture.dispose()
})

test('a newly inserted source control works through delegation without a full rescan', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  prepare(fixture)
  const originalQuerySelectorAll = fixture.document.querySelectorAll.bind(fixture.document)
  let fullControlScans = 0
  fixture.document.querySelectorAll = (selector) => {
    if (selector === 'button,[role="button"],a[href]') fullControlScans++
    return originalQuerySelectorAll(selector)
  }
  const opens = []
  executeRenderer(apply, fixture, { wingman: { openChild: (...args) => opens.push(args) } })
  const scansAfterInitialApply = fullControlScans
  const lateControl = control(fixture, 'New task', 'late-source-control')
  const event = new fixture.context.MouseEvent('contextmenu', { bubbles: true, cancelable: true })
  lateControl.dispatchEvent(event)
  try {
    assert.equal(event.defaultPrevented, true)
    assert.equal(opens.length, 1)
    assert.equal(fullControlScans, scansAfterInitialApply)
  } finally {
    executeRenderer(remove, fixture)
    fixture.dispose()
  }
})
