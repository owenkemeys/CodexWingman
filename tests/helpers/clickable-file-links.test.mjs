import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/clickable-file-links/', import.meta.url)
const linuxPrefix = '/mnt/example-data/'
const remoteFixtureConfig = {
      mappings: [{ sourcePrefix: linuxPrefix, windowsPrefix: 'R:/' }],
      excludePrefixes: [`${linuxPrefix}obsidian/`],
    }

const ownerSelector = '[data-codex-wingman-owner="clickable-file-links"]'

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function makeNativeFileControl(document, raw, label = 'Open the HTML prototype') {
  const control = document.createElement('button')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('data-prompt-link-label', label)
  control.textContent = label
  document.body.appendChild(control)
  return control
}

function snapshot(node) {
  if (!node?.tagName) return `#text:${JSON.stringify(node?.textContent ?? '')}`
  const attributes = [...node.attributes.entries()].sort(([a], [b]) => a.localeCompare(b))
  const content = node.children.length ? node.children.map(snapshot).join('') : JSON.stringify(node.textContent)
  return `<${node.tagName.toLowerCase()} ${JSON.stringify(attributes)}>${content}</${node.tagName.toLowerCase()}>`
}

test('package declares Jarvis translation plus local Windows path support', async () => {
  const [manifestSource, info] = await Promise.all([
    requiredFile('wingman.json'),
    requiredFile('HELPER_INFO.md'),
  ])
  assert.deepEqual(JSON.parse(manifestSource), {
    schemaVersion: 2,
    id: 'clickable-file-links',
    name: 'Clickable file links',
    version: '1.5.0',
    description: 'Opens local file and folder references in Windows applications from Codex or T3 Code, with optional remote path mappings.',
    refreshSeconds: 0,
    capabilities: ['system.openFilePath'],
    config: { mappings: [], excludePrefixes: [] },
    targets: {
      codex: { apply: 'apply.js', remove: 'remove.js' },
      't3-code': { apply: 'apply.t3.js', remove: 'remove.t3.js' },
    },
  })
  assert.match(info, /native Codex file-reference control/i)
  assert.match(info, /system\.openFilePath/)
})

test('T3 file chips gain a separate Windows-open control without changing their native click', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.t3.js'), requiredFile('remove.t3.js')])
  const fixture = createBrowserFixture()
  fixture.window.location = { protocol: 't3code:' }
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
  const makeChip = (raw) => {
    const chip = fixture.document.createElement('a')
    chip.setAttribute('class', 'chat-markdown-file-link')
    chip.setAttribute('href', raw)
    chip.textContent = 'file chip'
    markdown.appendChild(chip)
    return chip
  }
  const remote = makeChip(`${linuxPrefix}projects/report%20draft.html`)
  const local = makeChip('C:/Temp/notes.txt')
  makeChip(`${linuxPrefix}obsidian/Notes/Private.md`)
  makeChip(`${linuxPrefix}projects/../unsafe.txt`)
  const outside = fixture.document.createElement('a')
  outside.setAttribute('class', 'chat-markdown-file-link')
  outside.setAttribute('href', 'C:/Temp/outside.txt')
  fixture.document.body.appendChild(outside)
  let nativeClicks = 0
  remote.addEventListener('click', () => nativeClicks++)
  const requests = []
  try {
    executeRenderer(apply, fixture, { helperConfig: remoteFixtureConfig, wingman: { request: (...args) => requests.push(args) } })
    const buttons = fixture.document.querySelectorAll('[data-clickable-file-links-t3-open="true"]')
    assert.equal(buttons.length, 2)
    assert.equal(remote.getAttribute('href'), `${linuxPrefix}projects/report%20draft.html`)
    assert.equal(local.getAttribute('href'), 'C:/Temp/notes.txt')
    remote.dispatchEvent(new fixture.context.Event('click', { bubbles: true, cancelable: true }))
    assert.equal(nativeClicks, 1)
    assert.equal(requests.length, 0)
    const click = new fixture.context.Event('click', { bubbles: true, cancelable: true })
    remote.nextElementSibling.dispatchEvent(click)
    assert.equal(click.defaultPrevented, true)
    assert.equal(requests[0][0], 'system.openFilePath')
    assert.equal(requests[0][1].path, 'R:/projects/report draft.html')
    local.nextElementSibling.dispatchEvent(new fixture.context.Event('click', { bubbles: true, cancelable: true }))
    assert.equal(requests[1][0], 'system.openFilePath')
    assert.equal(requests[1][1].path, 'C:/Temp/notes.txt')
    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelectorAll('[data-clickable-file-links-t3-open="true"]').length, 0)
    assert.equal(remote.getAttribute('href'), `${linuxPrefix}projects/report%20draft.html`)
  } finally { fixture.dispose() }
})

