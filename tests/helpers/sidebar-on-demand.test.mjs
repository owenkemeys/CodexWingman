import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/sidebar-on-demand/', import.meta.url)
const controllerName = '__codexWingmanSidebarOnDemand'
const styleSelector = '[data-codex-wingman-sidebar-gate="true"]'
const panelSelector = '[data-pip-obstacle="app-shell-floating-left-panel"]'
const explicitAttribute = 'data-codex-wingman-sidebar-explicit'
const panelMarker = 'data-codex-wingman-sidebar-suppressed'

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function makeEdgeTrigger(document, id = 'edge') {
  const trigger = document.createElement('button')
  trigger.id = id
  trigger.setAttribute('data-app-shell-sidebar-trigger', 'true')
  trigger.setAttribute('aria-label', 'Show sidebar')
  document.body.appendChild(trigger)
  return trigger
}

function makePanel(document, id = 'floating-panel') {
  const panel = document.createElement('aside')
  panel.id = id
  panel.setAttribute('data-pip-obstacle', 'app-shell-floating-left-panel')
  document.body.appendChild(panel)
  return panel
}

function dispatchKey(fixture, target, key) {
  const event = new fixture.KeyboardEvent('keydown', { bubbles: true, key })
  event.key = key
  target.dispatchEvent(event)
}

test('Sidebar on Demand declares the css-gate renderer package', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'sidebar-on-demand',
    name: 'Sidebar on Demand',
    version: '1.1.0',
    description: 'Keeps the floating sidebar closed until you deliberately open it.',
    refreshSeconds: 0,
    capabilities: [],
    entrypoints: { apply: 'apply.js', remove: 'remove.js' },
  })
})

test('owned CSS gate hides and marks a panel inserted by an earlier window capture listener', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)
  fixture.window.addEventListener('pointerover', (event) => {
    if (event.target.closest?.('[data-app-shell-sidebar-trigger="true"]')) makePanel(fixture.document)
  }, true)

  try {
    executeRenderer(apply, fixture)
    trigger.dispatchEvent(new fixture.PointerEvent('pointerover', {
      bubbles: true,
      cancelable: true,
      pointerType: 'mouse',
    }))

    const style = fixture.document.querySelector(styleSelector)
    const panel = fixture.document.querySelector(panelSelector)
    assert.ok(style, 'the gate stylesheet exists before passive activation')
    assert.match(style.textContent, /html:not\(\[data-codex-wingman-sidebar-explicit="true"\]\) \[data-pip-obstacle="app-shell-floating-left-panel"\]/)
    assert.match(style.textContent, /visibility:\s*hidden\s*!important/)
    assert.match(style.textContent, /pointer-events:\s*none\s*!important/)
    assert.ok(panel, 'the earlier Codex listener reproduces the live inserted panel')
    assert.equal(panel.getAttribute(panelMarker), 'true')
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
    assert.deepEqual({ ...fixture.window[controllerName].status() }, {
      active: true,
      version: 'css-gate-v2',
      suppressedEventCount: 1,
      suppressedPanelCount: 1,
      explicitIntent: false,
      suppressedCount: 1,
    })
  } finally {
    fixture.dispose()
  }
})

test('primary pointer, click, Enter, and Space set explicit intent without blocking trigger actions', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)
  const actions = []
  trigger.addEventListener('pointerdown', () => actions.push('pointer'))
  trigger.addEventListener('click', () => actions.push('click'))
  trigger.addEventListener('keydown', (event) => actions.push(event.key))

  try {
    executeRenderer(apply, fixture)

    const pointer = new fixture.PointerEvent('pointerdown', { bubbles: true, pointerType: 'mouse' })
    pointer.button = 0
    trigger.dispatchEvent(pointer)
    assert.equal(fixture.document.documentElement.getAttribute(explicitAttribute), 'true')
    fixture.window.dispatchEvent(new fixture.Event('blur'))

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    assert.equal(fixture.document.documentElement.getAttribute(explicitAttribute), 'true')
    fixture.window.dispatchEvent(new fixture.Event('blur'))

    dispatchKey(fixture, trigger, 'Enter')
    assert.equal(fixture.document.documentElement.getAttribute(explicitAttribute), 'true')
    fixture.window.dispatchEvent(new fixture.Event('blur'))

    dispatchKey(fixture, trigger, ' ')
    assert.equal(fixture.document.documentElement.getAttribute(explicitAttribute), 'true')
    assert.deepEqual(actions, ['pointer', 'click', 'Enter', ' '])
  } finally {
    fixture.dispose()
  }
})

