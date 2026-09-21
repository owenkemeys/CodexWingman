import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/obsidian-links/', import.meta.url)
const prefix = '/mnt/example-data/obsidian/'
const remoteFixtureConfig = {
      mappings: [
        { sourcePrefix: `${prefix}Notes/Notes/`, vault: 'Personal' },
        { sourcePrefix: `${prefix}Synced Notes/Notes/`, vault: 'Synced Notes' },
        { sourcePrefix: prefix },
        { sourcePrefix: 'R:/obsidian/Notes/Notes/', vault: 'Personal' },
        { sourcePrefix: 'R:/obsidian/Synced Notes/Notes/', vault: 'Synced Notes' },
        { sourcePrefix: 'R:/obsidian/' },
      ],
    }

const ownerSelector = '[data-codex-wingman-owner="obsidian-links"]'
const linkSelector = '[data-obsidian-links-link="true"]'
const copySelector = '[data-obsidian-links-copy="true"]'
const iconSelector = '[data-obsidian-links-icon="true"]'

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

function makeMessage(document, author = 'assistant', marker = 'data-message-author') {
  let main = document.querySelector('main')
  if (!main) {
    main = document.createElement('main')
    document.body.appendChild(main)
  }
  const message = document.createElement('div')
  message.setAttribute(marker, marker === 'data-user-message-bubble' ? 'true' : author)
  main.appendChild(message)
  return message
}

function snapshot(node) {
  if (!node?.tagName) return `#text:${JSON.stringify(node?.textContent ?? '')}`
  const attributes = [...node.attributes.entries()].sort(([a], [b]) => a.localeCompare(b))
  const content = node.children.length ? node.children.map(snapshot).join('') : JSON.stringify(node.textContent)
  return `<${node.tagName.toLowerCase()} ${JSON.stringify(attributes)}>${content}</${node.tagName.toLowerCase()}>`
}

test('standard Obsidian open needs no plugin and action changes rebuild existing links', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const message = makeMessage(fixture.document)
  message.append(fixture.document.createTextNode('C:/Notes/Example.md'))
  const mappings = [{ sourcePrefix: 'C:/Notes/', vault: 'Notes' }]
  try {
    executeRenderer(apply, fixture, { helperConfig: { mappings, uriAction: 'open' } })
    assert.equal(fixture.document.querySelector(linkSelector).getAttribute('href'), 'obsidian://open?vault=Notes&file=Example.md')
    executeRenderer(apply, fixture, { helperConfig: { mappings, uriAction: 'wait-for-note' } })
    assert.equal(fixture.document.querySelector(linkSelector).getAttribute('href'), 'obsidian://wait-for-note?vault=Notes&file=Example.md')
  } finally { fixture.dispose() }
})

