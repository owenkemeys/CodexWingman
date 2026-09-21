import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { createBrowserFixture, executeRenderer } from './fixtures/browser-fixture.mjs'

const helperRoot = new URL('../../helpers/turn-render-recovery/', import.meta.url)
const helperFile = (name) => readFile(new URL(name, helperRoot), 'utf8')

function attachNativeTurn(fixture, {
  status = 'completed',
  renderedText = 'The response stops early',
  fullText = 'The response stops early but has a distinctive complete ending marker',
  clientHeight = 160,
  scrollHeight = 160,
} = {}) {
  const turnKey = 'history-content:turn:01a02ffb-1111-7111-8111-111111111111'
  const turn = fixture.document.createElement('div')
  turn.dataset.turnKey = turnKey
  turn.clientHeight = clientHeight
  turn.scrollHeight = scrollHeight
  const final = fixture.document.createElement('div')
  final.dataset.localConversationFinalAssistant = 'true'
  final.textContent = renderedText
  const footer = fixture.document.createElement('div')
  const timestamp = fixture.document.createElement('span')
  timestamp.dataset.assistantMessageSentTime = 'true'
  timestamp.setAttribute('class', 'ml-1.5 flex h-full items-center opacity-0 group-focus-within:opacity-100 group-hover:opacity-100')
  timestamp.textContent = '16:14'
  footer.appendChild(timestamp)
  turn.append(final, footer)
  fixture.document.body.appendChild(turn)

  let heights = { [turnKey]: 100, earlier: 80 }
  const updates = []
  const heightHook = {
    memoizedState: heights,
    baseState: heights,
    queue: {
      dispatch(action) {
        heights = typeof action === 'function' ? action(heights) : action
        heightHook.memoizedState = heights
        heightHook.baseState = heights
        updates.push(heights)
      },
    },
    next: null,
  }
  const listFiber = {
    memoizedProps: {
      entries: [],
      latestTurnFooterKey: turnKey,
      onLatestTurnHeightChange() {},
      onApiChange() {},
      RowComponent() {},
    },
    memoizedState: heightHook,
    return: null,
  }
  const rowFiber = {
    memoizedProps: {
      constrainedHeightPx: 100,
      entry: {
        isMostRecentTurn: true,
        turnKey,
        turnId: '01a02ffb-1111-7111-8111-111111111111',
        turn: {
          status,
          items: [
            { type: 'userMessage', id: 'user-1' },
            { type: 'agentMessage', id: 'assistant-1', text: fullText },
          ],
        },
      },
    },
    memoizedState: null,
    return: listFiber,
  }
  turn.__reactFiber$fixture = rowFiber
  return { turn, turnKey, footer, timestamp, updates: () => updates }
}

test('invalidates the native height cache when a completed final response is truncated', async () => {
  const fixture = createBrowserFixture()
  try {
    const native = attachNativeTurn(fixture)
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()

    assert.equal(native.updates().length, 1)
    assert.equal(native.turnKey in native.updates()[0], false)
    assert.equal(native.updates()[0].earlier, 80)
  } finally {
    fixture.dispose()
  }
})

test('places a provenance note after the native timestamp when repairing a clipped turn', async () => {
  const fixture = createBrowserFixture()
  try {
    const native = attachNativeTurn(fixture)
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()

    const notes = native.turn.querySelectorAll('[data-turn-render-recovery-note]')
    assert.equal(notes.length, 1)
    assert.equal(notes[0].textContent, 'Turn restored in UI by Wingman')
    assert.equal(native.timestamp.nextElementSibling, notes[0])
  } finally {
    fixture.dispose()
  }
})

test('leaves a completed response alone when its distinctive ending is rendered', async () => {
  const fixture = createBrowserFixture()
  try {
    const fullText = 'The response has a distinctive complete ending marker'
    const native = attachNativeTurn(fixture, { renderedText: fullText, fullText })
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    assert.equal(native.updates().length, 0)
    assert.equal(native.turn.querySelectorAll('[data-turn-render-recovery-note]').length, 0)
  } finally {
    fixture.dispose()
  }
})

