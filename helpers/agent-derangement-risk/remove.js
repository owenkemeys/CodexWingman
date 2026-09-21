(() => {
  window.__codexHelperAgentDerangementRisk?.cleanup?.();
  document.querySelectorAll('[data-codex-helper="agent-derangement-risk"],[data-codex-helper-style="agent-derangement-risk"]').forEach((node) => node.remove());
  return true;
})()
