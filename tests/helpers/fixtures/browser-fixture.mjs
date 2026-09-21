import vm from 'node:vm'

function dataName(property) {
  return `data-${String(property).replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`)}`
}

class MiniEvent {
  constructor(type, init = {}) {
    this.type = type
    this.bubbles = Boolean(init.bubbles)
    this.cancelable = Boolean(init.cancelable)
    this.relatedTarget = init.relatedTarget ?? null
    this.pointerType = init.pointerType ?? ''
    this.clientX = init.clientX ?? 0
    this.clientY = init.clientY ?? 0
    this.key = init.key ?? ''
    this.shiftKey = Boolean(init.shiftKey)
    this.ctrlKey = Boolean(init.ctrlKey)
    this.metaKey = Boolean(init.metaKey)
    this.altKey = Boolean(init.altKey)
    this.button = init.button ?? 0
    this.defaultPrevented = false
    this.target = null
    this.currentTarget = null
    this._stopped = false
    this._immediate = false
  }

  preventDefault() {
    if (this.cancelable) this.defaultPrevented = true
  }

  stopPropagation() {
    this._stopped = true
  }

  stopImmediatePropagation() {
    this._immediate = true
    this._stopped = true
  }
}

class MiniEventTarget {
  constructor() {
    this._listeners = new Map()
  }

  addEventListener(type, callback, options = false) {
    const capture = options === true || Boolean(options?.capture)
    const listeners = this._listeners.get(type) ?? []
    listeners.push({ callback, capture })
    this._listeners.set(type, listeners)
  }

  removeEventListener(type, callback, options = false) {
    const capture = options === true || Boolean(options?.capture)
    const listeners = this._listeners.get(type) ?? []
    this._listeners.set(type, listeners.filter((item) => item.callback !== callback || item.capture !== capture))
  }

  _invoke(event, capture) {
    for (const listener of [...(this._listeners.get(event.type) ?? [])]) {
      if (listener.capture !== capture) continue
      event.currentTarget = this
      listener.callback.call(this, event)
      if (event._immediate) break
    }
  }

  dispatchEvent(event) {
    if (!(event instanceof MiniEvent)) throw new TypeError('fixture events must extend MiniEvent')
    event.target = this
    const ancestors = []
    for (let node = this.parentNode; node; node = node.parentNode) ancestors.push(node)
    for (const node of [...ancestors].reverse()) {
      node._invoke(event, true)
      if (event._stopped) return !event.defaultPrevented
    }
    this._invoke(event, true)
    if (!event._immediate) this._invoke(event, false)
    if (event.bubbles && !event._stopped) {
      for (const node of ancestors) {
        node._invoke(event, false)
        if (event._stopped) break
      }
    }
    return !event.defaultPrevented
  }
}

class MiniText extends MiniEventTarget {
  constructor(value, ownerDocument) {
    super()
    this.ownerDocument = ownerDocument
    this.parentNode = null
    this._text = String(value ?? '')
  }

  get nodeValue() {
    return this._text
  }

  set nodeValue(value) {
    this._text = String(value ?? '')
    this.ownerDocument?._notify(this, 'characterData')
  }

  get textContent() {
    return this._text
  }

  set textContent(value) {
    this.nodeValue = value
  }

  remove() {
    const parent = this.parentNode
    if (!parent) return
    parent.children = parent.children.filter((child) => child !== this)
    this.parentNode = null
    this.ownerDocument?._notify(parent, 'childList')
  }
}

function splitSelectors(selector) {
  const result = []
  let start = 0
  let brackets = 0
  for (let index = 0; index < selector.length; index++) {
    if (selector[index] === '[') brackets++
    else if (selector[index] === ']') brackets--
    else if (selector[index] === ',' && brackets === 0) {
      result.push(selector.slice(start, index).trim())
      start = index + 1
    }
  }
  result.push(selector.slice(start).trim())
  return result.filter(Boolean)
}

function matchesSimple(element, selector) {
  let remaining = selector.trim()
  if (remaining.includes(':disabled')) {
    if (!element.hasAttribute('disabled')) return false
    remaining = remaining.replace(':disabled', '')
  }
  const tag = remaining.match(/^[a-z][a-z0-9-]*/i)
  if (tag) {
    if (element.tagName !== tag[0].toUpperCase()) return false
    remaining = remaining.slice(tag[0].length)
  }
  const idMatches = [...remaining.matchAll(/#([a-z0-9_-]+)/gi)]
  if (idMatches.some((match) => element.id !== match[1])) return false
  const classMatches = [...remaining.matchAll(/\.([a-z0-9_-]+)/gi)]
  const classes = new Set((element.className || '').split(/\s+/).filter(Boolean))
  if (classMatches.some((match) => !classes.has(match[1]))) return false
  const attributePattern = /\[([^\]\s~|^$*!=]+)(?:\s*(\^=|\*=|=)\s*["']?([^\]"']*)["']?\s*(i)?)?\]/gi
  for (const match of remaining.matchAll(attributePattern)) {
    const [, name, operator, expectedRaw = '', insensitive] = match
    if (!element.hasAttribute(name)) return false
    if (!operator) continue
    let actual = element.getAttribute(name) ?? ''
    let expected = expectedRaw.trim()
    if (insensitive) {
      actual = actual.toLowerCase()
      expected = expected.toLowerCase()
    }
    if (operator === '=' && actual !== expected) return false
    if (operator === '^=' && !actual.startsWith(expected)) return false
    if (operator === '*=' && !actual.includes(expected)) return false
  }
  return true
}

class MiniElement extends MiniEventTarget {
  constructor(tagName, ownerDocument) {
    super()
    this.tagName = String(tagName).toUpperCase()
    this.ownerDocument = ownerDocument
    this.parentNode = null
    this.children = []
    this.attributes = new Map()
    this.style = {}
    this._text = ''
    this.dataset = new Proxy({}, {
      get: (_, property) => this.getAttribute(dataName(property)),
      set: (_, property, value) => {
        this.setAttribute(dataName(property), String(value))
        return true
      },
      deleteProperty: (_, property) => {
        this.removeAttribute(dataName(property))
        return true
      },
    })
  }

  get parentElement() {
    return this.parentNode instanceof MiniElement ? this.parentNode : null
  }

  get id() {
    return this.getAttribute('id') ?? ''
  }

  set id(value) {
    this.setAttribute('id', value)
  }

  get className() {
    return this.getAttribute('class') ?? ''
  }

  set className(value) {
    this.setAttribute('class', value)
  }

  get textContent() {
    return this.children.length ? this.children.map((child) => child.textContent).join('') : this._text
  }

  set textContent(value) {
    this.children = []
    this._text = String(value ?? '')
    this.ownerDocument?._notify(this, 'childList')
  }

  get innerText() {
    return this.textContent
  }

  set innerText(value) {
    this.textContent = value
  }

  get previousElementSibling() {
    const siblings = this.parentElement?.children ?? []
    const index = siblings.indexOf(this)
    return index > 0 ? siblings[index - 1] : null
  }

  get nextElementSibling() {
    const siblings = this.parentElement?.children ?? []
    const index = siblings.indexOf(this)
    return index >= 0 ? siblings[index + 1] ?? null : null
  }

  get isConnected() {
    for (let node = this; node; node = node.parentNode) if (node instanceof MiniDocument) return true
    return false
  }

  focus() {
    if (this.ownerDocument) this.ownerDocument.activeElement = this
  }

  click() {
    this.dispatchEvent(new MiniEvent('click', { bubbles: true, cancelable: true }))
  }

  getBoundingClientRect() {
    const rect = this._rect ?? this.ownerDocument?._rectResolver?.(this) ?? {}
    const x = rect.x ?? rect.left ?? 0
    const y = rect.y ?? rect.top ?? 0
    const width = rect.width ?? 0
    const height = rect.height ?? 0
    return {
      x,
      y,
      width,
      height,
      left: rect.left ?? x,
      top: rect.top ?? y,
      right: rect.right ?? x + width,
      bottom: rect.bottom ?? y + height,
    }
  }

  setAttribute(name, value) {
    this.attributes.set(String(name), String(value))
    this.ownerDocument?._notify(this, 'attributes', String(name))
  }

  getAttribute(name) {
    return this.attributes.has(String(name)) ? this.attributes.get(String(name)) : null
  }

  hasAttribute(name) {
    return this.attributes.has(String(name))
  }

  removeAttribute(name) {
    if (this.attributes.delete(String(name))) this.ownerDocument?._notify(this, 'attributes', String(name))
  }

  append(...nodes) {
    for (const node of nodes) this.appendChild(node)
  }

  appendChild(node) {
    if (node.parentNode) node.remove()
    node.parentNode = this
    this.children.push(node)
    this._text = ''
    this.ownerDocument?._notify(this, 'childList')
    return node
  }

  replaceChildren(...nodes) {
    for (const child of this.children) child.parentNode = null
    this.children = []
    this._text = ''
    for (const node of nodes) this.appendChild(node)
    this.ownerDocument?._notify(this, 'childList')
  }

  insertAdjacentElement(position, node) {
    if (position !== 'afterend' || !this.parentElement) throw new Error(`unsupported position ${position}`)
    if (node.parentNode) node.remove()
    const index = this.parentElement.children.indexOf(this)
    node.parentNode = this.parentElement
    this.parentElement.children.splice(index + 1, 0, node)
    this.ownerDocument?._notify(this.parentElement, 'childList')
    return node
  }

  replaceWith(node) {
    if (!this.parentElement) return
    const parent = this.parentElement
    const index = parent.children.indexOf(this)
    this.parentNode = null
    if (node.parentNode) node.remove()
    node.parentNode = parent
    parent.children[index] = node
    this.ownerDocument?._notify(parent, 'childList')
  }

  remove() {
    if (!this.parentElement) return
    const parent = this.parentElement
    parent.children = parent.children.filter((child) => child !== this)
    this.parentNode = null
    this.ownerDocument?._notify(parent, 'childList')
  }

  contains(candidate) {
    for (let node = candidate; node; node = node.parentNode) if (node === this) return true
    return false
  }

  matches(selector) {
    return splitSelectors(selector).some((part) => matchesSimple(this, part))
  }

  closest(selector) {
    for (let node = this; node instanceof MiniElement; node = node.parentElement) if (node.matches(selector)) return node
    return null
  }

  querySelectorAll(selector) {
    const scopedChild = selector.match(/^:scope\s*>\s*(.+)$/)
    if (scopedChild) return this.children.filter((child) => child.matches(scopedChild[1]))
    const results = []
    const visit = (node) => {
      for (const child of node.children) {
        if (!(child instanceof MiniElement)) continue
        if (child.matches(selector)) results.push(child)
        visit(child)
      }
    }
    visit(this)
    return results
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] ?? null
  }
}

class MiniDocument extends MiniEventTarget {
  constructor() {
    super()
    this.parentNode = null
    this._observers = new Set()
    this.documentElement = new MiniElement('html', this)
    this.documentElement.parentNode = this
    this.head = new MiniElement('head', this)
    this.body = new MiniElement('body', this)
    this.documentElement.append(this.head, this.body)
    this.activeElement = this.body
    this._rectResolver = null
  }

  createElement(tagName) {
    return new MiniElement(tagName, this)
  }

  createElementNS(_namespace, tagName) {
    return this.createElement(tagName)
  }

  createTextNode(value) {
    return new MiniText(value, this)
  }

  querySelectorAll(selector) {
    const results = this.documentElement.matches(selector) ? [this.documentElement] : []
    return results.concat(this.documentElement.querySelectorAll(selector))
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] ?? null
  }

  getElementById(id) {
    return this.querySelector(`#${id}`)
  }

  _notify(target, type, attributeName = null) {
    const mutation = { target, type, attributeName }
    for (const observer of [...this._observers]) observer._notify(mutation)
  }
}

