import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const helpersRoot = fileURLToPath(new URL('../../helpers/', import.meta.url))

test('every bundled helper has agent-readable information matching its manifest', async () => {
  const folders = (await fs.readdir(helpersRoot, { withFileTypes: true })).filter((entry) => entry.isDirectory())
  assert.ok(folders.length > 0)
  for (const folder of folders) {
    const root = path.join(helpersRoot, folder.name)
    const manifest = JSON.parse(await fs.readFile(path.join(root, 'wingman.json'), 'utf8'))
    const info = (await fs.readFile(path.join(root, 'HELPER_INFO.md'), 'utf8')).replace(/\r\n/g, '\n')
    assert.ok(info.includes(`# ${manifest.name}\n`), folder.name)
    assert.ok(info.includes(`Helper ID: \`${manifest.id}\``), folder.name)
    assert.ok(info.includes(`Manifest version: ${manifest.version}`), folder.name)
    assert.match(info, /## Purpose and behavior/)
    assert.match(info, /## Sharp edges and failure behavior/)
    assert.match(info, /## Personal copy and updates/)
  }
})
