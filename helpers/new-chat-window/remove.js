(() => {
  const owner = 'new-chat-window'
  try { window.__codexHelperNewChatWindow?.cleanup?.() } catch { }
  document.querySelectorAll('[data-codex-helper-menu="new-chat-window"], [data-codex-helper-style="new-chat-window"]')
    .forEach((node) => node.remove())
  document.querySelectorAll('[data-codex-helper-context-menu="new-chat-window"]')
    .forEach((node) => delete node.dataset.codexHelperContextMenu)
  delete window.__codexHelperNewChatWindow
  return true
})()
