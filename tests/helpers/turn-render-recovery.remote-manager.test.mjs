import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

const conversationId = '01a031a4-d5bf-7d12-a884-e2c2a592b6f8'
const rollout = [
  { type: 'session_meta', payload: { id: conversationId } },
  { type: 'turn_context', timestamp: '2026-08-25T00:00:00Z', payload: { turn_id: '01a03636-428f-7932-aeef-50fdfe7d24b6' } },
  { type: 'event_msg', timestamp: '2026-08-25T00:00:00Z', payload: { type: 'task_started', turn_id: '01a03636-428f-7932-aeef-50fdfe7d24b6' } },
  { type: 'response_item', timestamp: '2026-08-25T00:00:01Z', payload: { type: 'message', id: 'final-1', role: 'assistant', phase: 'final_answer', content: [{ type: 'output_text', text: 'Recovered remote turn' }], internal_chat_message_metadata_passthrough: { turn_id: '01a03636-428f-7932-aeef-50fdfe7d24b6' } } },
  { type: 'event_msg', timestamp: '2026-08-25T00:00:02Z', payload: { type: 'task_complete', turn_id: '01a03636-428f-7932-aeef-50fdfe7d24b6' } },
].map((row) => JSON.stringify(row)).join('\n')

class FakeManager {
  constructor() {
    this.hostId = 'remote-ssh-discovered:Jarvis'
    this.updated = false
    this.readCalls = 0
    this.conversation = {
      resumeState: 'resumed',
      rolloutPath: '/mnt/example-data/codex/home/sessions/fixture.jsonl',
      turnHistory: { kind: 'canonical', history: { entitiesByKey: {}, generation: 1, islands: [] } },
      threadRuntimeStatus: { type: 'idle' },
      cwd: '/mnt/example-data/projects/TOTK Truly Breakable Weapons',
      latestModel: 'fixture-model',
      latestReasoningEffort: 'medium',
      currentPermissions: {
        activePermissionProfile: null,
        runtimeWorkspaceRoots: ['/mnt/example-data/projects/TOTK Truly Breakable Weapons'],
        approvalPolicy: 'on-request',
        approvalsReviewer: 'auto_review',
        sandboxPolicy: { type: 'workspaceWrite', writableRoots: [], networkAccess: false },
      },
    }
    this.requestClient = { sendRequest: async () => {
      this.readCalls++
      return { dataBase64: Buffer.from(rollout, 'utf8').toString('base64') }
    } }
    this.threadStore = {
      threadsById: new Map([[conversationId, { id: conversationId, cwd: this.conversation.cwd }]]),
      params: {},
    }
    this.history = {
      mapThreadTurns: ({ turns }) => turns.map((turn) => ({ ...turn, turnId: turn.id })),
    }
  }
  getConversation(id) { return id === conversationId ? this.conversation : null }
  updateConversationState(id, update) {
    assert.equal(id, conversationId)
    update(this.conversation)
    this.updated = true
  }
  loadRemainingConversationTurns() {}
}

const makeFiberChain = (remoteManager) => {
  const fibers = Array.from({ length: 100 }, () => ({ memoizedState: null, memoizedProps: null, dependencies: null, updateQueue: null, stateNode: null, child: null, sibling: null }))
  for (let index = 0; index < fibers.length - 1; index++) fibers[index].sibling = fibers[index + 1]
  const bloat = {}
  for (let index = 0; index < 100_100; index++) bloat['entry-' + index] = {}
  fibers[20].memoizedState = { bloat }
  fibers[92].updateQueue = { memoCache: { data: [null, null, null, [remoteManager]] } }
  return fibers[0]
}

test('recovers a visible remote task when an earlier local graph exhausts legacy discovery', async () => {
  const source = fs.readFileSync(new URL('../../helpers/turn-render-recovery/apply.js', import.meta.url), 'utf8')
  const manager = new FakeManager()
  const marker = { getAttribute: () => conversationId }
  const document = {
    documentElement: {},
    querySelectorAll(selector) {
      if (selector === '[data-above-composer-conversation-id]') return [marker]
      return []
    },
  }
  const window = { __codexRoot: { _internalRoot: { current: makeFiberChain(manager) } } }
  const context = vm.createContext({
    atob, Buffer, console, document, Map,
    MutationObserver: class { observe() {}; disconnect() {} },
    Object,
    requestAnimationFrame(callback) { callback(); return 1 },
    cancelAnimationFrame() {},
    Set, String, TextDecoder, Uint8Array, WeakSet, window,
  })
  vm.runInContext(source, context)
  await new Promise((resolve) => setImmediate(resolve))
  await new Promise((resolve) => setImmediate(resolve))
  const status = window.__codexWingmanTurnRenderRecovery.status()
  assert.equal(manager.readCalls, 1)
  assert.equal(manager.updated, true)
  assert.equal(status.hydrationUnavailable, 0)
  assert.equal(status.hydrationRepaired, 1)
})