test('package declares the exact manifest/config contract and keeps the prefix out of renderer source', async () => {
  const [manifestSource, apply, remove, info] = await Promise.all([
    requiredFile('wingman.json'),
    requiredFile('apply.js'),
    requiredFile('remove.js'),
    requiredFile('HELPER_INFO.md'),
  ])
  assert.deepEqual(JSON.parse(manifestSource), {
    schemaVersion: 1,
    id: 'obsidian-links',
    name: 'Obsidian links',
    version: '1.3.1',
    description: 'Turns configured Obsidian note paths in rendered messages into safe deep links.',
    refreshSeconds: 0,
    capabilities: ['system.openObsidianUri'],
    config: { mappings: [], uriAction: 'open' },
    entrypoints: { apply: 'apply.js', remove: 'remove.js' },
  })
  assert.equal(apply.includes(prefix), false)
  assert.equal(remove.includes(prefix), false)
  assert.match(info, /^# Obsidian links$/m)
  assert.match(info, /Helper ID: `obsidian-links`/)
  assert.match(info, /Manifest version: 1\.3\.1/)
  assert.match(info, /Wait for Note/)
  assert.match(info, /## Purpose and behavior/)
  assert.match(info, /## Sharp edges and failure behavior/)
  assert.match(info, /## Personal copy and updates/)
  assert.match(info, /whole-line folder path/i)
})

test('multiple mappings open local vault paths in their configured vaults', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const foundationRaw = 'R:\\obsidian\\ExampleVault\\Projects\\Example Device\\Components\\Display.md'
  const personalRaw = 'C:\\Notes\\Daily\\2026-07-17.md#Morning'
  const message = makeMessage(fixture.document)
  message.append(fixture.document.createTextNode(`${foundationRaw}\n${personalRaw}`))

  try {
    executeRenderer(apply, fixture, {
      helperConfig: {
        mappings: [
          { sourcePrefix: 'R:/obsidian/' },
          { sourcePrefix: 'C:\\Notes\\', vault: 'Personal' },
        ],
      },
    })
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(links.length, 2)
    assert.equal(links[0].getAttribute('href'), 'obsidian://wait-for-note?vault=ExampleVault&file=Projects%2FExample%20Device%2FComponents%2FDisplay.md')
    assert.equal(links[1].getAttribute('href'), 'obsidian://wait-for-note?vault=Personal&file=Daily%2F2026-07-17.md%23Morning')
  } finally {
    fixture.dispose()
  }
})

test('bundled mappings route nested canonical Vault roots to the local Vault identity', async () => {
  const [apply, manifestSource] = await Promise.all([
    requiredFile('apply.js'),
    requiredFile('wingman.json'),
  ])
  const fixture = createBrowserFixture()
  const personalRaw = `${prefix}Notes/Notes/Kit Cards/Trip.md`
  const testRaw = `${prefix}Synced Notes/Notes/iPhone LiveSync round trip.md`
  const message = makeMessage(fixture.document)
  message.append(fixture.document.createTextNode(`${personalRaw}\n${testRaw}`))

  try {
    const manifest = { config: remoteFixtureConfig }
    executeRenderer(apply, fixture, { helperConfig: manifest.config })
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(links.length, 2)
    assert.equal(links[0].getAttribute('href'), 'obsidian://wait-for-note?vault=Personal&file=Kit%20Cards%2FTrip.md')
    assert.equal(links[1].getAttribute('href'), 'obsidian://wait-for-note?vault=Synced%20Notes&file=iPhone%20LiveSync%20round%20trip.md')
  } finally {
    fixture.dispose()
  }
})

test('a whole-line Vault folder path opens its same-named folder note', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}App Projects/Projects/CodexWingman/`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(raw))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const link = fixture.document.querySelector(linkSelector)
    assert.ok(link)
    assert.equal(link.textContent, 'Open CodexWingman')
    assert.equal(link.getAttribute('data-obsidian-links-raw-path'), raw)
    assert.equal(
      link.getAttribute('href'),
      'obsidian://wait-for-note?vault=App%20Projects&file=Projects%2FCodexWingman%2FCodexWingman.md',
    )
  } finally {
    fixture.dispose()
  }
})

test('an inline Vault folder path remains plain text', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}App Projects/Projects/CodexWingman/`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(`Read ${raw} for context.`))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(fixture.document.querySelector(linkSelector), null)
    assert.equal(message.textContent, `Read ${raw} for context.`)
  } finally {
    fixture.dispose()
  }
})

test('a Codex exact inline-code folder path opens its same-named folder note', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}App Projects/Projects/CodexWingman/`
  const message = makeMessage(fixture.document)
  const inlineCode = fixture.document.createElement('span')
  inlineCode.setAttribute('data-markdown-copy', 'inline-code')
  inlineCode.appendChild(fixture.document.createTextNode(raw))
  message.appendChild(inlineCode)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const link = inlineCode.querySelector(linkSelector)
    assert.ok(link, snapshot(inlineCode))
    assert.equal(link.textContent, 'Open CodexWingman')
    assert.equal(link.getAttribute('data-obsidian-links-raw-path'), raw)
    assert.equal(link.getAttribute('href'), 'obsidian://wait-for-note?vault=App%20Projects&file=Projects%2FCodexWingman%2FCodexWingman.md')
  } finally {
    fixture.dispose()
  }
})

test('a slash-only Windows mapping transforms a real Codex file-reference control', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = 'R:\\obsidian\\ExampleVault\\Projects\\Example Device\\Components\\Display.md'
  const message = makeMessage(fixture.document)
  const control = fixture.document.createElement('span')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('role', 'button')
  control.textContent = raw
  message.appendChild(control)

  try {
    executeRenderer(apply, fixture, {
      helperConfig: { mappings: [{ sourcePrefix: 'R:/obsidian/' }] },
    })
    assert.equal(control.getAttribute('data-obsidian-links-link'), 'true')
    assert.equal(control.getAttribute('data-prompt-link-href'), 'obsidian://wait-for-note?vault=ExampleVault&file=Projects%2FExample%20Device%2FComponents%2FDisplay.md')
  } finally {
    fixture.dispose()
  }
})

test('plain heading and block paths render exact human labels and encoded Obsidian URIs', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const headingRaw = `${prefix}Jarvis/Notifications/Telegram concierge.md#Review or reminder`
  const blockRaw = `${prefix}Work Vault/Projects/Release Notes.md#^check-42`
  const headingMessage = makeMessage(fixture.document)
  const blockMessage = makeMessage(fixture.document, 'user', 'data-user-message-bubble')
  headingMessage.appendChild(fixture.document.createTextNode(headingRaw))
  blockMessage.appendChild(fixture.document.createTextNode(blockRaw))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(links.length, 2)
    assert.equal(links[0].textContent, 'Open Telegram concierge - Review or reminder')
    assert.equal(links[0].getAttribute('href'), 'obsidian://wait-for-note?vault=Jarvis&file=Notifications%2FTelegram%20concierge.md%23Review%20or%20reminder')
    assert.equal(links[1].textContent, 'Open Release Notes - ^check-42')
    assert.equal(links[1].getAttribute('href'), 'obsidian://wait-for-note?vault=Work%20Vault&file=Projects%2FRelease%20Notes.md%23%5Echeck-42')
  } finally {
    fixture.dispose()
  }
})

