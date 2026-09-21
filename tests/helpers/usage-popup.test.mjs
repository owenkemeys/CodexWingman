import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer, makeComposer, makeNativeTooltip } from './fixtures/browser-fixture.mjs'

test('usage popup is independent and its enlarged dial shares every segment with the composer dial', async () => {
  const source = await fs.readFile(new URL('../../helpers/usage-dials/apply.js', import.meta.url), 'utf8')
  const fixture = createBrowserFixture()
  const composer = makeComposer(fixture.document, 'popup', 29)
  let nativeOpens = 0
  composer.dial.addEventListener('pointerover', () => { nativeOpens++; makeNativeTooltip(fixture.document, composer.dial) })
  try {
    for (const [usedPercent, state] of [[75, 'neutral'], [92, 'warning'], [97, 'danger']]) {
      executeRenderer(source, fixture, { state: { secondary: { usedPercent, state, windowMinutes: 10080, resetsAtUnixSeconds: Math.floor(Date.now()/1000)+72576, resetLabel: 'Sat 1:20 PM' } } })
      await fixture.flush()
      const small = fixture.document.querySelector('[data-codex-helper="usage-dials"]').querySelector('[data-codex-helper-dial="secondary"]')
      small.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
      await fixture.flush()
      assert.equal(nativeOpens, 0)
      const popup = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
      assert.ok(popup)
      assert.doesNotMatch(popup.textContent, /Context window|Rate-limit resets/)
      assert.match(popup.textContent, /Resets unavailable/)
      const large = popup.querySelector('[data-codex-helper-dial="secondary"]')
      assert.ok(large)
      assert.equal(large.dataset.presentation, small.dataset.presentation)
      assert.equal(small.querySelector('[data-segment="unused"]').tagName.toLowerCase(), 'circle')
      assert.equal(large.querySelector('[data-segment="unused"]').tagName.toLowerCase(), 'path')
      for (const segment of small.querySelectorAll('[data-segment]')) {
        const copy = large.querySelector(`[data-segment="${segment.dataset.segment}"]`)
        assert.equal(copy.style.display, segment.style.display)
        if (segment.style.display === 'none') continue
        for (const key of ['data-start-percent', 'data-end-percent']) assert.equal(copy.getAttribute(key), segment.getAttribute(key))
      }
      small.dispatchEvent(new fixture.PointerEvent('pointerout', { bubbles: true }))
    }
    composer.dial.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
    await fixture.flush()
    const native = fixture.document.querySelector('[role="tooltip"]')
    assert.match(native.textContent, /Context window:/)
    assert.doesNotMatch(native.textContent, /Weekly usage|Resets available/)
  } finally { fixture.window.__codexHelperUsageDials?.cleanup(); fixture.dispose() }
})


test('five-hour and weekly account windows keep order, distinct popups, and one shared reset balance', async () => {
  const fixture = createBrowserFixture()
  makeComposer(fixture.document, 'dual-window', 30)
  const now = Math.floor(Date.now() / 1000)
  let data = { rate_limit: {
    primary_window: { used_percent: 75, limit_window_seconds: 604800, reset_at: now + 60480 },
    secondary_window: { used_percent: 25, limit_window_seconds: 18000, reset_at: now + 9000 },
  }, rate_limit_reset_credits: { available_count: 2 } }
  const listeners = new Set()
  fixture.window.__codexRoot = { _internalRoot: { current: { memoizedProps: { client: {
    getQueryState: () => ({ status: 'success', data }),
    getQueryCache: () => ({ subscribe: f => { listeners.add(f); return () => listeners.delete(f) } }),
  } } } } }
  try {
    executeRenderer(await fs.readFile(new URL('../../helpers/usage-dials/apply.js', import.meta.url), 'utf8'), fixture)
    await fixture.flush()
    const bank = fixture.document.querySelector('[data-codex-helper="usage-dials"]')
    const [five, week] = bank.children
    assert.equal(five.dataset.codexHelperDial, 'primary')
    assert.equal(week.dataset.codexHelperDial, 'secondary')
    assert.match(five.getAttribute('aria-label'), /5-hour usage: 25%/)
    assert.match(week.getAttribute('aria-label'), /Weekly usage: 75%/)
    for (const [dial, label, other] of [[five, '5-hour usage', 'Weekly usage'], [week, 'Weekly usage', '5-hour usage']]) {
      dial.dispatchEvent(new fixture.PointerEvent('pointerover', { bubbles: true }))
      const tip = fixture.document.querySelector('[data-codex-helper-tooltip="usage-fallback"]')
      assert.match(tip.textContent, new RegExp(label))
      assert.doesNotMatch(tip.textContent, new RegExp(other))
      assert.equal(tip.querySelectorAll('svg').length, 1)
      assert.match(tip.textContent, /2 resets available/)
      assert.match(tip.textContent, /Shared balance/)
      assert.equal(dial.getAttribute('aria-describedby'), tip.id)
    }
    data = { ...data, rate_limit: { primary_window: data.rate_limit.primary_window } }
    for (const listener of listeners) listener({ query: { queryKey: ['rate-limit-status'] } })
    await fixture.flush()
    assert.equal(five.style.display, 'none')
    assert.notEqual(week.style.display, 'none')
    assert.equal(fixture.window.__codexHelperUsageDials.status().primary, null)
  } finally { fixture.window.__codexHelperUsageDials?.cleanup(); fixture.dispose() }
})
