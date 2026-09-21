(() => {
  const version = 'hook-trace-v16'
  const owner = 'hook-trace'
  const globalName = '__codexHelperHookTrace'
  const existing = window[globalName]
  if (existing?.version === version) return existing.update(state || {})
  existing?.cleanup?.()

  let snapshot = state || {}
  let summary = null
  let summaryTrigger = null
  let closeTimer = 0
  let frame = 0
  const liveRecords = new Map()
  const liveScores = new Map()
  const liveSignatures = new Map()
  const liveFibers = new Map()
  const placementStates = new Map()
  const dirtyTurns = new Set()

  const style = document.createElement('style')
  style.dataset.codexWingmanOwner = owner
  style.dataset.hookTraceStyle = 'true'
  style.textContent = `
    [data-hook-trace-slot] { display:inline-flex; align-items:center; flex:0 0 auto; }
    [data-hook-trace-native-suppressed="true"] { display:none !important; }
    [data-hook-trace-trigger] { display:inline-flex; align-items:center; justify-content:center; box-sizing:border-box; width:26px; height:26px; padding:4px; border:1px solid transparent; border-radius:9999px; background:transparent; color:var(--color-token-text-tertiary, GrayText); cursor:pointer; }
    [data-hook-trace-trigger]:hover, [data-hook-trace-trigger][data-state="open"], [data-hook-trace-trigger][aria-pressed="true"] { color:var(--color-token-foreground, CanvasText); background:var(--color-token-list-hover-background, rgba(127,127,127,.14)); }
    [data-hook-trace-trigger]:focus-visible { outline:2px solid var(--color-token-focus-border, currentColor); outline-offset:1px; }
    [data-hook-trace-trigger] svg { width:16px; height:16px; display:block; }
    [data-hook-trace-visibility-active] { opacity:1 !important; }
    [data-hook-trace-actions-active] > *:not([data-hook-trace-slot]) { opacity:0; pointer-events:none; }
    [data-hook-trace-visibility-active] > *:not([data-hook-trace-actions-active]):not(:has([data-hook-trace-actions-active])) { opacity:0; pointer-events:none; }
    [data-hook-trace-actions-active]:hover > *, .group:hover [data-hook-trace-actions-active] > *, .group:focus-within [data-hook-trace-actions-active] > *, .group:hover [data-hook-trace-visibility-active] > *, .group:focus-within [data-hook-trace-visibility-active] > * { opacity:1; pointer-events:auto; }
    [data-hook-trace-summary] { position:fixed; z-index:2147483646; box-sizing:border-box; width:max-content; min-width:180px; max-width:min(360px, calc(100vw - 16px)); padding:10px 12px; border:1px solid var(--color-token-border, rgba(127,127,127,.32)); border-radius:8px; background:var(--color-token-dropdown-background, #303030); color:var(--color-token-foreground, #fff); box-shadow:0 8px 24px rgba(0,0,0,.24); font:inherit; font-size:13px; line-height:18px; overflow:auto; }
    [data-hook-trace-summary-title] { margin-bottom:8px; font-weight:600; }
    [data-hook-trace-summary-row] { display:grid; grid-template-columns:minmax(0, 1fr) max-content; align-items:start; column-gap:16px; }
    [data-hook-trace-summary-row] + [data-hook-trace-summary-row] { margin-top:4px; }
    [data-hook-trace-summary-event] { min-width:0; }
    [data-hook-trace-summary-meta] { color:var(--color-token-text-secondary, #c7c7c7); white-space:nowrap; }
    [data-hook-trace-summary-status] { grid-column:2; color:var(--color-token-text-warning, #ff8f3d); white-space:nowrap; }
    [data-hook-trace-inline] { color:var(--color-token-text-secondary, GrayText); align-self:stretch; }
    [data-hook-trace-row-trigger] { padding:0; margin:0; background:transparent; color:inherit; cursor:pointer; text-align:left; }
    [data-hook-trace-icon] { width:18px; height:18px; flex:0 0 auto; }
    [data-hook-trace-row-body] { margin:7px 0 12px 19px; color:var(--color-token-text-secondary, GrayText); }
    [data-hook-trace-row-body] pre { margin:0; white-space:pre-wrap; overflow-wrap:anywhere; font:inherit; color:var(--color-token-foreground, CanvasText); }
    [data-hook-trace-source] { margin-top:7px; font-size:12px; color:var(--color-token-text-tertiary, GrayText); }
    [data-hook-trace-status], [data-hook-trace-output] { margin-top:7px; font-size:12px; color:var(--color-token-text-tertiary, GrayText); white-space:pre-wrap; overflow-wrap:anywhere; }
    @media (prefers-reduced-motion: reduce) { [data-hook-trace-chevron] { transition:none; } }
  `
  document.documentElement.appendChild(style)

  const records = () => snapshot?.records && typeof snapshot.records === 'object' ? snapshot.records : {}
  const insertBefore = (parent, node, reference) => {
    if (parent.insertBefore) return parent.insertBefore(node, reference)
    if (node.parentNode) node.remove()
    const index = parent.children.indexOf(reference)
    node.parentNode = parent
    parent.children.splice(index < 0 ? parent.children.length : index, 0, node)
    node.ownerDocument?._notify?.(parent, 'childList')
    return node
  }
  const hookIcon = () => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    svg.setAttribute('width', '18')
    svg.setAttribute('height', '18')
    svg.setAttribute('viewBox', '0 0 18 18')
    svg.setAttribute('fill', 'none')
    svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
    svg.setAttribute('aria-hidden', 'true')
    svg.dataset.hookTraceIcon = 'true'
    const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle')
    circle.setAttribute('cx', '8.99805')
    circle.setAttribute('cy', '4.875')
    circle.setAttribute('r', '1.875')
    circle.setAttribute('stroke', 'currentColor')
    circle.setAttribute('stroke-width', '1.5')
    svg.append(circle)
    for (const d of [
      'M9 6.75V12C9 13.6569 10.3431 15 12 15C13.6569 15 15 13.6569 15 12V9.75L13.5 11.25',
      'M9 6.75V12C9 13.6569 7.65685 15 6 15C4.34315 15 3 13.6569 3 12V9.75L4.5 11.25',
    ]) {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
      path.setAttribute('d', d)
      path.setAttribute('stroke', 'currentColor')
      path.setAttribute('stroke-width', '1.5')
      path.setAttribute('stroke-linecap', 'round')
      path.setAttribute('stroke-linejoin', 'round')
      svg.append(path)
    }
    return svg
  }
  const chevron = () => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    svg.setAttribute('width', '20')
    svg.setAttribute('height', '20')
    svg.setAttribute('viewBox', '0 0 20 20')
    svg.dataset.hookTraceChevron = 'true'
    svg.setAttribute('class', 'icon-2xs text-token-conversation-summary-trailing transition-transform duration-basic rotate-0')
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    path.setAttribute('d', 'M7.52925 3.7793C7.75652 3.55203 8.10803 3.52383 8.36616 3.69434L8.47065 3.7793L14.2207 9.5293C14.4804 9.789 14.4804 10.211 14.2207 10.4707L8.47065 16.2207C8.21095 16.4804 7.78895 16.4804 7.52925 16.2207C7.26955 15.961 7.26955 15.539 7.52925 15.2793L12.8085 10L7.52925 4.7207L7.44429 4.61621C7.27378 4.35808 7.30198 4.00657 7.52925 3.7793Z')
    path.setAttribute('fill', 'currentColor')
    svg.append(path)
    return svg
  }
  const sourceLabel = (source, path) => {
    const labels = {
      user: 'User hook',
      project: 'Project hook',
      plugin: 'Plugin hook',
      system: 'System hook',
      mdm: 'Organization-managed hook',
      cloudRequirements: 'Organization-managed hook',
      cloudManagedConfig: 'Organization-managed hook',
      legacyManagedConfigFile: 'Managed hook',
      legacyManagedConfigMdm: 'Managed hook',
      sessionFlags: 'Session hook',
      unknown: 'Hook source not identified by Codex',
    }
    const label = labels[source] || 'Hook source not identified by Codex'
    return typeof path === 'string' && path.length > 0 ? `${label} — ${path}` : label
  }
  const placementForEvent = (eventName) => ({
    userPromptSubmit: { side: 'user', label: 'Prompt-submission context' },
    preToolUse: { side: 'response', label: 'Before tool use' },
    postToolUse: { side: 'response', label: 'After tool use' },
    stop: { side: 'response', label: 'Stop context' },
    preCompact: { side: 'response', label: 'Before compaction' },
    postCompact: { side: 'response', label: 'After compaction' },
    sessionStart: { side: 'user', label: 'Session-start context' },
    subagentStart: { side: 'response', label: 'Before subagent work' },
    subagentStop: { side: 'response', label: 'After subagent work' },
    permissionRequest: { side: 'response', label: 'Permission request' },
  })[eventName] || null
  const normalizeLiveRuns = (items) => {
    const output = []
    for (const [sequence, item] of items.entries()) {
      const run = item?.run || item
      const placement = placementForEvent(run?.eventName)
      if (!placement) continue
      const contexts = Array.isArray(run.entries)
        ? run.entries.filter((entry) => entry?.kind === 'context' && typeof entry.text === 'string').map((entry) => entry.text)
        : []
      const outputs = Array.isArray(run.entries)
        ? run.entries.filter((entry) => entry?.kind !== 'context' && typeof entry.text === 'string').map((entry) => ({ kind: entry.kind || 'output', text: entry.text }))
        : []
      const runId = typeof run.id === 'string' ? run.id : (typeof item?.id === 'string' ? item.id : '')
      output.push({
        sequence: Number.isFinite(Number(run.displayOrder)) ? Number(run.displayOrder) : sequence,
        kind: 'hook',
        eventName: run.eventName,
        ...placement,
        text: contexts.length > 0 ? contexts.join('\n') : 'No model-visible text inserted.',
        contextTexts: contexts,
        outputEntries: outputs,
        sourceLabel: sourceLabel(run.source, run.sourcePath),
        source: run.source || 'unknown',
        sourcePath: run.sourcePath || null,
        status: run.status || (run.completedAt ? 'completed' : null),
        statusMessage: run.statusMessage || null,
        completedAt: run.completedAt || null,
        runId,
      })
    }
    return output
  }
  const captureLiveHookRuns = () => {
    const targets = new Set([...document.querySelectorAll('button[aria-label="Hooks"]')]
      .filter((button) => !button.hasAttribute('data-hook-trace-trigger'))
      .map((button) => button.closest('[data-turn-key]')?.getAttribute('data-turn-key'))
      .filter(Boolean))
    if (targets.size === 0) return
    const unresolved = new Set(targets)
    const candidates = new Map()
    const inspectProps = (props, fiber) => {
      if (!props || typeof props !== 'object') return
      for (const candidate of [props.turn, props.value, props]) {
        if (!candidate || typeof candidate !== 'object' || !Array.isArray(candidate.hookRuns)) continue
        const turnId = candidate.turnId || props.turnId
        if (!targets.has(turnId)) continue
        liveFibers.set(turnId, fiber)
        unresolved.delete(turnId)
        const normalized = normalizeLiveRuns(candidate.hookRuns)
        const score = normalized.reduce((total, record) => total
          + (record.completedAt ? 1000 : 0)
          + (record.status && !/^in.?progress|started|running$/i.test(record.status) ? 100 : 0)
          + (record.contextTexts?.length || 0) * 10
          + (record.outputEntries?.length || 0), normalized.length * 10000)
        const signature = JSON.stringify(normalized)
        const existing = candidates.get(turnId)
        const previousSignature = liveSignatures.get(turnId)
        const replacesStaleTie = existing
          && score === existing.score
          && existing.signature === previousSignature
          && signature !== previousSignature
        if (score > (existing?.score ?? -1) || replacesStaleTie) candidates.set(turnId, { normalized, score, signature })
      }
    }
    const inspectFiber = (fiber) => {
      if (!fiber) return
      inspectProps(fiber.memoizedProps, fiber)
      inspectProps(fiber.pendingProps, fiber)
      if (fiber.alternate) {
        inspectProps(fiber.alternate.memoizedProps, fiber)
        inspectProps(fiber.alternate.pendingProps, fiber)
      }
    }
    const commit = () => {
      for (const [turnId, candidate] of candidates) {
        const signature = candidate.signature
        if (signature !== liveSignatures.get(turnId)) dirtyTurns.add(turnId)
        liveRecords.set(turnId, candidate.normalized)
        liveScores.set(turnId, candidate.score)
        liveSignatures.set(turnId, signature)
      }
    }
    for (const turnId of targets) inspectFiber(liveFibers.get(turnId))
    if (unresolved.size === 0) { commit(); return }
    const roots = []
    for (const node of [document.documentElement, document.body, document.getElementById('root'), ...document.body.children]) {
      if (!node) continue
      for (const key of Object.getOwnPropertyNames(node)) {
        if ((key.startsWith('__reactContainer$') || key.startsWith('__reactFiber$')) && node[key]) roots.push(node[key])
      }
    }
    const seen = new Set()
    for (const root of roots) {
      const stack = [root]
      while (stack.length && seen.size < 250000 && unresolved.size > 0) {
        const fiber = stack.pop()
        if (!fiber || seen.has(fiber)) continue
        seen.add(fiber)
        inspectFiber(fiber)
        if (fiber.child) stack.push(fiber.child)
        if (fiber.sibling) stack.push(fiber.sibling)
      }
    }
    commit()
  }
  const recordIdentity = (record) => record.hookTraceIdentity || (record.kind === 'hook' && record.runId
    ? JSON.stringify(['hook', record.runId])
    : JSON.stringify(['modelContext', Number(record.sequence || 0), record.label || '', record.text || '']))
  const sideRecords = (turnId, side) => {
    const live = (liveRecords.get(turnId) || []).filter((record) => record.side === side)
    const saved = (Array.isArray(records()[turnId]) ? records()[turnId] : [])
      .filter((record) => record && record.side === side && typeof record.text === 'string' && record.text.length > 0)
      .map((record) => ({ kind: 'modelContext', sourceLabel: 'Source not recorded by Codex', ...record }))
    const matchedSaved = new Set()
    const mergedLive = live.map((record) => {
      const contextTexts = record.contextTexts || []
      const matchIndex = saved.findIndex((candidate, index) => !matchedSaved.has(index)
        && candidate.label === record.label
        && (candidate.text === record.text || contextTexts.includes(candidate.text)))
      if (matchIndex < 0) return record
      matchedSaved.add(matchIndex)
      const candidate = saved[matchIndex]
      return {
        ...candidate,
        ...record,
        hookTraceIdentity: recordIdentity(candidate),
        sequence: candidate.sequence,
        activityIndex: Number.isInteger(candidate.activityIndex) ? candidate.activityIndex : record.activityIndex,
      }
    })
    return [...saved.filter((_, index) => !matchedSaved.has(index)), ...mergedLive]
      .sort((a, b) => Number(a.sequence || 0) - Number(b.sequence || 0))
  }
  const lifecycleRank = (record) => {
    const labels = record.side === 'user'
      ? ['Before your message', 'Session-start context', 'Prompt-submission context']
      : ['Before tool use', 'After tool use', 'During Codex work', 'Additional context', 'Before compaction', 'After compaction', 'Before subagent work', 'After subagent work', 'Permission request', 'Stop context']
    const index = labels.indexOf(record.label || 'Additional context')
    return index < 0 ? labels.length : index
  }
  const effectKind = (record) => {
    if (record.kind !== 'hook' || (record.contextTexts?.length || 0) > 0) return 'model'
    if (/fail|error/i.test(record.status || '') || record.statusMessage) return 'failure'
    if ((record.outputEntries?.length || 0) > 0) return 'diagnostic'
    return 'silent'
  }
  const effectRank = (record) => ({ failure: 0, model: 1, diagnostic: 2, silent: 3 })[effectKind(record)] ?? 4
  const displayRecords = (items) => {
    const output = []
    let previousKey = null
    const sorted = [...items].sort((a, b) => lifecycleRank(a) - lifecycleRank(b)
      || effectRank(a) - effectRank(b)
      || Number(a.sequence || 0) - Number(b.sequence || 0))
    for (const record of sorted) {
      if (record.kind !== 'hook') {
        output.push(record)
        previousKey = null
        continue
      }
      const key = JSON.stringify([
        record.eventName || record.label || '', record.source || 'unknown', record.sourcePath || '',
        effectKind(record), record.status || '', record.statusMessage || '', record.text || '',
        record.outputEntries || [],
      ])
      const existing = previousKey === key ? output.at(-1) : null
      if (!existing || existing.kind !== 'hook') {
        const grouped = { ...record, runCount: 1, runIds: record.runId ? [record.runId] : [] }
        output.push(grouped)
        previousKey = key
        continue
      }
      existing.runCount++
      if (record.runId) existing.runIds.push(record.runId)
      existing.hookTraceIdentity = JSON.stringify(['hookGroup', ...existing.runIds])
    }
    return output
  }
  const recordPrefix = (record) => record.kind === 'hook' ? 'Hook' : 'Added model context'
  const recordKey = (record) => {
    const identity = recordIdentity(record)
    let hash = 2166136261
    for (let index = 0; index < identity.length; index++) hash = Math.imul(hash ^ identity.charCodeAt(index), 16777619)
    return `record:${identity.length}:${hash >>> 0}`
  }

  const actionMount = (turn, side) => {
    const userAnchor = turn.querySelector('[data-local-conversation-user-anchor="true"]')
    const nativeHooks = [...turn.querySelectorAll('button[aria-label="Hooks"]')]
      .filter((button) => !button.hasAttribute('data-hook-trace-trigger'))
    const nativeHookControl = side === 'user'
      ? nativeHooks.find((button) => userAnchor?.contains(button)) || null
      : nativeHooks.filter((button) => !userAnchor?.contains(button)).at(-1) || null
    const label = side === 'user' ? 'Copy message' : 'Copy'
    const buttons = [...turn.querySelectorAll(`button[aria-label="${label}"]`)]
    const button = side === 'user' ? buttons[0] : buttons[buttons.length - 1]
    const actionAnchor = nativeHookControl || button
    const actionsHolder = actionAnchor?.parentElement?.parentElement || actionAnchor?.parentElement || null
    if (!actionsHolder) return null
    let visibilityHolder = actionsHolder
    for (let node = actionsHolder; node && node !== turn; node = node.parentElement) {
      const classes = (node.getAttribute('class') || '').split(/\s+/)
      if (classes.includes('opacity-0') && classes.some((item) => item.endsWith(':opacity-100'))) {
        visibilityHolder = node
        break
      }
    }
    return { actionsHolder, visibilityHolder, nativeHookControl }
  }
  const restoreHolder = (actionsHolder, visibilityHolder = actionsHolder) => {
    actionsHolder?.removeAttribute('data-hook-trace-actions-active')
    if (visibilityHolder === actionsHolder || !visibilityHolder?.querySelector?.('[data-hook-trace-actions-active]')) {
      visibilityHolder?.removeAttribute('data-hook-trace-visibility-active')
    }
  }
  const setHolderActive = (actionsHolder, visibilityHolder, isActive) => {
    if (!actionsHolder) return
    if (!isActive) return restoreHolder(actionsHolder, visibilityHolder)
    actionsHolder.dataset.hookTraceActionsActive = 'true'
    visibilityHolder.dataset.hookTraceVisibilityActive = 'true'
  }
  const restoreNativeHookControls = () => {
    document.querySelectorAll('[data-hook-trace-native-suppressed]').forEach((button) => button.removeAttribute('data-hook-trace-native-suppressed'))
  }
  const suppressNativeHookControls = (turn, mount, retainedControls) => {
    if (!turn || !mount) return
    for (const button of turn.querySelectorAll('button[aria-label="Hooks"]')) {
      if (button.hasAttribute('data-hook-trace-trigger')) continue
      if (!mount.actionsHolder.contains(button)) continue
      retainedControls.add(button)
      if (button.getAttribute('data-hook-trace-native-suppressed') !== 'true') button.dataset.hookTraceNativeSuppressed = 'true'
    }
  }
  const closeSummary = () => {
    if (closeTimer) clearTimeout(closeTimer)
    closeTimer = 0
    summaryTrigger?.removeAttribute('data-state')
    summary?.remove()
    summary = null
    summaryTrigger = null
  }
  const dismissNativeSummary = (trigger) => {
    const turn = trigger?.closest?.('[data-turn-key]')
    const mount = turn ? actionMount(turn, trigger.dataset.hookSide) : null
    if (!mount) return
    for (const button of mount.actionsHolder.querySelectorAll('button[aria-label="Hooks"]')) {
      if (button.hasAttribute('data-hook-trace-trigger')) continue
      button.dispatchEvent(new PointerEvent('pointerout', { bubbles: true, pointerType: 'mouse', relatedTarget: trigger }))
      button.dispatchEvent(new MouseEvent('mouseout', { bubbles: true, relatedTarget: trigger }))
      if (document.activeElement === button) button.blur?.()
    }
  }
  const positionSummary = () => {
    if (!summary || !summaryTrigger) return
    const triggerRect = summaryTrigger.getBoundingClientRect()
    const panelRect = summary.getBoundingClientRect()
    const margin = 8
    const gap = 6
    const naturalWidth = Math.min(panelRect.width || 320, Math.max(0, window.innerWidth - margin * 2))
    const naturalHeight = Math.min(panelRect.height || 100, Math.max(0, window.innerHeight - margin * 2))
    const spaces = {
      above: Math.max(0, triggerRect.top - margin - gap),
      below: Math.max(0, window.innerHeight - margin - triggerRect.bottom - gap),
      left: Math.max(0, triggerRect.left - margin - gap),
      right: Math.max(0, window.innerWidth - margin - triggerRect.right - gap),
    }
    const fits = [
      ['above', spaces.above >= naturalHeight],
      ['below', spaces.below >= naturalHeight],
      ['left', spaces.left >= naturalWidth],
      ['right', spaces.right >= naturalWidth],
    ].find(([, available]) => available)?.[0]
    const placement = fits || Object.entries(spaces).sort((a, b) => b[1] - a[1])[0][0]
    const vertical = placement === 'above' || placement === 'below'
    const width = vertical ? naturalWidth : Math.min(naturalWidth, spaces[placement])
    const height = vertical ? Math.min(naturalHeight, spaces[placement]) : naturalHeight
    const centeredLeft = triggerRect.left + triggerRect.width / 2 - width / 2
    const centeredTop = triggerRect.top + triggerRect.height / 2 - height / 2
    const left = placement === 'left'
      ? triggerRect.left - gap - width
      : placement === 'right'
        ? triggerRect.right + gap
        : Math.max(margin, Math.min(centeredLeft, window.innerWidth - margin - width))
    const top = placement === 'above'
      ? triggerRect.top - gap - height
      : placement === 'below'
        ? triggerRect.bottom + gap
        : Math.max(margin, Math.min(centeredTop, window.innerHeight - margin - height))
    summary.dataset.hookTracePlacement = placement
    summary.style.width = `${Math.max(0, width)}px`
    summary.style.maxWidth = `${Math.max(0, width)}px`
    summary.style.maxHeight = `${Math.max(0, height)}px`
    summary.style.left = `${Math.max(margin, left)}px`
    summary.style.top = `${Math.max(margin, top)}px`
  }
  const openSummary = (trigger) => {
    closeSummary()
    dismissNativeSummary(trigger)
    summaryTrigger = trigger
    trigger.dataset.state = 'open'
    summary = document.createElement('div')
    summary.dataset.codexWingmanOwner = owner
    summary.dataset.hookTraceSummary = 'true'
    summary.setAttribute('role', 'tooltip')
    const title = document.createElement('div')
    title.dataset.hookTraceSummaryTitle = 'true'
    title.textContent = 'Hooks'
    summary.append(title)
    const items = displayRecords(sideRecords(trigger.dataset.turnId, trigger.dataset.hookSide))
    const groups = []
    const byKey = new Map()
    for (const record of items) {
      const key = record.kind === 'hook'
        ? `${record.eventName || record.label || 'hook'}:${record.source || 'unknown'}:${effectKind(record)}`
        : `modelContext:${record.label || 'Additional context'}`
      let group = byKey.get(key)
      if (!group) {
        group = { count: 0, record, statuses: [] }
        byKey.set(key, group)
        groups.push(group)
      }
      group.count += Number(record.runCount || 1)
      if (record.statusMessage && !group.statuses.includes(record.statusMessage)) group.statuses.push(record.statusMessage)
      else if (/fail|error/i.test(record.status || '') && group.statuses.length === 0) group.statuses.push(record.status || 'Hook failed')
    }
    groups.sort((a, b) => lifecycleRank(a.record) - lifecycleRank(b.record)
      || effectRank(a.record) - effectRank(b.record)
      || Number(a.record.activityIndex ?? -1) - Number(b.record.activityIndex ?? -1)
      || Number(a.record.sequence || 0) - Number(b.record.sequence || 0))
    const eventName = (record) => record.label || 'Additional context'
    const sourceName = (record) => ({
      user: 'User', project: 'Project', plugin: 'Plugin', system: 'System',
      mdm: 'Organization', cloudRequirements: 'Organization', cloudManagedConfig: 'Organization',
      legacyManagedConfigFile: 'Managed', legacyManagedConfigMdm: 'Managed', sessionFlags: 'Session', unknown: 'Unknown',
    })[record.source] || 'Unknown'
    const statusText = (value) => String(value || '')
      .replace(/^Hook process\s+/i, 'hook ')
      .replace(/^Hook\s+/, 'hook ')
      .replace(/^./, (character) => character.toLowerCase())
    for (const { count, record, statuses } of groups) {
      const row = document.createElement('div')
      row.dataset.hookTraceSummaryRow = 'true'
      row.dataset.hookTraceSummaryCount = String(count)
      const label = document.createElement('span')
      label.dataset.hookTraceSummaryEvent = 'true'
      label.textContent = eventName(record)
      const meta = document.createElement('span')
      meta.dataset.hookTraceSummaryMeta = 'true'
      const effectLabel = ({
        model: 'Affected model', failure: 'Failed', diagnostic: 'Diagnostics only', silent: 'No recorded model effect',
      })[effectKind(record)] || 'Recorded context'
      meta.textContent = record.kind === 'hook'
        ? `${effectLabel} · ${sourceName(record)}${count > 1 ? ` · ${count} runs` : ''}`
        : `Retained${count > 1 ? ` · ${count} entries` : ''}`
      row.append(label, meta)
      for (const statusValue of statuses) {
        const status = document.createElement('span')
        status.dataset.hookTraceSummaryStatus = 'true'
        status.textContent = statusText(statusValue)
        row.append(status)
      }
      summary.append(row)
    }
    summary.addEventListener('pointerover', () => { if (closeTimer) clearTimeout(closeTimer); closeTimer = 0 })
    summary.addEventListener('pointerout', (event) => {
      if (event.relatedTarget && summary.contains(event.relatedTarget)) return
      closeTimer = setTimeout(closeSummary, 140)
    })
    document.body.append(summary)
    positionSummary()
  }
  const scheduleCloseSummary = (event) => {
    if (event.relatedTarget && (summary?.contains(event.relatedTarget) || summaryTrigger?.contains(event.relatedTarget))) return
    if (closeTimer) clearTimeout(closeTimer)
    closeTimer = setTimeout(closeSummary, 140)
  }

  const scrollContainersFor = (node) => {
    const containers = []
    for (let current = node?.parentElement; current && current !== document.documentElement; current = current.parentElement) {
      const computed = getComputedStyle(current)
      const overflowX = computed.overflowX || computed.overflow || ''
      const overflowY = computed.overflowY || computed.overflow || ''
      const x = /(auto|scroll|overlay)/.test(overflowX) || Number(current.scrollWidth || 0) > Number(current.clientWidth || 0)
      const y = /(auto|scroll|overlay)/.test(overflowY) || Number(current.scrollHeight || 0) > Number(current.clientHeight || 0)
      if (x || y) containers.push({ node: current, x, y })
    }
    const scrollingElement = node?.ownerDocument?.scrollingElement
    if (containers.length === 0 && scrollingElement) {
      containers.push({ node: scrollingElement, x: true, y: true })
    }
    return containers
  }
  const interactionAnchor = (event, element, fallback = element) => {
    const rect = element?.getBoundingClientRect?.()
    const pointerInside = rect
      && Number.isFinite(event?.clientX) && Number.isFinite(event?.clientY)
      && event.clientX >= rect.left && event.clientX <= rect.right
      && event.clientY >= rect.top && event.clientY <= rect.bottom
    return {
      containers: scrollContainersFor(element),
      element,
      fallback,
      pointX: pointerInside ? event.clientX : (rect ? rect.left + rect.width / 2 : 0),
      pointY: pointerInside ? event.clientY : (rect ? rect.top + rect.height / 2 : 0),
      offsetX: pointerInside ? event.clientX - rect.left : (rect?.width || 0) / 2,
      offsetY: pointerInside ? event.clientY - rect.top : (rect?.height || 0) / 2,
    }
  }
  const compensateInteraction = (anchor) => {
    const maxFrames = 8
    const settle = (frameCount = 0, stableFrames = 0) => {
      const survives = Boolean(anchor.element?.isConnected)
      const target = survives ? anchor.element : anchor.fallback
      if (!target?.isConnected || frameCount >= maxFrames) return
      const rect = target.getBoundingClientRect()
      const currentX = survives ? rect.left + anchor.offsetX : rect.left + rect.width / 2
      const currentY = survives ? rect.top + anchor.offsetY : rect.top + rect.height / 2
      const deltaX = currentX - anchor.pointX
      const deltaY = currentY - anchor.pointY
      const compensateAxis = (delta, axis, scrollProperty, scrollExtent, clientExtent) => {
        const ownerKey = axis === 'x' ? 'ownerX' : 'ownerY'
        let candidate = anchor[ownerKey]
        if (!candidate) {
          candidate = (anchor.containers || []).find((item) => item[axis]
            && Math.max(0, Number(item.node[scrollExtent] || 0) - Number(item.node[clientExtent] || 0)) > 0)
          if (candidate) anchor[ownerKey] = candidate
        }
        if (!candidate) return delta
        const container = candidate.node
        const max = Math.max(0, Number(container[scrollExtent] || 0) - Number(container[clientExtent] || 0))
        if (max <= 0 || !Number.isFinite(container[scrollProperty])) return delta
        const current = Number(container[scrollProperty] || 0)
        const next = Math.max(0, Math.min(max, current + delta))
        container[scrollProperty] = next
        return delta - (next - current)
      }
      const remainingX = compensateAxis(deltaX, 'x', 'scrollLeft', 'scrollWidth', 'clientWidth')
      const remainingY = compensateAxis(deltaY, 'y', 'scrollTop', 'scrollHeight', 'clientHeight')
      const stable = Math.abs(deltaX) <= 0.5 && Math.abs(deltaY) <= 0.5
        && Math.abs(remainingX) <= 0.5 && Math.abs(remainingY) <= 0.5
      const nextStableFrames = stable ? stableFrames + 1 : 0
      if (nextStableFrames < 2) requestAnimationFrame(() => settle(frameCount + 1, nextStableFrames))
    }
    settle()
  }
  const rowIsExpanded = (row) => row?.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true'
  const syncBulkTrigger = (turn, turnId, side) => {
    const trigger = turn?.querySelector(`[data-hook-trace-trigger][data-hook-side="${side}"]`)
    if (!trigger) return
    const rows = [...turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`)]
    const allExpanded = rows.length > 0 && rows.every(rowIsExpanded)
    const anyExpanded = rows.some(rowIsExpanded)
    trigger.setAttribute('aria-pressed', allExpanded ? 'true' : 'false')
    trigger.setAttribute('aria-label', `${allExpanded ? 'Collapse' : 'Expand'} ${side === 'user' ? 'prompt' : 'response'} hooks`)
    const mount = actionMount(turn, side)
    if (mount) setHolderActive(mount.actionsHolder, mount.visibilityHolder, anyExpanded)
  }

  const createInline = (turnId, side, record, activityLabel = null) => {
    const shell = document.createElement('div')
    shell.dataset.codexWingmanOwner = owner
    shell.dataset.hookTraceInline = 'true'
    shell.dataset.hookSide = side
    shell.dataset.turnId = turnId
    shell.dataset.hookSequence = String(record.sequence ?? 0)
    shell.dataset.hookRecordKey = recordKey(record)
    shell.setAttribute('class', 'text-size-chat text-token-text-secondary')
    const trigger = document.createElement('button')
    trigger.type = 'button'
    trigger.dataset.hookTraceRowTrigger = 'true'
    trigger.setAttribute('aria-expanded', 'false')
    trigger.setAttribute('class', 'text-size-chat hover:bg-token-bg-subtle inline-flex items-center gap-1 rounded-md border border-transparent focus-visible:ring-2 focus-visible:ring-token-focus-border focus-visible:outline-none')
    trigger.append(hookIcon())
    const labelShell = document.createElement('span')
    const label = document.createElement('span')
    label.setAttribute('class', 'text-token-conversation-body')
    const relation = record.eventName === 'preToolUse'
      ? 'before'
      : record.eventName === 'postToolUse'
        ? 'after'
        : 'at'
    const placement = activityLabel || String(record.label || 'additional context').toLowerCase()
    const effectPrefix = record.kind !== 'hook'
      ? `${recordPrefix(record)}: ${record.label || 'Additional context'}`
      : ({
          model: 'Hook affected the model', failure: 'Hook failed', diagnostic: 'Hook emitted diagnostics', silent: 'Hook ran silently',
        })[effectKind(record)] + ` ${relation} ${placement}`
    label.textContent = `${effectPrefix}${Number(record.runCount || 1) > 1 ? ` (${record.runCount}x)` : ''}`
    labelShell.append(label)
    trigger.append(labelShell, chevron())
    shell.append(trigger)
    const setArrowExpanded = (arrow, expanded) => {
      if (!arrow) return
      const classes = new Set((arrow.getAttribute('class') || '').split(/\s+/).filter(Boolean))
      classes.delete(expanded ? 'rotate-0' : 'rotate-90')
      classes.add(expanded ? 'rotate-90' : 'rotate-0')
      arrow.setAttribute('class', [...classes].join(' '))
    }
    const setExpanded = (expanded, event = null) => {
      const existingBody = shell.querySelector('[data-hook-trace-row-body]')
      if (expanded === Boolean(existingBody)) return
      const anchor = event ? interactionAnchor(event, trigger) : null
      const arrow = trigger.querySelector('[data-hook-trace-chevron]')
      if (!expanded) {
        existingBody?.remove()
        trigger.setAttribute('aria-expanded', 'false')
        setArrowExpanded(arrow, false)
        if (anchor) compensateInteraction(anchor)
        return
      }
      const details = document.createElement('div')
      details.dataset.hookTraceRowBody = 'true'
      const text = document.createElement('pre')
      text.textContent = effectKind(record) === 'silent'
        ? `Codex recorded no model-visible text, command, or diagnostic output for ${Number(record.runCount || 1) > 1 ? `these ${record.runCount} hook runs` : 'this hook run'}. Hook Trace cannot determine what the hook did from the task data. Inspect the hooks.json source below manually.`
        : record.text
      const source = document.createElement('div')
      source.dataset.hookTraceSource = 'true'
      source.textContent = record.sourceLabel || 'Source not recorded by Codex'
      details.append(text, source)
      if (record.kind === 'hook' && (record.status || record.statusMessage)) {
        const status = document.createElement('div')
        status.dataset.hookTraceStatus = 'true'
        const statusText = typeof record.status === 'string'
          ? record.status.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (value) => value.toUpperCase())
          : 'Outcome not recorded'
        status.textContent = `Status: ${statusText}${record.statusMessage ? ` — ${record.statusMessage}` : ''}`
        details.append(status)
      }
      if (record.kind === 'hook' && record.outputEntries?.length) {
        const output = document.createElement('div')
        output.dataset.hookTraceOutput = 'true'
        output.textContent = record.outputEntries.map((entry) => `${entry.kind}: ${entry.text}`).join('\n')
        details.append(output)
      }
      let pointerDown = null
      details.addEventListener('pointerdown', (pointerEvent) => {
        pointerDown = { x: pointerEvent.clientX, y: pointerEvent.clientY, target: pointerEvent.target }
      })
      details.addEventListener('click', (clickEvent) => {
        const protectedCandidate = clickEvent.target?.closest?.('a,button,input,textarea,select,summary,pre,code,[role="button"],[contenteditable="true"],[data-hook-trace-source],[data-hook-trace-status],[data-hook-trace-output]')
        const protectedTarget = protectedCandidate && details.contains(protectedCandidate)
        const selection = window.getSelection?.()
        const moved = !pointerDown || Math.hypot(clickEvent.clientX - pointerDown.x, clickEvent.clientY - pointerDown.y) > 5
        const modified = clickEvent.button !== 0 || clickEvent.shiftKey || clickEvent.ctrlKey || clickEvent.metaKey || clickEvent.altKey
        if (protectedTarget || modified || moved || (selection && (!selection.isCollapsed || String(selection).length > 0))) return
        clickEvent.stopPropagation()
        const bodyAnchor = interactionAnchor(clickEvent, details, trigger)
        details.remove()
        trigger.setAttribute('aria-expanded', 'false')
        setArrowExpanded(trigger.querySelector('[data-hook-trace-chevron]'), false)
        syncBulkTrigger(shell.closest('[data-turn-key]'), turnId, side)
        compensateInteraction(bodyAnchor)
      })
      shell.append(details)
      trigger.setAttribute('aria-expanded', 'true')
      setArrowExpanded(arrow, true)
      if (anchor) compensateInteraction(anchor)
    }
    shell.__hookTraceSetExpanded = setExpanded
    trigger.addEventListener('click', (event) => {
      event.stopPropagation()
      setExpanded(!rowIsExpanded(shell), event)
      syncBulkTrigger(shell.closest('[data-turn-key]'), turnId, side)
    })
    return shell
  }
  const placeInline = (turn, turnId, side) => {
    const existingRows = [...turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`)]
    const expanded = new Set(existingRows
      .filter((node) => node.querySelector('[data-hook-trace-row-trigger]')?.getAttribute('aria-expanded') === 'true')
      .map((node) => node.dataset.hookRecordKey))
    existingRows.forEach((node) => node.remove())
    const items = displayRecords(sideRecords(turnId, side))
    const createRow = (record, activityLabel = null) => {
      const row = createInline(turnId, side, record, activityLabel)
      if (expanded.has(recordKey(record))) row.__hookTraceSetExpanded?.(true)
      return row
    }
    if (side === 'user') {
      const anchor = turn.querySelector('[data-local-conversation-user-anchor="true"]')
      const bubble = anchor?.querySelector('[data-user-message-bubble="true"]')
      const body = bubble?.firstElementChild || bubble?.children?.[0] || bubble
      if (!body) return 0
      const messageContent = [...body.children].find((child) => !child.hasAttribute('data-hook-trace-inline')) || body.firstElementChild
      for (const record of items.filter((record) => record.label === 'Before your message')) {
        insertBefore(body, createRow(record), messageContent)
      }
      for (const record of items.filter((record) => record.label !== 'Before your message')) body.append(createRow(record))
      syncBulkTrigger(turn, turnId, side)
      return turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`).length
    }
    const activityButtons = [...turn.querySelectorAll('button')].filter((button) => /^(Running|Ran|Edited files)/.test((button.textContent || '').trim()))
    const activities = activityButtons.map((button) => button.closest('[data-local-conversation-item-target-ids]') || button.parentElement).filter(Boolean)
    const targetSurfaces = [...turn.querySelectorAll('[data-local-conversation-item-target-ids]')]
    const finalAssistant = turn.querySelector('[data-local-conversation-final-assistant="true"]')
    const finalAnnotation = finalAssistant?.querySelector('[data-response-annotation-target]')
    for (const record of items) {
      const runIds = record.runIds?.length ? record.runIds : (record.runId ? [record.runId] : [])
      const exactMatches = ['Before tool use', 'After tool use'].includes(record.label) && runIds.length > 0
        ? targetSurfaces.flatMap((surface) => (surface.getAttribute('data-local-conversation-item-target-ids') || '').split(/\s+/)
          .filter((targetId) => targetId && runIds.some((runId) => runId.endsWith(`:${targetId}`)))
          .map((targetId) => ({ surface, targetId })))
          .sort((a, b) => b.targetId.length - a.targetId.length)
        : []
      const exactActivity = exactMatches[0]?.surface || null
      const exactActivityIndex = exactActivity ? activities.indexOf(exactActivity) : -1
      const activityIndex = exactActivityIndex >= 0 ? exactActivityIndex : (Number.isInteger(record.activityIndex) ? record.activityIndex : -1)
      const activitySurface = exactActivity || activities[activityIndex] || null
      const activityButton = exactActivity
        ? [...exactActivity.querySelectorAll('button')].find((button) => /^(Running|Ran|Edited files)/.test((button.textContent || '').trim())) || exactActivity.querySelector('button')
        : activityButtons[activityIndex]
      const activity = activitySurface?.matches?.('button, [role="button"]')
        ? activitySurface.parentElement
        : activitySurface
      let activityHeader = activityButton
      while (activityHeader?.parentElement && activityHeader.parentElement !== activity) activityHeader = activityHeader.parentElement
      if (activityHeader?.parentElement !== activity) activityHeader = activity?.firstElementChild || null
      const activityLabel = (activityButton?.textContent || '').replace(/\s+/g, ' ').trim() || null
      const row = createRow(record, activityLabel)
      if (record.label === 'Before tool use' && activity) insertBefore(activity, row, activityHeader)
      else if (record.label === 'After tool use' && activity) activity.append(row)
      else if (record.label === 'Stop context' && finalAnnotation?.parentElement) finalAnnotation.parentElement.append(row)
      else if (!['Before tool use', 'After tool use'].includes(record.label) && finalAnnotation?.parentElement) insertBefore(finalAnnotation.parentElement, row, finalAnnotation)
    }
    syncBulkTrigger(turn, turnId, side)
    return turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`).length
  }
  const placementAnchorSignature = (turn, side) => {
    if (side === 'user') {
      const anchor = turn.querySelector('[data-local-conversation-user-anchor="true"]')
      const bubble = anchor?.querySelector('[data-user-message-bubble="true"]')
      const body = bubble?.firstElementChild || bubble?.children?.[0] || bubble
      return `user:${Boolean(body)}`
    }
    const activityTargets = [...turn.querySelectorAll('[data-local-conversation-item-target-ids]')]
      .map((surface) => surface.getAttribute('data-local-conversation-item-target-ids') || '')
      .join('|')
    const activityCount = [...turn.querySelectorAll('button')]
      .filter((button) => /^(Running|Ran|Edited files)/.test((button.textContent || '').trim())).length
    const finalAnnotation = turn.querySelector('[data-local-conversation-final-assistant="true"] [data-response-annotation-target]')
    return `response:${activityCount}:${activityTargets}:final:${Boolean(finalAnnotation?.parentElement)}`
  }
  const collapsedNativeParents = (rows, turn) => {
    const controls = new Set()
    const candidates = [...turn.querySelectorAll('button[aria-expanded="false"], [role="button"][aria-expanded="false"]')]
      .filter((control) => !control.hasAttribute('data-hook-trace-trigger')
        && !control.hasAttribute('data-hook-trace-row-trigger'))
    const standardFold = /^(Worked\s+for\b|Running\b|Ran\b|Read\s+files\b|Edited\s+files\b)/i
    for (const row of rows) {
      for (const control of candidates) {
        const controlledId = control.getAttribute('aria-controls')
        const controlled = controlledId ? document.getElementById(controlledId) : null
        const directlyOwns = control.contains(row)
          || controlled?.contains(row)
          || control.nextElementSibling?.contains?.(row)
        const sharesFold = control.parentElement?.contains(row)
          && standardFold.test((control.textContent || '').replace(/\s+/g, ' ').trim())
        if (directlyOwns || sharesFold) controls.add(control)
      }
    }
    const depth = (node) => {
      let value = 0
      for (let current = node; current && current !== turn; current = current.parentElement) value++
      return value
    }
    return [...controls].sort((a, b) => depth(a) - depth(b))
  }
  const makeTrigger = (turn, turnId, side, mount) => {
    const { actionsHolder, visibilityHolder, nativeHookControl } = mount
    const slot = document.createElement('span')
    slot.dataset.codexWingmanOwner = owner
    slot.dataset.hookTraceSlot = 'true'
    slot.dataset.turnId = turnId
    slot.dataset.hookSide = side
    const trigger = document.createElement('button')
    trigger.type = 'button'
    trigger.dataset.hookTraceTrigger = 'true'
    trigger.dataset.turnId = turnId
    trigger.dataset.hookSide = side
    trigger.setAttribute('aria-label', `${side === 'user' ? 'Expand prompt' : 'Expand response'} hooks`)
    trigger.setAttribute('aria-pressed', 'false')
    trigger.append(hookIcon())
    trigger.addEventListener('pointerover', (event) => { if (!event.pointerType || event.pointerType === 'mouse') openSummary(trigger) })
    trigger.addEventListener('pointerout', scheduleCloseSummary)
    trigger.addEventListener('focusin', () => openSummary(trigger))
    trigger.addEventListener('focusout', scheduleCloseSummary)
    trigger.addEventListener('click', (event) => {
      event.stopPropagation()
      closeSummary()
      const rows = [...turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`)]
      if (rows.length === 0) return
      const anchor = interactionAnchor(event, trigger)
      const expand = rows.some((row) => !rowIsExpanded(row))
      const nativeParents = expand ? collapsedNativeParents(rows, turn) : []
      for (const control of nativeParents) control.click()
      const finish = () => {
        const currentRows = [...turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`)]
        for (const row of currentRows) row.__hookTraceSetExpanded?.(expand)
        syncBulkTrigger(turn, turnId, side)
        compensateInteraction(anchor)
      }
      if (nativeParents.length > 0) requestAnimationFrame(finish)
      else finish()
    })
    slot.append(trigger)
    if (nativeHookControl?.parentElement && actionsHolder.contains(nativeHookControl)) {
      insertBefore(nativeHookControl.parentElement, slot, nativeHookControl)
    } else actionsHolder.append(slot)
    return trigger
  }
  const reconcile = (refreshTurns = new Set()) => {
    const retainedNativeControls = new Set()
    const turns = [...document.querySelectorAll('[data-turn-key]')]
    const currentTurnIds = new Set(turns.map((turn) => turn.getAttribute('data-turn-key')).filter(Boolean))
    for (const key of [...placementStates.keys()]) if (!currentTurnIds.has(key.split(':', 1)[0])) placementStates.delete(key)
    for (const turnId of [...liveFibers.keys()]) if (!currentTurnIds.has(turnId)) liveFibers.delete(turnId)
    for (const turnId of [...liveRecords.keys()]) if (!currentTurnIds.has(turnId)) { liveRecords.delete(turnId); liveScores.delete(turnId); liveSignatures.delete(turnId) }
    document.querySelectorAll('[data-hook-trace-inline]').forEach((node) => {
      if (!currentTurnIds.has(node.dataset.turnId)
        || sideRecords(node.dataset.turnId, node.dataset.hookSide).length === 0) node.remove()
    })
    document.querySelectorAll('[data-hook-trace-slot]').forEach((slot) => {
      if (!currentTurnIds.has(slot.dataset.turnId) || sideRecords(slot.dataset.turnId, slot.dataset.hookSide).length === 0) {
        const turn = slot.closest('[data-turn-key]')
        const mount = turn ? actionMount(turn, slot.dataset.hookSide) : null
        if (mount) restoreHolder(mount.actionsHolder, mount.visibilityHolder)
        if (slot.contains(summaryTrigger)) closeSummary()
        slot.remove()
      }
    })
    for (const turn of turns) {
      const turnId = turn.getAttribute('data-turn-key')
      if (!turnId) continue
      for (const side of ['user', 'response']) {
        const items = sideRecords(turnId, side)
        const placementKey = `${turnId}:${side}`
        if (items.length === 0) {
          placementStates.delete(placementKey)
          turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`).forEach((row) => row.remove())
          const emptyTrigger = turn.querySelector(`[data-hook-trace-trigger][data-hook-side="${side}"]`)
          if (emptyTrigger) {
            const mount = actionMount(turn, side)
            if (mount) restoreHolder(mount.actionsHolder, mount.visibilityHolder)
            const slot = emptyTrigger.closest('[data-hook-trace-slot]')
            if (slot?.contains(summaryTrigger)) closeSummary()
            slot?.remove()
          }
          continue
        }
        let trigger = turn.querySelector(`[data-hook-trace-trigger][data-hook-side="${side}"]`)
        let attachedCount = turn.querySelectorAll(`[data-hook-trace-inline][data-hook-side="${side}"]`).length
        const placementSignature = JSON.stringify(items.map(recordKey)) + `:${placementAnchorSignature(turn, side)}`
        const previousPlacement = placementStates.get(placementKey)
        if (refreshTurns.has(turnId)
          || previousPlacement?.signature !== placementSignature
          || previousPlacement?.count !== attachedCount) {
          attachedCount = placeInline(turn, turnId, side) || 0
          placementStates.set(placementKey, { signature: placementSignature, count: attachedCount })
        }
        const mount = actionMount(turn, side)
        if (attachedCount === 0) {
          if (mount) restoreHolder(mount.actionsHolder, mount.visibilityHolder)
          const slot = trigger?.closest('[data-hook-trace-slot]')
          if (slot?.contains(summaryTrigger)) closeSummary()
          slot?.remove()
          continue
        }
        if (!trigger && mount) trigger = makeTrigger(turn, turnId, side, mount)
        if (trigger && mount) suppressNativeHookControls(turn, mount, retainedNativeControls)
        syncBulkTrigger(turn, turnId, side)
      }
    }
    document.querySelectorAll('[data-hook-trace-native-suppressed]').forEach((button) => {
      if (!retainedNativeControls.has(button)) button.removeAttribute('data-hook-trace-native-suppressed')
    })
  }
  const schedule = () => {
    if (frame) return
    frame = requestAnimationFrame(() => { frame = 0; reconcile() })
  }
  const observer = new MutationObserver(schedule)
  observer.observe(document.body, { childList: true, subtree: true })
  window.addEventListener('resize', positionSummary)
  window.addEventListener('scroll', positionSummary, true)
  const update = (next) => {
    const previousRecords = snapshot?.records || {}
    const nextRecords = next?.records || {}
    for (const turnId of new Set([...Object.keys(previousRecords), ...Object.keys(nextRecords)])) {
      if (JSON.stringify(previousRecords[turnId] || []) !== JSON.stringify(nextRecords[turnId] || [])) dirtyTurns.add(turnId)
    }
    snapshot = next || {}
    captureLiveHookRuns()
    const refreshTurns = new Set(dirtyTurns)
    dirtyTurns.clear()
    reconcile(refreshTurns)
    return status()
  }
  const status = () => ({ threadId: typeof snapshot?.threadId === 'string' ? snapshot.threadId : null, attachedCount: document.querySelectorAll('[data-hook-trace-trigger]').length, inlineCount: document.querySelectorAll('[data-hook-trace-inline]').length, liveTurnCount: liveRecords.size, expandedCount: [...document.querySelectorAll('[data-hook-trace-row-trigger]')].filter((trigger) => trigger.getAttribute('aria-expanded') === 'true').length })
  const cleanup = () => {
    observer.disconnect()
    if (frame) cancelAnimationFrame(frame)
    closeSummary()
    window.removeEventListener('resize', positionSummary)
    window.removeEventListener('scroll', positionSummary, true)
    document.querySelectorAll('[data-hook-trace-actions-active]').forEach((node) => node.removeAttribute('data-hook-trace-actions-active'))
    document.querySelectorAll('[data-hook-trace-visibility-active]').forEach((node) => node.removeAttribute('data-hook-trace-visibility-active'))
    restoreNativeHookControls()
    document.querySelectorAll('[data-codex-wingman-owner="hook-trace"]').forEach((node) => node.remove())
    delete window[globalName]
    return true
  }
  const controller = { version, update, reconcile, cleanup, status }
  window[globalName] = controller
  return update(snapshot)
})()