class MiniMutationObserver {
  constructor(callback) {
    this._callback = callback
    this._document = null
    this._target = null
    this._options = null
  }

  observe(target, options = {}) {
    this._document = target instanceof MiniDocument ? target : target.ownerDocument
    this._target = target
    this._options = options
    this._document?._observers.add(this)
  }

  _notify(mutation) {
    const isObservedTarget = mutation.target === this._target
      || Boolean(this._options?.subtree && this._target?.contains?.(mutation.target))
    if (!isObservedTarget) return
    if (mutation.type === 'attributes') {
      if (!this._options?.attributes) return
      if (this._options.attributeFilter && !this._options.attributeFilter.includes(mutation.attributeName)) return
    }
    if (mutation.type === 'childList' && !this._options?.childList) return
    if (mutation.type === 'characterData' && !this._options?.characterData) return
    this._callback([mutation])
  }

  disconnect() {
    this._document?._observers.delete(this)
    this._document = null
    this._target = null
    this._options = null
  }
}

export function createBrowserFixture() {
  const document = new MiniDocument()
  const window = new MiniEventTarget()
  document.parentNode = window
  window.parentNode = null
  window.window = window
  window.document = document
  window.innerWidth = 1024
  window.innerHeight = 768
  const timers = new Set()
  const trackedSetTimeout = (callback, delay = 0) => {
    const timer = setTimeout(() => {
      timers.delete(timer)
      callback()
    }, delay)
    timers.add(timer)
    return timer
  }
  const trackedClearTimeout = (timer) => {
    clearTimeout(timer)
    timers.delete(timer)
  }
  let nextFrame = 0
  const frames = new Map()
  const requestAnimationFrame = (callback) => {
    const id = ++nextFrame
    const timer = setTimeout(() => {
      timers.delete(timer)
      frames.delete(id)
      callback(Date.now())
    }, 0)
    timers.add(timer)
    frames.set(id, timer)
    return id
  }
  const cancelAnimationFrame = (id) => {
    const timer = frames.get(id)
    if (!timer) return
    clearTimeout(timer)
    timers.delete(timer)
    frames.delete(id)
  }
  const intervals = new Set()
  const fakeSetInterval = (callback) => {
    const token = { callback }
    intervals.add(token)
    return token
  }
  const fakeClearInterval = (token) => intervals.delete(token)
  const context = {
    window,
    document,
    MutationObserver: MiniMutationObserver,
    Event: MiniEvent,
    PointerEvent: MiniEvent,
    MouseEvent: MiniEvent,
    KeyboardEvent: MiniEvent,
    FocusEvent: MiniEvent,
    requestAnimationFrame,
    cancelAnimationFrame,
    setInterval: fakeSetInterval,
    clearInterval: fakeClearInterval,
    setTimeout: trackedSetTimeout,
    clearTimeout: trackedClearTimeout,
    getComputedStyle: (element) => ({
      display: element.style.display ?? 'block',
      alignItems: element.style.alignItems ?? 'normal',
      position: element.style.position ?? 'static',
      flex: element.style.flex ?? '0 1 auto',
      overflow: element.style.overflow ?? 'visible',
      overflowX: element.style.overflowX ?? element.style.overflow ?? 'visible',
      overflowY: element.style.overflowY ?? element.style.overflow ?? 'visible',
    }),
    Date,
    Math,
    JSON,
    Number,
    String,
    Object,
    Array,
    Map,
    Set,
  }
  window.Event = MiniEvent
  window.PointerEvent = MiniEvent
  window.MouseEvent = MiniEvent
  window.KeyboardEvent = MiniEvent
  window.FocusEvent = MiniEvent
  window.MutationObserver = MiniMutationObserver
  window.requestAnimationFrame = requestAnimationFrame
  window.cancelAnimationFrame = cancelAnimationFrame
  window.setTimeout = trackedSetTimeout
  window.clearTimeout = trackedClearTimeout

  return {
    document,
    window,
    Event: MiniEvent,
    PointerEvent: MiniEvent,
    KeyboardEvent: MiniEvent,
    context,
    setRectResolver(callback) {
      document._rectResolver = callback
    },
    async flush() {
      await new Promise((resolve) => setTimeout(resolve, 5))
    },
    dispose() {
      for (const timer of timers) clearTimeout(timer)
      timers.clear()
      intervals.clear()
    },
  }
}