test('real final-assistant message boundary transforms a plain Obsidian path', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notifications/Telegram concierge.md#Review or reminder`
  const main = fixture.document.createElement('main')
  const message = fixture.document.createElement('div')
  message.setAttribute('data-local-conversation-final-assistant', 'true')
  const paragraph = fixture.document.createElement('p')
  paragraph.appendChild(fixture.document.createTextNode(raw))
  message.appendChild(paragraph)
  main.appendChild(message)
  fixture.document.body.appendChild(main)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const link = fixture.document.querySelector(linkSelector)
    assert.ok(link, 'the real Codex assistant message boundary must be reconciled')
    assert.equal(link.textContent, 'Open Telegram concierge - Review or reminder')
    assert.equal(link.getAttribute('data-obsidian-links-generated-link'), 'true')
    const helperStyle = fixture.document.querySelector('[data-obsidian-links-style="true"]')
    assert.match(helperStyle.textContent, /data-obsidian-links-generated-link/)
    assert.match(helperStyle.textContent, /var\(--text-link/)
    assert.match(helperStyle.textContent, /text-decoration:underline/)
  } finally {
    fixture.dispose()
  }
})

test('real Codex prompt-link control keeps its label and routes clicks through the Obsidian Host Action', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notifications/Telegram concierge.md#Review or reminder`
  const expectedUri = 'obsidian://wait-for-note?vault=Jarvis&file=Notifications%2FTelegram%20concierge.md%23Review%20or%20reminder'
  const message = fixture.document.createElement('div')
  message.setAttribute('data-local-conversation-final-assistant', 'true')
  const control = fixture.document.createElement('span')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('data-prompt-link-label', 'Open Telegram concierge review')
  control.setAttribute('role', 'button')
  const iconHost = fixture.document.createElement('span')
  const nativeIcon = fixture.document.createElement('svg')
  nativeIcon.setAttribute('data-native-file-icon', 'true')
  iconHost.appendChild(nativeIcon)
  const label = fixture.document.createElement('span')
  label.textContent = 'Open Telegram concierge review'
  control.append(iconHost, label)
  message.appendChild(control)
  const main = fixture.document.createElement('main')
  main.appendChild(message)
  fixture.document.body.appendChild(main)
  const requests = []

  try {
    executeRenderer(apply, fixture, {
      helperConfig: { pathPrefix: prefix },
      wingman: { request: (...args) => requests.push(args) },
    })
    assert.equal(control.getAttribute('data-obsidian-links-link'), 'true')
    assert.equal(control.getAttribute('data-obsidian-links-raw-path'), raw)
    assert.equal(control.getAttribute('data-prompt-link-href'), expectedUri)
    assert.equal(label.textContent, 'Open Telegram concierge review')
    assert.equal(nativeIcon.parentNode?.getAttribute('data-obsidian-links-native-icon-stash'), 'true')
    assert.equal(nativeIcon.parentNode?.hasAttribute('hidden'), true)
    assert.ok(iconHost.querySelector(iconSelector), 'the native file icon is replaced with the Obsidian icon')
    assert.equal(control.nextElementSibling?.matches(copySelector), true)
    control.click()
    assert.equal(requests.length, 1)
    assert.equal(requests[0][0], 'system.openObsidianUri')
    assert.equal(requests[0][1].uri, expectedUri)
  } finally {
    fixture.dispose()
  }
})

