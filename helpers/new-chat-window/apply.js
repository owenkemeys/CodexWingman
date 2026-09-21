(() => {
  const owner = 'new-chat-window'
  const globalName = '__codexHelperNewChatWindow'
  const version = 'delegated-contextmenu-v10'
  const existing = window[globalName]
  if (existing?.version === version) return existing.reconcile()
  if (existing) {
    try { existing.cleanup?.() } catch {}
  }

  const marked = new Set()
  let bootstrapConsumed = false
  let bootstrapStarted = false
  let pendingFrame = 0
  let bootstrapObserver = null

  const cleanLabel = (value) => String(value || '').replace(/\s+/g, ' ').trim()
  const normalize = (value) => cleanLabel(value).toLowerCase()
  const semanticLabel = (control) => {
    const labelledBy = control.getAttribute('aria-labelledby')
    if (labelledBy) {
      const referencedText = labelledBy.split(/\s+/)
        .map((id) => document.getElementById(id)?.textContent || '')
        .join(' ')
      if (normalize(referencedText)) return cleanLabel(referencedText)
    }
    return cleanLabel(control.getAttribute('aria-label') || control.getAttribute('title') || control.textContent)
  }
  const hasAncestor = (control, predicate) => {
    for (let node = control?.parentElement; node; node = node.parentElement) {
      if (predicate(node)) return true
    }
    return false
  }
  const hasNewTaskGlyph = (control) => control.querySelector?.('path')
    ?.getAttribute?.('d')?.startsWith('M6.33325 1.88379') === true
  const isSidebarRow = (control, explicit = semanticLabel(control)) => /^new task ?ctrl\+n$/.test(normalize(explicit))
    && hasAncestor(control, (node) => node.matches?.('nav[aria-label="Scheduled task folders"]'))
  const isCollapsedHeader = (control, explicit = semanticLabel(control)) => {
    const inVisibleCollapsedHeader = hasAncestor(control, (node) => {
      const classes = cleanLabel(node.className)
      return node.tagName === 'DIV' && classes.includes('pointer-events-none')
        && classes.includes('relative') && classes.includes('h-full') && classes.includes('shrink-0')
        && classes.includes('[container-type:inline-size]')
        && !classes.includes('invisible')
    })
    return !explicit && hasNewTaskGlyph(control) && inVisibleCollapsedHeader
      && hasAncestor(control, (node) => node.tagName === 'HEADER')
  }
  const nativeControlLabel = (control) => {
    const explicit = semanticLabel(control)
    if (isVerifiedLabel(explicit)) return explicit
    if (isSidebarRow(control, explicit) || isCollapsedHeader(control, explicit)) return 'New task'
    return explicit
  }
  const nativeControlKind = (control) => isSidebarRow(control) ? 'sidebar-row'
    : isCollapsedHeader(control) ? 'collapsed-header'
      : 'semantic'
  const isVerifiedLabel = (label) => {
    const normalized = normalize(label)
    return /^(?:new (?:chat|task|thread|conversation)|(?:start|create)(?: a)? new (?:chat|task|thread|conversation))$/.test(normalized)
      || /^(?:start|create)(?: a)? new (?:chat|task|thread|conversation) in [\p{L}\p{N}][\p{L}\p{N} .()_&'’+-]*$/u.test(normalized)
  }
  const isVerifiedControl = (control) => {
    if (!control?.matches?.('button,[role="button"],a[href]')) return false
    if (control.matches(':disabled,[aria-disabled="true"]')) return false
    return isVerifiedLabel(nativeControlLabel(control))
  }
  const findControls = () => [...document.querySelectorAll('button,[role="button"],a[href]')]
    .filter(isVerifiedControl)

  const consumeBootstrap = (controls) => {
    if (bootstrapConsumed) return false
    const controlLabel = bootstrap?.controlLabel
    const controlKind = bootstrap?.controlKind
    if (bootstrap?.mode !== 'native-new-chat' || typeof controlLabel !== 'string'
      || controlLabel !== cleanLabel(controlLabel) || controlLabel.length > 512
      || !isVerifiedLabel(controlLabel)
      || (controlKind !== undefined && !['semantic', 'sidebar-row', 'collapsed-header'].includes(controlKind))) {
      bootstrapConsumed = true
      return false
    }
    const generic = !/\s+in\s+/i.test(controlLabel)
    const matches = controls.filter((control) => nativeControlLabel(control) === controlLabel
      && (!generic || nativeControlKind(control) === controlKind))
    if (!bootstrapStarted) {
      if (matches.length === 0) return false
      if (matches.length !== 1) {
        bootstrapConsumed = true
        return false
      }
      bootstrapStarted = true
      matches[0].click()
      if (!generic) {
        bootstrapConsumed = true
        wingman.completeChild()
        return true
      }
    }
    if (!generic) return false
    const clearProjectControls = [...document.querySelectorAll('button')]
      .filter((control) => control.getAttribute('aria-label') === "Don't work in a project"
        && !control.matches(':disabled,[aria-disabled="true"]'))
    if (clearProjectControls.length === 0) return false
    bootstrapConsumed = true
    if (clearProjectControls.length !== 1) return false
    clearProjectControls[0].click()
    return true
  }
  const openChild = (control) => {
    const label = nativeControlLabel(control)
    if (!isVerifiedControl(control) || typeof wingman?.openChild !== 'function') return false
    const bootstrap = { mode: 'native-new-chat', controlLabel: label }
    if (!/\s+in\s+/i.test(label)) bootstrap.controlKind = nativeControlKind(control)
    wingman.openChild('/', bootstrap)
    return true
  }
  const onContextMenu = (event) => {
    const control = event.target?.closest?.('button,[role="button"],a[href]')
    if (!openChild(control)) return
    event.preventDefault()
    event.stopPropagation()
  }
  const mark = (control) => {
    if (marked.has(control)) return
    control.dataset.codexHelperContextMenu = owner
    marked.add(control)
  }
  const unmark = (control) => {
    if (control.dataset.codexHelperContextMenu === owner) delete control.dataset.codexHelperContextMenu
    marked.delete(control)
  }
  const reconcile = () => {
    const controls = findControls()
    const current = new Set(controls)
    for (const control of controls) mark(control)
    for (const control of [...marked]) if (!current.has(control) || !control.isConnected) unmark(control)
    consumeBootstrap(controls)
    if (bootstrapConsumed) {
      bootstrapObserver?.disconnect()
      bootstrapObserver = null
    }
    return { controlCount: marked.size, bootstrapStarted, bootstrapConsumed }
  }
  const scheduleReconcile = () => {
    if (pendingFrame) return
    pendingFrame = requestAnimationFrame(() => {
      pendingFrame = 0
      reconcile()
    })
  }
  const cleanup = () => {
    bootstrapObserver?.disconnect()
    bootstrapObserver = null
    if (pendingFrame) cancelAnimationFrame(pendingFrame)
    pendingFrame = 0
    document.removeEventListener('contextmenu', onContextMenu, true)
    for (const control of [...marked]) unmark(control)
    delete window[globalName]
    return true
  }

  window[globalName] = { version, cleanup, reconcile }
  document.addEventListener('contextmenu', onContextMenu, true)
  const result = reconcile()
  if (!bootstrapConsumed) {
    bootstrapObserver = new MutationObserver(scheduleReconcile)
    bootstrapObserver.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['aria-label', 'aria-labelledby', 'title', 'role', 'href', 'disabled', 'aria-disabled'],
    })
  }
  return result
})()
