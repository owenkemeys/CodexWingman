(() => {
  const owner = 'clickable-file-links'
  const globalName = '__codexWingmanClickableFileLinks'
  const version = 'clickable-file-links-v8'
  const controlSelector = '[data-file-reference="true"][data-prompt-link-href]'
  const excludedAncestorSelector = '[contenteditable="true"],[data-codex-composer="true"],[data-codex-wingman-owner]'

  const normalizeMappings = (config) => (Array.isArray(config?.mappings) ? config.mappings : [])
    .filter((entry) => typeof entry?.sourcePrefix === 'string' && entry.sourcePrefix.startsWith('/')
      && entry.sourcePrefix.endsWith('/') && typeof entry?.windowsPrefix === 'string'
      && /^[A-Za-z]:\/$/.test(entry.windowsPrefix))
    .sort((left, right) => right.sourcePrefix.length - left.sourcePrefix.length)
  const normalizeExclusions = (config) => (Array.isArray(config?.excludePrefixes) ? config.excludePrefixes : [])
    .filter((value) => typeof value === 'string' && value.startsWith('/') && value.endsWith('/'))
  const mappings = normalizeMappings(helperConfig)
  const exclusions = normalizeExclusions(helperConfig)
  const configIdentity = JSON.stringify({ mappings, exclusions })
  const existing = window[globalName]
  if (existing?.version === version && existing.configIdentity === configIdentity) return existing.reconcile()
  try { existing?.cleanup?.() } catch {}

  const records = new Map()
  let observer = null
  let pendingFrame = 0
  let reconciling = false

  const originalAttributes = (element) => Array.from(element.attributes || []).map((entry) => {
    if (Array.isArray(entry)) return entry
    return [entry.name, entry.value]
  })
  const restoreAttributes = (element, attributes) => {
    for (const entry of Array.from(element.attributes || [])) {
      element.removeAttribute(Array.isArray(entry) ? entry[0] : entry.name)
    }
    for (const [name, value] of attributes) element.setAttribute(name, value)
  }
  const replaceExactPath = (value, raw, destination, depth = 0, seen = new WeakMap()) => {
    if (value === raw || value === `/${destination}`) return { value: destination, changed: true }
    if (!value || typeof value !== 'object' || depth > 5) return { value, changed: false }
    if (seen.has(value)) return { value: seen.get(value), changed: false }
    const clone = Array.isArray(value)
      ? value.slice()
      : Object.assign(Object.create(Object.getPrototypeOf(value)), value)
    seen.set(value, clone)
    let changed = false
    for (const key of Object.keys(value)) {
      let next
      try { next = replaceExactPath(value[key], raw, destination, depth + 1, seen) } catch { continue }
      if (!next.changed) continue
      try { clone[key] = next.value; changed = true } catch {}
    }
    return { value: changed ? clone : value, changed }
  }
  const patchReactSlot = (record, target, key) => {
    if (!target || !(key in target)) return false
    let current
    try { current = target[key] } catch { return false }
    const next = replaceExactPath(current, record.raw, record.destination)
    if (!next.changed) return false
    let snapshot = record.reactSlots.find((entry) => entry.target === target && entry.key === key)
    if (!snapshot) {
      snapshot = { target, key, original: current, patched: next.value }
      record.reactSlots.push(snapshot)
    } else {
      snapshot.patched = next.value
    }
    try { target[key] = next.value } catch { return false }
    return true
  }
  const patchReactState = (control, record) => {
    let changed = false
    let fiber = null
    for (const key of Object.getOwnPropertyNames(control)) {
      if (key.startsWith('__reactProps$')) changed = patchReactSlot(record, control, key) || changed
      if (key.startsWith('__reactFiber$')) {
        try { fiber = control[key] } catch {}
      }
    }
    const seenFibers = new Set()
    let depth = 0
    while (fiber && depth++ < 12) {
      for (const candidate of [fiber, fiber.alternate]) {
        if (!candidate || seenFibers.has(candidate)) continue
        seenFibers.add(candidate)
        changed = patchReactSlot(record, candidate, 'memoizedProps') || changed
        changed = patchReactSlot(record, candidate, 'pendingProps') || changed
      }
      fiber = fiber.return
    }
    return changed
  }
  const restoreReactState = (record) => {
    for (const slot of record.reactSlots) {
      try {
        if (slot.target[slot.key] === slot.patched) slot.target[slot.key] = slot.original
      } catch {}
    }
    record.reactSlots.length = 0
  }
  const bindOpen = (control, destination) => {
    const clickHandler = (event) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey
        || typeof wingman?.request !== 'function') return
      event.preventDefault()
      event.stopImmediatePropagation?.()
      event.stopPropagation()
      wingman.request('system.openFilePath', { path: destination })
    }
    control.addEventListener('click', clickHandler, true)
    return clickHandler
  }
  const decodePathSuffix = (suffix) => {
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

  const parseWindowsDestination = (raw) => {
    let candidate = raw
    if (/^\/[A-Za-z]:[\\/]/.test(candidate)) candidate = candidate.slice(1)
    if (!/^[A-Za-z]:[\\/]/.test(candidate)) return null
    const drive = candidate.slice(0, 2).toUpperCase()
    const suffix = candidate.slice(3)
    if (!suffix) return { raw, destination: `${drive}/` }
    if (suffix.startsWith('/') || suffix.startsWith('\\')) return null
    const decoded = decodePathSuffix(suffix.replace(/\\/g, '/'))
    if (!decoded) return null
    return { raw, destination: `${drive}/${decoded}` }
  }

  const parseDestination = (raw) => {
    if (typeof raw !== 'string' || !raw || raw.trim() !== raw
      || raw.includes('\n') || raw.includes('\r')) return null
    const windowsDestination = parseWindowsDestination(raw)
    if (windowsDestination) return windowsDestination
    if (exclusions.some((prefix) => raw.startsWith(prefix))) return null
    const mapping = mappings.find((entry) => raw.startsWith(entry.sourcePrefix))
    if (!mapping) return null
    const suffix = raw.slice(mapping.sourcePrefix.length)
    if (!suffix || suffix.startsWith('/') || suffix.startsWith('\\')) return null
    const decoded = decodePathSuffix(suffix)
    if (!decoded) return null
    return { raw, destination: mapping.windowsPrefix + decoded }
  }
  const isExcluded = (control) => Boolean(control.parentElement?.closest?.(excludedAncestorSelector))
  const transformControl = (control) => {
    const record = records.get(control)
    if (record) {
      let changed = false
      if ([record.raw, `/${record.destination}`].includes(control.getAttribute('data-prompt-link-href'))) {
        control.setAttribute('data-prompt-link-href', record.destination)
        changed = true
      }
      if ([record.raw, `/${record.destination}`].includes(control.getAttribute('title'))) {
        control.setAttribute('title', record.destination)
        changed = true
      }
      changed = patchReactState(control, record) || changed
      return changed
    }
    if (control.hasAttribute('data-clickable-file-links-native') || isExcluded(control)) return false
    const parsed = parseDestination(control.getAttribute('data-prompt-link-href'))
    if (!parsed) return false
    const attributes = originalAttributes(control)
    const nextRecord = { attributes, raw: parsed.raw, destination: parsed.destination, reactSlots: [], clickHandler: null }
    records.set(control, nextRecord)
    control.setAttribute('data-codex-wingman-owner', owner)
    control.setAttribute('data-clickable-file-links-native', 'true')
    control.setAttribute('data-clickable-file-links-original-attributes', JSON.stringify(attributes))
    control.setAttribute('data-clickable-file-links-raw-path', parsed.raw)
    control.setAttribute('data-prompt-link-href', parsed.destination)
    if (control.getAttribute('title') === parsed.raw) control.setAttribute('title', parsed.destination)
    patchReactState(control, nextRecord)
    nextRecord.clickHandler = bindOpen(control, parsed.destination)
    return true
  }
  const status = () => ({
    version,
    linkCount: document.querySelectorAll('[data-clickable-file-links-native="true"]').length,
    pending: Boolean(pendingFrame),
  })
  const reconcile = () => {
    if (reconciling) return status()
    reconciling = true
    try {
      for (const [control] of [...records]) if (!control.isConnected) records.delete(control)
      for (const control of Array.from(document.querySelectorAll(controlSelector))) transformControl(control)
      return status()
    } finally {
      reconciling = false
    }
  }
  const cleanup = () => {
    observer?.disconnect()
    observer = null
    if (pendingFrame) cancelAnimationFrame(pendingFrame)
    pendingFrame = 0
    for (const [control, record] of [...records]) {
      if (record.clickHandler) control.removeEventListener('click', record.clickHandler, true)
      restoreReactState(record)
      if (control.isConnected) restoreAttributes(control, record.attributes)
    }
    records.clear()
    if (window[globalName] === controller) delete window[globalName]
    return true
  }
  const controller = { version, configIdentity, cleanup, reconcile, status }
  window[globalName] = controller
  reconcile()
  observer = new MutationObserver(() => {
    if (reconciling || pendingFrame) return
    pendingFrame = requestAnimationFrame(() => {
      pendingFrame = 0
      reconcile()
    })
  })
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-prompt-link-href', 'title'],
    childList: true,
    subtree: true,
  })
  return status()
})()
