(() => {
  const globalName = '__codexWingmanT3ClickableFileLinks'
  try { window[globalName]?.cleanup?.() } catch {}
  for (const node of Array.from(document.querySelectorAll('[data-codex-wingman-owner="clickable-file-links"][data-clickable-file-links-t3-open],style[data-clickable-file-links-t3-style]'))) node.remove()
  if (window[globalName]) delete window[globalName]
  return true
})()