test('T3 file-link script does not run in a Codex renderer', async () => {
  const apply = await requiredFile('apply.t3.js')
  const fixture = createBrowserFixture()
  try {
    executeRenderer(apply, fixture)
    assert.equal(fixture.window.__codexWingmanT3ClickableFileLinks, undefined)
  } finally { fixture.dispose() }
})

test('the Example native control exposes J slash path through DOM and React props while preserving native click handling', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}codex/Projects/ExampleWorkspace/ExampleWorkspace Agent Home/30 Projects/Example Project/brain-landing-nhc-variants.html`
  const expected = 'R:/codex/Projects/ExampleWorkspace/ExampleWorkspace Agent Home/30 Projects/Example Project/brain-landing-nhc-variants.html'
  const control = makeNativeFileControl(fixture.document, raw)
  control.setAttribute('title', raw)
  const reactPropsKey = '__reactProps$nhc'
  const reactFiberKey = '__reactFiber$nhc'
  const attachedProps = { 'data-prompt-link-href': raw, title: raw, children: 'Open the HTML prototype' }
  const parentProps = { fileReference: { href: raw, label: 'Open the HTML prototype' } }
  control[reactPropsKey] = attachedProps
  control[reactFiberKey] = {
    memoizedProps: attachedProps,
    pendingProps: attachedProps,
    alternate: null,
    return: {
      memoizedProps: parentProps,
      pendingProps: parentProps,
      alternate: null,
      return: null,
    },
  }
  const nativeDestinations = []
  const wingmanRequests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: remoteFixtureConfig,
      wingman: { request: (...args) => wingmanRequests.push(args) },
    })
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    assert.equal(control.getAttribute('title'), expected)
    assert.equal(control[reactPropsKey]['data-prompt-link-href'], expected)
    assert.equal(control[reactPropsKey].title, expected)
    assert.equal(control[reactFiberKey].return.memoizedProps.fileReference.href, expected)
    assert.equal(control.textContent, 'Open the HTML prototype')
    control.setAttribute('data-prompt-link-href', raw)
    control.setAttribute('title', raw)
    await fixture.flush()
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    assert.equal(control.getAttribute('title'), expected)
    control.addEventListener('click', () => nativeDestinations.push(control.getAttribute('data-prompt-link-href')))
    control.click()
    assert.deepEqual(nativeDestinations, [])
    assert.equal(wingmanRequests[0]?.[0], 'system.openFilePath')
    assert.equal(wingmanRequests[0]?.[1]?.path, expected)
  } finally {
    fixture.dispose()
  }
})

test('a native file control added after startup is rewritten without a message-root assumption', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}projects/CodexWingman/README.md`
  const expected = 'R:/projects/CodexWingman/README.md'

  try {
    executeRenderer(apply, fixture, { helperConfig: remoteFixtureConfig })
    const control = makeNativeFileControl(fixture.document, raw, 'Open README')
    fixture.window.__codexWingmanClickableFileLinks.reconcile()
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
  } finally {
    fixture.dispose()
  }
})

test('percent-encoded Markdown paths are decoded before Windows opens the file', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}projects/TOTK%20EventEditor/release-drafts/v1.3.4.md`
  const expected = 'R:/projects/TOTK EventEditor/release-drafts/v1.3.4.md'
  const control = makeNativeFileControl(fixture.document, raw, 'Release draft')
  const wingmanRequests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: remoteFixtureConfig,
      wingman: { request: (...args) => wingmanRequests.push(args) },
    })
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    control.click()
    assert.equal(wingmanRequests[0]?.[0], 'system.openFilePath')
    assert.equal(wingmanRequests[0]?.[1]?.path, expected)
  } finally {
    fixture.dispose()
  }
})

test('folder controls keep a clean drive path after native URL normalization and request Explorer opening', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}TOTK/Mod builds/TBW Blacksmiths v14`
  const expected = 'R:/TOTK/Mod builds/TBW Blacksmiths v14'
  const control = makeNativeFileControl(fixture.document, raw, 'TBW Blacksmiths v14')
  const wingmanRequests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: remoteFixtureConfig,
      wingman: { request: (...args) => wingmanRequests.push(args) },
    })
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    control.setAttribute('data-prompt-link-href', `/${expected}`)
    await fixture.flush()
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    control.click()
    assert.equal(wingmanRequests[0]?.[0], 'system.openFilePath')
    assert.equal(wingmanRequests[0]?.[1]?.path, expected)
  } finally {
    fixture.dispose()
  }
})
test('absolute Windows executable controls are normalized and opened through the Host', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = 'C:\\Users\\Example\\Documents\\Tools\\Example Tool.exe'
  const expected = 'C:/Users/Example/Documents/Tools/Example Tool.exe'
  const control = makeNativeFileControl(fixture.document, raw, 'Example Tool.exe')
  const wingmanRequests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: remoteFixtureConfig,
      wingman: { request: (...args) => wingmanRequests.push(args) },
    })
    assert.equal(control.getAttribute('data-codex-wingman-owner'), 'clickable-file-links')
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    control.setAttribute('data-prompt-link-href', `/${expected}`)
    await fixture.flush()
    assert.equal(control.getAttribute('data-prompt-link-href'), expected)
    control.click()
    assert.equal(wingmanRequests[0]?.[0], 'system.openFilePath')
    assert.equal(wingmanRequests[0]?.[1]?.path, expected)
  } finally {
    fixture.dispose()
  }
})

