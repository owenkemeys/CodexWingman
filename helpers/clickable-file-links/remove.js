(() => {
  const controller = window.__codexWingmanClickableFileLinks
  if (controller?.cleanup) return controller.cleanup()
  for (const control of Array.from(document.querySelectorAll('[data-codex-wingman-owner="clickable-file-links"][data-clickable-file-links-native="true"]'))) {
    let attributes = null
    try { attributes = JSON.parse(control.getAttribute('data-clickable-file-links-original-attributes') || 'null') } catch {}
    if (!Array.isArray(attributes)) continue
    for (const entry of Array.from(control.attributes || [])) {
      control.removeAttribute(Array.isArray(entry) ? entry[0] : entry.name)
    }
    for (const [name, value] of attributes) control.setAttribute(name, value)
  }
  delete window.__codexWingmanClickableFileLinks
  return true
})()