test('remote final outside the first main claims its real Codex prompt-link control', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/House Rules/House Rules.md`
  const expectedUri = 'obsidian://wait-for-note?vault=Jarvis&file=House%20Rules%2FHouse%20Rules.md'
  fixture.document.body.appendChild(fixture.document.createElement('main'))
  const final = fixture.document.createElement('div')
  final.setAttribute('class', 'flex flex-col')
  final.setAttribute('data-local-conversation-final-assistant', 'true')
  const unit = fixture.document.createElement('div')
  unit.setAttribute('data-content-search-unit-key', 'turn:assistant')
  const annotation = fixture.document.createElement('div')
  annotation.setAttribute('data-response-annotation-conversation', '019ff14a-2256-7911-a248-a372dc32449b')
  const markdown = fixture.document.createElement('div')
  markdown.setAttribute('data-markdown-text-style', 'assistant-message')
  const control = fixture.document.createElement('span')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('data-prompt-link-label', 'Jarvis House Rules')
  control.setAttribute('role', 'button')
  control.setAttribute('tabindex', '0')
  control.setAttribute('data-inline-mention-interactive', '')
  control.setAttribute('class', 'cursor-interaction')
  control.textContent = 'Jarvis House Rules'
  markdown.appendChild(control)
  annotation.appendChild(markdown)
  unit.appendChild(annotation)
  final.appendChild(unit)
  fixture.document.body.appendChild(final)
  const requests = []
  let nativeClicks = 0
  final.addEventListener('click', () => nativeClicks++)

  try {
    executeRenderer(apply, fixture, {
      helperConfig: { pathPrefix: prefix },
      wingman: { request: (...args) => requests.push(args) },
    })
    assert.equal(control.getAttribute('data-codex-wingman-owner'), 'obsidian-links')
    assert.equal(control.getAttribute('data-obsidian-links-link'), 'true')
    assert.equal(control.getAttribute('data-prompt-link-href'), expectedUri)
    control.click()
    assert.equal(nativeClicks, 0)
    assert.equal(requests.length, 1)
    assert.equal(requests[0][0], 'system.openObsidianUri')
    assert.equal(requests[0][1].uri, expectedUri)
  } finally {
    fixture.dispose()
  }
})

test('a malformed earlier canonical path cannot abort a later real prompt-link control', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const earlier = makeMessage(fixture.document, 'user', 'data-user-message-bubble')
  earlier.appendChild(fixture.document.createTextNode(`Earlier invalid path: ${prefix}Jarvis/.md`))
  const raw = `${prefix}Jarvis/House Rules/House Rules.md`
  const expectedUri = 'obsidian://wait-for-note?vault=Jarvis&file=House%20Rules%2FHouse%20Rules.md'
  const message = makeMessage(fixture.document)
  const control = fixture.document.createElement('span')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('data-prompt-link-label', 'Jarvis House Rules')
  control.setAttribute('role', 'button')
  control.textContent = 'Jarvis House Rules'
  message.appendChild(control)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(control.getAttribute('data-codex-wingman-owner'), 'obsidian-links')
    assert.equal(control.getAttribute('data-prompt-link-href'), expectedUri)
    assert.equal(earlier.textContent, `Earlier invalid path: ${prefix}Jarvis/.md`)
  } finally {
    fixture.dispose()
  }
})

test('cleanup restores a real Codex prompt-link control exactly', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notes/Restore prompt link.md`
  const message = fixture.document.createElement('div')
  message.setAttribute('data-local-conversation-final-assistant', 'true')
  const control = fixture.document.createElement('span')
  control.setAttribute('data-file-reference', 'true')
  control.setAttribute('data-prompt-link-href', raw)
  control.setAttribute('data-prompt-link-label', 'Keep this label')
  control.setAttribute('role', 'button')
  const iconHost = fixture.document.createElement('span')
  const nativeIcon = fixture.document.createElement('svg')
  nativeIcon.setAttribute('data-native-file-icon', 'true')
  iconHost.appendChild(nativeIcon)
  const label = fixture.document.createElement('strong')
  label.textContent = 'Keep this label'
  control.append(iconHost, label)
  message.appendChild(control)
  const main = fixture.document.createElement('main')
  main.appendChild(message)
  fixture.document.body.appendChild(main)
  const before = snapshot(control)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.notEqual(snapshot(control), before)
    executeRenderer(remove, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(snapshot(control), before)
    assert.equal(control.children[0], iconHost)
    assert.equal(iconHost.children[0], nativeIcon)
    assert.equal(control.children[1], label)
  } finally {
    fixture.dispose()
  }
})

test('plain matching skips false md suffixes and scans through md directory components to the final note', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const invalid = [
    `${prefix}Jarvis/Notes/Note.md.bak`,
    `${prefix}Jarvis/Notes/Note.mdx`,
    `${prefix}Jarvis/Notes/Note.md?download=1`,
    `${prefix}Jarvis/Notes/Note.md-longer`,
  ]
  const nested = `${prefix}Jarvis/Folder.md/Note.md`
  const invalidMessages = invalid.map((raw) => {
    const message = makeMessage(fixture.document)
    message.appendChild(fixture.document.createTextNode(raw))
    return message
  })
  const nestedMessage = makeMessage(fixture.document, 'user', 'data-user-message-bubble')
  nestedMessage.appendChild(fixture.document.createTextNode(nested))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.deepEqual(invalidMessages.map((message) => message.textContent), invalid)
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(links.length, 1)
    assert.equal(links[0].getAttribute('data-obsidian-links-raw-path'), nested)
    assert.equal(links[0].textContent, 'Open Note')
    assert.equal(links[0].getAttribute('href'), 'obsidian://wait-for-note?vault=Jarvis&file=Folder.md%2FNote.md')
  } finally {
    fixture.dispose()
  }
})

test('line-leading prose-like fragment stays raw while a labeled anchor keeps exact punctuation', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const ambiguous = `${prefix}Jarvis/Notes/Heading.md#Review for details.`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(ambiguous))
  const exactAnchor = fixture.document.createElement('a')
  exactAnchor.setAttribute('href', ambiguous)
  exactAnchor.appendChild(fixture.document.createTextNode('Exact punctuated heading'))
  message.appendChild(exactAnchor)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(message.children[0].textContent, ambiguous)
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 1)
    assert.equal(exactAnchor.textContent, 'Exact punctuated heading')
    assert.equal(
      exactAnchor.getAttribute('href'),
      'obsidian://wait-for-note?vault=Jarvis&file=Notes%2FHeading.md%23Review%20for%20details.',
    )
  } finally {
    fixture.dispose()
  }
})

