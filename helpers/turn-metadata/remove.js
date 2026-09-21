(() => {
  window.__codexHelperTurnMetadata?.cleanup?.()
  document.querySelectorAll('[data-turn-metadata-relocated]').forEach((timestamp) => {
    const toolbar = timestamp.parentElement
    const originalVisibility = (timestamp.getAttribute('data-turn-metadata-original-visibility-classes') || '').split(/\s+/).filter(Boolean)
    const classes = new Set((timestamp.getAttribute('class') || '').split(/\s+/).filter(Boolean))
    originalVisibility.forEach((token) => classes.add(token))
    timestamp.setAttribute('class', [...classes].join(' '))
    timestamp.removeAttribute('data-turn-metadata-relocated')
    timestamp.removeAttribute('data-turn-metadata-original-visibility-classes')
    if (toolbar?.parentElement) toolbar.insertAdjacentElement('afterend', timestamp)
  })
  document.querySelectorAll('[data-codex-wingman-owner="turn-metadata"]').forEach((node) => node.remove())
  delete window.__codexHelperTurnMetadata
  return true
})()
