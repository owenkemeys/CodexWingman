(() => {
  const globalName = '__codexJsonDebug'
  const existing = window[globalName]
  if (existing) {
    existing.applyCount = (existing.applyCount || 1) + 1
    existing.refresh()
    return existing
  }

  const owner = 'json-debug'
  const targetId = wingman?.target?.id ?? '(unknown target)'
  const root = document.createElement('aside')
  root.dataset.codexHelper = owner
  root.dataset.codexJsonDebugRoot = 'true'
  root.dataset.codexJsonDebugInstance = `json-debug-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  root.setAttribute('aria-label', 'Codex JSON UI debug')
  root.style.position = 'fixed'
  root.style.top = '8px'
  root.style.right = '8px'
  root.style.zIndex = '2147483647'
  root.style.width = 'min(430px, calc(100vw - 16px))'
  root.style.maxHeight = 'calc(100vh - 16px)'
  root.style.overflow = 'auto'
  root.style.pointerEvents = 'none'
  root.style.boxSizing = 'border-box'
  root.style.padding = '8px'
  root.style.border = '1px solid #64748b'
  root.style.borderRadius = '8px'
  root.style.background = 'rgba(15, 23, 42, .94)'
  root.style.color = '#f8fafc'
  root.style.font = '12px/1.3 ui-monospace, SFMono-Regular, Consolas, monospace'
  root.style.textAlign = 'left'

  const style = document.createElement('style')
  style.dataset.codexJsonDebugStyle = owner
  style.textContent = '[data-codex-json-debug-root] * { box-sizing: border-box; }'
  document.documentElement.appendChild(style)
  document.body.appendChild(root)

  const canaries = [
    { key: 'sidebar-trigger', label: 'sidebar trigger', selector: '[data-app-shell-sidebar-trigger="true"], [aria-label*="sidebar" i]', color: '#f59e0b' },
    { key: 'composer-navigation', label: 'composer navigation', selector: 'nav, [data-testid*="composer" i], [aria-label*="conversation" i]', color: '#38bdf8' },
    { key: 'composer-input', label: 'composer input', selector: 'textarea, [contenteditable="true"], [data-testid*="composer" i], [aria-label*="message" i]', color: '#22d3ee' },
    { key: 'composer-actions', label: 'composer actions', selector: '[aria-label*="send" i], [aria-label*="attach" i], [aria-label*="microphone" i], [aria-label*="voice" i]', color: '#0ea5e9' },
    { key: 'model-picker', label: 'model picker candidates', selector: '[data-testid*="model" i], [aria-label*="model" i], [title*="model" i], [role="combobox"], [aria-haspopup="listbox"]', color: '#a78bfa' },
    { key: 'native-context-dials', label: 'native context dials', selector: '[role="img"][aria-label^="Context usage:"]', color: '#34d399' },
    { key: 'context-usage', label: 'context usage anchors', selector: '[role="img"][aria-label^="Context usage:"], [aria-label*="Context usage:" i], [data-testid*="context" i]', color: '#34d399' },
    { key: 'add-context', label: 'add-context controls', selector: '[aria-label*="add context" i], [data-testid*="context" i], [aria-label*="context" i]', color: '#2dd4bf' },
    { key: 'permissions-controls', label: 'permissions controls', selector: '[aria-label*="permission" i], [aria-label*="full access" i], [data-testid*="permission" i]', color: '#f97316' },
    { key: 'reasoning-controls', label: 'reasoning controls', selector: '[aria-label*="reasoning" i], [aria-label*="thinking" i], [data-testid*="reasoning" i]', color: '#c084fc' },
    { key: 'mode-controls', label: 'mode/intelligence controls', selector: '[aria-label*="fast" i], [aria-label*="intelligence" i], [aria-label*="extra high" i], [data-testid*="mode" i]', color: '#e879f9' },
    { key: 'new-chat', label: 'new-chat controls', selector: '[data-testid*="new-chat" i], [aria-label*="new chat" i], [title*="new chat" i]', color: '#fb7185' },
    { key: 'thread-surfaces', label: 'thread surfaces', selector: 'aside, [aria-label*="thread" i], [data-testid*="thread" i]', color: '#fbbf24' },
    { key: 'message-surfaces', label: 'message surfaces', selector: 'main, [role="main"], [data-testid*="message" i], [aria-label*="message" i]', color: '#4ade80' },
    { key: 'popovers', label: 'menus/dialogs/tooltips', selector: '[role="menu"], [role="dialog"], [role="listbox"], [role="tooltip"]', color: '#f472b6' },
    { key: 'window-actions', label: 'window actions', selector: '[aria-label*="close" i], [aria-label*="minimize" i], [aria-label*="maximize" i]', color: '#fb923c' },
    { key: 'visible-tooltips', label: 'visible tooltips', selector: '[role="tooltip"], [data-tooltip]', color: '#facc15' },
    { key: 'body-root', label: 'body/root', selector: 'body, #root, #__next, [data-reactroot]', color: '#e2e8f0' },
  ]
  const originalStyles = new Map()
  let refreshCount = 0
  let applyCount = 1
  let frame = 0
  let disposed = false

  const isOwned = (node) => root.contains(node) || node === root || style.contains?.(node)
  const restoreTargets = () => {
    for (const [node, previous] of originalStyles) {
      if (previous.outline === undefined) delete node.style.outline
      else node.style.outline = previous.outline
      if (previous.outlineOffset === undefined) delete node.style.outlineOffset
      else node.style.outlineOffset = previous.outlineOffset
    }
    originalStyles.clear()
  }
  const safeMatches = (selector) => {
    try { return [...document.querySelectorAll(selector)].filter((node) => !isOwned(node)) } catch { return [] }
  }
  const now = () => new Date().toISOString()
  const text = (value) => document.createTextNode(String(value ?? ''))
  const resultFor = (canary) => {
    const matches = safeMatches(canary.selector)
    for (const node of matches) {
      if (!originalStyles.has(node)) originalStyles.set(node, { outline: node.style.outline, outlineOffset: node.style.outlineOffset })
      node.style.outline = `2px solid ${canary.color}`
      node.style.outlineOffset = '1px'
    }
    return { key: canary.key, label: canary.label, selector: canary.selector, count: matches.length, status: matches.length ? 'FOUND' : 'MISSING', matches }
  }
  const render = (results) => {
    const legend = document.createElement('div')
    legend.dataset.codexJsonDebugLegend = 'true'
    legend.style.display = 'grid'
    legend.style.gap = '4px'
    legend.style.maxHeight = 'calc(100vh - 32px)'
    legend.style.overflow = 'auto'
    const heading = document.createElement('div')
    heading.style.fontWeight = '700'
    heading.append(text('JSON UI DEBUG'))
    const meta = document.createElement('div')
    meta.append(text(`URL: ${window.location?.href || '(renderer)'}`), text(` | Title: ${document.title || '(untitled)'}`), text(` | CDP target: ${targetId}`))
    const counts = document.createElement('div')
    counts.dataset.codexJsonDebugMeta = 'true'
    counts.append(text(`applies: ${api?.applyCount || applyCount} | refreshes: ${refreshCount} | last: ${root.dataset.codexJsonDebugLastRefresh}`))
    legend.append(heading, meta, counts)
    for (const result of results) {
      const row = document.createElement('div')
      row.dataset.codexJsonDebugCanary = result.key
      row.style.display = 'flex'
      row.style.justifyContent = 'space-between'
      row.style.gap = '8px'
      const label = document.createElement('span')
      label.append(text(`${result.label} (${result.count})`))
      const badge = document.createElement('span')
      badge.append(text(result.status === 'FOUND' ? `FOUND x${result.count}` : 'MISSING'))
      badge.style.color = result.status === 'FOUND' ? '#86efac' : '#fca5a5'
      row.append(label, badge)
      legend.appendChild(row)
    }
    root.replaceChildren(legend)
  }
  let results = []
  const refresh = () => {
    if (disposed) return api
    restoreTargets()
    refreshCount++
    root.dataset.codexJsonDebugCreatedAt ||= now()
    root.dataset.codexJsonDebugLastRefresh = now()
    results = canaries.map(resultFor)
    render(results)
    return api
  }
  const queueRefresh = () => {
    if (disposed || frame) return
    frame = requestAnimationFrame(() => { frame = 0; refresh() })
  }
  const observer = new MutationObserver((mutations) => {
    if (mutations.some((mutation) => !mutation.target.closest?.('[data-codex-json-debug-root]'))) queueRefresh()
  })
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['aria-label', 'title', 'role', 'id', 'class', 'data-app-shell-sidebar-trigger', 'data-testid', 'data-reactroot'],
    characterData: true,
  })
  const interval = setInterval(refresh, 2000)
  const getResults = () => results.map(({ matches, ...result }) => ({ ...result, refreshes: refreshCount }))
  const cleanup = () => {
    if (disposed) return true
    disposed = true
    observer.disconnect()
    clearInterval(interval)
    cancelAnimationFrame(frame)
    restoreTargets()
    root.remove()
    style.remove()
    delete window[globalName]
    return true
  }
  const api = { instanceId: root.dataset.codexJsonDebugInstance, targetId, refresh, getResults, cleanup, applyCount }
  window[globalName] = api
  refresh()
  return api
})()