test('ambiguous inline fragments preserve adjacent prose and do not swallow later same-line paths', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const ambiguousHeading = `${prefix}Jarvis/Notes/Heading.md#Review`
  const laterPlain = `${prefix}Jarvis/Notes/Later.md`
  const ambiguousBlock = `${prefix}Jarvis/Notes/Block.md#^item-1`
  const finalPlain = `${prefix}Jarvis/Notes/Final.md`
  const headingSource = `${ambiguousHeading} for details; then ${laterPlain} and ${finalPlain}.`
  const blockSource = `${ambiguousBlock} beside ${laterPlain} and ${finalPlain}.`
  const headingMessage = makeMessage(fixture.document)
  const blockMessage = makeMessage(fixture.document, 'user', 'data-user-message-bubble')
  headingMessage.appendChild(fixture.document.createTextNode(headingSource))
  blockMessage.appendChild(fixture.document.createTextNode(blockSource))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(
      links.length,
      4,
      JSON.stringify(links.map((link) => link.getAttribute('data-obsidian-links-raw-path'))),
    )
    assert.deepEqual(links.map((link) => link.getAttribute('data-obsidian-links-raw-path')), [
      laterPlain, finalPlain, laterPlain, finalPlain,
    ])
    assert.equal(headingMessage.textContent.includes(`${ambiguousHeading} for details; then`), true)
    assert.equal(blockMessage.textContent.includes(`${ambiguousBlock} beside`), true)
  } finally {
    fixture.dispose()
  }
})

test('recognized anchors preserve authored markup, humanize raw labels, and expose owned raw-path controls', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const authoredRaw = `${prefix}Jarvis/Guides/Obsidian Links.md#Deep links`
  const rawLabelPath = `${prefix}Jarvis/Notes/Raw Label.md`
  const message = makeMessage(fixture.document)
  const authored = fixture.document.createElement('a')
  authored.setAttribute('href', authoredRaw)
  authored.setAttribute('class', 'author-link')
  const strong = fixture.document.createElement('strong')
  strong.textContent = 'Read the link guide'
  authored.appendChild(strong)
  const rawLabel = fixture.document.createElement('a')
  rawLabel.setAttribute('href', rawLabelPath)
  rawLabel.textContent = rawLabelPath
  message.append(authored, fixture.document.createTextNode(' / '), rawLabel)
  const originalStrong = authored.children[0]

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(authored.children[1], originalStrong)
    assert.equal(authored.textContent, 'Read the link guide')
    assert.equal(authored.getAttribute('class'), 'author-link')
    assert.equal(authored.getAttribute('href'), 'obsidian://wait-for-note?vault=Jarvis&file=Guides%2FObsidian%20Links.md%23Deep%20links')
    assert.equal(rawLabel.children[1].textContent, 'Open Raw Label')
    for (const anchor of [authored, rawLabel]) {
      assert.equal(anchor.getAttribute('data-codex-wingman-owner'), 'obsidian-links')
      assert.equal(anchor.getAttribute('data-obsidian-links-raw-path'), anchor === authored ? authoredRaw : rawLabelPath)
      const icon = anchor.querySelector(iconSelector)
      assert.ok(icon)
      assert.equal(icon.tagName, 'SVG')
      assert.match(icon.querySelector('path')?.getAttribute('d') || '', /^M/)
      assert.equal(icon.getAttribute('aria-hidden'), 'true')
      const copy = anchor.nextElementSibling
      assert.equal(copy?.matches(copySelector), true)
      assert.notEqual(copy.textContent, 'Copy')
      assert.equal(copy.getAttribute('aria-label'), 'Copy Obsidian source path')
      assert.equal(copy.getAttribute('data-obsidian-links-raw-path'), anchor === authored ? authoredRaw : rawLabelPath)
      assert.equal(copy.getAttribute('title'), anchor === authored ? authoredRaw : rawLabelPath)
    }
  } finally {
    fixture.dispose()
  }
})