test('ignores the small scroll-height gap produced by normal browser rounding', async () => {
  const fixture = createBrowserFixture()
  try {
    const fullText = 'The response has a distinctive complete ending marker'
    const native = attachNativeTurn(fixture, {
      renderedText: fullText,
      fullText,
      clientHeight: 100,
      scrollHeight: 103,
    })
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    assert.equal(native.updates().length, 0)
    assert.equal(native.turn.querySelectorAll('[data-turn-render-recovery-note]').length, 0)
  } finally {
    fixture.dispose()
  }
})
test('invalidates the native height cache when full response text is present but the row is clipped', async () => {
  const fixture = createBrowserFixture()
  try {
    const fullText = 'The response has a distinctive complete ending marker'
    const native = attachNativeTurn(fixture, {
      renderedText: fullText,
      fullText,
      clientHeight: 100,
      scrollHeight: 160,
    })
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()

    assert.equal(native.updates().length, 1)
    assert.equal(native.turnKey in native.updates()[0], false)
  } finally {
    fixture.dispose()
  }
})
test('does not invalidate an active turn while it is still streaming', async () => {
  const fixture = createBrowserFixture()
  try {
    const native = attachNativeTurn(fixture, { status: 'inProgress' })
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    assert.equal(native.updates().length, 0)
    assert.equal(native.turn.querySelectorAll('[data-turn-render-recovery-note]').length, 0)
  } finally {
    fixture.dispose()
  }
})

test('remove clears the helper global without touching native conversation nodes', async () => {
  const fixture = createBrowserFixture()
  try {
    const native = attachNativeTurn(fixture)
    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    assert.equal(native.turn.querySelectorAll('[data-turn-render-recovery-note]').length, 1)
    delete fixture.window.__codexWingmanTurnRenderRecovery
    executeRenderer(await helperFile('remove.js'), fixture)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery, undefined)
    assert.equal(native.turn.querySelectorAll('[data-turn-render-recovery-note]').length, 0)
    assert.equal(native.turn.isConnected, true)
  } finally {
    fixture.dispose()
  }
})

