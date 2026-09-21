(() => {
  const version = 'turn-metadata-v7'
  const owner = 'turn-metadata'
  const globalName = '__codexHelperTurnMetadata'
  const existing = window[globalName]
  if (existing?.version === version) {
    return existing.update(state || {})
  }
  existing?.cleanup?.()

  let snapshot = state || {}
  let openTrigger = null
  let openPanel = null
  let closeTimer = 0
  let focusTimer = 0
  let restoringFocus = false
  let frame = 0
  let coarseTrigger = null
  let coarseWasOpen = false
  const observers = new Map()

  const style = document.createElement('style')
  style.dataset.codexWingmanOwner = owner
  style.dataset.turnMetadataStyle = 'true'
  style.textContent = `
    [data-turn-metadata-slot] { display: inline-flex; align-items: center; flex: 0 0 auto; }
    [data-turn-metadata-trigger] {
      display: inline-flex; align-items: center; gap: 4px; min-height: 24px; padding: 2px 4px;
      border: 0; border-radius: 6px; background: transparent;
      color: var(--color-token-text-tertiary, GrayText); cursor: pointer;
      font: inherit; font-size: 12px; line-height: 1; white-space: nowrap;
    }
    [data-turn-metadata-trigger]:hover,
    [data-turn-metadata-trigger][aria-expanded="true"] { background: var(--color-token-list-hover-background, rgba(127,127,127,.12)); }
    [data-turn-metadata-trigger]:focus-visible { outline: 2px solid var(--color-token-focus-border, currentColor); outline-offset: 1px; }
    [data-turn-metadata-trigger] svg { width: 14px; height: 14px; flex: 0 0 auto; }
    [data-turn-metadata-panel] {
      position: fixed; z-index: 2147483646; box-sizing: border-box; max-height: min(420px, 55vh);
      overflow: auto; padding: 12px; border: 1px solid var(--color-token-border, rgba(127,127,127,.3));
      border-radius: 10px; background: var(--color-token-dropdown-background, Canvas);
      color: var(--color-token-foreground, CanvasText); box-shadow: 0 12px 32px rgba(0,0,0,.24);
      font: inherit; font-size: 13px; line-height: 1.4;
    }
    [data-turn-metadata-heading] { margin: 0 0 8px; font-weight: 600; }
    [data-turn-metadata-row] { display: grid; grid-template-columns: minmax(96px, auto) minmax(0, 1fr); gap: 12px; padding: 2px 0; }
    [data-turn-metadata-label] { color: var(--color-token-text-tertiary, GrayText); }
    [data-turn-metadata-value] { min-width: 0; overflow-wrap: anywhere; }
    [data-turn-metadata-section] { margin-top: 9px; padding-top: 8px; border-top: 1px solid var(--color-token-border, rgba(127,127,127,.24)); }
    [data-turn-metadata-partial] { margin-top: 9px; color: var(--color-token-text-tertiary, GrayText); }
    @media (prefers-reduced-motion: reduce) {
      [data-turn-metadata-trigger], [data-turn-metadata-panel] { transition: none !important; animation: none !important; }
    }
  `
  document.documentElement.appendChild(style)

  const records = () => snapshot?.records && typeof snapshot.records === 'object' ? snapshot.records : {}
  const modelLabel = (record) => typeof record?.model === 'string' && record.model.trim()
    ? record.model.trim()
    : 'Model details'
  const formatNumber = (value) => Number.isFinite(value) ? Math.round(value).toLocaleString('en-US') : null
  const formatTime = (value) => Number.isFinite(value) ? new Date(value).toLocaleString() : null
  const formatDuration = (value) => {
    if (!Number.isFinite(value)) return null
    if (value < 1000) return `${Math.round(value)} ms`
    const seconds = Math.round(value / 100) / 10
    return `${seconds} second${seconds === 1 ? '' : 's'}`
  }
  const insertBefore = (parent, node, reference) => {
    if (parent.insertBefore) return parent.insertBefore(node, reference)
    if (node.parentNode) node.remove()
    const index = parent.children.indexOf(reference)
    node.parentNode = parent
    parent.children.splice(Math.max(0, index), 0, node)
    node.ownerDocument?._notify?.(parent, 'childList')
    return node
  }
  const textNode = (text, attribute) => {
    const node = document.createElement('div')
    if (attribute) node.setAttribute(attribute, 'true')
    node.textContent = String(text)
    return node
  }
  const appendRow = (panel, label, value) => {
    if (value === null || value === undefined || value === '') return
    const row = document.createElement('div')
    row.dataset.turnMetadataRow = 'true'
    const name = textNode(label, 'data-turn-metadata-label')
    const content = textNode(value, 'data-turn-metadata-value')
    row.append(name, content)
    panel.appendChild(row)
  }

  const buildPanel = (trigger, record) => {
    const panel = document.createElement('div')
    panel.dataset.codexWingmanOwner = owner
    panel.dataset.turnMetadataPanel = 'true'
    panel.id = `codex-turn-metadata-${record.turnId}`
    panel.setAttribute('role', 'dialog')
    panel.setAttribute('aria-label', `Turn metadata for ${modelLabel(record)}`)
    panel.setAttribute('tabindex', '-1')
    const heading = textNode('Turn Metadata', 'data-turn-metadata-heading')
    panel.appendChild(heading)
    appendRow(panel, 'Model', modelLabel(record))
    appendRow(panel, 'Supplier', record.providerLabel)
    appendRow(panel, 'Provider ID', record.providerId)
    appendRow(panel, 'Started', formatTime(record.startedAt))
    appendRow(panel, 'Completed', formatTime(record.completedAt))
    appendRow(panel, 'Elapsed', formatDuration(record.durationMs))
    const usage = record.tokenUsage
    if (usage && typeof usage === 'object') {
      const section = document.createElement('div')
      section.dataset.turnMetadataSection = 'usage'
      appendRow(section, 'Input', formatNumber(usage.inputTokens))
      appendRow(section, 'Cached input', formatNumber(usage.cachedInputTokens))
      appendRow(section, 'Output', formatNumber(usage.outputTokens))
      appendRow(section, 'Reasoning', formatNumber(usage.reasoningOutputTokens))
      appendRow(section, 'Total', formatNumber(usage.totalTokens))
      if (section.children.length) panel.appendChild(section)
    }
    if (Array.isArray(record.subagents) && record.subagents.length) {
      const section = document.createElement('div')
      section.dataset.turnMetadataSection = 'subagents'
      for (const agent of record.subagents) {
        const details = [agent.threadId, agent.model, agent.reasoningEffort, agent.status].filter(Boolean).join(' | ')
        appendRow(section, 'Subagent', details)
      }
      panel.appendChild(section)
    }
    if (record.completeness === 'partial') {
      const partial = textNode('This historical record is partial. Missing fields were not inferred.', 'data-turn-metadata-partial')
      panel.appendChild(partial)
    }
    trigger.setAttribute('aria-controls', panel.id)
    return panel
  }

  const positionPanel = () => {
    if (!openTrigger?.isConnected || !openPanel?.isConnected) return
    const anchor = openTrigger.getBoundingClientRect()
    const measured = openPanel.getBoundingClientRect()
    const gutter = 12
    const gap = 10
    const width = Math.min(420, Math.max(240, window.innerWidth - gutter * 2))
    const height = measured.height || Math.min(420, window.innerHeight * 0.55)
    const left = Math.min(
      Math.max(gutter, anchor.right - width),
      Math.max(gutter, window.innerWidth - width - gutter),
    )
    const preferredTop = anchor.top - height - gap
    const top = preferredTop >= gutter
      ? preferredTop
      : Math.min(anchor.bottom + gap, Math.max(gutter, window.innerHeight - height - gutter))
    openPanel.style.left = `${left}px`
    openPanel.style.top = `${top}px`
    openPanel.style.width = `${width}px`
    openPanel.style.maxHeight = `${Math.min(420, window.innerHeight * 0.55)}px`
  }

  const cancelClose = () => {
    if (closeTimer) clearTimeout(closeTimer)
    closeTimer = 0
  }
  const close = ({ returnFocus = false } = {}) => {
    cancelClose()
    if (focusTimer) clearTimeout(focusTimer)
    focusTimer = 0
    const trigger = openTrigger
    openPanel?.remove()
    openPanel = null
    openTrigger = null
    if (trigger) {
      trigger.setAttribute('aria-expanded', 'false')
      trigger.removeAttribute('aria-controls')
      if (returnFocus) {
        trigger.focus({ preventScroll: true })
        focusTimer = setTimeout(() => {
          focusTimer = 0
          if (trigger.isConnected) {
            restoringFocus = true
            try { trigger.focus({ preventScroll: true }) }
            finally { restoringFocus = false }
          }
        }, 0)
      }
    }
  }
  const open = (trigger) => {
    cancelClose()
    if (openTrigger === trigger && openPanel?.isConnected) return
    close()
    const turnId = trigger.getAttribute('data-turn-id')
    const record = records()[turnId]
    if (!record || record.turnId !== turnId) return
    openTrigger = trigger
    openPanel = buildPanel(trigger, record)
    trigger.setAttribute('aria-expanded', 'true')
    document.body.appendChild(openPanel)
    requestAnimationFrame(positionPanel)
  }
  const closeSoon = () => {
    cancelClose()
    closeTimer = setTimeout(() => {
      const active = document.activeElement
      if (openTrigger?.contains(active) || openPanel?.contains(active)) return
      close()
    }, 160)
  }

  const visibilityClasses = (node) => (node.getAttribute('class') || '').split(/\s+/)
    .filter((token) => /(?:^|:)opacity-/.test(token))
  const relocateTimestamp = (toolbar, timestamp) => {
    if (!timestamp.hasAttribute('data-turn-metadata-relocated')) {
      const visibility = visibilityClasses(timestamp)
      timestamp.dataset.turnMetadataRelocated = 'true'
      timestamp.dataset.turnMetadataOriginalVisibilityClasses = visibility.join(' ')
      timestamp.setAttribute('class', (timestamp.getAttribute('class') || '').split(/\s+/)
        .filter((token) => token && !visibility.includes(token)).join(' '))
    }
    if (timestamp.parentElement !== toolbar) toolbar.appendChild(timestamp)
  }
  const restoreTimestamp = (timestamp) => {
    if (!timestamp?.hasAttribute('data-turn-metadata-relocated')) return
    const toolbar = timestamp.parentElement
    const originalVisibility = (timestamp.dataset.turnMetadataOriginalVisibilityClasses || '').split(/\s+/).filter(Boolean)
    const classes = new Set((timestamp.getAttribute('class') || '').split(/\s+/).filter(Boolean))
    originalVisibility.forEach((token) => classes.add(token))
    timestamp.setAttribute('class', [...classes].join(' '))
    timestamp.removeAttribute('data-turn-metadata-relocated')
    timestamp.removeAttribute('data-turn-metadata-original-visibility-classes')
    if (toolbar?.parentElement) toolbar.insertAdjacentElement('afterend', timestamp)
  }

  const createSlot = (turnId, record, toolbar, timestamp) => {
    const slot = document.createElement('span')
    slot.dataset.codexWingmanOwner = owner
    slot.dataset.turnMetadataSlot = 'true'
    slot.dataset.turnId = turnId
    const trigger = document.createElement('button')
    trigger.type = 'button'
    trigger.dataset.turnMetadataTrigger = 'true'
    trigger.dataset.turnId = turnId
    trigger.setAttribute('aria-expanded', 'false')
    trigger.setAttribute('aria-haspopup', 'dialog')
    const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    icon.setAttribute('viewBox', '0 0 20 20')
    icon.setAttribute('aria-hidden', 'true')
    const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle')
    circle.setAttribute('cx', '10')
    circle.setAttribute('cy', '10')
    circle.setAttribute('r', '8')
    circle.setAttribute('fill', 'none')
    circle.setAttribute('stroke', 'currentColor')
    circle.setAttribute('stroke-width', '1.5')
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    line.setAttribute('d', 'M10 8.5v5M10 5.5h.01')
    line.setAttribute('fill', 'none')
    line.setAttribute('stroke', 'currentColor')
    line.setAttribute('stroke-width', '1.7')
    line.setAttribute('stroke-linecap', 'round')
    icon.append(circle, line)
    const label = document.createElement('span')
    label.dataset.turnMetadataModel = 'true'
    trigger.append(icon, label)
    slot.appendChild(trigger)
    insertBefore(toolbar, slot, timestamp)
    updateSlot(slot, record)
    return slot
  }
  const updateSlot = (slot, record) => {
    const trigger = slot.querySelector('[data-turn-metadata-trigger]')
    const label = modelLabel(record)
    slot.dataset.turnId = record.turnId
    trigger.dataset.turnId = record.turnId
    trigger.querySelector('[data-turn-metadata-model]').textContent = label
    trigger.setAttribute('aria-label', `Show response metadata for ${label}`)
  }

  const observedRootFor = (turn) =>
    turn.closest('[data-request-user-input-auto-resolution-conversation-id]') || turn.parentElement
  const observeRoot = (root) => {
    if (!root || observers.has(root)) return
    const observer = new MutationObserver(() => queueReconcile())
    observer.observe(root, { childList: true, subtree: true })
    observers.set(root, observer)
  }
  const reconcile = () => {
    const active = new Set()
    for (const turn of document.querySelectorAll('[data-turn-key]')) {
      observeRoot(observedRootFor(turn))
      const turnId = turn.getAttribute('data-turn-key')
      const record = records()[turnId]
      if (!record || record.turnId !== turnId) continue
      const finalAssistant = turn.querySelector('[data-local-conversation-final-assistant="true"]')
      const annotation = finalAssistant?.querySelector('[data-response-annotation-target]')
      const copy = annotation
        ? [...annotation.querySelectorAll('button[aria-label="Copy"]')].find((candidate) => {
            if (!candidate.parentElement?.hasAttribute('data-state')) return false
            const candidateToolbar = candidate.parentElement?.parentElement
            return candidateToolbar?.querySelector('button[aria-label="Continue in new task from here"]')
              || candidateToolbar?.querySelector('button[aria-label="Good response"]')
          })
        : null
      const copyWrapper = copy?.parentElement
      const toolbar = copyWrapper?.parentElement
      const footer = toolbar?.parentElement
      const timestamp = footer?.querySelector('[data-assistant-message-sent-time]') || null
      if (!toolbar || !footer || !timestamp) continue
      if (timestamp.parentElement !== footer && timestamp.parentElement !== toolbar) continue
      relocateTimestamp(toolbar, timestamp)
      let slot = toolbar.querySelector(`[data-turn-metadata-slot][data-turn-id="${turnId}"]`)
      if (!slot) slot = createSlot(turnId, record, toolbar, timestamp)
      else {
        updateSlot(slot, record)
        if (slot.nextElementSibling !== timestamp) insertBefore(toolbar, slot, timestamp)
      }
      active.add(slot)
    }
    for (const slot of document.querySelectorAll('[data-turn-metadata-slot]')) {
      if (active.has(slot)) continue
      if (openTrigger && slot.contains(openTrigger)) close()
      restoreTimestamp(slot.parentElement?.querySelector('[data-turn-metadata-relocated]'))
      slot.remove()
    }
    for (const [root, observer] of [...observers]) {
      if (root.isConnected) continue
      observer.disconnect()
      observers.delete(root)
    }
    return { attachedCount: active.size, open: Boolean(openPanel?.isConnected) }
  }
  const queueReconcile = () => {
    if (frame) return
    frame = requestAnimationFrame(() => {
      frame = 0
      reconcile()
    })
  }

  const triggerForEvent = (event) => event.target?.closest?.('[data-turn-metadata-trigger]')
  const onPointerDown = (event) => {
    const trigger = triggerForEvent(event)
    if (!trigger || (event.pointerType !== 'touch' && event.pointerType !== 'pen')) return
    coarseTrigger = trigger
    coarseWasOpen = openTrigger === trigger
  }
  const onClick = (event) => {
    const trigger = triggerForEvent(event)
    if (!trigger) return
    event.preventDefault()
    if (coarseTrigger === trigger) {
      const shouldOpen = !coarseWasOpen
      coarseTrigger = null
      coarseWasOpen = false
      if (shouldOpen) open(trigger)
      else close()
      return
    }
    if (openTrigger === trigger) close()
    else open(trigger)
  }
  const onPointerOver = (event) => {
    const trigger = triggerForEvent(event)
    if (!trigger) return
    if (trigger.contains(event.relatedTarget)) return
    open(trigger)
  }
  const onPointerOut = (event) => {
    if (!openTrigger) return
    if (openTrigger.contains(event.relatedTarget) || openPanel?.contains(event.relatedTarget)) return
    closeSoon()
  }
  const onFocusIn = (event) => {
    if (restoringFocus) return
    const trigger = triggerForEvent(event)
    if (trigger) open(trigger)
  }
  const onFocusOut = (event) => {
    if (!openTrigger) return
    if (openTrigger.contains(event.relatedTarget) || openPanel?.contains(event.relatedTarget)) return
    closeSoon()
  }
  const onKeyDown = (event) => {
    if (event.key !== 'Escape' || !openTrigger) return
    event.preventDefault()
    event.stopImmediatePropagation()
    close({ returnFocus: true })
  }
  const onResize = () => positionPanel()
  const onScroll = () => positionPanel()
  document.addEventListener('click', onClick)
  document.addEventListener('pointerdown', onPointerDown)
  document.addEventListener('pointerover', onPointerOver)
  document.addEventListener('pointerout', onPointerOut)
  document.addEventListener('focusin', onFocusIn)
  document.addEventListener('focusout', onFocusOut)
  document.addEventListener('keydown', onKeyDown, true)
  window.addEventListener('keydown', onKeyDown, true)
  window.addEventListener('resize', onResize)
  window.addEventListener('scroll', onScroll, true)

  const update = (nextState) => {
    snapshot = nextState || {}
    return reconcile()
  }
  const status = () => {
    const recordIds = Object.keys(records())
    const visibleRecordIds = [...document.querySelectorAll('[data-turn-key]')]
      .map((turn) => turn.getAttribute('data-turn-key'))
      .filter((turnId) => recordIds.includes(turnId)
        && Boolean(document.querySelector(`[data-turn-metadata-slot][data-turn-id="${turnId}"]`)))
    return {
      threadId: typeof snapshot?.threadId === 'string' ? snapshot.threadId : null,
      recordIds,
      visibleRecordIds,
      attachedCount: document.querySelectorAll('[data-turn-metadata-slot]').length,
      open: Boolean(openPanel?.isConnected),
    }
  }
  const cleanup = () => {
    cancelClose()
    if (frame) cancelAnimationFrame(frame)
    for (const observer of observers.values()) observer.disconnect()
    observers.clear()
    document.removeEventListener('click', onClick)
    document.removeEventListener('pointerdown', onPointerDown)
    document.removeEventListener('pointerover', onPointerOver)
    document.removeEventListener('pointerout', onPointerOut)
    document.removeEventListener('focusin', onFocusIn)
    document.removeEventListener('focusout', onFocusOut)
    document.removeEventListener('keydown', onKeyDown, true)
    window.removeEventListener('keydown', onKeyDown, true)
    window.removeEventListener('resize', onResize)
    window.removeEventListener('scroll', onScroll, true)
    close()
    document.querySelectorAll('[data-turn-metadata-relocated]').forEach(restoreTimestamp)
    document.querySelectorAll('[data-codex-wingman-owner="turn-metadata"]').forEach((node) => node.remove())
    delete window[globalName]
    return true
  }

  const controller = { version, update, cleanup, reconcile, positionPanel, status }
  window[globalName] = controller
  return update(snapshot)
})()