test('affected links use the exact supplied Obsidian artwork as a unique prefix icon', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const plainRaw = `${prefix}ExampleVault/Home.md#ExampleVault home`
  const authoredRaw = `${prefix}ExampleVault/README.md#ExampleVault vault`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(plainRaw))
  const authored = fixture.document.createElement('a')
  authored.setAttribute('href', authoredRaw)
  const label = fixture.document.createElement('strong')
  label.textContent = 'ExampleVault documentation'
  authored.appendChild(label)
  message.appendChild(authored)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const links = fixture.document.querySelectorAll(linkSelector)
    assert.equal(links.length, 2)
    const icons = links.map((link) => link.querySelector(iconSelector))
    for (const [index, icon] of icons.entries()) {
      assert.equal(links[index].children[0], icon, 'the Obsidian mark prefixes the visible label')
      assert.equal(icon.getAttribute('viewBox'), '0 0 397 512')
      assert.equal(icon.getAttribute('preserveAspectRatio'), 'xMidYMid meet')
      assert.equal(icon.querySelectorAll('[data-obsidian-links-official-path="true"]').length, 9)
      assert.equal(icon.querySelectorAll('[data-obsidian-links-gradient="true"]').length, 8)
      assert.equal(icon.querySelector('[data-obsidian-links-official-path="true"]').getAttribute('fill'), '#6C31E3')
      const firstGradient = icon.querySelector('[data-obsidian-links-gradient-index="0"]')
      assert.equal(firstGradient.getAttribute('gradientTransform'), 'translate(103.845 469.791) rotate(-104.574) scale(232.965 155.247)')
      assert.equal(firstGradient.querySelectorAll('stop').length, 2)
      const lastGradient = icon.querySelector('[data-obsidian-links-gradient-index="7"]')
      assert.equal(lastGradient.querySelectorAll('stop')[1].getAttribute('offset'), '0.46738')
    }
    const firstIds = icons[0].querySelectorAll('[data-obsidian-links-gradient="true"]').map((gradient) => gradient.id)
    const secondIds = icons[1].querySelectorAll('[data-obsidian-links-gradient="true"]').map((gradient) => gradient.id)
    assert.equal(new Set([...firstIds, ...secondIds]).size, 16, 'every repeated icon has collision-free gradient IDs')
    assert.equal(authored.children[1], label, 'authored label markup remains after the prefix icon')
  } finally {
    fixture.dispose()
  }
})

test('copy control writes the exact raw path and clipboard failure leaves unrelated UI unchanged', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notes/Copy Me.md`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(raw))
  const writes = []
  fixture.context.navigator = { clipboard: { writeText(value) { writes.push(value); return Promise.resolve() } } }
  fixture.window.navigator = fixture.context.navigator

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const copy = fixture.document.querySelector(copySelector)
    copy.click()
    assert.deepEqual(writes, [raw])
    const unrelated = fixture.document.createElement('div')
    unrelated.textContent = 'keep me'
    fixture.document.body.appendChild(unrelated)
    const before = snapshot(unrelated)
    fixture.context.navigator.clipboard.writeText = () => Promise.reject(new Error('denied'))
    copy.click()
    await fixture.flush()
    assert.equal(snapshot(unrelated), before)
  } finally {
    fixture.dispose()
  }
})

test('owned links request the narrow Obsidian Host Action while modified clicks keep native behavior', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notes/Open Me.md#Heading`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(raw))
  const requests = []

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix }, wingman: { request: (...args) => requests.push(args) } })
    const link = fixture.document.querySelector(linkSelector)
    link.click()
    assert.equal(requests.length, 1)
    assert.equal(requests[0][0], 'system.openObsidianUri')
    assert.equal(requests[0][1].uri, 'obsidian://wait-for-note?vault=Jarvis&file=Notes%2FOpen%20Me.md%23Heading')
    link.dispatchEvent(new fixture.context.Event('click', { bubbles: true, cancelable: true, ctrlKey: true }))
    assert.equal(requests.length, 1)
  } finally {
    fixture.dispose()
  }
})