test('absolute Windows path handling is extension agnostic', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const cases = [
    ['D:\\packages\\archive.zip', 'D:/packages/archive.zip'],
    ['E:/assets/icon.png', 'E:/assets/icon.png'],
    ['F:\\workspace\\README', 'F:/workspace/README'],
    ['/G:/shared/folder', 'G:/shared/folder'],
    ['H:\\', 'H:/'],
  ]
  const controls = cases.map(([raw]) => makeNativeFileControl(fixture.document, raw, raw))
  const wingmanRequests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: remoteFixtureConfig,
      wingman: { request: (...args) => wingmanRequests.push(args) },
    })
    for (let index = 0; index < cases.length; index += 1) {
      assert.equal(controls[index].getAttribute('data-codex-wingman-owner'), 'clickable-file-links')
      assert.equal(controls[index].getAttribute('data-prompt-link-href'), cases[index][1])
      controls[index].click()
    }
    assert.deepEqual(wingmanRequests.map(([, payload]) => payload.path), cases.map(([, expected]) => expected))
  } finally {
    fixture.dispose()
  }
})

test('printed Linux and J paths remain ordinary message text', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const message = fixture.document.createElement('div')
  message.setAttribute('data-local-conversation-final-assistant', 'true')
  message.textContent = `${linuxPrefix}projects/CodexWingman/README.md and R:/projects/CodexWingman/README.md`
  fixture.document.body.appendChild(message)

  try {
    executeRenderer(apply, fixture, { helperConfig: remoteFixtureConfig })
    assert.equal(message.querySelector('a'), null)
    assert.equal(message.querySelector(ownerSelector), null)
    assert.equal(message.textContent, `${linuxPrefix}projects/CodexWingman/README.md and R:/projects/CodexWingman/README.md`)
  } finally {
    fixture.dispose()
  }
})

test('Obsidian native destinations remain owned by Obsidian Links', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}obsidian/Jarvis/House Rules/Communication and progress.md`
  const control = makeNativeFileControl(fixture.document, raw, 'Open note')

  try {
    executeRenderer(apply, fixture, { helperConfig: remoteFixtureConfig })
    assert.equal(control.getAttribute('data-prompt-link-href'), raw)
    assert.equal(control.getAttribute('data-codex-wingman-owner'), null)
  } finally {
    fixture.dispose()
  }
})

test('cleanup restores a claimed native file control and its React props exactly', async () => {
  const [apply, remove, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('remove.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const raw = `${linuxPrefix}projects/CodexWingman/README.md`
  const control = makeNativeFileControl(fixture.document, raw, 'Wingman README')
  const icon = fixture.document.createElement('svg')
  icon.setAttribute('data-native-file-icon', 'true')
  control.appendChild(icon)
  const before = snapshot(control)
  const reactPropsKey = '__reactProps$cleanup'
  const originalReactProps = { 'data-prompt-link-href': raw }
  control[reactPropsKey] = originalReactProps

  try {
    executeRenderer(apply, fixture, { helperConfig: remoteFixtureConfig })
    assert.notEqual(snapshot(control), before)
    executeRenderer(remove, fixture, { helperConfig: remoteFixtureConfig })
    assert.equal(snapshot(control), before)
    assert.equal(control[reactPropsKey], originalReactProps)
    assert.equal(control[reactPropsKey]['data-prompt-link-href'], raw)
    assert.equal(fixture.document.querySelector(ownerSelector), null)
  } finally {
    fixture.dispose()
  }
})
