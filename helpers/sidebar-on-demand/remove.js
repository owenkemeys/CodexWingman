(() => {
  const globalName = '__codexWingmanSidebarOnDemand';
  const styleSelector = '[data-codex-wingman-sidebar-gate="true"]';
  const explicitAttribute = 'data-codex-wingman-sidebar-explicit';
  const panelMarker = 'data-codex-wingman-sidebar-suppressed';

  window[globalName]?.cleanup?.();
  document.querySelectorAll(styleSelector).forEach((style) => style.remove());
  document.documentElement.removeAttribute(explicitAttribute);
  document.querySelectorAll(`[${panelMarker}="true"]`).forEach((panel) =>
    panel.removeAttribute(panelMarker));
  delete window[globalName];
  return true;
})()
