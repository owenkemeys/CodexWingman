import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer, makeComposer, makeNativeTooltip } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/usage-dials/', import.meta.url)

async function requiredFile(name) {
  const file = new URL(name, helperRoot)
  const exists = await fs.access(file).then(() => true, () => false)
  assert.equal(exists, true, `${name} must exist`)
  return fs.readFile(file, 'utf8')
}

test('Usage Dials package declares the readable v1 package contract', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.deepEqual(manifest, {
    schemaVersion: 1,
    id: 'usage-dials',
    name: 'Usage dials',
    version: '1.3.2',
    description: 'Shows five-hour and weekly usage beside every context dial.',
    refreshSeconds: 60,
    capabilities: [],
    entrypoints: { apply: 'apply.js', remove: 'remove.js' },
  })
})

test('readable and embedded Usage Dials renderers remain identical', async () => {
  const readable = await requiredFile('apply.js')
  const embedded = await fs.readFile(new URL('../../src/CodexWingman.Core/Injection/usage-dials.mjs', import.meta.url), 'utf8')
  assert.equal(embedded, readable.replace('state || {}', '__CODEX_HELPER_SNAPSHOT__'))
})

test('renderer reconciles every context surface, preserves native tooltip, updates, rerenders, and cleans up', async () => {
  const apply = await requiredFile('apply.js')
  const remove = await requiredFile('remove.js')
  const fixture = createBrowserFixture()
  const first = makeComposer(fixture.document, 'main', 60)
  const second = makeComposer(fixture.document, 'side', 25)
  const open = (dial) => () => makeNativeTooltip(fixture.document, dial)
  first.dial.addEventListener('pointerover', open(first.dial))
  second.dial.addEventListener('pointerover', open(second.dial))
  const nowSeconds = Math.floor(Date.now() / 1000)
  const state = {
    primary: { usedPercent: 42, windowMinutes: 300, resetsAtUnixSeconds: nowSeconds + 8280, state: 'neutral', resetLabel: 'Today 7:00 PM' },
    secondary: { usedPercent: 77, windowMinutes: 10080, resetsAtUnixSeconds: nowSeconds + 532224, state: 'warning', resetLabel: 'Monday 9:00 AM' },
  }

  try {
    executeRenderer(apply, fixture, { state })
    executeRenderer(apply, fixture, { state })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 2)
    assert.deepEqual(
      fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').map((bank) => bank.previousElementSibling.id),
      ['main-context-wrapper', 'side-context-wrapper'],
    )
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-dial]').length, 4)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-style="usage-dials"]').length, 1)

    fixture.document.querySelector('[data-codex-helper-dial="primary"]')
      .dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const tooltip = fixture.document.querySelector('[role="tooltip"]')
    assert.equal(tooltip.querySelector('[data-native-context-content]'), null)
    assert.equal(tooltip.dataset.codexHelperTooltip, 'usage-fallback')
    const usageText = tooltip.textContent
    assert.match(usageText, /5-hour usage:/)
    assert.match(usageText, /42% used \(58% left\)/)
    assert.match(usageText, /% of time elapsed/)
    assert.doesNotMatch(usageText, /Weekly usage:/)
    assert.doesNotMatch(usageText, /77% used \(23% left\)/)

    executeRenderer(apply, fixture, { state: { primary: null, secondary: state.secondary } })
    await fixture.flush()
    const availableUsageText = tooltip.textContent
    assert.doesNotMatch(availableUsageText, /5-hour usage:/)
    assert.doesNotMatch(availableUsageText, /Usage unavailable/)
    assert.match(availableUsageText, /Weekly usage:/)
    assert.match(availableUsageText, /77% used \(23% left\)/)

    executeRenderer(apply, fixture, { state: { ...state, primary: { ...state.primary, usedPercent: 50, state: 'warning' } } })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 2)
    assert.match(fixture.document.querySelector('[data-codex-helper-dial="primary"]').getAttribute('aria-label'), /50%/)

    second.row.remove()
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 1)

    const rerender = makeComposer(fixture.document, 'rerendered', 33)
    rerender.dial.addEventListener('pointerover', open(rerender.dial))
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 2)

    makeNativeTooltip(fixture.document, first.dial)
    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-style="usage-dials"]').length, 0)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper-tooltip="usage"]').length, 0)
    assert.ok(fixture.document.querySelector('[data-native-context-content]'), 'native tooltip content is preserved')
    assert.equal(fixture.window.__codexHelperUsageDials, undefined)
  } finally {
    fixture.dispose()
  }
})