test('non-target content and excluded message subtrees remain exactly unchanged', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const targetLooking = `${prefix}Jarvis/Notes/Leave Me.md`
  const main = fixture.document.createElement('main')
  fixture.document.body.appendChild(main)
  const message = makeMessage(fixture.document)
  const ordinary = fixture.document.createTextNode('/home/user/note.md C:\\Notes\\note.md https://example.test/note.md')
  message.appendChild(ordinary)
  const unrelatedAnchor = fixture.document.createElement('a')
  unrelatedAnchor.setAttribute('href', 'https://example.test')
  unrelatedAnchor.textContent = targetLooking
  message.appendChild(unrelatedAnchor)
  const excludedTags = ['code', 'pre', 'script', 'style', 'textarea', 'input', 'button']
  const excluded = excludedTags.map((tag) => {
    const element = fixture.document.createElement(tag)
    element.textContent = targetLooking
    message.appendChild(element)
    return element
  })
  const codeAnchor = fixture.document.createElement('a')
  codeAnchor.setAttribute('href', targetLooking)
  codeAnchor.textContent = 'code link'
  excluded[0].appendChild(codeAnchor)
  const editable = fixture.document.createElement('div')
  editable.setAttribute('contenteditable', 'true')
  editable.textContent = targetLooking
  message.appendChild(editable)
  const composer = fixture.document.createElement('div')
  composer.setAttribute('data-codex-composer', 'true')
  composer.textContent = targetLooking
  message.appendChild(composer)
  const owned = fixture.document.createElement('span')
  owned.setAttribute('data-codex-wingman-owner', 'another-helper')
  owned.textContent = targetLooking
  message.appendChild(owned)
  const selfEditableAnchor = fixture.document.createElement('a')
  selfEditableAnchor.setAttribute('href', targetLooking)
  selfEditableAnchor.setAttribute('contenteditable', 'true')
  selfEditableAnchor.textContent = 'editable link'
  message.appendChild(selfEditableAnchor)
  const selfOwnedAnchor = fixture.document.createElement('a')
  selfOwnedAnchor.setAttribute('href', targetLooking)
  selfOwnedAnchor.setAttribute('data-codex-wingman-owner', 'another-helper')
  selfOwnedAnchor.textContent = 'owned link'
  message.appendChild(selfOwnedAnchor)
  const outside = fixture.document.createElement('div')
  outside.textContent = targetLooking
  fixture.document.body.appendChild(outside)
  const broadRoot = fixture.document.createElement('article')
  broadRoot.setAttribute('role', 'article')
  broadRoot.setAttribute('data-message-id', 'not-safe')
  broadRoot.textContent = targetLooking
  main.appendChild(broadRoot)
  const userNavigation = fixture.document.createElement('nav')
  userNavigation.setAttribute('aria-label', 'User messages')
  userNavigation.textContent = targetLooking
  main.appendChild(userNavigation)
  const observed = [message, unrelatedAnchor, ...excluded, editable, composer, owned, selfEditableAnchor, selfOwnedAnchor, outside, broadRoot, userNavigation]
  const before = observed.map(snapshot)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.deepEqual(observed.map(snapshot), before)
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 0)
    assert.equal(fixture.document.querySelectorAll(copySelector).length, 0)
    assert.equal(codeAnchor.getAttribute('href'), targetLooking)
    assert.equal(selfEditableAnchor.getAttribute('href'), targetLooking)
    assert.equal(selfOwnedAnchor.getAttribute('data-codex-wingman-owner'), 'another-helper')
  } finally {
    fixture.dispose()
  }
})

test('repeat apply is idempotent and streaming plus inserted message content reconcile once', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const streaming = makeMessage(fixture.document)
  const text = fixture.document.createTextNode(`${prefix}Jarvis/Notes/Stream`)
  streaming.appendChild(text)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const controller = fixture.window.__codexWingmanObsidianLinks
    assert.equal(controller.version, 'message-links-v12')
    assert.equal(typeof controller.cleanup, 'function')
    assert.equal(typeof controller.reconcile, 'function')
    assert.equal(typeof controller.status, 'function')
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 0)

    text.nodeValue += 'ing.md'
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 1)

    const inserted = makeMessage(fixture.document, 'assistant', 'data-author')
    inserted.appendChild(fixture.document.createTextNode(`${prefix}Jarvis/Notes/Inserted.md`))
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 2)

    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    await fixture.flush()
    assert.equal(fixture.window.__codexWingmanObsidianLinks, controller)
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 2)
    assert.equal(fixture.document.querySelectorAll(copySelector).length, 2)
    assert.equal(controller.status().linkCount, 2)

    const ownedLabelText = fixture.document.querySelector(linkSelector).children[0]
    ownedLabelText.nodeValue += '!'
    assert.equal(controller.status().pending, false, 'owned character-data mutations are ignored')

  } finally {
    fixture.dispose()
  }
})

test('owned child-list mutations during a scheduled reconcile do not queue a second frame', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const message = makeMessage(fixture.document)
  const frames = new Map()
  let nextFrame = 0
  const requestAnimationFrame = (callback) => {
    const id = ++nextFrame
    frames.set(id, callback)
    return id
  }
  const cancelAnimationFrame = (id) => frames.delete(id)
  fixture.context.requestAnimationFrame = requestAnimationFrame
  fixture.context.cancelAnimationFrame = cancelAnimationFrame
  fixture.window.requestAnimationFrame = requestAnimationFrame
  fixture.window.cancelAnimationFrame = cancelAnimationFrame

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    message.appendChild(fixture.document.createTextNode(`${prefix}Jarvis/Notes/One Frame.md`))
    assert.equal(frames.size, 1, 'the external insertion schedules one reconcile')
    const [id, callback] = frames.entries().next().value
    frames.delete(id)
    callback(Date.now())
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 1)
    assert.equal(frames.size, 0, 'owned replacement mutations do not schedule another reconcile')
  } finally {
    fixture.dispose()
  }
})

test('nested message roots reconcile once and remain idempotent', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const outer = makeMessage(fixture.document, 'assistant', 'data-message-author')
  const inner = fixture.document.createElement('div')
  inner.setAttribute('data-author', 'user')
  inner.appendChild(fixture.document.createTextNode(`${prefix}Jarvis/Notes/Nested.md`))
  outer.appendChild(inner)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const controller = fixture.window.__codexWingmanObsidianLinks
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(fixture.window.__codexWingmanObsidianLinks, controller)
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 1)
    assert.equal(fixture.document.querySelectorAll(copySelector).length, 1)
  } finally {
    fixture.dispose()
  }
})

