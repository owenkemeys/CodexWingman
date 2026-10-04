(() => {
  const globalName = '__codexWingmanT3ClickableFileLinks'
  const version = 't3-file-links-v1'
  if (window.location?.protocol !== 't3code:') return false

  const mappings = (Array.isArray(helperConfig?.mappings) ? helperConfig.mappings : [])
    .filter((entry) => typeof entry?.sourcePrefix === 'string' && entry.sourcePrefix.startsWith('/')
      && entry.sourcePrefix.endsWith('/') && typeof entry?.windowsPrefix === 'string'
      && /^[A-Za-z]:\/$/.test(entry.windowsPrefix))
    .sort((left, right) => right.sourcePrefix.length - left.sourcePrefix.length)
  const exclusions = (Array.isArray(helperConfig?.excludePrefixes) ? helperConfig.excludePrefixes : [])
    .filter((value) => typeof value === 'string' && value.startsWith('/') && value.endsWith('/'))
  const configIdentity = JSON.stringify({ mappings, exclusions })
  const existing = window[globalName]
  if (existing?.version === version && existing.configIdentity === configIdentity) return existing.reconcile()
  try { existing?.cleanup?.() } catch {}

  const records = new Map()
  let observer = null
  let pendingFrame = 0
  let reconciling = false

  const decodeSuffix = (suffix) => {
    const segments = suffix.split('/')
    const decoded = []
    for (const segment of segments) {
      let value
      try { value = decodeURIComponent(segment) } catch { return null }
      if (!value || value === '.' || value === '..' || value.includes('/') || value.includes('\\')
        || /[<>:"|?*]/.test(value)
        || [...value].some((character) => character.charCodeAt(0) < 32)) return null
      decoded.push(value)
    }
    return decoded.join('/')
  }
  const destinationFor = (raw) => {
    if (typeof raw !== 'string' || !raw || raw.trim() !== raw || /[\r\n]/.test(raw)) return null
    if (exclusions.some((prefix) => raw.startsWith(prefix))) return null
    let candidate = raw
    if (/^\/[A-Za-z]:[\\/]/.test(candidate)) candidate = candidate.slice(1)
    if (/^[A-Za-z]:[\\/]/.test(candidate)) {
      const suffix = candidate.slice(3).replace(/\\/g, '/')
      const decoded = decodeSuffix(suffix)
      return decoded ? `${candidate.slice(0, 2).toUpperCase()}/${decoded}` : null
    }
    const mapping = mappings.find((entry) => raw.startsWith(entry.sourcePrefix))
    if (!mapping) return null
    const suffix = raw.slice(mapping.sourcePrefix.length)
    const decoded = decodeSuffix(suffix)
    return decoded ? mapping.windowsPrefix + decoded : null
  }
  const isT3MessageChip = (anchor) => Boolean(anchor.closest?.('[data-message-id][data-message-role]')
    ?.closest?.('[data-timeline-root]') && anchor.closest?.('.chat-markdown'))
  const removeRecord = (anchor, record) => {
    record.button.removeEventListener('click', record.clickHandler)
    record.button.remove()
    records.delete(anchor)
  }
  const addOpenButton = (anchor, destination) => {
    const button = document.createElement('button')
    button.setAttribute('type', 'button')
    button.setAttribute('data-codex-wingman-owner', 'clickable-file-links')
    button.setAttribute('data-clickable-file-links-t3-open', 'true')
    button.setAttribute('aria-label', 'Open file in Windows')
    button.setAttribute('title', 'Open in Windows app')
    button.textContent = '↗'
    const clickHandler = (event) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey
        || typeof wingman?.request !== 'function') return
      event.preventDefault()
      event.stopPropagation()
      wingman.request('system.openFilePath', { path: destination })
    }
    button.addEventListener('click', clickHandler)
    anchor.insertAdjacentElement('afterend', button)
    records.set(anchor, { button, clickHandler, destination })
  }
  const status = () => ({ version, linkCount: records.size, pending: Boolean(pendingFrame) })
  const reconcile = () => {
    if (reconciling) return status()
    reconciling = true
    try {
      for (const [anchor, record] of [...records]) {
        if (!anchor.isConnected || !record.button.isConnected || anchor.nextElementSibling !== record.button
          || anchor.hasAttribute('data-obsidian-links-link')
          || destinationFor(anchor.getAttribute('href')) !== record.destination) removeRecord(anchor, record)
      }
      for (const anchor of Array.from(document.querySelectorAll('a.chat-markdown-file-link'))) {
        if (!isT3MessageChip(anchor) || anchor.hasAttribute('data-obsidian-links-link') || records.has(anchor)) continue
        const destination = destinationFor(anchor.getAttribute('href'))
        if (destination) addOpenButton(anchor, destination)
      }
      return status()
    } finally { reconciling = false }
  }
  const cleanup = () => {
    observer?.disconnect()
    observer = null
    if (pendingFrame) cancelAnimationFrame(pendingFrame)
    pendingFrame = 0
    for (const [anchor, record] of [...records]) removeRecord(anchor, record)
    style.remove()
    if (window[globalName] === controller) delete window[globalName]
    return true
  }
  const controller = { version, configIdentity, cleanup, reconcile, status }
  const style = document.createElement('style')
  style.setAttribute('data-codex-wingman-owner', 'clickable-file-links')
  style.setAttribute('data-clickable-file-links-t3-style', 'true')
  style.textContent = '[data-clickable-file-links-t3-open="true"]{display:inline-grid;place-items:center;margin-inline-start:.2em;padding:0 .2em;border:0;background:transparent;color:inherit;font:inherit;opacity:.7;cursor:pointer}[data-clickable-file-links-t3-open="true"]:hover{opacity:1;text-decoration:underline}'
  document.head.appendChild(style)
  window[globalName] = controller
  reconcile()
  observer = new MutationObserver(() => {
    if (reconciling || pendingFrame) return
    pendingFrame = requestAnimationFrame(() => { pendingFrame = 0; reconcile() })
  })
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['href', 'data-obsidian-links-link'], childList: true, subtree: true })
  return status()
})()