test('explicit intent clears when its panel disappears, on blur, and on mouseleave', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)

  try {
    executeRenderer(apply, fixture)

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    const explicitPanel = makePanel(fixture.document, 'explicit-panel')
    assert.equal(explicitPanel.hasAttribute(panelMarker), false)
    explicitPanel.remove()
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    fixture.window.dispatchEvent(new fixture.Event('blur'))
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    fixture.window.dispatchEvent(new fixture.Event('mouseleave'))
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
  } finally {
    fixture.dispose()
  }
})

test('descendant blur and mouseleave preserve explicit intent while real window departure clears it', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)

  try {
    executeRenderer(apply, fixture)

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    trigger.dispatchEvent(new fixture.Event('blur'))
    assert.equal(
      fixture.document.documentElement.getAttribute(explicitAttribute),
      'true',
      'blur from a descendant control is not a window blur',
    )

    trigger.dispatchEvent(new fixture.Event('mouseleave'))
    assert.equal(
      fixture.document.documentElement.getAttribute(explicitAttribute),
      'true',
      'mouseleave from a descendant control is not window departure',
    )

    fixture.window.dispatchEvent(new fixture.Event('blur'))
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)

    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    fixture.window.dispatchEvent(new fixture.Event('mouseleave'))
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
  } finally {
    fixture.dispose()
  }
})

test('docked-sidebar intent expires after two frames and re-arms suppression for later passive panels', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)
  const frames = new Map()
  let nextFrame = 0
  const requestAnimationFrame = (callback) => {
    const id = ++nextFrame
    frames.set(id, callback)
    return id
  }
  const cancelAnimationFrame = (id) => frames.delete(id)
  const runNextFrame = () => {
    const next = frames.entries().next().value
    assert.ok(next, 'an owned intent-expiry frame is pending')
    const [id, callback] = next
    frames.delete(id)
    callback(Date.now())
  }
  fixture.context.requestAnimationFrame = requestAnimationFrame
  fixture.context.cancelAnimationFrame = cancelAnimationFrame
  fixture.window.requestAnimationFrame = requestAnimationFrame
  fixture.window.cancelAnimationFrame = cancelAnimationFrame

  try {
    executeRenderer(apply, fixture)
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    assert.equal(fixture.document.documentElement.getAttribute(explicitAttribute), 'true')

    runNextFrame()
    assert.equal(
      fixture.document.documentElement.getAttribute(explicitAttribute),
      'true',
      'the pending exemption remains through the first animation frame',
    )

    runNextFrame()
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
    assert.equal(fixture.window[controllerName].status().explicitIntent, false)

    const passivePanel = makePanel(fixture.document, 'late-passive-panel')
    assert.equal(passivePanel.getAttribute(panelMarker), 'true')
    assert.equal(fixture.window[controllerName].status().suppressedPanelCount, 1)
  } finally {
    fixture.dispose()
  }
})

test('a stale controller is cleaned up while an active css-gate-v2 controller is reused', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  let staleCleanupCount = 0
  fixture.window[controllerName] = {
    version: 'event-gate-v1',
    cleanup() { staleCleanupCount++ },
    status() { return { version: 'event-gate-v1' } },
  }

  try {
    executeRenderer(apply, fixture)
    const firstController = fixture.window[controllerName]
    const firstStatus = { ...firstController.status() }
    executeRenderer(apply, fixture)
    const secondStatus = { ...fixture.window[controllerName].status() }
    assert.equal(staleCleanupCount, 1)
    assert.equal(firstStatus.version, 'css-gate-v2')
    assert.equal(secondStatus.version, 'css-gate-v2')
    assert.equal(fixture.window[controllerName], firstController)
    assert.equal(fixture.document.querySelectorAll(styleSelector).length, 1)
  } finally {
    fixture.dispose()
  }
})