test('controller-missing remove restores raw-destination anchor child markup exactly', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  const raw = `${prefix}Jarvis/Notes/Marked Raw.md`
  const message = makeMessage(fixture.document)
  const anchor = fixture.document.createElement('a')
  anchor.setAttribute('href', raw)
  anchor.setAttribute('class', 'authored')
  const em = fixture.document.createElement('em')
  em.setAttribute('data-author-markup', 'keep')
  em.textContent = raw
  anchor.appendChild(em)
  message.appendChild(anchor)
  const before = snapshot(anchor)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.ok(anchor.querySelector('[data-obsidian-links-original-label="true"]'))
    delete fixture.window.__codexWingmanObsidianLinks
    executeRenderer(remove, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(snapshot(anchor), before)
    assert.equal(anchor.children[0], em)
  } finally {
    fixture.dispose()
  }
})

test('config identity replaces changed prefixes and invalid reload cleans the active controller', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const alternatePrefix = '/other-vaults/'
  const firstRaw = `${prefix}Jarvis/Notes/First.md`
  const secondRaw = `${alternatePrefix}Second Vault/Notes/Second.md`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(`${firstRaw}\n${secondRaw}`))

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    const firstController = fixture.window.__codexWingmanObsidianLinks
    assert.equal(firstController.configIdentity, JSON.stringify({ action: 'wait-for-note', mappings: [{ sourcePrefix: prefix, vault: null }] }))
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: alternatePrefix } })
    const secondController = fixture.window.__codexWingmanObsidianLinks
    assert.notEqual(secondController, firstController)
    assert.equal(secondController.configIdentity, JSON.stringify({ action: 'wait-for-note', mappings: [{ sourcePrefix: alternatePrefix, vault: null }] }))
    assert.equal(fixture.document.querySelectorAll(linkSelector).length, 1)
    assert.equal(fixture.document.querySelector(linkSelector).getAttribute('data-obsidian-links-raw-path'), secondRaw)

    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: 'invalid' } })
    assert.equal(fixture.window.__codexWingmanObsidianLinks, undefined)
    assert.equal(message.textContent, `${firstRaw}\n${secondRaw}`)
  } finally {
    fixture.dispose()
  }
})

test('remove restores exact generated text and original anchor state while preserving unrelated DOM', async () => {
  const [apply, remove] = await Promise.all([requiredFile('apply.js'), requiredFile('remove.js')])
  const fixture = createBrowserFixture()
  const plainRaw = `${prefix}Jarvis/Notes/Restore Me.md#Original heading`
  const anchorRaw = `${prefix}Jarvis/Notes/Authored.md`
  const message = makeMessage(fixture.document)
  message.appendChild(fixture.document.createTextNode(`before ${plainRaw} after`))
  const anchor = fixture.document.createElement('a')
  anchor.setAttribute('href', anchorRaw)
  anchor.setAttribute('title', 'author title')
  anchor.setAttribute('data-foreign', 'keep')
  const em = fixture.document.createElement('em')
  em.textContent = 'Authored label'
  anchor.appendChild(em)
  message.appendChild(anchor)
  const unrelated = fixture.document.createElement('div')
  unrelated.setAttribute('data-foreign', 'unchanged')
  unrelated.textContent = 'Do not touch'
  fixture.document.body.appendChild(unrelated)
  const anchorBefore = snapshot(anchor)
  const unrelatedBefore = snapshot(unrelated)

  try {
    executeRenderer(apply, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.ok(fixture.document.querySelector(linkSelector))
    executeRenderer(remove, fixture, { helperConfig: { pathPrefix: prefix } })
    assert.equal(message.textContent, `before ${plainRaw} afterAuthored label`)
    assert.equal(snapshot(anchor), anchorBefore)
    assert.equal(snapshot(unrelated), unrelatedBefore)
    assert.equal(fixture.document.querySelectorAll(ownerSelector).length, 0)
    assert.equal(fixture.window.__codexWingmanObsidianLinks, undefined)
  } finally {
    fixture.dispose()
  }
})

test('invalid helper configuration fails closed without starting a controller', async () => {
  const apply = await requiredFile('apply.js')
  for (const helperConfig of [{}, { pathPrefix: 42 }, { pathPrefix: 'relative/' }, { pathPrefix: '/missing-end' }]) {
    const fixture = createBrowserFixture()
    const message = makeMessage(fixture.document)
    const raw = `${prefix}Jarvis/Notes/Untouched.md`
    message.appendChild(fixture.document.createTextNode(raw))
    try {
      executeRenderer(apply, fixture, { helperConfig })
      assert.equal(message.textContent, raw)
      assert.equal(fixture.window.__codexWingmanObsidianLinks, undefined)
    } finally {
      fixture.dispose()
    }
  }
})
