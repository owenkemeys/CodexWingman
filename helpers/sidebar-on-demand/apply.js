(() => {
  const globalName = '__codexWingmanSidebarOnDemand';
  const controllerVersion = 'css-gate-v2';
  const triggerSelector = '[data-app-shell-sidebar-trigger="true"]';
  const panelSelector = '[data-pip-obstacle="app-shell-floating-left-panel"]';
  const styleSelector = '[data-codex-wingman-sidebar-gate="true"]';
  const explicitAttribute = 'data-codex-wingman-sidebar-explicit';
  const panelMarker = 'data-codex-wingman-sidebar-suppressed';
  const hoverEvents = ['pointerover', 'pointerenter', 'mouseover', 'mouseenter'];
  const existing = window[globalName];
  if (existing?.version === controllerVersion) return existing.status();
  existing?.cleanup?.();

  let active = true;
  let suppressedEventCount = 0;
  let suppressedPanelCount = 0;
  let explicitIntent = false;
  let explicitPanelSeen = false;
  let pendingIntentFrame = null;
  let intentLeaseGeneration = 0;

  const style = document.createElement('style');
  style.setAttribute('data-codex-wingman-sidebar-gate', 'true');
  style.textContent = `html:not([data-codex-wingman-sidebar-explicit="true"]) [data-pip-obstacle="app-shell-floating-left-panel"] {
  visibility: hidden !important;
  pointer-events: none !important;
}`;
  document.head.appendChild(style);

  const isSemanticSidebarTrigger = (target) => Boolean(target?.closest?.(triggerSelector));
  const panels = () => [...document.querySelectorAll(panelSelector)];
  const markPassivePanels = () => {
    if (explicitIntent) return;
    for (const panel of panels()) {
      if (panel.getAttribute(panelMarker) === 'true') continue;
      panel.setAttribute(panelMarker, 'true');
      suppressedPanelCount++;
    }
  };
  const cancelPendingIntentLease = () => {
    intentLeaseGeneration++;
    if (pendingIntentFrame !== null) cancelAnimationFrame(pendingIntentFrame);
    pendingIntentFrame = null;
  };
  const clearExplicitIntent = () => {
    cancelPendingIntentLease();
    explicitIntent = false;
    explicitPanelSeen = false;
    document.documentElement.removeAttribute(explicitAttribute);
    markPassivePanels();
  };
  const schedulePendingIntentLease = () => {
    cancelPendingIntentLease();
    const generation = intentLeaseGeneration;
    pendingIntentFrame = requestAnimationFrame(() => {
      pendingIntentFrame = null;
      if (!active || generation !== intentLeaseGeneration || !explicitIntent || explicitPanelSeen) return;
      pendingIntentFrame = requestAnimationFrame(() => {
        pendingIntentFrame = null;
        if (!active || generation !== intentLeaseGeneration || !explicitIntent || explicitPanelSeen) return;
        const currentPanels = panels();
        if (currentPanels.length > 0) {
          explicitPanelSeen = true;
          cancelPendingIntentLease();
          return;
        }
        clearExplicitIntent();
      });
    });
  };
  const setExplicitIntent = () => {
    explicitIntent = true;
    document.documentElement.setAttribute(explicitAttribute, 'true');
    explicitPanelSeen = panels().length > 0;
    if (explicitPanelSeen) cancelPendingIntentLease();
    else schedulePendingIntentLease();
  };
  const suppressEdgeActivation = (event) => {
    if (!isSemanticSidebarTrigger(event.target)) return;
    suppressedEventCount++;
    event.preventDefault();
    event.stopImmediatePropagation();
  };
  const allowPrimaryPointer = (event) => {
    if (!isSemanticSidebarTrigger(event.target)) return;
    if (event.button !== undefined && event.button !== 0) return;
    setExplicitIntent();
  };
  const allowClick = (event) => {
    if (isSemanticSidebarTrigger(event.target)) setExplicitIntent();
  };
  const allowKeyboard = (event) => {
    if (!isSemanticSidebarTrigger(event.target)) return;
    if (!['Enter', ' ', 'Space', 'Spacebar'].includes(event.key)) return;
    setExplicitIntent();
  };
  const clearOnWindowBlur = (event) => {
    if (event.target === window) clearExplicitIntent();
  };
  const clearOnWindowMouseLeave = (event) => {
    const boundaryTarget = event.target === window
      || event.target === document
      || event.target === document.documentElement;
    if (boundaryTarget && !event.relatedTarget) clearExplicitIntent();
  };
  const observePanels = () => {
    const currentPanels = panels();
    if (explicitIntent) {
      if (currentPanels.length > 0) {
        explicitPanelSeen = true;
        cancelPendingIntentLease();
      }
      else if (explicitPanelSeen) clearExplicitIntent();
      return;
    }
    markPassivePanels();
  };

  for (const eventName of hoverEvents)
    window.addEventListener(eventName, suppressEdgeActivation, true);
  window.addEventListener('pointerdown', allowPrimaryPointer, true);
  window.addEventListener('click', allowClick, true);
  window.addEventListener('keydown', allowKeyboard, true);
  window.addEventListener('blur', clearOnWindowBlur, true);
  window.addEventListener('mouseleave', clearOnWindowMouseLeave, true);

  const observer = new MutationObserver(observePanels);
  observer.observe(document.documentElement, { childList: true, subtree: true });
  markPassivePanels();

  let controller;
  const cleanup = () => {
    if (!active) return true;
    active = false;
    cancelPendingIntentLease();
    for (const eventName of hoverEvents)
      window.removeEventListener(eventName, suppressEdgeActivation, true);
    window.removeEventListener('pointerdown', allowPrimaryPointer, true);
    window.removeEventListener('click', allowClick, true);
    window.removeEventListener('keydown', allowKeyboard, true);
    window.removeEventListener('blur', clearOnWindowBlur, true);
    window.removeEventListener('mouseleave', clearOnWindowMouseLeave, true);
    observer.disconnect();
    style.remove();
    document.documentElement.removeAttribute(explicitAttribute);
    document.querySelectorAll(`[${panelMarker}="true"]`).forEach((panel) =>
      panel.removeAttribute(panelMarker));
    if (window[globalName] === controller) delete window[globalName];
    return true;
  };
  const status = () => ({
    active,
    version: controllerVersion,
    suppressedEventCount,
    suppressedPanelCount,
    explicitIntent,
    suppressedCount: suppressedEventCount,
  });

  controller = { version: controllerVersion, cleanup, status };
  window[globalName] = controller;
  return status();
})()
