(() => {
  window.__codexJsonDebug?.cleanup?.()
  document.querySelectorAll('[data-codex-json-debug-root]').forEach((node) => node.remove())
  document.querySelectorAll('[data-codex-json-debug-style="json-debug"]').forEach((node) => node.remove())
  delete window.__codexJsonDebug
  return true
})()