test('renderer keeps a native-style full background and divides timing into unused, in-time, and ahead-of-time segments', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'segments', 60)
  const durationMs = 300 * 60 * 1000
  const resetForElapsed = (elapsedPercent) => Math.floor((Date.now() + durationMs * (1 - elapsedPercent / 100)) / 1000)
  const stateFor = (usedPercent, elapsedPercent, state = 'neutral') => ({
    primary: {
      usedPercent,
      windowMinutes: 300,
      resetsAtUnixSeconds: resetForElapsed(elapsedPercent),
      state,
      resetLabel: 'Later',
    },
    secondary: null,
  })
  const segment = (name) => fixture.document.querySelector('[data-codex-helper-dial="primary"]')
    ?.querySelector(`[data-segment="${name}"]`)
  const range = (name) => [segment(name).getAttribute('data-start-percent'), segment(name).getAttribute('data-end-percent')]

  try {
    executeRenderer(apply, fixture, { state: stateFor(20, 40) })
    await fixture.flush()
    const dial = fixture.document.querySelector('[data-codex-helper-dial="primary"]')
    const styleText = fixture.document.querySelector('[data-codex-helper-style="usage-dials"]').textContent
    assert.equal(dial.className, 'text-token-description-foreground')
    assert.match(styleText, /\.codex-helper-background\s*\{[^}]*opacity:\s*\.16;/)
    assert.match(styleText, /\.codex-helper-unused\s*\{[^}]*opacity:\s*\.4;/)
    assert.deepEqual(range('background'), ['0', '100'])
    assert.deepEqual(range('unused'), ['20', '40'])
    assert.equal(segment('unused').tagName.toLowerCase(), 'circle')
    assert.equal(segment('unused').getAttribute('r'), '7')
    assert.equal(segment('unused').getAttribute('transform'), 'rotate(-90 10 10)')
    assert.equal(segment('unused').getAttribute('stroke-dasharray'), `${2 * Math.PI * 7 * .2} ${2 * Math.PI * 7 * .8}`)

    assert.deepEqual(range('within'), ['0', '20'])
    assert.equal(segment('ahead').style.display, 'none')
    assert.equal(segment('background').style.display, '')

    executeRenderer(apply, fixture, { state: stateFor(0, 100) })
    await fixture.flush()
    assert.deepEqual(range('unused'), ['0', '100'])

    executeRenderer(apply, fixture, { state: stateFor(39, 20, 'warning') })
    await fixture.flush()
    assert.deepEqual(range('background'), ['0', '100'])
    assert.equal(segment('unused').style.display, 'none')
    assert.deepEqual(range('within'), ['0', '20'])
    assert.deepEqual(range('ahead'), ['20', '39'])
    assert.equal(segment('ahead').style.display, '')

    executeRenderer(apply, fixture, { state: stateFor(95, 94, 'danger') })
    await fixture.flush()
    assert.equal(segment('unused').style.display, 'none')
    assert.equal(segment('within').style.display, 'none')
    assert.equal(segment('ahead').style.display, 'none')
    assert.deepEqual(range('background'), ['0', '100'])
    assert.deepEqual(range('conventional-value'), ['0', '95'])
    assert.equal(segment('background').style.display, '')

    executeRenderer(apply, fixture, {
      state: { primary: { usedPercent: 42, windowMinutes: null, resetsAtUnixSeconds: null, state: 'neutral', resetLabel: null }, secondary: null },
    })
    await fixture.flush()
    assert.deepEqual(range('background'), ['0', '100'])
    assert.deepEqual(range('conventional-value'), ['0', '42'])
    assert.equal(segment('background').style.display, '')
  } finally {
    fixture.window.__codexHelperUsageDials?.cleanup()
    fixture.dispose()
  }
})

test('renderer prefers native anchors over fallback model-picker candidates', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  fixture.setRectResolver((element) => element.dataset.fallbackSurface ? { width: 120, height: 32, bottom: 700 } : {})
  const native = makeComposer(fixture.document, 'native', 60)
  const fallbackRow = fixture.document.createElement('div')
  fallbackRow.style.display = 'flex'
  fallbackRow.style.alignItems = 'center'
  const picker = fixture.document.createElement('button')
  picker.dataset.fallbackSurface = 'picker'
  picker.setAttribute('role', 'button')
  picker.setAttribute('aria-haspopup', 'listbox')
  picker.setAttribute('aria-label', 'Luna Extra High')
  fallbackRow.append(picker)
  fixture.document.body.append(fallbackRow)
  try {
    executeRenderer(apply, fixture, { state: {} })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 1)
    assert.equal(fixture.document.querySelector('[data-codex-helper="usage-dials"]').previousElementSibling.id, 'native-context-wrapper')
    assert.equal(picker.previousElementSibling, null, 'fallback picker is ignored while native anchors exist')
  } finally {
    fixture.window.__codexHelperUsageDials?.cleanup()
    fixture.dispose()
  }
})