test("recovers completed rollout turns through Codex's native mapper", async () => {
  const fixture = createBrowserFixture()
  try {
    fixture.context.TextDecoder = TextDecoder
    fixture.context.Uint8Array = Uint8Array
    fixture.context.atob = atob
    const conversationId = '01a02c29-5610-7ff0-9d02-84208a89e59c'
    const turnId = '01a02c2b-39d2-71f3-8d39-0fb2afb1ffcc'
    const identity = fixture.document.createElement('div')
    identity.dataset.aboveComposerConversationId = conversationId
    fixture.document.body.appendChild(identity)

    const renderedTurn = fixture.document.createElement('div')
    renderedTurn.dataset.turnKey = `history-content:turn:${turnId}`
    const renderedTimestamp = fixture.document.createElement('span')
    renderedTimestamp.dataset.assistantMessageSentTime = 'true'
    renderedTimestamp.textContent = '16:14'
    renderedTurn.appendChild(renderedTimestamp)
    fixture.document.body.appendChild(renderedTurn)

    const rollout = [
      { timestamp: '2026-08-23T01:10:26Z', type: 'session_meta', payload: { id: conversationId } },
      { timestamp: '2026-08-23T01:10:26Z', type: 'event_msg', payload: { type: 'task_started', turn_id: turnId } },
      { timestamp: '2026-08-23T01:10:26Z', type: 'turn_context', payload: { turn_id: turnId } },
      { timestamp: '2026-08-23T01:10:27Z', type: 'response_item', payload: { type: 'message', role: 'user', id: 'user-1', content: [{ type: 'input_text', text: 'Visible prompt' }], internal_chat_message_metadata_passthrough: { turn_id: turnId } } },
      { timestamp: '2026-08-23T01:10:28Z', type: 'response_item', payload: { type: 'message', role: 'assistant', id: 'commentary-1', phase: 'commentary', content: [{ type: 'output_text', text: 'Working update' }], internal_chat_message_metadata_passthrough: { turn_id: turnId } } },
      { timestamp: '2026-08-23T01:10:29Z', type: 'response_item', payload: { type: 'custom_tool_call', id: 'tool-1', call_id: 'call-1', name: 'exec', status: 'completed', input: 'text(42)', internal_chat_message_metadata_passthrough: { turn_id: turnId } } },
      { timestamp: '2026-08-23T01:10:30Z', type: 'response_item', payload: { type: 'custom_tool_call_output', id: 'output-1', call_id: 'call-1', output: [{ type: 'input_text', text: '42' }], internal_chat_message_metadata_passthrough: { turn_id: turnId } } },
      { timestamp: '2026-08-23T01:10:31Z', type: 'response_item', payload: { type: 'message', role: 'assistant', id: 'final-1', phase: 'final_answer', content: [{ type: 'output_text', text: 'Complete final answer' }], internal_chat_message_metadata_passthrough: { turn_id: turnId } } },
      { timestamp: '2026-08-23T01:10:32Z', type: 'event_msg', payload: { type: 'task_complete', turn_id: turnId, last_agent_message: 'Complete final answer' } },
    ].map((row) => JSON.stringify(row)).join('\n')

    const staleTurn = {
      turnId,
      status: 'interrupted',
      items: [{ type: 'userMessage', id: 'user-1' }],
    }
    const conversation = {
      id: conversationId,
      resumeState: 'resumed',
      rolloutPath: '/sessions/affected.jsonl',
      threadRuntimeStatus: { type: 'idle' },
      latestModel: 'gpt-test',
      latestReasoningEffort: null,
      turnHistory: {
        kind: 'canonical',
        history: {
          entitiesByKey: { [`turn:${turnId}`]: staleTurn },
          generation: 1,
          isComplete: true,
          islands: [{ id: 'tail:1', entries: [{ key: `turn:${turnId}`, value: `turn:${turnId}` }] }],
        },
      },
    }
    const requests = []
    class NativeManager {
      getConversation(id) { return id === conversationId ? conversation : null }
      updateConversationState(id, updater) {
        assert.equal(id, conversationId)
        updater(conversation)
      }
      loadRemainingConversationTurns() {}
    }
    const manager = new NativeManager()
    manager.requestClient = {
      async sendRequest(method, params) {
        requests.push({ method, params })
        return { dataBase64: Buffer.from(rollout, 'utf8').toString('base64') }
      },
    }
    manager.threadStore = {
      threadsById: new Map([[conversationId, { id: conversationId, cwd: '/workspace', turns: [] }]]),
      params: {
        productPolicy: {
          threadStorePolicy: {
            mapThreadReadResponseToConversationTurns(response) {
              return response.thread.turns.map((turn) => ({
                ...turn,
                turnId: turn.id,
                turnStartedAtMs: turn.startedAt * 1000,
              }))
            },
          },
        },
      },
    }
    fixture.window.__codexRoot = {
      _internalRoot: {
        current: {
          memoizedState: { manager },
          memoizedProps: null,
          dependencies: null,
          updateQueue: null,
          stateNode: null,
          child: null,
          sibling: null,
        },
      },
    }

    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    await fixture.flush()

    assert.equal(requests.length, 1)
    assert.equal(requests[0].method, 'fs/readFile')
    assert.equal(requests[0].params.path, '/sessions/affected.jsonl')
    const recovered = conversation.turnHistory.history.entitiesByKey[`turn:${turnId}`]
    assert.equal(recovered.status, 'completed')
    assert.equal(recovered.items.length, 4)
    assert.equal(recovered.items[2].type, 'dynamicToolCall')
    assert.equal(recovered.items[2].contentItems[0].text, '42')
    assert.equal(recovered.items[3].phase, 'final_answer')
    assert.equal(recovered.items[3].text, 'Complete final answer')
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().hydrationRepaired, 1)
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    assert.equal(requests.length, 1)
    const restorationNote = renderedTurn.querySelector('[data-turn-render-recovery-note]')
    assert.equal(restorationNote?.textContent, 'Turn restored in UI by Wingman')
    assert.equal(renderedTimestamp.nextElementSibling, restorationNote)
  } finally {
    fixture.dispose()
  }
})

