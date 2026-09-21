(() => {
  const owner = 'obsidian-links'
  const globalName = '__codexWingmanObsidianLinks'
  const version = 'message-links-v12'
  const normalizeMappings = (config) => {
    const supplied = Array.isArray(config?.mappings)
      ? config.mappings
      : typeof config?.pathPrefix === 'string'
        ? [{ sourcePrefix: config.pathPrefix }]
        : []
    const seen = new Set()
    const mappings = []
    for (const entry of supplied) {
      const sourcePrefix = entry?.sourcePrefix
      const vault = entry?.vault
      const isWindowsPath = /^[A-Za-z]:[\\/]/.test(sourcePrefix || '')
      if (typeof sourcePrefix !== 'string' || !sourcePrefix || !/^(\/|[A-Za-z]:[\\/]|\\\\)/.test(sourcePrefix) || !/[\\/]$/.test(sourcePrefix)
        || (vault !== undefined && (typeof vault !== 'string' || !vault || /[\\/]/.test(vault)))) continue
      const matchPrefix = isWindowsPath ? sourcePrefix.replace(/\\/g, '/').toLowerCase() : sourcePrefix
      const identity = `${matchPrefix}\u0000${vault || ''}`
      if (seen.has(identity)) continue
      seen.add(identity)
      mappings.push({ sourcePrefix, matchPrefix, vault: vault || null, isWindowsPath })
    }
    return mappings.sort((left, right) => right.sourcePrefix.length - left.sourcePrefix.length)
  }
  const mappings = normalizeMappings(helperConfig)
  const action = helperConfig?.uriAction === 'open' ? 'open' : 'wait-for-note'
  const configIdentity = JSON.stringify({ action, mappings: mappings.map(({ sourcePrefix, vault }) => ({ sourcePrefix, vault })) })
  const existing = window[globalName]
  if (!mappings.length) {
    if (existing) {
      try { existing.cleanup?.() } catch {}
    }
    return false
  }
  if (existing?.version === version && existing.configIdentity === configIdentity) return existing.reconcile()
  if (existing) {
    try { existing.cleanup?.() } catch {}
  }

  const rootSelector = [
    '[data-user-message-bubble="true"]',
    '[data-message-author="user"]',
    '[data-author="user"]',
    '[data-testid*="user-message" i]',
    '[data-local-conversation-final-assistant="true"]',
    '[data-content-search-unit-key$=":assistant"]',
    '[data-message-author="assistant"]',
    '[data-author="assistant"]',
    '[data-testid*="assistant-message" i]',
  ].join(',')
  const excludedSelector = [
    'code', 'pre', 'script', 'style', 'textarea', 'input', 'button',
    '[contenteditable="true"]', '[data-codex-composer="true"]',
    '[data-codex-wingman-owner]',
  ].join(',')
  const records = new Map()
  const settledTextNodes = new WeakSet()
  let iconSequence = 0
  let pendingFrame = 0
  let observer = null
  let reconciling = false

  const decode = (value) => {
    try { return decodeURIComponent(value) } catch { return null }
  }
  const mappingFor = (raw) => mappings.find((mapping) => {
    if (typeof raw !== 'string') return false
    const candidate = raw.slice(0, mapping.sourcePrefix.length)
    return mapping.isWindowsPath
      ? candidate.replace(/\\/g, '/').toLowerCase() === mapping.matchPrefix
      : candidate === mapping.sourcePrefix
  }) || null
  const indexOfMapping = (value, mapping, from) => mapping.isWindowsPath
    ? value.replace(/\\/g, '/').toLowerCase().indexOf(mapping.matchPrefix, from)
    : value.indexOf(mapping.sourcePrefix, from)
  const findPrefix = (value, from = 0) => {
    let match = null
    for (const mapping of mappings) {
      const index = indexOfMapping(value, mapping, from)
      if (index < 0 || (match && index > match.index) || (match && index === match.index && mapping.sourcePrefix.length <= match.mapping.sourcePrefix.length)) continue
      match = { index, mapping }
    }
    return match
  }
  const parseDestination = (raw) => {
    const mapping = mappingFor(raw)
    if (!mapping || raw.includes('?')) return null
    const suffix = raw.slice(mapping.sourcePrefix.length)
    const hashIndex = suffix.indexOf('#')
    const pathPart = hashIndex < 0 ? suffix : suffix.slice(0, hashIndex)
    const isFolderPath = hashIndex < 0 && /[\\/]$/.test(pathPart)
    const fragmentPart = hashIndex < 0 ? '' : suffix.slice(hashIndex + 1)
    const rawComponents = pathPart.split(/[\\/]/)
    if (isFolderPath) rawComponents.pop()
    if (rawComponents.some((part) => !part)) return null
    const components = rawComponents.map(decode)
    if (components.some((part) => part === null || !part || part.includes('/'))) return null
    const vault = mapping.vault || components.shift()
    if (!vault || !components.length) return null
    const noteWithExtension = isFolderPath
      ? [...components, `${components.at(-1)}.md`].join('/')
      : components.join('/')
    if (!noteWithExtension.endsWith('.md') || noteWithExtension === '.md') return null
    const note = noteWithExtension.slice(0, -3)
    const decodedFragment = fragmentPart ? decode(fragmentPart) : ''
    if (fragmentPart && (!decodedFragment || decodedFragment.includes('#'))) return null
    const isBlock = decodedFragment.startsWith('^')
    if (isBlock && decodedFragment.length === 1) return null
    const file = noteWithExtension + (decodedFragment ? `#${decodedFragment}` : '')
    const basename = note.split('/').pop()
    const suffixLabel = decodedFragment ? ` - ${isBlock ? '^' + decodedFragment.slice(1) : decodedFragment}` : ''
    return {
      raw,
      href: `obsidian://${action}?vault=${encodeURIComponent(vault)}&file=${encodeURIComponent(file)}`,
      label: `Open ${basename}${suffixLabel}`,
    }
  }
  const childNodes = (node) => Array.from(node?.childNodes || node?.children || [])
  const originalAttributes = (element) => Array.from(element.attributes || []).map((entry) => {
    if (Array.isArray(entry)) return entry
    return [entry.name, entry.value]
  })
  const restoreAttributes = (element, attributes) => {
    for (const entry of Array.from(element.attributes || [])) {
      const name = Array.isArray(entry) ? entry[0] : entry.name
      element.removeAttribute(name)
    }
    for (const [name, value] of attributes) element.setAttribute(name, value)
  }
  const mark = (element, kind) => {
    element.setAttribute('data-codex-wingman-owner', owner)
    element.setAttribute(`data-obsidian-links-${kind}`, 'true')
    return element
  }
  const svgNamespace = 'http://www.w3.org/2000/svg'
  const officialPaths = [
    ['M324.709 475.437C321.564 498.795 298.704 517.043 275.987 510.743C243.618 501.826 206.139 487.918 172.416 485.326C167.97 484.984 120.732 481.403 120.732 481.403C112.376 480.808 104.533 477.157 98.6965 471.148L9.63858 379.448C-0.0941202 369.428 -2.72861 354.484 2.99039 341.74C2.99039 341.74 58.0589 220.728 60.1042 214.435C62.1495 208.142 69.6555 153.256 74.1035 123.77C75.2825 115.956 79.1405 108.793 85.0165 103.508L190.369 8.74822C205.027 -4.43479 227.778 -2.4812 239.972 13.0074L328.473 125.419C333.48 131.781 336.047 139.688 336.084 147.783C336.185 169.08 337.943 212.805 349.719 240.968C361.175 268.361 382.2 297.946 393.189 312.504C397.405 318.091 398.052 325.646 394.489 331.673C386.734 344.801 371.412 370.009 349.719 402.351C334.764 424.651 327.834 452.218 324.709 475.437Z', '#6C31E3'],
    ['M108.293 478.079C149.651 394.115 148.498 333.963 130.899 291.034C114.702 251.53 84.578 226.613 60.834 211.149C60.334 213.383 59.609 215.564 58.67 217.658L2.98893 341.74C-2.73008 354.484 -0.0955851 369.428 9.63712 379.448L98.695 471.148C101.489 474.024 104.743 476.361 108.293 478.079Z', 0],
    ['M275.998 510.731C298.71 517.031 321.567 498.783 324.712 475.424C327.42 455.314 332.979 431.94 344.136 411.554C318.538 356.452 287.582 327.877 253.647 315.212C217.726 301.805 178.467 306.225 138.689 315.885C147.581 356.353 142.258 409.204 108.34 478.072C112.202 479.942 116.413 481.081 120.765 481.391C120.765 481.391 145.24 483.452 174.348 485.512C203.456 487.572 246.772 502.626 275.998 510.731Z', 1],
    ['M220.844 307.659C232.021 308.826 242.974 311.235 253.636 315.213C287.577 327.879 318.539 356.455 344.142 411.553C345.863 408.408 347.719 405.333 349.719 402.351C371.411 370.009 386.733 344.801 394.488 331.673C398.051 325.646 397.404 318.091 393.187 312.504C382.199 297.946 361.174 268.361 349.719 240.968C337.942 212.805 336.184 169.08 336.084 147.783C336.046 139.688 333.479 131.781 328.472 125.419L239.971 13.0073C239.498 12.4062 239.008 11.8256 238.504 11.2654C244.998 32.5466 244.558 49.6659 240.552 65.2287C236.838 79.6557 230.058 92.7451 222.897 106.571L222.896 106.573C220.493 111.21 218.049 115.93 215.662 120.812C206.161 140.247 197.582 162.241 196.317 191.734C195.051 221.228 201.097 258.22 220.847 307.658L220.844 307.659Z', 2],
    ['M220.832 307.658C201.084 258.221 195.035 221.228 196.301 191.733C197.567 162.238 206.146 140.244 215.649 120.807C218.036 115.924 220.481 111.204 222.884 106.566C230.044 92.7399 236.824 79.6517 240.537 65.226C244.544 49.6597 244.984 32.5357 238.484 11.2456C225.999 -2.61128 204.446 -3.91716 190.365 8.74822L85.0122 103.508C79.1362 108.793 75.2782 115.956 74.0992 123.77L61.2742 208.786C61.1552 209.575 61.0082 210.359 60.8342 211.138C84.5792 226.601 114.708 251.521 130.906 291.03C134.07 298.748 136.702 307.019 138.652 315.888C166.627 309.094 194.347 304.893 220.832 307.658Z', 3],
    ['M196.508 189.79C195.238 219.048 198.89 252.61 218.598 301.944L212.409 301.386C194.728 249.904 190.88 223.509 192.168 193.847C193.456 164.17 203.045 141.35 212.601 121.884C215.021 116.954 220.668 107.696 223.085 103.049C230.24 89.2908 235.002 82.0228 239.09 69.4468C244.804 51.8747 243.568 43.5519 242.917 35.2692C247.453 65.2105 230.234 91.2462 217.217 117.763C207.734 137.08 197.777 160.547 196.508 189.79Z', 4, true],
    ['M136.726 293.21C139.063 298.615 141.271 302.979 142.665 309.667L137.501 310.828C135.352 303.019 133.692 297.463 130.718 290.765C112.922 248.78 84.362 227.184 61.022 211.344C89.2138 226.511 118.149 250.251 136.726 293.21Z', 5, true],
    ['M142.965 314.949C152.827 360.838 141.826 419.137 109.406 475.807C136.504 419.644 149.641 365.703 138.7 315.864L142.965 314.949Z', 6, true],
    ['M254.86 310.821C308.014 330.713 328.48 374.389 343.778 410.822C324.885 372.671 298.618 330.538 252.953 314.9C218.207 303 188.862 304.411 138.697 315.796L137.579 310.821C190.819 298.691 218.655 297.272 254.86 310.821Z', 7, true],
  ]
  const officialGradients = [
    ['translate(103.845 469.791) rotate(-104.574) scale(232.965 155.247)', [['', 'white', '0.4'], ['1', '', '0.1']]],
    ['matrix(-96.2576 -163.001 187.145 -110.545 277.685 511.988)', [['', 'white', '0.3'], ['1', '', '0.25']]],
    ['translate(302.401 374) rotate(-82.4846) scale(382.284 282.434)', [['', 'white', '0.55'], ['1', 'white', '0.05']]],
    ['translate(117.805 306.884) rotate(-77.7214) scale(326.45 222.631)', [['', 'white', '0.83'], ['1', 'white', '0.4']]],
    ['translate(252.4 128) rotate(102.236) scale(169.859 114.542)', [['', 'white', '0'], ['1', 'white', '0.17']]],
    ['translate(53.399 220) rotate(45.3237) scale(125.16 266.579)', [['', 'white', '0.2'], ['1', 'white', '0.44']]],
    ['translate(147.891 279.224) rotate(80.2016) scale(146.696 311.515)', [['', 'white', '0.25'], ['1', 'white', '0.3']]],
    ['translate(342.401 398.999) rotate(-152.297) scale(223.528 703.43)', [['', 'white', '0.21'], ['0.46738', 'white', '0.19'], ['1', 'white', '0.29']]],
  ]
  const makeIcon = () => {
    const icon = mark(document.createElementNS(svgNamespace, 'svg'), 'icon')
    icon.setAttribute('aria-hidden', 'true')
    icon.setAttribute('viewBox', '0 0 397 512')
    icon.setAttribute('preserveAspectRatio', 'xMidYMid meet')
    icon.setAttribute('focusable', 'false')
    const instanceId = `obsidian-links-icon-${++iconSequence}`
    const defs = mark(document.createElementNS(svgNamespace, 'defs'), 'icon-defs')
    officialGradients.forEach(([transform, stops], index) => {
      const gradient = mark(document.createElementNS(svgNamespace, 'radialGradient'), 'gradient')
      gradient.setAttribute('id', `${instanceId}-paint${index}`)
      gradient.setAttribute('data-obsidian-links-gradient-index', String(index))
      gradient.setAttribute('cx', '0')
      gradient.setAttribute('cy', '0')
      gradient.setAttribute('r', '1')
      gradient.setAttribute('gradientUnits', 'userSpaceOnUse')
      gradient.setAttribute('gradientTransform', transform)
      for (const [offset, color, opacity] of stops) {
        const stop = document.createElementNS(svgNamespace, 'stop')
        if (offset) stop.setAttribute('offset', offset)
        if (color) stop.setAttribute('stop-color', color)
        stop.setAttribute('stop-opacity', opacity)
        gradient.appendChild(stop)
      }
      defs.appendChild(gradient)
    })
    icon.appendChild(defs)
    officialPaths.forEach(([d, fill, rule]) => {
      const path = mark(document.createElementNS(svgNamespace, 'path'), 'icon-path')
      path.setAttribute('data-obsidian-links-official-path', 'true')
      path.setAttribute('d', d)
      path.setAttribute('fill', typeof fill === 'number' ? `url(#${instanceId}-paint${fill})` : fill)
      if (rule) {
        path.setAttribute('fill-rule', 'evenodd')
        path.setAttribute('clip-rule', 'evenodd')
      }
      icon.appendChild(path)
    })
    return icon
  }
  const copyRawPath = (raw) => {
    try {
      const result = navigator?.clipboard?.writeText?.(raw)
      result?.catch?.(() => {})
    } catch {}
  }
  const makeCopy = (raw) => {
    const copy = mark(document.createElement('button'), 'copy')
    copy.setAttribute('type', 'button')
    copy.setAttribute('aria-label', 'Copy Obsidian source path')
    copy.setAttribute('title', raw)
    copy.setAttribute('data-obsidian-links-raw-path', raw)
    copy.textContent = '⧉'
    copy.addEventListener('click', () => copyRawPath(raw))
    return copy
  }
  const bindOpen = (anchor, href) => {
    const clickHandler = (event) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey
        || typeof wingman?.request !== 'function') return
      event.preventDefault()
      event.stopPropagation()
      wingman.request('system.openObsidianUri', { uri: href })
    }
    anchor.addEventListener('click', clickHandler)
    return clickHandler
  }
  const isRawLabel = (anchor, raw) => anchor.textContent.trim() === raw
  const transformAnchor = (anchor) => {
    if (records.has(anchor) || anchor.hasAttribute('data-obsidian-links-link')) return false
    const parsed = parseDestination(anchor.getAttribute('href'))
    if (!parsed) return false
    const attributes = originalAttributes(anchor)
    const nodes = childNodes(anchor)
    const text = anchor.textContent
    const rawLabel = isRawLabel(anchor, parsed.raw)
    const copy = makeCopy(parsed.raw)
    let stash = null
    if (rawLabel) {
      stash = mark(document.createElement('span'), 'original-label')
      stash.setAttribute('hidden', '')
      stash.setAttribute('aria-hidden', 'true')
      if (nodes.length) stash.append(...nodes)
      else stash.appendChild(document.createTextNode(text))
    }
    const clickHandler = bindOpen(anchor, parsed.href)
    records.set(anchor, { type: 'anchor', attributes, nodes, text, rawLabel, copy, stash, clickHandler })
    mark(anchor, 'link')
    anchor.setAttribute('href', parsed.href)
    anchor.setAttribute('data-obsidian-links-raw-path', parsed.raw)
    anchor.setAttribute('data-obsidian-links-original-attributes', JSON.stringify(attributes))
    anchor.setAttribute('data-obsidian-links-raw-label', rawLabel ? 'true' : 'false')
    if (rawLabel) anchor.replaceChildren(makeIcon(), document.createTextNode(parsed.label), stash)
    else anchor.replaceChildren(makeIcon(), ...nodes)
    anchor.insertAdjacentElement('afterend', copy)
    return true
  }
  const transformPromptLink = (control) => {
    if (records.has(control) || control.hasAttribute('data-obsidian-links-link')) return false
    const parsed = parseDestination(control.getAttribute('data-prompt-link-href'))
    if (!parsed) return false
    const attributes = originalAttributes(control)
    const copy = makeCopy(parsed.raw)
    const nativeIcon = control.querySelector('svg')
    const iconHost = nativeIcon?.parentNode || null
    let iconStash = null
    let appendedIcon = null
    if (iconHost) {
      iconStash = mark(document.createElement('span'), 'native-icon-stash')
      iconStash.setAttribute('hidden', '')
      iconStash.setAttribute('aria-hidden', 'true')
      iconStash.append(...childNodes(iconHost))
      iconHost.append(makeIcon(), iconStash)
    } else {
      appendedIcon = makeIcon()
      control.appendChild(appendedIcon)
    }
    const clickHandler = bindOpen(control, parsed.href)
    records.set(control, { type: 'prompt-link', attributes, copy, clickHandler, iconHost, iconStash, appendedIcon })
    mark(control, 'link')
    control.setAttribute('data-prompt-link-href', parsed.href)
    control.setAttribute('data-obsidian-links-raw-path', parsed.raw)
    control.setAttribute('data-obsidian-links-original-attributes', JSON.stringify(attributes))
    control.insertAdjacentElement('afterend', copy)
    return true
  }
  const isPlainMarkdownBoundary = (value, end) => {
    const next = value[end]
    if (next === undefined || next === '#' || /\s/.test(next)) return true
    if (next === '.') return end + 1 === value.length || /\s/.test(value[end + 1] || '')
    return /[,;:!\)\]\}]/.test(next)
  }
  const isConservativePlainFragment = (raw) => {
    const hashIndex = raw.indexOf('#')
    if (hashIndex < 0) return true
    const fragment = decode(raw.slice(hashIndex + 1))
    if (!fragment || /[.!?,;:]$/.test(fragment)) return false
    return !fragment.startsWith('^') || !/\s/.test(fragment)
  }
  const findWholeLineFolderMatch = (value, start) => {
    const lineStart = value.lastIndexOf('\n', start - 1) + 1
    const newline = value.indexOf('\n', start)
    const lineEnd = newline < 0 ? value.length : newline
    const trimmedStart = lineStart + value.slice(lineStart, lineEnd).search(/\S/)
    const trailingWhitespace = value.slice(lineStart, lineEnd).match(/\s*$/)?.[0].length || 0
    const trimmedEnd = lineEnd - trailingWhitespace
    if (trimmedStart !== start) return null
    const raw = value.slice(start, trimmedEnd)
    if (!/[\\/]$/.test(raw)) return null
    const parsed = parseDestination(raw)
    return parsed ? { start, end: trimmedEnd, parsed } : null
  }
  const findPlainMatch = (value, from = 0) => {
    const prefixMatch = findPrefix(value, from)
    if (!prefixMatch) return null
    const { index: start, mapping } = prefixMatch
    let markdownEnd = value.indexOf('.md', start + mapping.sourcePrefix.length)
    while (markdownEnd >= 0 && !isPlainMarkdownBoundary(value, markdownEnd + 3)) {
      const nextPrefix = findPrefix(value, markdownEnd + 3)?.index ?? -1
      const nextMarkdown = value.indexOf('.md', markdownEnd + 3)
      if (nextPrefix >= 0 && (nextMarkdown < 0 || nextPrefix < nextMarkdown)) return findPlainMatch(value, nextPrefix)
      markdownEnd = nextMarkdown
    }
    if (markdownEnd < 0) return findWholeLineFolderMatch(value, start)
      || findPlainMatch(value, start + mapping.sourcePrefix.length)
    let end = markdownEnd + 3
    if (value[end] === '#') {
      const lineStart = value.lastIndexOf('\n', start - 1) + 1
      const newline = value.indexOf('\n', end)
      const lineEnd = newline < 0 ? value.length : newline
      const trimmedStart = lineStart + value.slice(lineStart, lineEnd).search(/\S/)
      const trailingWhitespace = value.slice(lineStart, lineEnd).match(/\s*$/)?.[0].length || 0
      const trimmedEnd = lineEnd - trailingWhitespace
      const nextPath = findPrefix(value, end)?.index ?? -1
      if (trimmedStart !== start || (nextPath >= 0 && nextPath < trimmedEnd)) return findPlainMatch(value, end)
      end = trimmedEnd
    }
    const raw = value.slice(start, end)
    if (!isConservativePlainFragment(raw)) return findPlainMatch(value, start + mapping.sourcePrefix.length)
    const parsed = parseDestination(raw)
    return parsed ? { start, end, parsed } : findPlainMatch(value, start + mapping.sourcePrefix.length)
  }
  const makeGeneratedWrapper = (parsed) => {
    const wrapper = mark(document.createElement('span'), 'generated')
    wrapper.setAttribute('data-obsidian-links-raw-text', parsed.raw)
    const anchor = mark(document.createElement('a'), 'link')
    anchor.setAttribute('href', parsed.href)
    anchor.setAttribute('data-obsidian-links-raw-path', parsed.raw)
    anchor.setAttribute('data-obsidian-links-generated-link', 'true')
    anchor.append(makeIcon(), document.createTextNode(parsed.label))
    const clickHandler = bindOpen(anchor, parsed.href)
    wrapper.append(anchor, makeCopy(parsed.raw))
    records.set(wrapper, { type: 'generated', raw: parsed.raw, anchor, clickHandler })
    return wrapper
  }
  const replaceTextNode = (textNode) => {
    if (settledTextNodes.has(textNode)) return false
    const value = textNode.nodeValue
    if (typeof value !== 'string' || !findPrefix(value)) return false
    const parent = textNode.parentNode
    if (!parent) return false
    const replacements = []
    let cursor = 0
    let match = findPlainMatch(value)
    while (match) {
      if (match.start > cursor) {
        const settled = document.createTextNode(value.slice(cursor, match.start))
        settledTextNodes.add(settled)
        replacements.push(settled)
      }
      replacements.push(makeGeneratedWrapper(match.parsed))
      cursor = match.end
      match = findPlainMatch(value, cursor)
    }
    if (!replacements.length) return false
    if (cursor < value.length) {
      const settled = document.createTextNode(value.slice(cursor))
      settledTextNodes.add(settled)
      replacements.push(settled)
    }
    const siblings = childNodes(parent)
    const index = siblings.indexOf(textNode)
    if (index < 0) return false
    siblings.splice(index, 1, ...replacements)
    parent.replaceChildren(...siblings)
    return true
  }
  const isExcluded = (node, root) => {
    for (let element = node?.parentElement || node?.parentNode; element && element !== root; element = element.parentElement) {
      if (element.matches?.(excludedSelector) || element.tagName === 'A') return true
    }
    return false
  }
  const isAnchorExcluded = (anchor, root) => anchor.matches?.(excludedSelector) || isExcluded(anchor, root)
  const transformExactInlineCodeText = (textNode) => {
    const inlineCode = textNode.parentElement
    if (inlineCode?.getAttribute?.('data-markdown-copy') !== 'inline-code') return false
    if (childNodes(inlineCode).length !== 1 || typeof textNode.nodeValue !== 'string') return false
    const raw = textNode.nodeValue
    if (!raw || raw.trim() !== raw) return false
    const parsed = parseDestination(raw)
    if (!parsed) return false
    inlineCode.replaceChildren(makeGeneratedWrapper(parsed))
    return true
  }
  const transformText = (root) => {
    const visit = (node) => {
      for (const child of childNodes(node)) {
        if (child.nodeValue !== undefined && !child.tagName) {
          if (!isExcluded(child, root) && !transformExactInlineCodeText(child)) replaceTextNode(child)
        } else if (!child.matches?.(excludedSelector) && child.tagName !== 'A') visit(child)
      }
    }
    visit(root)
  }
  const messageRoots = () => {
    return Array.from(document.querySelectorAll(rootSelector))
      .filter((root) => !root.closest?.(excludedSelector))
  }
  const reconcile = () => {
    if (reconciling) return status()
    reconciling = true
    try {
      for (const [node] of [...records]) if (!node.isConnected) records.delete(node)
      for (const root of messageRoots()) {
        for (const control of Array.from(root.querySelectorAll('[data-file-reference="true"][data-prompt-link-href]'))) {
          if (!isAnchorExcluded(control, root)) transformPromptLink(control)
        }
        for (const anchor of Array.from(root.querySelectorAll('a'))) {
          if (!isAnchorExcluded(anchor, root)) transformAnchor(anchor)
        }
        transformText(root)
      }
      return status()
    } finally {
      reconciling = false
    }
  }
  const isOwnedMutation = (mutation) => {
    for (let node = mutation.target?.tagName ? mutation.target : mutation.target?.parentNode; node; node = node.parentElement) {
      if (node.matches?.('[data-codex-wingman-owner]')) return true
    }
    const changedNodes = [...Array.from(mutation.addedNodes || []), ...Array.from(mutation.removedNodes || [])]
    return changedNodes.length > 0 && changedNodes.every((changed) => {
      for (let node = changed?.tagName ? changed : changed?.parentNode; node; node = node.parentElement) {
        if (node.matches?.('[data-codex-wingman-owner]')) return true
      }
      return false
    })
  }
  const scheduleReconcile = (mutations) => {
    if (reconciling) return
    if (mutations?.every(isOwnedMutation)) return
    if (pendingFrame) return
    pendingFrame = requestAnimationFrame(() => {
      pendingFrame = 0
      reconcile()
    })
  }
  const restoreRecord = (node, record) => {
    record.anchor?.removeEventListener?.('click', record.clickHandler)
    if (!record.anchor) node.removeEventListener?.('click', record.clickHandler)
    if (record.type === 'generated') {
      if (node.isConnected) node.replaceWith(document.createTextNode(record.raw))
      return
    }
    record.copy?.remove()
    if (record.type === 'prompt-link') {
      if (record.iconHost && record.iconStash) {
        const originalIconNodes = childNodes(record.iconStash)
        record.iconHost.replaceChildren(...originalIconNodes)
      } else {
        record.appendedIcon?.remove()
      }
      restoreAttributes(node, record.attributes)
      return
    }
    if (record.rawLabel) {
      const originalNodes = childNodes(record.stash)
      node.replaceChildren(...(originalNodes.length ? originalNodes : [document.createTextNode(record.text)]))
    } else {
      for (const icon of Array.from(node.querySelectorAll('[data-obsidian-links-icon="true"]'))) icon.remove()
      if (record.nodes.length) node.replaceChildren(...record.nodes)
    }
    restoreAttributes(node, record.attributes)
  }
  const cleanup = () => {
    observer?.disconnect()
    observer = null
    if (pendingFrame) cancelAnimationFrame(pendingFrame)
    pendingFrame = 0
    for (const [node, record] of [...records]) restoreRecord(node, record)
    records.clear()
    for (const node of Array.from(document.querySelectorAll('[data-codex-wingman-owner="obsidian-links"]'))) node.remove()
    if (window[globalName] === controller) delete window[globalName]
    return true
  }
  const status = () => ({
    version,
    linkCount: document.querySelectorAll('[data-obsidian-links-link="true"]').length,
    pending: Boolean(pendingFrame),
  })
  const controller = { version, configIdentity, cleanup, reconcile, status }

  const style = mark(document.createElement('style'), 'style')
  style.textContent = [
    '[data-obsidian-links-link="true"]{display:inline-flex;align-items:baseline;gap:.25em}',
    '[data-obsidian-links-generated-link="true"]{color:var(--text-link,#70b7ff);font-weight:500;text-decoration:none}',
    '[data-obsidian-links-generated-link="true"]:hover{text-decoration:underline}',
    '[data-obsidian-links-icon="true"]{display:inline-block;width:.7em;height:.9em;flex:none}',
    '[data-obsidian-links-copy="true"]{display:inline-grid;place-items:center;width:1.25em;height:1.25em;padding:0;border:0;background:transparent;color:inherit;font:inherit;font-size:.8em;line-height:1;margin-inline-start:.2em;cursor:pointer}',
  ].join('')
  document.head.appendChild(style)
  window[globalName] = controller
  reconcile()
  observer = new MutationObserver(scheduleReconcile)
  observer.observe(document.documentElement, { childList: true, characterData: true, subtree: true })
  return status()
})()