test('renderer copies the native context slot until Codex creates the real context dial', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  fixture.setRectResolver((element) => element.dataset.fallbackSurface ? { width: 120, height: 32, bottom: 700 } : {})
  const footer = fixture.document.createElement('div')
  footer.className = '_footer_fixture grid grid-cols-[minmax(0,auto)_auto_minmax(0,1fr)] items-center gap-x-[5px] select-none mb-2 px-2'
  const picker = fixture.document.createElement('button')
  picker.dataset.fallbackSurface = 'picker'
  picker.setAttribute('role', 'button')
  picker.setAttribute('aria-haspopup', 'listbox')
  picker.setAttribute('aria-label', 'Luna Extra High')
  const spacer = fixture.document.createElement('div')
  const controlsOuter = fixture.document.createElement('div')
  controlsOuter.className = 'flex min-w-0 flex-1 justify-end'
  const controls = fixture.document.createElement('div')
  controls.className = 'flex min-w-0 items-center gap-1'
  controls.style.display = 'flex'
  controls.style.alignItems = 'center'
  const modelWrapper = fixture.document.createElement('span')
  const modelPicker = fixture.document.createElement('button')
  modelPicker.dataset.fallbackSurface = 'model-picker'
  modelPicker.append(fixture.document.createTextNode('5.6 Sol Medium'))
  modelWrapper.append(modelPicker)
  controls.append(modelWrapper)
  controlsOuter.append(controls)
  footer.append(picker, spacer, controlsOuter)
  fixture.document.body.append(footer)
  const fallbackState = {
    primary: null,
    secondary: {
      usedPercent: 77,
      windowMinutes: 10080,
      resetsAtUnixSeconds: Math.floor(Date.now() / 1000) + 532224,
      state: 'warning',
      resetLabel: 'Monday 9:00 AM',
    },
  }
  try {
    const accountCache = installAccountCache(fixture, { status: 'success', data: { rate_limit_reset_credits: { available_count: 3 } } })
    executeRenderer(apply, fixture, { state: fallbackState })
    await fixture.flush()
    const bank = fixture.document.querySelector('[data-codex-helper="usage-dials"]')
    assert.ok(bank)
    const fallbackSlot = fixture.document.querySelector('[data-codex-helper-context-slot="usage-dials"]')
    assert.ok(fallbackSlot)
    assert.equal(fallbackSlot.parentElement, controls)
    assert.equal(fallbackSlot.nextElementSibling, bank)
    assert.equal(bank.nextElementSibling, modelWrapper)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 1)

    bank.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    await fixture.flush()
    const fallbackTooltip = fixture.document.querySelector('[role="tooltip"][data-codex-helper-tooltip="usage-fallback"]')
    assert.ok(fallbackTooltip)
    assert.match(fallbackTooltip.textContent, /3 resets available/)
    accountCache.set(undefined)
    await fixture.flush()
    assert.match(fallbackTooltip.textContent, /Resets unavailable/)
    assert.doesNotMatch(fallbackTooltip.textContent, /77% used/)
    // The standalone legacy renderer still accepts explicit quota state when
    // a successful cache has no native usage fields.
    accountCache.set({ status: 'success', data: {} })
    await fixture.flush()
    assert.equal(
      fallbackTooltip.className,
      'z-50 w-fit select-none text-sm whitespace-normal break-words bg-token-dropdown-background text-token-foreground border-token-border rounded-lg border px-2 py-1',
    )
    assert.equal(fallbackTooltip.style.position, 'fixed')
    assert.equal(fallbackTooltip.style.pointerEvents, 'none')
    assert.equal(fallbackTooltip.style.padding ?? '', '')
    assert.equal(fallbackTooltip.style.boxShadow ?? '', '')
    assert.equal(fallbackTooltip.querySelector('div').className, 'flex w-38 flex-col gap-0.5 text-center')
    assert.doesNotMatch(fallbackTooltip.textContent, /5-hour usage:/)
    assert.doesNotMatch(fallbackTooltip.textContent, /Usage unavailable/)
    assert.match(fallbackTooltip.textContent, /Weekly usage:/)
    assert.match(fallbackTooltip.textContent, /77% used \(23% left\)/)

    const nativeWrapper = fixture.document.createElement('span')
    nativeWrapper.id = 'late-native-context-wrapper'
    const nativeDial = fixture.document.createElement('span')
    nativeDial.setAttribute('role', 'img')
    nativeDial.setAttribute('aria-label', 'Context usage: 1%')
    nativeWrapper.append(nativeDial)
    modelWrapper.remove()
    controls.append(nativeWrapper, modelWrapper)
    await fixture.flush()
    assert.equal(fixture.document.querySelector('[data-codex-helper-context-slot="usage-dials"]'), null)
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 1)
    assert.equal(nativeWrapper.nextElementSibling?.getAttribute('data-codex-helper'), 'usage-dials')

    fixture.window.__codexHelperUsageDials.cleanup()
    assert.equal(fixture.document.querySelector('[data-codex-helper="usage-dials"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-context-slot="usage-dials"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]'), null)
  } finally {
    fixture.window.__codexHelperUsageDials?.cleanup()
    fixture.dispose()
  }
})