export function executeRenderer(source, fixture, { state = null, wingman = { request() {} }, helperConfig = {} } = {}) {
  fixture.context.state = state
  fixture.context.wingman = wingman
  fixture.context.__helperConfig = helperConfig
  return vm.runInNewContext(`(() => { const helperConfig = Object.freeze(__helperConfig); ${source}\n})()`, fixture.context)
}

export function makeComposer(document, id, percent) {
  const row = document.createElement('div')
  row.id = id
  row.style.display = 'flex'
  row.style.alignItems = 'center'
  const wrapper = document.createElement('span')
  wrapper.id = `${id}-context-wrapper`
  const dial = document.createElement('span')
  dial.setAttribute('role', 'img')
  dial.setAttribute('aria-label', `Context usage: ${percent}%`)
  wrapper.appendChild(dial)
  const nativeAfter = document.createElement('button')
  nativeAfter.id = `${id}-native-after`
  row.append(wrapper, nativeAfter)
  document.body.appendChild(row)
  return { row, wrapper, dial }
}

export function makeNativeTooltip(document, contextDial) {
  document.querySelector('[role="tooltip"]')?.remove()
  const tooltip = document.createElement('div')
  tooltip.setAttribute('role', 'tooltip')
  const native = document.createElement('div')
  native.dataset.nativeContextContent = 'true'
  native.textContent = `Context window: ${contextDial.getAttribute('aria-label')}`
  tooltip.appendChild(native)
  document.body.appendChild(tooltip)
  return tooltip
}
