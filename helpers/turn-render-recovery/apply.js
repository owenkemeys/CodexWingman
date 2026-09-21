(() => {
  const globalName = '__codexWingmanTurnRenderRecovery'
  const version = 'turn-render-recovery-v14'
  const existing = window[globalName]
  if (existing?.version === version) return existing.reconcile()
  existing?.cleanup?.()

  let frame = 0
  let observer = null
  let managers = []
  const repaired = new Set()
  const hydrationInFlight = new Map()
  const nextProbeAtByConversation = new Map()
  const probeFailuresByConversation = new Map()
  const recoveredByConversation = new Map()
  const restoredTurnIds = new Set()
  const restoredTurnKeys = new Set()
  const noteSelector = '[data-turn-render-recovery-note]'
  const diagnostics = {
    inspected: 0,
    repaired: 0,
    hydrationInspected: 0,
    hydrationRepaired: 0,
    staleActiveCleared: 0,
    probeDeferred: 0,
    healthyCacheSkips: 0,
    readFailures: 0,
    hydrationUnavailable: 0,
    incompatible: 0,
    lastTurnId: null,
    lastConversationId: null,
    lastHydrationError: null,
  }

  const words = (value) => String(value || '')
    .toLocaleLowerCase()
    .match(/[\p{L}\p{N}]+/gu) || []

  const endingIsRendered = (source, rendered) => {
    const sourceWords = words(source)
    if (sourceWords.length === 0) return true
    const renderedWords = words(rendered)
    const suffix = sourceWords.slice(-Math.min(10, sourceWords.length))
    if (renderedWords.length < suffix.length) return false
    const needle = suffix.join(' ')
    return renderedWords.join(' ').includes(needle)
  }

  const isVisuallyClipped = (turnNode, finalNode) => {
    const clientHeight = Number(turnNode.clientHeight) || 0
    const scrollHeight = Number(turnNode.scrollHeight) || 0
    if (clientHeight > 0 && scrollHeight - clientHeight > 8) return true
    const turnRect = turnNode.getBoundingClientRect?.()
    const finalRect = finalNode?.getBoundingClientRect?.()
    return Boolean(turnRect && finalRect && finalRect.bottom - turnRect.bottom > 8)
  }

  const normalizeConversationId = (value) => {
    const normalized = String(value || '').replace(/^local:/i, '')
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(normalized)
      ? normalized.toLowerCase()
      : null
  }

  const turnIdFromKey = (value) => {
    const match = String(value || '').match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
    return normalizeConversationId(match?.[0])
  }

  const visibilityClasses = (node) => (node?.getAttribute('class') || '').split(/\s+/)
    .filter((token) => /(?:^|:)opacity-/.test(token))

  const mountRestorationNote = (turnNode) => {
    const turnKey = turnNode?.getAttribute?.('data-turn-key') || ''
    const turnId = turnIdFromKey(turnKey)
    if (!restoredTurnKeys.has(turnKey) && !restoredTurnIds.has(turnId)) return false
    const timestamp = turnNode.querySelector('[data-assistant-message-sent-time=true]')
    if (!timestamp?.parentElement) return false
    let note = turnNode.querySelector(noteSelector)
    if (!note) {
      note = document.createElement('span')
      note.dataset.codexWingmanOwner = 'turn-render-recovery'
      note.dataset.turnRenderRecoveryNote = 'true'
      note.setAttribute('role', 'note')
      note.textContent = 'Turn restored in UI by Wingman'
      note.style.marginLeft = '6px'
      note.style.display = 'inline-flex'
      note.style.alignItems = 'center'
      note.style.fontSize = '12px'
      note.style.lineHeight = '1'
      note.style.fontWeight = '400'
      note.style.color = 'var(--color-token-text-tertiary)'
      note.style.whiteSpace = 'nowrap'
    }
    note.setAttribute('class', visibilityClasses(timestamp).join(' '))
    if (timestamp.nextElementSibling !== note) timestamp.insertAdjacentElement('afterend', note)
    return true
  }

  const rememberRestoredTurn = (turnNode, turnId) => {
    const normalizedId = normalizeConversationId(turnId) || turnIdFromKey(turnNode?.getAttribute?.('data-turn-key'))
    const turnKey = turnNode?.getAttribute?.('data-turn-key')
    if (normalizedId) restoredTurnIds.add(normalizedId)
    if (turnKey) restoredTurnKeys.add(turnKey)
    mountRestorationNote(turnNode)
  }

  const mountRestorationNotes = () => {
    for (const turnNode of document.querySelectorAll('[data-turn-key]')) mountRestorationNote(turnNode)
  }

  const visibleConversationId = () => {
    const values = []
    for (const [selector, attribute] of [
      ['[data-above-composer-conversation-id]', 'data-above-composer-conversation-id'],
      ['[data-request-user-input-auto-resolution-conversation-id]', 'data-request-user-input-auto-resolution-conversation-id'],
    ]) {
      for (const node of document.querySelectorAll(selector)) {
        const value = normalizeConversationId(node.getAttribute(attribute))
        if (value) values.push(value)
      }
    }
    const unique = [...new Set(values)]
    return unique.length === 1 ? unique[0] : null
  }

  const reactFiber = (node) => {
    const key = Object.getOwnPropertyNames(node)
      .find((name) => name.startsWith('__reactFiber$'))
    return key ? node[key] : null
  }

  const candidateChains = (fiber) => {
    const chains = []
    if (fiber) chains.push(fiber)
    if (fiber?.alternate) chains.push(fiber.alternate)
    return chains
  }

  const findNativeState = (turnNode, expectedConversationId) => {
    const turnKey = turnNode.getAttribute('data-turn-key')
    if (!turnKey) return null
    for (const start of candidateChains(reactFiber(turnNode))) {
      let rowFiber = null
      let listFiber = null
      let conversationId = null
      for (let fiber = start, depth = 0; fiber && depth < 28; fiber = fiber.return, depth++) {
        const props = fiber.memoizedProps
        const candidateId = normalizeConversationId(props?.conversationId)
        if (candidateId) conversationId = candidateId
        if (!rowFiber && props?.entry?.turnKey === turnKey) rowFiber = fiber
        if (rowFiber
          && props?.latestTurnFooterKey === turnKey
          && Array.isArray(props.entries)
          && typeof props.onLatestTurnHeightChange === 'function'
          && typeof props.onApiChange === 'function'
          && typeof props.RowComponent === 'function') {
          listFiber = fiber
          break
        }
      }
      if (expectedConversationId && conversationId && conversationId !== expectedConversationId) continue
      const entry = rowFiber?.memoizedProps?.entry
      if (!entry?.isMostRecentTurn || entry.turnKey !== turnKey || !listFiber) continue
      for (let hook = listFiber.memoizedState, index = 0; hook && index < 8; hook = hook.next, index++) {
        const heights = hook.memoizedState
        if (!heights || typeof heights !== 'object' || Array.isArray(heights)) continue
        if (!Object.prototype.hasOwnProperty.call(heights, turnKey)) continue
        if (typeof hook.queue?.dispatch !== 'function') continue
        return { entry, heightHook: hook, turnKey }
      }
    }
    return null
  }

  const finalAgentMessage = (turn) => {
    const items = Array.isArray(turn?.items) ? turn.items : []
    for (let index = items.length - 1; index >= 0; index--) {
      const item = items[index]
      if (item?.type === 'agentMessage' && typeof item.text === 'string') return item
    }
    return null
  }

  const inspectTurn = (turnNode, conversationId) => {
    const native = findNativeState(turnNode, conversationId)
    if (!native) {
      diagnostics.incompatible++
      return false
    }
    const turn = native.entry.turn
    if (turn?.status !== 'completed') return false
    const message = finalAgentMessage(turn)
    if (!message || !message.id || !message.text) return false
    diagnostics.inspected++
    diagnostics.lastTurnId = native.entry.turnId || turn.turnId || null
    const finalNode = turnNode.querySelector('[data-local-conversation-final-assistant="true"]')
    const rendered = finalNode?.innerText || finalNode?.textContent || ''
    if (finalNode && endingIsRendered(message.text, rendered) && !isVisuallyClipped(turnNode, finalNode)) return false

    const signature = `${native.turnKey}:${message.id}:${message.text.length}`
    if (repaired.has(signature)) return false
    repaired.add(signature)
    native.heightHook.queue.dispatch((current) => {
      if (!current || typeof current !== 'object' || Array.isArray(current)) return current
      if (!Object.prototype.hasOwnProperty.call(current, native.turnKey)) return current
      const next = { ...current }
      delete next[native.turnKey]
      return next
    })
    rememberRestoredTurn(turnNode, native.entry.turnId || turn.turnId)
    diagnostics.repaired++
    return true
  }

  const managerLike = (candidate) => {
    if (!candidate || (typeof candidate !== 'object' && typeof candidate !== 'function')) return false
    let prototype = candidate
    for (let depth = 0; prototype && depth < 3; depth++, prototype = Object.getPrototypeOf(prototype)) {
      const names = Reflect.ownKeys(prototype)
      if (names.includes('getConversation')
        && names.includes('updateConversationState')
        && names.includes('loadRemainingConversationTurns')) return true
    }
    return false
  }

  const discoverManagerForConversation = (conversationId) => {
    const seen = new WeakSet()
    let visited = 0
    let found = null
    const walk = (candidate, depth) => {
      if (found || !candidate || (typeof candidate !== 'object' && typeof candidate !== 'function')) return
      if (seen.has(candidate) || depth > 6 || visited > 100000) return
      seen.add(candidate)
      visited++
      if (managerLike(candidate)) {
        managers = [...new Set([...managers, candidate])]
        try {
          if (candidate.getConversation(conversationId)) found = candidate
        } catch {}
        return
      }
      if (candidate === window || candidate === document || candidate?.ownerDocument === document) return
      if (candidate instanceof Map) {
        let count = 0
        for (const [key, value] of candidate) {
          if (found || count++ > 400) break
          walk(key, depth + 1)
          walk(value, depth + 1)
        }
        return
      }
      if (candidate instanceof Set) {
        let count = 0
        for (const value of candidate) {
          if (found || count++ > 400) break
          walk(value, depth + 1)
        }
        return
      }
      let keys = []
      try { keys = Reflect.ownKeys(candidate) } catch { return }
      for (const key of keys) {
        if (found) break
        if (['return', 'child', 'sibling', 'alternate', 'stateNode', 'ownerDocument', 'parentNode'].includes(String(key))) continue
        let value
        try { value = candidate[key] } catch { continue }
        walk(value, depth + 1)
      }
    }
    const root = window.__codexRoot?._internalRoot?.current
    const stack = root ? [root] : []
    let fibers = 0
    while (stack.length && !found && fibers++ < 50000) {
      const fiber = stack.pop()
      walk(fiber.updateQueue, 0)
      if (fiber.child) stack.push(fiber.child)
      if (fiber.sibling) stack.push(fiber.sibling)
    }
    return found
  }

  const discoverManagers = () => {
    const found = []
    const seen = new WeakSet()
    let visited = 0
    const walk = (candidate, depth) => {
      if (!candidate || (typeof candidate !== 'object' && typeof candidate !== 'function')) return
      if (seen.has(candidate) || depth > 10 || visited > 100000) return
      seen.add(candidate)
      visited++
      if (managerLike(candidate)) {
        found.push(candidate)
        return
      }
      if (candidate === window || candidate === document || candidate?.ownerDocument === document) return
      if (candidate instanceof Map) {
        let count = 0
        for (const [key, value] of candidate) {
          if (count++ > 400) break
          walk(key, depth + 1)
          walk(value, depth + 1)
        }
        return
      }
      if (candidate instanceof Set) {
        let count = 0
        for (const value of candidate) {
          if (count++ > 400) break
          walk(value, depth + 1)
        }
        return
      }
      let keys = []
      try { keys = Reflect.ownKeys(candidate) } catch { return }
      for (const key of keys) {
        if (['return', 'child', 'sibling', 'alternate', 'stateNode', 'ownerDocument', 'parentNode'].includes(String(key))) continue
        let value
        try { value = candidate[key] } catch { continue }
        walk(value, depth + 1)
      }
    }
    const root = window.__codexRoot?._internalRoot?.current
    const stack = root ? [root] : []
    let fibers = 0
    while (stack.length && fibers++ < 50000) {
      const fiber = stack.pop()
      walk(fiber.memoizedState, 0)
      walk(fiber.memoizedProps, 0)
      walk(fiber.dependencies, 0)
      walk(fiber.updateQueue, 0)
      if (fiber.stateNode && !fiber.stateNode?.ownerDocument) walk(fiber.stateNode, 0)
      if (fiber.child) stack.push(fiber.child)
      if (fiber.sibling) stack.push(fiber.sibling)
    }
    managers = [...new Set(found)]
    return managers
  }

  const managerForConversation = (conversationId) => {
    for (const manager of managers) {
      try {
        if (manager.getConversation(conversationId)) return manager
      } catch {}
    }
    const targeted = discoverManagerForConversation(conversationId)
    if (targeted) return targeted
    for (const manager of discoverManagers()) {
      try {
        if (manager.getConversation(conversationId)) return manager
      } catch {}
    }
    return null
  }

  const textFromParts = (parts) => (Array.isArray(parts) ? parts : [])
    .map((part) => typeof part?.text === 'string' ? part.text : '')
    .join('')

  const parseTimestampSeconds = (value) => {
    const milliseconds = Date.parse(String(value || ''))
    return Number.isFinite(milliseconds) ? Math.floor(milliseconds / 1000) : null
  }

  const parseRollout = (text, expectedConversationId) => {
    const turns = new Map()
    const order = []
    const toolsByCallId = new Map()
    const contextReady = new Set()
    let declaredConversationId = null
    let currentTurnId = null
    let latestStartedTurnId = null
    const ensureTurn = (turnId, timestamp) => {
      if (!turnId) return null
      let turn = turns.get(turnId)
      if (!turn) {
        turn = {
          id: turnId,
          items: [],
          itemsView: 'full',
          status: 'interrupted',
          error: null,
          startedAt: parseTimestampSeconds(timestamp),
          completedAt: null,
          durationMs: null,
        }
        turns.set(turnId, turn)
        order.push(turnId)
      }
      return turn
    }

    for (const line of String(text || '').split(/\r?\n/)) {
      if (!line.trim()) continue
      let row
      try { row = JSON.parse(line) } catch { continue }
      const payload = row?.payload
      if (!payload || typeof payload !== 'object') continue
      if (row.type === 'session_meta') {
        declaredConversationId = normalizeConversationId(payload.id)
        continue
      }
      if (row.type === 'turn_context') {
        const turnId = normalizeConversationId(payload.turn_id)
        if (turnId) {
          currentTurnId = turnId
          contextReady.add(turnId)
          ensureTurn(turnId, row.timestamp)
        }
        continue
      }
      if (row.type === 'event_msg' && payload.type === 'task_started') {
        const turnId = normalizeConversationId(payload.turn_id)
        if (turnId) {
          currentTurnId = turnId
          latestStartedTurnId = turnId
          ensureTurn(turnId, row.timestamp).status = 'inProgress'
        }
        continue
      }
      if (row.type === 'event_msg' && payload.type === 'task_complete') {
        const turnId = normalizeConversationId(payload.turn_id) || currentTurnId
        const turn = ensureTurn(turnId, row.timestamp)
        if (turn) {
          turn.status = 'completed'
          turn.completedAt = parseTimestampSeconds(row.timestamp)
          if (turn.startedAt != null && turn.completedAt != null) turn.durationMs = Math.max(0, (turn.completedAt - turn.startedAt) * 1000)
        }
        continue
      }
      if (row.type !== 'response_item') continue
      const metadataTurnId = normalizeConversationId(payload.internal_chat_message_metadata_passthrough?.turn_id)
      const turnId = metadataTurnId || currentTurnId
      const turn = turns.get(turnId)
      if (!turn || !contextReady.has(turnId)) continue

      if (payload.type === 'message') {
        if (payload.role === 'user') {
          if (!metadataTurnId) continue
          const messageText = textFromParts(payload.content)
          if (!messageText) continue
          turn.items.push({
            type: 'userMessage',
            id: payload.id || `wingman-user-${turnId}`,
            clientId: null,
            content: [{ type: 'text', text: messageText, text_elements: [] }],
          })
        } else if (payload.role === 'assistant') {
          const messageText = textFromParts(payload.content)
          if (!messageText) continue
          turn.items.push({
            type: 'agentMessage',
            id: payload.id || `wingman-agent-${turnId}-${turn.items.length}`,
            text: messageText,
            phase: payload.phase || 'commentary',
            memoryCitation: null,
          })
        }
        continue
      }

      if (payload.type === 'reasoning') {
        const summary = (Array.isArray(payload.summary) ? payload.summary : [])
          .map((part) => typeof part === 'string' ? part : part?.text)
          .filter((part) => typeof part === 'string' && part.length > 0)
        turn.items.push({
          type: 'reasoning',
          id: payload.id || `wingman-reasoning-${turnId}-${turn.items.length}`,
          summary,
          content: [],
        })
        continue
      }

      if (['custom_tool_call', 'function_call', 'mcp_tool_call'].includes(payload.type)) {
        const savedArguments = payload.type === 'custom_tool_call' ? payload.input : payload.arguments
        const item = {
          type: 'dynamicToolCall',
          id: payload.id || `wingman-tool-${turnId}-${turn.items.length}`,
          namespace: null,
          tool: typeof payload.name === 'string' ? payload.name : 'tool',
          arguments: typeof savedArguments === 'string' ? savedArguments : JSON.stringify(savedArguments ?? {}),
          status: payload.status === 'failed' ? 'failed' : 'completed',
          contentItems: [],
          success: payload.status !== 'failed',
          durationMs: null,
        }
        turn.items.push(item)
        if (typeof payload.call_id === 'string') toolsByCallId.set(payload.call_id, item)
        continue
      }

      if (['custom_tool_call_output', 'function_call_output', 'mcp_tool_call_output'].includes(payload.type)
        && typeof payload.call_id === 'string') {
        const item = toolsByCallId.get(payload.call_id)
        if (!item) continue
        item.contentItems = (Array.isArray(payload.output) ? payload.output : [])
          .map((part) => ({
            type: typeof part?.type === 'string' ? part.type : 'input_text',
            text: typeof part?.text === 'string' ? part.text : '',
          }))
          .filter((part) => part.text.length > 0)
      }
    }

    if (declaredConversationId !== expectedConversationId) return []
    return order.map((turnId) => turns.get(turnId)).filter((turn) => {
      const final = finalAgentMessage(turn)
      if (turn.status === 'completed') return final?.phase === 'final_answer' && final.text.length > 0
      return turn.id === latestStartedTurnId && turn.status === 'inProgress' && turn.items.length > 0
    })
  }

  const decodeBase64Utf8 = (value) => {
    const binary = atob(String(value || ''))
    const bytes = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index)
    return new TextDecoder().decode(bytes)
  }

  const completedRolloutTailId = (text, recoveredTurns) => {
    let latestStartedTurnId = null
    const completedTurnIds = new Set()
    for (const line of String(text || '').split(/\r?\n/)) {
      if (!line.trim()) continue
      let row
      try { row = JSON.parse(line) } catch { continue }
      const payload = row?.payload
      if (row.type !== 'event_msg' || !payload || typeof payload !== 'object') continue
      if (payload.type === 'task_started') {
        latestStartedTurnId = normalizeConversationId(payload.turn_id)
      } else if (payload.type === 'task_complete') {
        const turnId = normalizeConversationId(payload.turn_id)
        if (turnId) completedTurnIds.add(turnId)
      }
    }
    if (!latestStartedTurnId || !completedTurnIds.has(latestStartedTurnId)) return null
    return recoveredTurns.some((turn) => normalizeConversationId(turn?.id) === latestStartedTurnId)
      ? latestStartedTurnId
      : null
  }

  const conversationTurns = (conversation) => {
    if (conversation?.turnHistory?.kind === 'canonical') {
      const history = conversation.turnHistory.history
      const result = []
      for (const island of history?.islands || []) {
        for (const entry of island?.entries || []) {
          const turn = history.entitiesByKey?.[entry.value]
          if (turn) result.push(turn)
        }
      }
      return result
    }
    return Array.isArray(conversation?.turns) ? conversation.turns : []
  }

  const needsHydrationRecovery = (conversation, recoveredTurns) => {
    if (!Array.isArray(recoveredTurns) || recoveredTurns.length === 0) return false
    const nativeTurns = conversationTurns(conversation)
    const nativeById = new Map(nativeTurns.map((turn) => [turn.turnId, turn]))
    for (const recovered of recoveredTurns) {
      const native = nativeById.get(recovered.id)
      const recoveredFinal = finalAgentMessage(recovered)
      const nativeFinal = finalAgentMessage(native)
      if (!native || native.status !== recovered.status) return true
      if (recovered.status === 'completed' && (!nativeFinal || nativeFinal.text !== recoveredFinal?.text)) return true
      if ((native.items?.length || 0) < recovered.items.length) return true
    }
    return false
  }

  const installRecoveredTurns = (manager, conversationId, recoveredTurns, clearStaleActive = false) => {
    const conversation = manager.getConversation(conversationId)
    const thread = manager.threadStore?.threadsById?.get(conversationId)
    const legacyMapper = manager.threadStore?.params?.productPolicy?.threadStorePolicy?.mapThreadReadResponseToConversationTurns
    const currentMapper = manager.history?.mapThreadTurns
    if (!conversation || !thread
      || (typeof legacyMapper !== 'function' && typeof currentMapper !== 'function')) return false
    const nativeById = new Map(conversationTurns(conversation).map((turn) => [turn.turnId, turn]))
    const restoredIds = recoveredTurns.filter((recovered) => {
      const native = nativeById.get(recovered.id)
      const recoveredFinal = finalAgentMessage(recovered)
      const nativeFinal = finalAgentMessage(native)
      return !native
        || native.status !== recovered.status
        || (recovered.status === 'completed' && nativeFinal?.text !== recoveredFinal?.text)
        || (native.items?.length || 0) < recovered.items.length
    }).filter((turn) => turn.status === 'completed')
      .map((turn) => normalizeConversationId(turn.id)).filter(Boolean)
    const mapped = typeof legacyMapper === 'function'
      ? legacyMapper({
        thread: { ...thread, turns: recoveredTurns },
      }, {
        fallbackCwd: thread.cwd || conversation.cwd || '/',
        model: conversation.latestModel || '',
        reasoningEffort: conversation.latestReasoningEffort || null,
      })
      : currentMapper({
        threadId: conversationId,
        turns: recoveredTurns,
        model: conversation.latestModel || '',
        reasoningEffort: conversation.latestReasoningEffort || null,
        cwd: thread.cwd || conversation.cwd || '/',
        permissions: conversation.currentPermissions,
      })
    if (!Array.isArray(mapped) || mapped.length !== recoveredTurns.length) return false

    manager.updateConversationState(conversationId, (draft) => {
      const previous = draft.turnHistory?.kind === 'canonical' ? draft.turnHistory.history : null
      const generation = (Number(previous?.generation) || 0) + 1
      const entitiesByKey = {}
      const entries = []
      for (const turn of mapped) {
        if (!turn?.turnId) continue
        const key = `turn:${turn.turnId}`
        entitiesByKey[key] = turn
        entries.push({ key, value: key })
      }
      const islandId = `wingman-recovery:${generation}`
      draft.turnHistory = {
        kind: 'canonical',
        history: {
          entitiesByKey,
          generation,
          isComplete: true,
          islands: [{
            id: islandId,
            entries,
            olderBoundary: { status: 'exhausted', boundaryId: `${islandId}:older` },
            newerBoundary: { status: 'exhausted', boundaryId: `${islandId}:newer` },
          }],
        },
      }
      if (clearStaleActive && draft.threadRuntimeStatus?.type === 'active') {
        draft.threadRuntimeStatus = { type: 'idle' }
      }
    })
    if (clearStaleActive) diagnostics.staleActiveCleared++
    restoredIds.forEach((turnId) => restoredTurnIds.add(turnId))
    return true
  }

  const recoverHydration = async (conversationId) => {
    if (!conversationId || hydrationInFlight.has(conversationId)) return false
    const manager = managerForConversation(conversationId)
    const conversation = manager?.getConversation?.(conversationId)
    if (!manager || !conversation || conversation.resumeState !== 'resumed' || typeof conversation.rolloutPath !== 'string') {
      diagnostics.hydrationUnavailable++
      return false
    }
    const nativeLooksActive = conversation.threadRuntimeStatus?.type === 'active'
      || conversationTurns(conversation).some((turn) => turn?.status === 'inProgress')
    const cached = recoveredByConversation.get(conversationId)
    let installedCached = false
    if (cached?.path === conversation.rolloutPath && !nativeLooksActive) {
      if (needsHydrationRecovery(conversation, cached.turns)) {
        installedCached = installRecoveredTurns(manager, conversationId, cached.turns)
        if (installedCached) diagnostics.hydrationRepaired++
      } else {
        diagnostics.healthyCacheSkips++
        return false
      }
    }

    const now = Date.now()
    const nextProbeAt = nextProbeAtByConversation.get(conversationId)
    if (Number.isFinite(nextProbeAt) && now < nextProbeAt) {
      diagnostics.probeDeferred++
      return installedCached
    }
    nextProbeAtByConversation.set(conversationId, now + (nativeLooksActive ? 10_000 : 5_000))

    const work = (async () => {
      diagnostics.hydrationInspected++
      const response = await manager.requestClient.sendRequest('fs/readFile', {
        path: conversation.rolloutPath,
      }, {
        priority: 'background',
        source: 'turn_render_recovery',
      })
      diagnostics.lastHydrationError = null
      if (visibleConversationId() !== conversationId) return false
      const text = decodeBase64Utf8(response?.dataBase64)
      const recoveredTurns = parseRollout(text, conversationId)
      probeFailuresByConversation.delete(conversationId)
      const latest = manager.getConversation(conversationId)
      const latestLooksActive = latest?.threadRuntimeStatus?.type === 'active'
        || conversationTurns(latest).some((turn) => turn?.status === 'inProgress')
      const completedTailId = completedRolloutTailId(text, recoveredTurns)
      recoveredByConversation.set(conversationId, { path: conversation.rolloutPath, turns: recoveredTurns, completedTailId })
      if (!needsHydrationRecovery(latest, recoveredTurns)) return false
      const installed = installRecoveredTurns(manager, conversationId, recoveredTurns, Boolean(latestLooksActive && completedTailId))
      if (installed) diagnostics.hydrationRepaired++
      return installed
    })().catch((error) => {
      const failures = (probeFailuresByConversation.get(conversationId) || 0) + 1
      probeFailuresByConversation.set(conversationId, failures)
      nextProbeAtByConversation.set(
        conversationId,
        Date.now() + Math.min(60_000, 10_000 * (2 ** (failures - 1))),
      )
      diagnostics.readFailures++
      diagnostics.lastHydrationError = String(error?.message || error)
      return false
    }).finally(() => hydrationInFlight.delete(conversationId))
    hydrationInFlight.set(conversationId, work)
    return work
  }

  const reconcile = () => {
    frame = 0
    const conversationId = visibleConversationId()
    diagnostics.lastConversationId = conversationId
    if (conversationId) void recoverHydration(conversationId)
    const turns = [...document.querySelectorAll('[data-turn-key]')]
    if (turns.length) inspectTurn(turns[turns.length - 1], conversationId)
    mountRestorationNotes()
    return { ...diagnostics, active: true }
  }

  const schedule = () => {
    if (frame) return
    frame = requestAnimationFrame(reconcile)
  }

  const cleanup = () => {
    observer?.disconnect()
    observer = null
    if (frame) cancelAnimationFrame(frame)
    frame = 0
    managers = []
    hydrationInFlight.clear()
    nextProbeAtByConversation.clear()
    probeFailuresByConversation.clear()
    recoveredByConversation.clear()
    restoredTurnIds.clear()
    restoredTurnKeys.clear()
    document.querySelectorAll(noteSelector).forEach((node) => node.remove())
    delete window[globalName]
    return true
  }

  window[globalName] = {
    version,
    reconcile,
    cleanup,
    status: () => ({ ...diagnostics, active: true }),
  }
  observer = new MutationObserver(schedule)
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    characterData: true,
    attributes: true,
    attributeFilter: [
      'data-turn-key',
      'data-local-conversation-final-assistant',
      'data-above-composer-conversation-id',
      'data-request-user-input-auto-resolution-conversation-id',
    ],
  })
  schedule()
  return { ...diagnostics, active: true }
})()