test('renderer replaces a stale Usage Dials controller during a Helper upgrade', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  const native = makeComposer(fixture.document, 'upgrade', 12)
  let cleaned = 0
  fixture.window.__codexHelperUsageDials = {
    version: 'legacy',
    update() { return { fallbackStrategy: 'legacy' } },
    cleanup() {
      cleaned += 1
      delete fixture.window.__codexHelperUsageDials
      return true
    },
  }
  try {
    executeRenderer(apply, fixture, { state: {} })
    await fixture.flush()
    assert.equal(cleaned, 1)
    assert.equal(fixture.window.__codexHelperUsageDials?.version, 'native-slot-v15')
    assert.equal(fixture.window.__codexHelperUsageDials?.status().fallbackStrategy, 'native-slot-v15')
    assert.equal(native.wrapper.nextElementSibling?.getAttribute('data-codex-helper'), 'usage-dials')
  } finally {
    fixture.window.__codexHelperUsageDials?.cleanup()
    fixture.dispose()
  }
})

test('renderer does not fall back to a model picker outside the native context control group', async () => {
  const apply = await requiredFile('apply.js')
  const fixture = createBrowserFixture()
  fixture.setRectResolver((element) => element.dataset.fallbackSurface ? { width: 120, height: 32, bottom: 700 } : {})
  const row = fixture.document.createElement('div')
  row.style.display = 'flex'
  row.style.alignItems = 'center'
  const settings = fixture.document.createElement('button')
  settings.dataset.fallbackSurface = 'settings'
  settings.setAttribute('role', 'button')
  settings.setAttribute('aria-label', 'Settings')
  const unrelatedModelWord = fixture.document.createElement('button')
  unrelatedModelWord.dataset.fallbackSurface = 'unrelated-model-word'
  unrelatedModelWord.setAttribute('role', 'button')
  unrelatedModelWord.setAttribute('aria-label', 'Auto-save preferences')
  const picker = fixture.document.createElement('button')
  picker.dataset.fallbackSurface = 'picker'
  picker.setAttribute('role', 'button')
  picker.append(fixture.document.createTextNode('5.6 Sol Medium'))
  row.append(settings, unrelatedModelWord, picker)
  fixture.document.body.append(row)
  try {
    executeRenderer(apply, fixture, { state: {} })
    await fixture.flush()
    assert.equal(fixture.document.querySelectorAll('[data-codex-helper="usage-dials"]').length, 0)
    assert.equal(picker.previousElementSibling, unrelatedModelWord)
  } finally {
    fixture.window.__codexHelperUsageDials?.cleanup()
    fixture.dispose()
  }
})

test('remove owns legacy Usage Dials markers even when no controller global remains', async () => {
  const remove = await requiredFile('remove.js')
  const fixture = createBrowserFixture()
  try {
    const bank = fixture.document.createElement('span')
    bank.dataset.codexHelper = 'usage-dials'
    const style = fixture.document.createElement('style')
    style.dataset.codexHelperStyle = 'usage-dials'
    const tooltip = fixture.document.createElement('div')
    tooltip.dataset.codexHelperTooltip = 'usage'
    const native = fixture.document.createElement('div')
    native.dataset.nativeContextContent = 'true'
    fixture.document.body.append(bank, style, tooltip, native)
    executeRenderer(remove, fixture)
    assert.equal(fixture.document.querySelector('[data-codex-helper="usage-dials"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-style="usage-dials"]'), null)
    assert.equal(fixture.document.querySelector('[data-codex-helper-tooltip="usage"]'), null)
    assert.ok(fixture.document.querySelector('[data-native-context-content]'))
  } finally {
    fixture.dispose()
  }
})

