(() => {
  const owner = 'obsidian-links'
  const globalName = '__codexWingmanObsidianLinks'
  try { window[globalName]?.cleanup?.() } catch {}

  const restoreAttributes = (element) => {
    let attributes = null
    try { attributes = JSON.parse(element.getAttribute('data-obsidian-links-original-attributes') || 'null') } catch {}
    if (!Array.isArray(attributes)) return
    for (const entry of Array.from(element.attributes || [])) {
      const name = Array.isArray(entry) ? entry[0] : entry.name
      element.removeAttribute(name)
    }
    for (const entry of attributes) {
      if (Array.isArray(entry) && entry.length === 2) element.setAttribute(entry[0], entry[1])
    }
  }
  for (const wrapper of Array.from(document.querySelectorAll('[data-codex-wingman-owner="obsidian-links"][data-obsidian-links-generated="true"]'))) {
    const raw = wrapper.getAttribute('data-obsidian-links-raw-text')
    if (raw !== null) wrapper.replaceWith(document.createTextNode(raw))
  }
  for (const anchor of Array.from(document.querySelectorAll('a[data-codex-wingman-owner="obsidian-links"][data-obsidian-links-link="true"]'))) {
    const rawLabel = anchor.getAttribute('data-obsidian-links-raw-label') === 'true'
    const raw = anchor.getAttribute('data-obsidian-links-raw-path') || ''
    const stash = anchor.querySelector('[data-obsidian-links-original-label="true"]')
    if (rawLabel) {
      const originalNodes = Array.from(stash?.childNodes || stash?.children || [])
      anchor.replaceChildren(...(originalNodes.length ? originalNodes : [document.createTextNode(raw)]))
    } else {
      for (const icon of Array.from(anchor.querySelectorAll('[data-obsidian-links-icon="true"]'))) icon.remove()
    }
    restoreAttributes(anchor)
  }
  for (const control of Array.from(document.querySelectorAll('[data-codex-wingman-owner="obsidian-links"][data-obsidian-links-link="true"]'))) {
    if (control.tagName === 'A') continue
    const stash = control.querySelector('[data-obsidian-links-native-icon-stash="true"]')
    if (stash?.parentNode) stash.parentNode.replaceChildren(...Array.from(stash.childNodes || stash.children || []))
    else for (const icon of Array.from(control.querySelectorAll('[data-obsidian-links-icon="true"]'))) icon.remove()
    restoreAttributes(control)
  }
  for (const node of Array.from(document.querySelectorAll('[data-codex-wingman-owner="obsidian-links"]'))) node.remove()
  if (window[globalName]) delete window[globalName]
  return true
})()
