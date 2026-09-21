(() => {
  const controllerVersion = 'risk-ring-v18';
  const owner = 'agent-derangement-risk';
  const initialSnapshot = state || {};
  const existing = window.__codexHelperAgentDerangementRisk;
  if (existing?.version === controllerVersion) {
    existing.update(initialSnapshot);
    return existing.status();
  }
  existing?.cleanup?.();

  document.querySelectorAll(`[data-codex-helper="${owner}"],[data-codex-helper-style="${owner}"],[data-codex-helper-tooltip="${owner}"],[data-codex-helper-context-slot="${owner}"]`)
    .forEach((node) => node.remove());

  const svgNamespace = 'http://www.w3.org/2000/svg';
  const circumference = 2 * Math.PI * 7;
  const instances = new Map();
  let snapshot = initialSnapshot;
  let frame = 0;
  const style = document.createElement('style');
  style.dataset.codexHelperStyle = owner;
  style.textContent = `
    .codex-helper-risk-heading { color: inherit; opacity: .6; }
    .codex-helper-risk-details { border-top: 1px solid var(--color-token-border, #444); margin-top: 4px; padding-top: 4px; }
    [data-codex-helper="${owner}"] { display:inline-flex; position:static; flex:0 0 auto; align-items:center; justify-content:center; width:16px; height:24px; color:color-mix(in srgb,currentColor 58%,transparent); font:inherit; }
    [data-codex-helper="${owner}"][data-state="watch"] { color:#d89614; }
    [data-codex-helper="${owner}"][data-state="handoff"] { color:#e5484d; }
    [data-codex-helper="${owner}"][data-state="unavailable"] { opacity:.4; }
    [data-codex-helper="${owner}"] svg { display:block; width:16px; height:16px; overflow:visible; }
    [data-codex-helper="${owner}"] .codex-helper-unavailable-marker { width:16px; text-align:center; font-weight:600; line-height:24px; }
    [data-codex-helper="${owner}"] .codex-helper-track { stroke:currentColor; opacity:.2; }
    [data-codex-helper="${owner}"] .codex-helper-value { stroke:currentColor; transition:stroke-dashoffset 180ms ease; }
  `;
  document.documentElement.appendChild(style);

  const isNativeNewTaskScreen = () => [...document.querySelectorAll('main')].some((main) => {
    const heading = [...main.querySelectorAll('div.heading-xl')]
      .find((node) => [...node.querySelectorAll('span')]
        .some((span) => {
          const text = span.textContent?.trim() || '';
          return text === 'What should we build?'
            || /^What should we work on in .+\?$/.test(text);
        }));
    if (!heading) return false;
    for (let node = heading; node; node = node.parentElement) {
      const nodeStyle = getComputedStyle(node);
      if (node.getAttribute('aria-hidden') === 'true' || node.hasAttribute('hidden')
        || nodeStyle.display === 'none' || nodeStyle.visibility === 'hidden') return false;
      if (node === main) break;
    }
    return Boolean(main.querySelector('[data-codex-composer="true"][contenteditable="true"]'));
  });

  const currentThreadId = () => {
    if (isNativeNewTaskScreen()) return null;
    const ids = new Set();
    const attributes = [
      'data-above-composer-conversation-id',
      'data-request-user-input-auto-resolution-conversation-id',
    ];
    for (const node of document.querySelectorAll(attributes.map((name) => `[${name}]`).join(','))) {
      for (const name of attributes) {
        const id = node.getAttribute(name)?.trim().replace(/^local:/i, '');
        if (id) ids.add(id);
      }
    }
    return ids.size === 1 ? [...ids][0] : null;
  };

  const resolveSnapshot = () => {
    const threadId = currentThreadId();
    if (!threadId) return {};
    if (snapshot && Object.prototype.hasOwnProperty.call(snapshot, 'threadId')) {
      return threadId && snapshot.threadId && threadId.toLowerCase() === String(snapshot.threadId).toLowerCase() ? snapshot : {};
    }
    if (!snapshot?.byThreadId) return {};
    const entry = Object.entries(snapshot.byThreadId)
      .find(([candidate]) => candidate.toLowerCase() === threadId.toLowerCase());
    return entry?.[1] || {};
  };

  const isVerifiedAnchor = (dial) => {
    if (!dial || dial.tagName !== 'SPAN' || dial.getAttribute('role') !== 'img') return false;
    if (!/^Context usage:\s*\d+(?:\.\d+)?%/i.test(dial.getAttribute('aria-label') || '')) return false;
    const wrapper = dial.parentElement;
    const row = wrapper?.parentElement;
    if (!wrapper || !row) return false;
    const rowStyle = getComputedStyle(row);
    return (rowStyle.display === 'flex' || rowStyle.display === 'inline-flex') && rowStyle.alignItems === 'center';
  };

  const isVisible = (element) => {
    if (!element?.isConnected || element.getAttribute('aria-hidden') === 'true') return false;
    const computed = getComputedStyle(element);
    if (computed.display === 'none' || computed.visibility === 'hidden') return false;
    const rect = element.getBoundingClientRect?.();
    const viewportHeight = window.innerHeight || 0;
    return Boolean(rect && rect.width > 0 && rect.height > 0
      && rect.top < viewportHeight && rect.bottom > 0
      && rect.bottom > viewportHeight * 0.55);
  };

  const insertBefore = (parent, node, reference) => {
    if (parent.insertBefore) parent.insertBefore(node, reference);
  };

  const findFallbackAnchors = (excludedSurfaces = new Set()) => {
    const groups = [...document.querySelectorAll('div')].map((group) => {
      if (excludedSurfaces.has(group)) return null;
      const classes = new Set(String(group.className || '').split(/\s+/));
      if (!classes.has('flex') || !classes.has('min-w-0')
        || !classes.has('items-center') || !classes.has('gap-1')) return null;
      const modelWrapper = [...group.children].find((child) => {
        if (child.getAttribute('data-codex-helper') || child.getAttribute('data-codex-helper-context-slot')) return false;
        const text = String(child.textContent || '');
        if (!/(?:codex|sol|luna|gpt|sonnet|opus|haiku|claude)[\s\S]{0,100}(?:medium|high|low|extra|mini|pro|auto)/i.test(text)) return false;
        return [...child.querySelectorAll('button,[role="button"],[role="combobox"],[role="listbox"]')].some(isVisible);
      });
      return modelWrapper ? { group, modelWrapper } : null;
    }).filter(Boolean);
    return groups.map(({ group, modelWrapper }) => {
      let wrapper = [...group.children].find((child) => child.getAttribute('data-codex-helper-context-slot') === owner);
      if (!wrapper) {
        wrapper = document.createElement('span');
        wrapper.dataset.codexHelperContextSlot = owner;
        wrapper.style.display = 'block';
        wrapper.style.width = '16px';
        wrapper.style.height = '24px';
        wrapper.style.flex = '0 0 auto';
        insertBefore(group, wrapper, modelWrapper);
      }
      return { mode: 'fallback', dial: null, wrapper };
    });
  };

  const findAnchors = () => {
    const allNative = [...document.querySelectorAll('span[role="img"][aria-label^="Context usage:"]')]
      .filter(isVerifiedAnchor)
      .map((dial) => ({ mode: 'native', dial, wrapper: dial.parentElement }));
    const nativeBySurface = new Map(allNative.map((anchor) => [anchor.wrapper.parentElement, anchor]));
    const usageSurfaces = new Set();
    const usageBankAnchors = [];
    for (const bank of document.querySelectorAll('[data-codex-helper="usage-dials"]')) {
      const surface = bank.parentElement;
      if (!surface?.isConnected || usageSurfaces.has(surface)) continue;
      const siblings = [...surface.children];
      const usageSlot = siblings.find((child) => child.getAttribute('data-codex-helper-context-slot') === 'usage-dials');
      const preceding = siblings.slice(0, siblings.indexOf(bank)).reverse()
        .find((child) => child.getAttribute('data-codex-helper') !== owner);
      const wrapper = usageSlot || nativeBySurface.get(surface)?.wrapper || preceding;
      if (!wrapper?.isConnected) continue;
      usageSurfaces.add(surface);
      usageBankAnchors.push({ mode: 'usage-bank', dial: null, wrapper });
    }
    const native = allNative.filter((anchor) => !usageSurfaces.has(anchor.wrapper.parentElement));
    const occupiedSurfaces = new Set([...usageSurfaces, ...native.map((anchor) => anchor.wrapper.parentElement)]);
    const fallback = findFallbackAnchors(occupiedSurfaces);
    return [...usageBankAnchors, ...native, ...fallback];
  };

  const makeRing = () => {
    const ring = document.createElement('span');
    ring.dataset.codexHelper = owner;
    ring.dataset.state = 'unavailable';
    ring.setAttribute('role', 'img');
    ring.setAttribute('tabindex', '0');
    const svg = document.createElementNS(svgNamespace, 'svg');
    svg.setAttribute('viewBox', '0 0 20 20');
    svg.setAttribute('aria-hidden', 'true');
    const track = document.createElementNS(svgNamespace, 'circle');
    track.setAttribute('class', 'codex-helper-track'); track.setAttribute('cx', '10'); track.setAttribute('cy', '10'); track.setAttribute('r', '7'); track.setAttribute('fill', 'none'); track.setAttribute('stroke-width', '2');
    const value = document.createElementNS(svgNamespace, 'circle');
    value.setAttribute('class', 'codex-helper-value'); value.setAttribute('cx', '10'); value.setAttribute('cy', '10'); value.setAttribute('r', '7'); value.setAttribute('fill', 'none'); value.setAttribute('stroke-width', '2'); value.setAttribute('stroke-linecap', 'round'); value.setAttribute('stroke-dasharray', String(circumference)); value.setAttribute('transform', 'rotate(-90 10 10)');
    const unavailableMarker = document.createElement('span');
    unavailableMarker.className = 'codex-helper-unavailable-marker';
    unavailableMarker.textContent = '?';
    unavailableMarker.setAttribute('aria-hidden', 'true');
    svg.append(track, value); ring.append(svg, unavailableMarker); ring.__codexHelper = { value, svg, unavailableMarker }; return ring;
  };

  const updateRing = (ring, state) => {
    const risk = Number.isFinite(state?.risk) ? Math.max(0, Math.min(1, state.risk)) : null;
    const band = risk === null ? 'unavailable' : (state.band || (risk < .6 ? 'healthy' : risk < .85 ? 'watch' : 'handoff'));
    ring.dataset.state = band;
    ring.style.display = 'inline-flex';
    ring.__codexHelper.svg.style.display = risk === null ? 'none' : 'block';
    ring.__codexHelper.unavailableMarker.style.display = risk === null ? 'inline' : 'none';
    ring.__codexHelper.value.setAttribute('stroke-dashoffset', String(circumference * (1 - (risk || 0))));
    ring.setAttribute('aria-label', risk === null ? 'Agent derangement risk: unavailable.' : `Agent derangement risk: ${Math.round(risk * 100)}%, ${band}.`);
  };

  const formatNumber = (value) => Number.isFinite(value) ? Math.round(value).toLocaleString('en-US') : 'unavailable';
  const renderTooltip = (instance) => {
    if (!instance.tooltip) return;
    const state = instance.snapshot || {};
    const risk = Number.isFinite(state.risk) ? Math.max(0, Math.min(1, state.risk)) : null;
    const cumulativeFallback = Number.isFinite(state.cumTokens) && state.cumTokens === state.usedTokens;
    const lines = [
      ['Agent derangement risk', 'codex-helper-risk-heading'],
      [`Risk: ${risk === null ? 'unavailable' : `${Math.round(risk * 100)}% (${state.band || 'unavailable'})`}`, ''],
      [`Compactions: ${Number.isFinite(state.compactions) ? Math.round(state.compactions) : 'unavailable'}`, 'codex-helper-risk-details'],
      [`Total tokens: ${formatNumber(state.cumTokens)}${cumulativeFallback ? ' (estimated from current context)' : ''}`, ''],
    ];
    instance.tooltip.replaceChildren();
    const stack = document.createElement('div');
    stack.className = 'flex w-48 flex-col gap-0.5 text-center';
    lines.forEach(([text, className]) => {
      const line = document.createElement('div');
      line.textContent = text;
      if (className) line.className = className;
      stack.appendChild(line);
    });
    instance.tooltip.appendChild(stack);
    const rect = instance.ring.getBoundingClientRect();
    const tooltipRect = instance.tooltip.getBoundingClientRect();
    const margin = 4;
    const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
    const left = Math.min(Math.max(margin, rect.left), Math.max(margin, viewportWidth - tooltipRect.width - margin));
    const below = rect.bottom + 6;
    const top = below + tooltipRect.height <= viewportHeight - margin
      ? below : Math.max(margin, rect.top - tooltipRect.height - 6);
    instance.tooltip.style.left = `${left}px`;
    instance.tooltip.style.top = `${top}px`;
  };

  const hideTooltip = (instance) => {
    if (instance.tooltipTimer) { clearTimeout(instance.tooltipTimer); instance.tooltipTimer = null; }
    if (!instance.tooltip) return;
    instance.tooltip.remove();
    instance.tooltip = null;
    instance.ring.removeAttribute('aria-describedby');
  };

  const showTooltip = (instance) => {
    if (instance.tooltipTimer) { clearTimeout(instance.tooltipTimer); instance.tooltipTimer = null; }
    if (!instance.tooltip) {
      const tooltip = document.createElement('div');
      tooltip.dataset.codexHelperTooltip = owner;
      tooltip.setAttribute('role', 'tooltip');
      tooltip.id = `codex-helper-risk-tooltip-${instances.size}`;
      tooltip.className = 'z-50 w-fit select-none text-sm whitespace-normal break-words bg-token-dropdown-background text-token-foreground border-token-border rounded-lg border px-2 py-1';
      tooltip.style.position = 'fixed';
      tooltip.style.zIndex = '2147483647';
      tooltip.style.pointerEvents = 'none';
      instance.tooltip = tooltip;
      instance.ring.setAttribute('aria-describedby', tooltip.id);
      document.body.appendChild(tooltip);
    }
    renderTooltip(instance);
  };

  const destroyInstance = (instance) => {
    instance.ring.removeEventListener('pointerover', instance.onPointerOver);
    instance.ring.removeEventListener('pointerout', instance.onPointerOut);
    instance.ring.removeEventListener('pointerleave', instance.onPointerLeave);
    hideTooltip(instance);
    instance.ring.remove();
    if (instance.mode === 'fallback' && instance.wrapper?.getAttribute('data-codex-helper-context-slot') === owner)
      instance.wrapper.remove();
  };

  const reconcile = () => {
    const ownedRings = new Set([...instances.values()].map((instance) => instance.ring));
    document.querySelectorAll(`[data-codex-helper="${owner}"]`).forEach((ring) => {
      if (!ownedRings.has(ring)) ring.remove();
    });
    const anchors = findAnchors();
    const active = new Set(anchors.map((item) => item.wrapper));
    for (const anchor of anchors) {
      let instance = instances.get(anchor.wrapper);
      if (!instance) {
        instance = { mode: anchor.mode, wrapper: anchor.wrapper, ring: makeRing(), tooltip: null, tooltipTimer: null };
        instance.onPointerOver = (event) => {
          if (instance.ring.contains(event.relatedTarget)) return;
          showTooltip(instance);
          if (event.isTrusted === false)
            instance.tooltipTimer = setTimeout(() => hideTooltip(instance), 1200);
        };
        instance.onPointerOut = (event) => { if (!instance.ring.contains(event.relatedTarget)) hideTooltip(instance); };
        instance.onPointerLeave = () => hideTooltip(instance);
        instance.ring.addEventListener('pointerover', instance.onPointerOver);
        instance.ring.addEventListener('pointerout', instance.onPointerOut);
        instance.ring.addEventListener('pointerleave', instance.onPointerLeave);
        instances.set(anchor.wrapper, instance);
      }
      instance.mode = anchor.mode;
      instance.wrapper = anchor.wrapper;
      instance.snapshot = resolveSnapshot();
      updateRing(instance.ring, instance.snapshot);
      renderTooltip(instance);
      const usage = anchor.wrapper.parentElement?.querySelector(':scope > [data-codex-helper="usage-dials"]');
      const reference = usage || anchor.wrapper;
      if (instance.ring.parentElement !== reference.parentElement || instance.ring.previousElementSibling !== reference)
        reference.insertAdjacentElement('afterend', instance.ring);
    }
    for (const [key, instance] of instances) {
      if (!active.has(key) || !instance.wrapper.isConnected) {
        destroyInstance(instance);
        instances.delete(key);
      }
    }
    return anchors.length;
  };

  const observer = new MutationObserver((mutations) => { if (mutations.some((mutation) => !mutation.target.closest?.(`[data-codex-helper="${owner}"]`))) { if (!frame) frame = requestAnimationFrame(() => { frame = 0; reconcile(); }); } });
  observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-label', 'data-above-composer-conversation-id', 'data-request-user-input-auto-resolution-conversation-id'] });
  const interval = setInterval(reconcile, 2000);
  const hideAllTooltips = () => { for (const instance of instances.values()) hideTooltip(instance); };
  const onWindowBlur = () => hideAllTooltips();
  window.addEventListener('blur', onWindowBlur);
  let controller;
  let leaseWatchdog = null;
  const cleanup = () => { observer.disconnect(); clearInterval(interval); if (leaseWatchdog !== null) { clearInterval(leaseWatchdog); leaseWatchdog = null; } cancelAnimationFrame(frame); frame = 0; window.removeEventListener('blur', onWindowBlur); for (const instance of instances.values()) destroyInstance(instance); instances.clear(); document.querySelectorAll(`[data-codex-helper="${owner}"],[data-codex-helper-style="${owner}"],[data-codex-helper-tooltip="${owner}"],[data-codex-helper-context-slot="${owner}"]`).forEach((node) => node.remove()); if (window.__codexHelperAgentDerangementRisk === controller) delete window.__codexHelperAgentDerangementRisk; return true; };
  const update = (next) => { snapshot = next || {}; reconcile(); return status(); };
  const status = () => { const state = resolveSnapshot(); return { version: controllerVersion, attachedCount: [...instances.values()].filter((item) => item.ring.isConnected).length, risk: state.risk ?? null, band: state.band || 'unavailable', sourceThreadId: snapshot.threadId || null, currentThreadId: currentThreadId(), home: isNativeNewTaskScreen() }; };
  controller = { version: controllerVersion, update, cleanup, status };
  window.__codexHelperAgentDerangementRisk = controller;
  leaseWatchdog = setInterval(() => {
    const expiresAt = window.__codexHelperAgentDerangementRiskLeaseExpiresAt;
    if (typeof expiresAt === 'number' && Number.isFinite(expiresAt) && Date.now() >= expiresAt)
      cleanup();
  }, 1000);
  return update(initialSnapshot);
})()
