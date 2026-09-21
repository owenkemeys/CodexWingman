(() => {
  const helper = window.__codexWingmanTurnRenderRecovery
  try { helper?.cleanup?.() } catch {}
  document.querySelectorAll('[data-turn-render-recovery-note]').forEach((node) => node.remove())
  delete window.__codexWingmanTurnRenderRecovery
  return true
})()
