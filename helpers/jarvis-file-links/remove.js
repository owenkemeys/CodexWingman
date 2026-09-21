(() => {
  const controller = window.__codexWingmanJarvisFileLinks
  if (controller?.cleanup) return controller.cleanup()
  for (const control of Array.from(document.querySelectorAll('[data-codex-wingman-owner="jarvis-file-links"][data-jarvis-file-links-native="true"]'))) {
    let attributes = null
    try { attributes = JSON.parse(control.getAttribute('data-jarvis-file-links-original-attributes') || 'null') } catch {}
    if (!Array.isArray(attributes)) continue
    for (const entry of Array.from(control.attributes || [])) {
      control.removeAttribute(Array.isArray(entry) ? entry[0] : entry.name)
    }
    for (const [name, value] of attributes) control.setAttribute(name, value)
  }
  delete window.__codexWingmanJarvisFileLinks
  return true
})()
