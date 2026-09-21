import assert from 'node:assert/strict'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

test('executeRenderer binds supplied and default helperConfig objects as frozen lexical values', () => {
  const fixture = createBrowserFixture()
  try {
    executeRenderer(
      'window.__suppliedHelperConfig={pathPrefix:helperConfig.pathPrefix,frozen:Object.isFrozen(helperConfig)};',
      fixture,
      { helperConfig: { pathPrefix: '/mnt/example-data/obsidian/' } },
    )
    assert.equal(fixture.window.__suppliedHelperConfig.pathPrefix, '/mnt/example-data/obsidian/')
    assert.equal(fixture.window.__suppliedHelperConfig.frozen, true)

    executeRenderer(
      'window.__defaultHelperConfig={keys:Object.keys(helperConfig).length,frozen:Object.isFrozen(helperConfig)};',
      fixture,
    )
    assert.equal(fixture.window.__defaultHelperConfig.keys, 0)
    assert.equal(fixture.window.__defaultHelperConfig.frozen, true)
  } finally {
    fixture.dispose()
  }
})