test('recovers completed turns and current function-call work after a transient read failure', async () => {
  const fixture = createBrowserFixture()
  try {
    fixture.context.TextDecoder = TextDecoder
    fixture.context.Uint8Array = Uint8Array
    fixture.context.atob = atob

    const conversationId = '01a03a65-b9d8-7200-aab6-84b5356aef4e'
    let now = 1_000
    fixture.context.Date = class extends Date {
      static now() { return now }
    }
    const strandedTurnId = '01a03a65-c301-75c2-bc20-101068a450d5'
    const completedTurnId = '01a03b74-c40b-76a1-8a5d-f30393817cd0'
    const identity = fixture.document.createElement('div')
    identity.dataset.aboveComposerConversationId = conversationId
    fixture.document.body.appendChild(identity)

    const renderedTurn = fixture.document.createElement('div')
    renderedTurn.dataset.turnKey = `history-content:turn:${strandedTurnId}`
    const renderedTimestamp = fixture.document.createElement('span')
    renderedTimestamp.dataset.assistantMessageSentTime = 'true'
    renderedTimestamp.textContent = '19:30'
    renderedTurn.appendChild(renderedTimestamp)
    fixture.document.body.appendChild(renderedTurn)

    const rolloutRows = [
      { timestamp: '2026-08-25T19:29:00Z', type: 'session_meta', payload: { id: conversationId } },
      { timestamp: '2026-08-25T19:29:03Z', type: 'event_msg', payload: { type: 'task_started', turn_id: strandedTurnId } },
      { timestamp: '2026-08-25T19:29:03Z', type: 'turn_context', payload: { turn_id: strandedTurnId } },
      { timestamp: '2026-08-25T19:29:04Z', type: 'response_item', payload: { type: 'message', role: 'user', id: 'user-1', content: [{ type: 'input_text', text: 'Design the workspace' }], internal_chat_message_metadata_passthrough: { turn_id: strandedTurnId } } },
      { timestamp: '2026-08-25T19:30:07Z', type: 'response_item', payload: { type: 'message', role: 'assistant', id: 'final-1', phase: 'final_answer', content: [{ type: 'output_text', text: 'First completed answer' }], internal_chat_message_metadata_passthrough: { turn_id: strandedTurnId } } },
      { timestamp: '2026-08-25T19:30:07Z', type: 'event_msg', payload: { type: 'task_complete', turn_id: strandedTurnId, last_agent_message: 'First completed answer' } },
      { timestamp: '2026-08-26T00:25:03Z', type: 'event_msg', payload: { type: 'task_started', turn_id: completedTurnId } },
      { timestamp: '2026-08-26T00:25:03Z', type: 'turn_context', payload: { turn_id: completedTurnId } },
      { timestamp: '2026-08-26T00:25:04Z', type: 'response_item', payload: { type: 'message', role: 'user', id: 'user-2', content: [{ type: 'input_text', text: 'Finish the specification' }], internal_chat_message_metadata_passthrough: { turn_id: completedTurnId } } },
      { timestamp: '2026-08-26T00:33:18Z', type: 'response_item', payload: { type: 'function_call', id: 'outcome-1', call_id: 'call-1', name: 'exec', arguments: 'update_plan()', internal_chat_message_metadata_passthrough: { turn_id: completedTurnId } } },
      { timestamp: '2026-08-26T00:33:19Z', type: 'response_item', payload: { type: 'function_call_output', id: 'outcome-output-1', call_id: 'call-1', output: [{ type: 'input_text', text: '{}' }], internal_chat_message_metadata_passthrough: { turn_id: completedTurnId } } },
      { timestamp: '2026-08-26T00:33:32Z', type: 'response_item', payload: { type: 'message', role: 'assistant', id: 'final-2', phase: 'final_answer', content: [{ type: 'output_text', text: 'Formal design complete' }], internal_chat_message_metadata_passthrough: { turn_id: completedTurnId } } },
      { timestamp: '2026-08-26T00:33:32Z', type: 'event_msg', payload: { type: 'task_complete', turn_id: completedTurnId, last_agent_message: 'Formal design complete' } },
    ]
    const fullRollout = rolloutRows.map((row) => JSON.stringify(row)).join('\n')
    let rollout = rolloutRows.slice(0, -2).map((row) => JSON.stringify(row)).join('\n')

    const staleTurn = {
      turnId: strandedTurnId,
      status: 'inProgress',
      items: [{ type: 'userMessage', id: 'user-1' }],
    }
    const conversation = {
      id: conversationId,
      resumeState: 'resumed',
      rolloutPath: '/sessions/affected-multi-turn.jsonl',
      threadRuntimeStatus: { type: 'active' },
      latestModel: 'gpt-test',
      latestReasoningEffort: null,
      turnHistory: {
        kind: 'canonical',
        history: {
          entitiesByKey: { [`turn:${strandedTurnId}`]: staleTurn },
          generation: 1,
          isComplete: true,
          islands: [{ id: 'tail:1', entries: [{ key: `turn:${strandedTurnId}`, value: `turn:${strandedTurnId}` }] }],
        },
      },
    }
    class NativeManager {
      getConversation(id) { return id === conversationId ? conversation : null }
      updateConversationState(id, updater) {
        assert.equal(id, conversationId)
        updater(conversation)
      }
      loadRemainingConversationTurns() {}
    }
    const manager = new NativeManager()
    let rolloutReads = 0
    let remainingReadFailures = 2
    manager.requestClient = {
      async sendRequest() {
        rolloutReads++
        if (remainingReadFailures > 0) {
          remainingReadFailures--
          throw new Error('Codex app-server is not available')
        }
        return { dataBase64: Buffer.from(rollout, 'utf8').toString('base64') }
      },
    }
    manager.threadStore = {
      threadsById: new Map([[conversationId, { id: conversationId, cwd: '/workspace', turns: [] }]]),
      params: {
        productPolicy: {
          threadStorePolicy: {
            mapThreadReadResponseToConversationTurns(response) {
              return response.thread.turns.map((turn) => ({
                ...turn,
                turnId: turn.id,
                turnStartedAtMs: turn.startedAt * 1000,
              }))
            },
          },
        },
      },
    }
    fixture.window.__codexRoot = {
      _internalRoot: {
        current: {
          memoizedState: { manager },
          memoizedProps: null,
          dependencies: null,
          updateQueue: null,
          stateNode: null,
          child: null,
          sibling: null,
        },
      },
    }

    executeRenderer(await helperFile('apply.js'), fixture)
    await fixture.flush()
    await fixture.flush()

    assert.equal(conversation.threadRuntimeStatus.type, 'active')
    assert.equal(rolloutReads, 1)
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${strandedTurnId}`].status, 'inProgress')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`], undefined)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().lastHydrationError, 'Codex app-server is not available')

    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()
    assert.equal(rolloutReads, 1)

    now += 9_999
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    assert.equal(rolloutReads, 1)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().probeDeferred, 2)

    now += 1
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    assert.equal(conversation.threadRuntimeStatus.type, 'active')
    assert.equal(rolloutReads, 2)
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`], undefined)

    now += 19_999
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()
    assert.equal(rolloutReads, 2)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().readFailures, 2)

    now += 1
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    assert.equal(conversation.threadRuntimeStatus.type, 'active')
    assert.equal(rolloutReads, 3)
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${strandedTurnId}`].status, 'completed')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`].status, 'inProgress')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`].items.at(-1).type, 'dynamicToolCall')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`].items.at(-1).arguments, 'update_plan()')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`].items.at(-1).contentItems[0].text, '{}')
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().lastHydrationError, null)

    now += 9_999
    rollout = fullRollout
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    assert.equal(rolloutReads, 3)

    now += 1
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    const history = conversation.turnHistory.history
    const recoveredFirst = history.entitiesByKey[`turn:${strandedTurnId}`]
    const recoveredLater = history.entitiesByKey[`turn:${completedTurnId}`]
    assert.equal(recoveredFirst.status, 'completed')
    assert.equal(recoveredLater.status, 'completed')
    assert.equal(recoveredLater.items.at(-1).phase, 'final_answer')
    assert.equal(recoveredLater.items.at(-1).text, 'Formal design complete')
    assert.equal(history.islands[0].entries.at(-1).key, `turn:${completedTurnId}`)
    assert.equal(conversation.threadRuntimeStatus.type, 'idle')
    assert.equal(rolloutReads, 4)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().staleActiveCleared, 1)

    now += 60_000
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()
    assert.equal(rolloutReads, 4)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().healthyCacheSkips, 1)

    const streamingTurnId = '01a03b80-1111-7111-8111-111111111111'
    conversation.threadRuntimeStatus = { type: 'active' }
    conversation.turnHistory = {
      kind: 'canonical',
      history: {
        entitiesByKey: { [`turn:${strandedTurnId}`]: { ...staleTurn, status: 'inProgress' } },
        generation: 3,
        isComplete: true,
        islands: [{ id: 'tail:3', entries: [{ key: `turn:${strandedTurnId}`, value: `turn:${strandedTurnId}` }] }],
      },
    }
    rollout = [
      fullRollout,
      JSON.stringify({ timestamp: '2026-08-26T00:40:00Z', type: 'event_msg', payload: { type: 'task_started', turn_id: streamingTurnId } }),
      JSON.stringify({ timestamp: '2026-08-26T00:40:00Z', type: 'turn_context', payload: { turn_id: streamingTurnId } }),
      JSON.stringify({ timestamp: '2026-08-26T00:40:01Z', type: 'response_item', payload: { type: 'message', role: 'user', id: 'user-3', content: [{ type: 'input_text', text: 'Continue working' }], internal_chat_message_metadata_passthrough: { turn_id: streamingTurnId } } }),
    ].join('\n')
    now += 10_000
    fixture.window.__codexWingmanTurnRenderRecovery.reconcile()
    await fixture.flush()
    await fixture.flush()

    assert.equal(conversation.threadRuntimeStatus.type, 'active')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${strandedTurnId}`].status, 'completed')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${completedTurnId}`].status, 'completed')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${streamingTurnId}`].status, 'inProgress')
    assert.equal(conversation.turnHistory.history.entitiesByKey[`turn:${streamingTurnId}`].items[0].type, 'userMessage')
    assert.ok(rolloutReads > 4)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().hydrationRepaired, 3)
    assert.equal(fixture.window.__codexWingmanTurnRenderRecovery.status().staleActiveCleared, 1)
  } finally {
    fixture.dispose()
  }
})