function installAccountCache(fixture, initialState) {
  let state = initialState
  const listeners = new Set()
  const cache = { subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener) } }
  const client = { getQueryCache: () => cache, getQueryState: () => state }
  fixture.window.__codexRoot = { _internalRoot: { current: { memoizedProps: { client } } } }
  return { set(next) { state = next; for (const listener of listeners) listener({ query: { queryKey: ['rate-limit-status'] } }) }, listeners }
}

test('native account quota renders without any session backend and updates when reset count is unchanged', async () => {
  const manifest = JSON.parse(await requiredFile('wingman.json'))
  assert.equal(manifest.entrypoints.backend, undefined, 'session reads must not gate renderer activation')
  assert.deepEqual(manifest.capabilities, [])
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'account-usage', 30)
  const response = (used) => ({ status: 'success', data: {
    rate_limit: { primary_window: { used_percent: used, limit_window_seconds: 604800, reset_at: Math.floor(Date.now()/1000)+302400 }, secondary_window: null },
    rate_limit_reset_credits: { available_count: 2 },
  } })
  const cache = installAccountCache(fixture, response(16))
  try {
    executeRenderer(await requiredFile('apply.js'), fixture)
    fixture.document.querySelector('[data-codex-helper="usage-dials"]').dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
    const tooltip = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
    await fixture.flush()
    const controller = fixture.window.__codexHelperUsageDials
    assert.equal(controller.status().primary, null)
    assert.equal(controller.status().secondary.usedPercent, 16)
    assert.match(tooltip.textContent, /16% used/)
    cache.set(response(95))
    await fixture.flush()
    assert.equal(controller.status().secondary.state, 'danger')
    assert.match(tooltip.textContent, /95% used/)
    cache.set({status:'error',data:response(95).data})
    await fixture.flush()
    assert.equal(controller.status().secondary, null, 'discard stale quota when account state fails')
    controller.cleanup()
    assert.equal(cache.listeners.size,0)
  } finally {fixture.dispose()}
})

test('native quota rejects malformed and expired windows while preserving pacing', async () => {
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'account-validation', 30)
  const now = Math.floor(Date.now()/1000)
  const window = {used_percent:80,limit_window_seconds:18000,reset_at:now+9000}
  const response = (primary) => ({status:'success',data:{rate_limit:{primary_window:primary,secondary_window:null}}})
  const cache=installAccountCache(fixture,response(window))
  try {
    executeRenderer(await requiredFile('apply.js'),fixture)
    assert.equal(fixture.window.__codexHelperUsageDials.status().primary.state,'danger')
    for (const invalid of [{...window,used_percent:'80'},{...window,reset_at:now-1},{...window,reset_at:1e30},{...window,limit_window_seconds:1.5},null,[]]) {
      cache.set(response(invalid)); await fixture.flush()
      assert.equal(fixture.window.__codexHelperUsageDials.status().primary,null)
    }
  } finally {fixture.dispose()}
})

test('reset balance uses available credits, updates the open tooltip, and removes its subscription', async () => {
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'reset-balance', 30)
  const response = (count) => ({ status: 'success', data: { rate_limit_reset_credits: { available_count: count, applicable_available_count: 0 } } })
  const cache = installAccountCache(fixture, response(3))
  try {
    executeRenderer(await requiredFile('apply.js'), fixture)
    fixture.document.querySelector('[data-codex-helper="usage-dials"]').dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
    const tooltip = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
    await fixture.flush()
    assert.match(tooltip.textContent, /3 resets available/)
    assert.equal(tooltip.querySelector('.codex-helper-usage-resets').textContent, '3 resets available')
    cache.set(response(1))
    await fixture.flush()
    assert.match(tooltip.textContent, /1 reset available/)
    cache.set(response(0))
    await fixture.flush()
    assert.match(tooltip.textContent, /0 resets available/)
    for (const count of [null, -1, 1.5, '3', undefined]) {
      cache.set(response(count))
      await fixture.flush()
      assert.match(tooltip.textContent, /Resets unavailable/)
    }
    cache.set({ status: 'error', data: response(3).data })
    await fixture.flush()
    assert.match(tooltip.textContent, /Resets unavailable/)
    fixture.window.__codexHelperUsageDials.cleanup()
    assert.equal(cache.listeners.size, 0)
  } finally { fixture.dispose() }
})

test('missing native account cache shows unavailable instead of zero', async () => {
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'missing-account', 30)
  try {
    executeRenderer(await requiredFile('apply.js'), fixture)
    fixture.document.querySelector('[data-codex-helper="usage-dials"]').dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
    const tooltip = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
    await fixture.flush()
    assert.match(tooltip.textContent, /Resets unavailable/)
  } finally { fixture.dispose() }
})