test('rerendered triggers stay guarded and inserted passive panels are counted only once', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()

  try {
    executeRenderer(apply, fixture)
    const rerendered = makeEdgeTrigger(fixture.document, 'rerendered-edge')
    let hoverOpens = 0
    rerendered.addEventListener('mouseenter', () => hoverOpens++)
    rerendered.dispatchEvent(new fixture.Event('mouseenter'))
    assert.equal(hoverOpens, 0)

    const firstPanel = makePanel(fixture.document, 'first-panel')
    const child = fixture.document.createElement('div')
    firstPanel.appendChild(child)
    child.appendChild(fixture.document.createElement('span'))
    assert.equal(firstPanel.getAttribute(panelMarker), 'true')
    assert.equal(fixture.window[controllerName].status().suppressedPanelCount, 1)

    firstPanel.remove()
    const secondPanel = makePanel(fixture.document, 'second-panel')
    assert.equal(secondPanel.getAttribute(panelMarker), 'true')
    assert.equal(fixture.window[controllerName].status().suppressedPanelCount, 2)
  } finally {
    fixture.dispose()
  }
})

test('ordinary sidebar-labelled controls retain hover, click, and keyboard behavior', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const controls = [
    ['toggle', 'Toggle sidebar'],
    ['close', 'Close sidebar'],
  ].map(([id, label]) => {
    const button = fixture.document.createElement('button')
    button.id = id
    button.dataset.slot = 'sidebar-trigger'
    button.setAttribute('aria-label', label)
    fixture.document.body.appendChild(button)
    return button
  })
  const counts = new Map(controls.map((control) => [control.id, { hover: 0, explicit: 0 }]))
  for (const control of controls) {
    for (const eventName of ['pointerover', 'pointerenter', 'mouseover', 'mouseenter'])
      control.addEventListener(eventName, () => counts.get(control.id).hover++)
    control.addEventListener('click', () => counts.get(control.id).explicit++)
    control.addEventListener('keydown', () => counts.get(control.id).explicit++)
  }

  try {
    executeRenderer(apply, fixture)
    for (const control of controls) {
      control.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
      control.dispatchEvent(new fixture.PointerEvent('pointerenter', { pointerType: 'mouse' }))
      control.dispatchEvent(new fixture.Event('mouseover', { bubbles: true }))
      control.dispatchEvent(new fixture.Event('mouseenter'))
      control.dispatchEvent(new fixture.Event('click', { bubbles: true }))
      dispatchKey(fixture, control, 'Enter')
      assert.deepEqual(counts.get(control.id), { hover: 4, explicit: 2 })
    }
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
  } finally {
    fixture.dispose()
  }
})

test('cleanup removes the owned gate, intent, markers, listeners, observer, and global only', async () => {
  const apply = await requiredFile('apply.js')
  const remove = await requiredFile('remove.js')
  const fixture = createBrowserFixture()
  const trigger = makeEdgeTrigger(fixture.document)
  const foreignStyle = fixture.document.createElement('style')
  foreignStyle.setAttribute('data-other-helper', 'true')
  fixture.document.head.appendChild(foreignStyle)
  fixture.document.documentElement.setAttribute('data-other-helper-state', 'keep')

  try {
    executeRenderer(apply, fixture)
    const panel = makePanel(fixture.document)
    trigger.dispatchEvent(new fixture.Event('click', { bubbles: true }))
    assert.equal(panel.getAttribute(panelMarker), 'true')

    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelector(styleSelector), null)
    assert.equal(fixture.document.documentElement.hasAttribute(explicitAttribute), false)
    assert.equal(panel.hasAttribute(panelMarker), false)
    assert.equal(fixture.window[controllerName], undefined)
    assert.equal(foreignStyle.isConnected, true)
    assert.equal(fixture.document.documentElement.getAttribute('data-other-helper-state'), 'keep')

    const afterCleanupPanel = makePanel(fixture.document, 'after-cleanup')
    assert.equal(afterCleanupPanel.hasAttribute(panelMarker), false, 'the observer is disconnected')
    let hoverOpens = 0
    trigger.addEventListener('mouseenter', () => hoverOpens++)
    trigger.dispatchEvent(new fixture.Event('mouseenter'))
    assert.equal(hoverOpens, 1, 'hover suppression listeners are removed')
  } finally {
    fixture.dispose()
  }
})
