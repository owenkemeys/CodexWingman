(() => {
  window.__codexHelperUsageDials?.cleanup?.();
  document.querySelectorAll('[data-codex-helper="usage-dials"]').forEach((node) => node.remove());
  document.querySelectorAll('[data-codex-helper-style="usage-dials"]').forEach((node) => node.remove());
  document.querySelectorAll('[data-codex-helper-tooltip="usage"]').forEach((node) => node.remove());
  delete window.__codexHelperUsageDials;
  return true;
})()
