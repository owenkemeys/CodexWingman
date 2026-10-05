(() => {
  const controllerVersion = 'native-slot-v23';
  const t3Renderer = window.location?.protocol === 't3code:';
  const initialSnapshot = state || {};
  const existing = window.__codexHelperUsageDials;
  if (existing?.version === controllerVersion) {
    existing.update(initialSnapshot);
    if (typeof wingman !== 'undefined') wingman.reportRenderHealth?.(existing.status().renderHealth);
    return existing.status();
  }
  existing?.cleanup?.();

  const owner = 'usage-dials';
  const svgNamespace = 'http://www.w3.org/2000/svg';
  const circumference = 2 * Math.PI * 7;
  const instances = new Map();
  let snapshot = initialSnapshot;
  let fallbackSnapshot = initialSnapshot;
  let frame = 0;
  let accountClient = null;
  let accountQuery = null;
  let unsubscribeAccount = null;
  let availableResets = null;
  let quotaUnavailableReason = 'Waiting for current provider usage.';
  let providerLabel = 'Provider';
  let quotaNotReported = false;
  let t3Registry = null;
  let t3RefreshCommand = null;
  let t3RefreshLoading = false;
  let disposed = false;
  const t3RefreshAttempts = new Map();

  // Read Codex's account cache; session usage does not establish a reset balance.
  const hasAccountFields = (data) => data && !Array.isArray(data)
    && (Object.prototype.hasOwnProperty.call(data, 'rate_limit')
      || Object.prototype.hasOwnProperty.call(data, 'rate_limit_reset_credits'));

  const readAccountState = () => {
    try {
      const direct = accountClient?.getQueryState(['rate-limit-status']);
      if (direct) { accountQuery = null; return direct; }
      const matches = accountClient?.getQueryCache()?.findAll?.()
        ?.filter((query) => hasAccountFields(query?.state?.data)) || [];
      // Never choose among old or cross-account copies of the same shape.
      accountQuery = matches.length === 1 ? matches[0] : null;
      return accountQuery?.state || null;
    } catch { accountQuery = null; return null; }
  };

  const normalizeAccountWindow = (value) => {
    if (!value || Array.isArray(value)) return null;
    const used = value.used_percent;
    const seconds = value.limit_window_seconds;
    const reset = value.reset_at;
    if (!Number.isFinite(used) || !Number.isSafeInteger(seconds) || seconds <= 0
      || !Number.isSafeInteger(reset) || reset < -62135596800 || reset > 253402300799
      || reset * 1000 <= Date.now()) return null;
    const usedPercent = Math.max(0, Math.min(100, used));
    const elapsed = Math.max(0, Math.min(100, (Date.now() - (reset - seconds) * 1000) / (seconds * 1000) * 100));
    const state = usedPercent >= 95 ? 'danger' : usedPercent <= elapsed ? 'neutral'
      : usedPercent >= elapsed * 1.5 ? 'danger' : 'warning';
    const date = new Date(reset * 1000);
    const hour = date.getHours();
    const resetLabel = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][date.getDay()]
      + ' ' + (hour % 12 || 12) + ':' + String(date.getMinutes()).padStart(2, '0')
      + ' ' + (hour >= 12 ? 'PM' : 'AM');
    return { usedPercent, windowMinutes: seconds / 60, resetsAtUnixSeconds: reset, state, resetLabel };
  };

  const refreshAccountState = () => {
    if (t3Renderer) { refreshT3AccountState(); return; }
    const query = readAccountState();
    const count = query?.status === 'success'
      ? query.data?.rate_limit_reset_credits?.available_count : null;
    availableResets = Number.isSafeInteger(count) && count >= 0 ? count : null;
    // Explicit state remains supported by the legacy embedded renderer. The
    // packaged helper has no session backend and never waits for shared files.
    snapshot = fallbackSnapshot;
    if (!accountClient) return;
    try {
      if (query?.status !== 'success') { snapshot = {}; return; }
      if (!Object.prototype.hasOwnProperty.call(query.data || {}, 'rate_limit')) return;
      snapshot = { primary: null, secondary: null };
      const limits = query.data.rate_limit;
      const windows = [limits?.primary_window, limits?.secondary_window]
        .map(normalizeAccountWindow).filter(Boolean).sort((a, b) => a.windowMinutes - b.windowMinutes);
      for (const value of windows) {
        const slot = value.windowMinutes >= 10080 ? 'secondary' : 'primary';
        if (!snapshot[slot]) snapshot[slot] = value;
      }
    } catch { snapshot = {}; }
  };

  const connectAccountCache = () => {
    if (t3Renderer) return;
    if (accountClient) return;
    const queue = [window.__codexRoot?._internalRoot?.current];
    const seen = new Set();
    for (let index = 0; index < queue.length && index < 256; index += 1) {
      const fiber = queue[index];
      if (!fiber || seen.has(fiber)) continue;
      seen.add(fiber);
      const client = fiber.memoizedProps?.client;
      if (typeof client?.getQueryState === 'function' && typeof client?.getQueryCache === 'function') {
        try {
          const cache = client.getQueryCache();
          if (typeof cache?.subscribe !== 'function') continue;
          accountClient = client;
          unsubscribeAccount = cache.subscribe((event) => {
            if (event?.query !== accountQuery
              && event?.query?.queryKey?.[0] !== 'rate-limit-status'
              && !hasAccountFields(event?.query?.state?.data)) return;
            reconcile();
          });
          return;
        } catch { accountClient = null; }
      }
      if (fiber.child) queue.push(fiber.child);
      if (fiber.sibling) queue.push(fiber.sibling);
    }
  };

  // T3's committed composer props identify the exact provider instance. Never
  // borrow another provider's quota, infer account identity, or invoke an action.
  const readT3ComposerProps = () => {
    const editors = [...document.querySelectorAll('[data-testid="composer-editor"]')]
      .filter((editor) => editor.closest?.('[data-chat-composer-main-surface="true"]'));
    if (editors.length !== 1) return null;
    // Lexical creates the editable node itself. Find its nearest React-owned
    // DOM ancestor, bounded to this composer surface, before resolving commit.
    let surface = editors[0];
    const boundary = surface.closest('[data-chat-composer-main-surface="true"]');
    let fiberKey = null;
    for (let depth = 0; surface && depth < 16; depth++, surface = surface.parentElement) {
      fiberKey = Object.keys(surface).find((key) => key.startsWith('__reactFiber$'));
      if (fiberKey || surface === boundary) break;
    }
    let fiber = fiberKey ? surface[fiberKey] : null;
    if (!fiber) return null;
    let root = fiber;
    let steps = 0;
    while (root.return && steps++ < 128) root = root.return;
    if (root.return || !root.stateNode?.current) return null;
    if (root.stateNode.current !== root) {
      fiber = fiber.alternate;
      if (!fiber) return null;
      root = fiber;
      steps = 0;
      while (root.return && steps++ < 128) root = root.return;
      if (root.return || root.stateNode?.current !== root) return null;
    }
    for (let depth = 0; fiber && depth < 128; depth++, fiber = fiber.return) {
      const props = fiber.memoizedProps;
      if (Array.isArray(props?.providerStatuses) && typeof props.activeThreadId === 'string'
        && props.activeThread?.id === props.activeThreadId) {
        for (let ancestor = fiber; ancestor; ancestor = ancestor.return) {
          const value = ancestor.memoizedProps?.value;
          if (value?.['~effect/reactivity/AtomRegistry'] && typeof value.get === 'function'
            && typeof value.set === 'function') { t3Registry = value; break; }
        }
        return props;
      }
    }
    return null;
  };
  // Use T3's existing read-only refresh in the owning environment. Discover
  // this release's module by its command contract, without copying credentials
  // or pinning a hashed asset name. This never requests model rediscovery.
  const refreshT3Provider = (props, provider) => {
    const instanceId = provider.instanceId;
    const environmentId = props.activeThread?.environmentId;
    const now = Date.now();
    const checked = Date.parse(provider.usageLimits?.checkedAt);
    if (!provider.enabled || !provider.installed || !environmentId || !t3Registry || disposed
      || (Number.isFinite(checked) && now - checked < 60000)
      || now - (t3RefreshAttempts.get(instanceId) || 0) < 60000 || t3RefreshLoading) return;
    const urls = [...document.querySelectorAll('link[rel="modulepreload"]')]
      .map(element => element.href).filter(url => typeof url === 'string'
        && /^t3code:\/\/app\/assets\/server-[^/]+\.js$/.test(url));
    if (!t3RefreshCommand && (urls.length === 0 || urls.length > 8)) return;
    t3RefreshAttempts.set(instanceId, now);
    t3RefreshLoading = true;
    const registry = t3Registry;
    Promise.resolve(t3RefreshCommand || Promise.all(urls.map(url => import(url).catch(() => null))).then(modules => {
      // Lazy routes can preload an unrelated second server module. Resolve
      // the command contract across the bounded set instead of counting names.
      const commands = [...new Set(modules.flatMap(exports => Object.values(exports || {})
        .map(value => value?.refreshProviders)
        .filter(command => command?.label === 'environment-data:server:refresh-providers'
          && typeof command.run === 'function')))];
      if (commands.length !== 1) throw new Error('Provider refresh interface unavailable');
      return t3RefreshCommand = commands[0];
    })).then(command => {
      if (!disposed) return command.run(registry, { environmentId, input: { instanceId } });
    }).catch(() => {}).finally(() => {
      t3RefreshLoading = false;
      if (!disposed) queueReconcile();
    });
  };

  const readT3PickerInstanceId = () => {
    const pickers = [...document.querySelectorAll('[data-chat-provider-model-picker="true"]')]
      .filter(picker => picker.closest?.('[data-chat-composer-main-surface="true"]'));
    if (pickers.length !== 1) return null;
    const key = Object.keys(pickers[0]).find(key => key.startsWith('__reactFiber$'));
    let fiber = key ? pickers[0][key] : null;
    if (!fiber) return null;
    let root = fiber;
    for (let depth = 0; root.return && depth < 128; depth++) root = root.return;
    if (root.return || !root.stateNode?.current) return null;
    if (root.stateNode.current !== root) {
      fiber = fiber.alternate;
      if (!fiber) return null;
      root = fiber;
      for (let depth = 0; root.return && depth < 128; depth++) root = root.return;
      if (root.return || root.stateNode?.current !== root) return null;
    }
    for (let depth = 0; fiber && depth < 64; depth++, fiber = fiber.return) {
      const props = fiber.memoizedProps;
      if (props?.isComposerOwned === true && typeof props.activeInstanceId === 'string'
        && props.activeInstanceId) return props.activeInstanceId;
    }
    return null;
  };

  const refreshT3AccountState = () => {
    snapshot = { primary: null, secondary: null };
    availableResets = null;
    quotaNotReported = false;
    providerLabel = 'Provider';
    quotaUnavailableReason = 'Waiting for committed composer provider data.';
    const props = readT3ComposerProps();
    if (!props) return;
    // The picker includes the unsent composer draft selection. The owning
    // thread's saved model can still name the previously selected provider.
    const instanceId = readT3PickerInstanceId();
    if (typeof instanceId !== 'string' || !instanceId) return;
    const matches = props.providerStatuses.filter((provider) => provider?.instanceId === instanceId);
    if (matches.length !== 1) return;
    const provider = matches[0];
    const name = provider.displayName || provider.name;
    providerLabel = typeof name === 'string' && name.trim() && name.length <= 80 ? name.trim()
      : ({ codex: 'Codex', claudeAgent: 'Claude', grok: 'Grok' }[provider.driver || instanceId] || 'Provider');
    refreshT3Provider(props, provider);
    const limits = provider.usageLimits;
    const checkedAt = Date.parse(limits?.checkedAt);
    quotaUnavailableReason = `${providerLabel} usage is unavailable or out of date.`;
    if (!provider.enabled || !provider.installed || limits?.unavailable
      || !Number.isFinite(checkedAt) || checkedAt > Date.now() + 60000
      || Date.now() - checkedAt > 15 * 60000 || !Array.isArray(limits.windows)) {
      if (limits?.unavailable?.reason === 'unsupported') quotaUnavailableReason = `${providerLabel} does not report subscription limits for this account.`;
      else if (provider.auth?.status === 'unauthenticated') quotaUnavailableReason = `${providerLabel} needs to sign in before usage can be read.`;
      return;
    }
    const resetCount = limits.resetCredits?.availableCount;
    quotaNotReported = limits.windows.length === 0;
    quotaUnavailableReason = quotaNotReported ? `${providerLabel} has not reported a usage percentage yet.`
      : `${providerLabel} has no valid current usage windows.`;
    availableResets = Number.isSafeInteger(resetCount) && resetCount >= 0 ? resetCount : null;
    for (const kind of ['session', 'weekly', 'monthly', 'other']) {
      const windows = limits.windows.filter((value) => value?.kind === kind);
      if (windows.length !== 1) continue;
      const value = windows[0];
      if (!Number.isFinite(value.usedPercent) || value.usedPercent < 0 || value.usedPercent > 100) continue;
      const minutes = value.windowDurationMins;
      const reset = Date.parse(value.resetsAt);
      if (value.resetsAt !== undefined && (!Number.isFinite(reset) || reset <= Date.now())) continue;
      let normalized = Number.isSafeInteger(minutes) && minutes > 0 && Number.isFinite(reset)
        ? normalizeAccountWindow({ used_percent: value.usedPercent, limit_window_seconds: minutes * 60,
          reset_at: Math.floor(reset / 1000) }) : null;
      normalized ||= { usedPercent: value.usedPercent, state: value.usedPercent >= 95 ? 'danger' : 'neutral' };
      // Calendar allowances can report a reset without a fixed duration.
      // Preserve that reset, without inventing a pacing arc.
      if (Number.isFinite(reset) && !normalized.resetLabel) {
        normalized.resetsAtUnixSeconds = Math.floor(reset / 1000);
        normalized.resetLabel = new Date(reset).toLocaleString([], { weekday: 'short', hour: 'numeric', minute: '2-digit' });
      }
      normalized.label = typeof value.label === 'string' && value.label.trim() && value.label.length <= 80
        ? value.label.trim() : ({ session: 'Session', weekly: 'Weekly', monthly: 'Monthly' }[kind] || 'Subscription');
      const slot = kind === 'session' ? 'primary' : kind === 'weekly' ? 'secondary'
        : !snapshot.primary ? 'primary' : !snapshot.secondary ? 'secondary' : null;
      if (slot) snapshot[slot] = normalized;
    }
    if (snapshot.primary || snapshot.secondary) quotaUnavailableReason = null;
  };

  const style = document.createElement('style');
  style.dataset.codexHelperStyle = owner;
  style.textContent = `
    [data-codex-helper="usage-dials"] {
      display: inline-flex;
      position: static;
      flex: 0 0 auto;
      align-items: center;
      gap: 4px;
      height: 24px;
      color: inherit;
      font: inherit;
    }
    [data-codex-helper-dial] {
      display: inline-flex;
      position: static;
      flex: 0 0 auto;
      align-items: center;
      justify-content: center;
      width: 16px;
      height: 24px;
      --codex-helper-usage-neutral: currentColor;
      --codex-helper-usage-alert: var(--codex-helper-usage-neutral);
      cursor: default;
    }
    [data-codex-helper-dial][data-state="warning"] { --codex-helper-usage-alert: #d89614; }
    [data-codex-helper-dial][data-state="danger"] { --codex-helper-usage-alert: #e5484d; }
    [data-codex-helper-dial][data-state="unavailable"] { opacity: .4; }
    [data-codex-helper-dial] svg { display: block; width: 16px; height: 16px; overflow: visible; }
    [data-codex-helper-dial] .codex-helper-segment { fill: none; stroke-width: 2; }
    [data-codex-helper-dial] .codex-helper-background { stroke: var(--codex-helper-usage-neutral); opacity: .16; }
    [data-codex-helper-dial][data-presentation="danger"] .codex-helper-background { stroke: var(--codex-helper-usage-alert); }
    [data-codex-helper-dial] .codex-helper-value { stroke: var(--codex-helper-usage-alert); transition: stroke-dasharray 180ms ease, stroke-dashoffset 180ms ease; }
    [data-codex-helper-dial] .codex-helper-unused { stroke: var(--codex-helper-usage-neutral); opacity: .4; }
    [data-codex-helper-dial] .codex-helper-within { stroke: var(--codex-helper-usage-neutral); }
    [data-codex-helper-dial] .codex-helper-ahead { stroke: var(--codex-helper-usage-alert); }
    /* Match Codex's neutral dial paint, independent of T3's muted controls.
       Scope the palette to both the miniature and its separate hover preview. */
    [data-codex-helper-dial][data-wingman-app="t3-code"] { color: #000; }
    .dark [data-codex-helper-dial][data-wingman-app="t3-code"] { color: #fff; }
    /* Keep the original composer circle; only the enlarged popup uses an outline. */
    .codex-helper-usage-preview [data-codex-helper-dial] .codex-helper-unused { stroke-width: 2px; vector-effect: non-scaling-stroke; }
    .codex-helper-usage-shared { opacity: .6; }
    .codex-helper-usage-resets { border-top: 1px solid var(--color-token-border, #444); margin-top: 4px; padding-top: 4px; }
    .codex-helper-usage-heading { color: inherit; opacity: .6; }
    .codex-helper-usage-preview { display: flex; justify-content: center; gap: 8px; padding: 6px 0 8px; margin-bottom: 4px; border-bottom: 1px solid var(--color-token-border, #444); }
    .codex-helper-usage-preview [data-codex-helper-dial],
    .codex-helper-usage-preview [data-codex-helper-dial] svg { width: 76px; height: 76px; }
  `;
  document.documentElement.appendChild(style);

  const isVerifiedAnchor = (dial) => {
    if (!dial || !['img', 'progressbar'].includes(dial.getAttribute('role'))
      && dial.tagName !== 'BUTTON') return false;
    if (!/^Context usage:\s*\d+(?:\.\d+)?%/i.test(dial.getAttribute('aria-label') || '')) return false;
    const wrapper = dial.parentElement;
    const row = wrapper?.parentElement;
    if (!wrapper || !row) return false;
    const rowStyle = getComputedStyle(row);
    return (rowStyle.display === 'flex' || rowStyle.display === 'inline-flex')
      && rowStyle.alignItems === 'center';
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

  const findFallbackAnchors = () => {
    const groups = [...document.querySelectorAll('div')].map((group) => {
      const classes = new Set(String(group.className || '').split(/\s+/));
      if (!classes.has('flex') || !classes.has('min-w-0')
        || !classes.has('items-center')
        || ![...classes].some((name) => /^gap-(?:\d+(?:\.\d+)?|\[[^\]]+\])$/.test(name))) return null;
      const modelWrapper = [...group.children].find((child) => {
        if (child.getAttribute('data-codex-helper') || child.getAttribute('data-codex-helper-context-slot')) return false;
        const text = String(child.textContent || '');
        const effortLabel = /(?:codex|sol|luna|gpt|sonnet|opus|haiku|claude)[\s\S]{0,100}(?:medium|high|low|extra|mini|pro|auto)/i.test(text);
        const modelName = /\b(?:gpt[-\s]?\d|codex|sol|luna|astra|sonnet|opus|haiku|claude)\b/i.test(text);
        return [...child.querySelectorAll('button,[role="button"],[role="combobox"],[role="listbox"]')]
          .some((control) => isVisible(control) && (effortLabel
            || (modelName && /model/i.test([
              control.getAttribute('aria-label'), control.getAttribute('title'),
              control.getAttribute('data-testid'),
            ].filter(Boolean).join(' ')))));
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
      return { mode: 'fallback', wrapper };
    });
  };

  const findAnchors = () => {
    if (t3Renderer) {
      return [...document.querySelectorAll('[data-chat-provider-model-picker="true"]')]
        .filter(picker => picker.closest?.('[data-chat-composer-main-surface="true"]') && isVisible(picker))
        .map(picker => ({ mode: 'model-picker', wrapper: picker }));
    }
    const native = [...document.querySelectorAll('[aria-label^="Context usage:"]')]
      .filter(isVerifiedAnchor)
      .map((dial) => ({ mode: 'native', dial, wrapper: dial.parentElement }));
    return native.length ? native : findFallbackAnchors();
  };

  const formatUsage = (value) => Number.isFinite(value) ? `${Math.round(value * 10) / 10}%` : 'unavailable';
  const elapsedPercent = (value) => {
    if (!Number.isFinite(value?.windowMinutes) || value.windowMinutes <= 0 || !Number.isFinite(value?.resetsAtUnixSeconds))
      return null;
    const duration = value.windowMinutes * 60 * 1000;
    const reset = value.resetsAtUnixSeconds * 1000;
    const start = reset - duration;
    return Math.round(Math.max(0, Math.min(1, (Date.now() - start) / duration)) * 100);
  };

  const formatPercentAttribute = (value) => String(Math.round(value * 100) / 100);
  const setAttributeIfChanged = (element, name, value) => {
    const next = String(value);
    if (element.getAttribute(name) !== next) element.setAttribute(name, next);
  };
  const setSegment = (segment, startPercent, endPercent) => {
    const start = Math.max(0, Math.min(100, startPercent));
    const end = Math.max(start, Math.min(100, endPercent));
    const visible = end > start;
    const display = visible ? '' : 'none';
    if (segment.style.display !== display) segment.style.display = display;
    setAttributeIfChanged(segment, 'data-start-percent', formatPercentAttribute(start));
    setAttributeIfChanged(segment, 'data-end-percent', formatPercentAttribute(end));
    if (!visible) return;
    if (segment.tagName.toLowerCase() === 'path') {
      // Leave the background visible inside the outline, at both dial sizes.
      const point = (radius, percent) => {
        const angle = percent * Math.PI / 50;
        return `${10 + radius * Math.cos(angle)} ${10 + radius * Math.sin(angle)}`;
      };
      const arc = (radius, from, to, sweep) => {
        const middle = (from + to) / 2;
        return `A ${radius} ${radius} 0 0 ${sweep} ${point(radius, middle)} A ${radius} ${radius} 0 0 ${sweep} ${point(radius, to)}`;
      };
      // Two arcs also support an entire unused ring without a degenerate SVG arc.
      const outer = `M ${point(8, start)} ${arc(8, start, end, 1)}`;
      const inner = `${point(6, end)} ${arc(6, end, start, 0)} Z`;
      setAttributeIfChanged(segment, 'd', end - start === 100
        ? `${outer} Z M ${inner}` : `${outer} L ${inner}`);
      return;
    }
    const length = circumference * (end - start) / 100;
    setAttributeIfChanged(segment, 'stroke-dasharray', `${length} ${circumference - length}`);
    setAttributeIfChanged(segment, 'stroke-dashoffset', -circumference * start / 100);
  };

  const makeSegment = (name, className, roundCaps = false, outline = false) => {
    const segment = document.createElementNS(svgNamespace, outline ? 'path' : 'circle');
    segment.setAttribute('class', `codex-helper-segment ${className}`);
    segment.setAttribute('data-segment', name);
    segment.setAttribute('cx', '10');
    segment.setAttribute('cy', '10');
    segment.setAttribute('r', '7');
    segment.setAttribute('stroke-linecap', roundCaps ? 'round' : 'butt');
    segment.setAttribute('transform', 'rotate(-90 10 10)');
    return segment;
  };

  const makeDial = (key, windowLabel, preview = false) => {
    const dial = document.createElement('span');
    dial.className = 'text-token-description-foreground';
    if (t3Renderer) dial.dataset.wingmanApp = 't3-code';
    dial.dataset.codexHelperDial = key;
    dial.dataset.state = 'unavailable';
    dial.setAttribute('role', 'img');
    dial.setAttribute('tabindex', '0');
    const svg = document.createElementNS(svgNamespace, 'svg');
    svg.setAttribute('viewBox', '0 0 20 20');
    svg.setAttribute('aria-hidden', 'true');
    const background = makeSegment('background', 'codex-helper-background', true);
    const unused = makeSegment('unused', 'codex-helper-unused', false, preview);
    const within = makeSegment('within', 'codex-helper-within');
    const ahead = makeSegment('ahead', 'codex-helper-ahead');
    const conventionalValue = makeSegment('conventional-value', 'codex-helper-value', true);
    svg.append(background, unused, within, ahead, conventionalValue);
    dial.appendChild(svg);
    dial.__codexHelper = { background, unused, within, ahead, conventionalValue, windowLabel };
    return dial;
  };

  const renderDial = (dial, value, showUnavailable = false) => {
    const usage = Number.isFinite(value?.usedPercent) ? Math.max(0, Math.min(100, value.usedPercent)) : null;
    const elapsed = elapsedPercent(value);
    const hardDanger = usage !== null && usage >= 95;
    const conventional = elapsed === null || hardDanger;
    dial.style.display = usage === null && !showUnavailable ? 'none' : 'inline-flex';
    if (showUnavailable && !dial.__codexHelper.unknown) {
      const unknown = document.createElementNS(svgNamespace, 'text');
      unknown.setAttribute('x', '10');
      unknown.setAttribute('y', '14');
      unknown.setAttribute('text-anchor', 'middle');
      unknown.setAttribute('fill', 'currentColor');
      unknown.setAttribute('font-size', '12');
      unknown.textContent = '?';
      dial.querySelector('svg').appendChild(unknown);
      dial.__codexHelper.unknown = unknown;
    }
    if (dial.__codexHelper.unknown) {
      const text = quotaNotReported ? '–' : '?';
      if (dial.__codexHelper.unknown.textContent !== text) dial.__codexHelper.unknown.textContent = text;
      dial.__codexHelper.unknown.style.display = showUnavailable ? '' : 'none';
    }
    dial.dataset.state = usage === null ? 'unavailable' : (hardDanger ? 'danger' : (value.state || 'neutral'));
    dial.dataset.presentation = hardDanger ? 'danger' : (conventional ? 'conventional' : 'paced');
    setSegment(dial.__codexHelper.background, 0, usage === null ? 0 : 100);
    setSegment(dial.__codexHelper.conventionalValue, 0, conventional ? (usage ?? 0) : 0);
    setSegment(dial.__codexHelper.unused, conventional ? 0 : (usage ?? 0), conventional ? 0 : elapsed);
    setSegment(dial.__codexHelper.within, 0, conventional ? 0 : Math.min(usage ?? 0, elapsed));
    setSegment(dial.__codexHelper.ahead, conventional ? 0 : elapsed, conventional ? 0 : (usage ?? 0));
    const reset = value?.resetLabel ? ` Resets ${value.resetLabel}.` : '';
    setAttributeIfChanged(dial, 'aria-label', showUnavailable ? `Usage unavailable. ${quotaUnavailableReason}`
      : `${formatWindowLabel(value, dial.__codexHelper.windowLabel)}: ${formatUsage(usage)}.${reset}`);
    dial.removeAttribute('title');
  };

  const formatWindowLabel = (value, fallback) => {
    if (t3Renderer && value?.label) return value.label;
    const minutes = value?.windowMinutes;
    if (!Number.isFinite(minutes) || minutes <= 0) return fallback;
    if (minutes === 300) return '5-hour usage';
    if (minutes === 10080) return 'Weekly usage';
    if (minutes % 60 === 0) return `${minutes / 60}-hour usage`;
    if (minutes % (24 * 60) === 0) return `${minutes / (24 * 60)}-day usage`;
    return `${minutes}-minute usage`;
  };

  const createInstance = (anchor) => {
    const bank = document.createElement('span');
    bank.dataset.codexHelper = owner;
    bank.setAttribute('aria-label', t3Renderer ? 'T3 provider usage' : 'Codex account usage');
    const primary = makeDial('primary', '5-hour usage');
    const secondary = makeDial('secondary', 'Weekly usage');
    bank.append(primary, secondary);
    const instance = { wrapper: anchor.wrapper, anchor: anchor.dial || anchor.wrapper, mode: anchor.mode, bank, primary, secondary };
    const selectWindow = (event) => {
      const dial = event.target?.closest?.('[data-codex-helper-dial]');
      instance.activeWindow = dial && bank.contains(dial) ? dial.dataset.codexHelperDial
        : (snapshot.primary ? 'primary' : 'secondary');
      showFallbackTooltip(instance);
    };
    instance.onPointerOver = selectWindow;
    instance.onPointerOut = (event) => {
      if (bank.contains(event.relatedTarget)) return;
      hideFallbackTooltip(instance);
    };
    instance.onFocusIn = selectWindow;
    instance.onFocusOut = instance.onPointerOut;
    bank.addEventListener('pointerover', instance.onPointerOver);
    bank.addEventListener('pointerout', instance.onPointerOut);
    bank.addEventListener('focusin', instance.onFocusIn);
    bank.addEventListener('focusout', instance.onFocusOut);
    return instance;
  };

  const destroyInstance = (instance) => {
    instance.bank.removeEventListener('pointerover', instance.onPointerOver);
    instance.bank.removeEventListener('pointerout', instance.onPointerOut);
    instance.bank.removeEventListener('focusin', instance.onFocusIn);
    instance.bank.removeEventListener('focusout', instance.onFocusOut);
    hideFallbackTooltip(instance);
    instance.bank.remove();
    if (instance.mode === 'fallback') instance.wrapper?.remove();
  };

  const insertBefore = (parent, node, reference) => {
    if (parent.insertBefore) {
      parent.insertBefore(node, reference);
      return;
    }
    if (node.parentNode) node.remove();
    const index = parent.children.indexOf(reference);
    node.parentNode = parent;
    parent.children.splice(Math.max(0, index), 0, node);
  };

  const updateInstance = (instance) => {
    if (t3Renderer) setAttributeIfChanged(instance.bank, 'aria-label', `${providerLabel} usage`);
    renderDial(instance.primary, snapshot.primary, t3Renderer && !snapshot.primary && !snapshot.secondary);
    renderDial(instance.secondary, snapshot.secondary);
    if (!instance.wrapper?.parentElement) return;
    if (t3Renderer) {
      if (instance.bank.parentElement !== instance.wrapper.parentElement || instance.bank.nextElementSibling !== instance.wrapper)
        insertBefore(instance.wrapper.parentElement, instance.bank, instance.wrapper);
    } else if (instance.bank.parentElement !== instance.wrapper.parentElement || instance.bank.previousElementSibling !== instance.wrapper)
      instance.wrapper.insertAdjacentElement('afterend', instance.bank);
  };

  const reconcile = () => {
    connectAccountCache();
    refreshAccountState();
    const anchors = findAnchors();
    const activeAnchors = new Set(anchors.map((anchor) => anchor.wrapper));
    for (const anchor of anchors) {
      const key = anchor.wrapper;
      let instance = instances.get(key);
      if (!instance) {
        instance = createInstance(anchor);
        instances.set(key, instance);
      } else {
        instance.wrapper = anchor.wrapper;
        instance.anchor = anchor.dial || anchor.wrapper;
        instance.mode = anchor.mode;
      }
      updateInstance(instance);
    }
    for (const [key, instance] of [...instances]) {
      if (!activeAnchors.has(key) || !instance.anchor?.isConnected) {
        destroyInstance(instance);
        instances.delete(key);
      }
    }
    for (const instance of instances.values()) {
      if (instance.tooltip) showFallbackTooltip(instance);
    }
    return anchors.length;
  };

  const makeText = (text, className = '') => {
    const span = document.createElement('span');
    span.textContent = text;
    if (className) span.className = className;
    return span;
  };

  const appendWindowReadout = (stack, label, value, addSpacing) => {
    const usage = Number.isFinite(value?.usedPercent) ? Math.max(0, Math.min(100, value.usedPercent)) : null;
    const remaining = usage === null ? null : Math.max(0, 100 - usage);
    const elapsed = elapsedPercent(value);
    stack.appendChild(makeText(`${label}:`, `${addSpacing ? 'mt-1 ' : ''}whitespace-pre-line codex-helper-usage-heading`));
    stack.appendChild(makeText(usage === null ? 'Usage unavailable' : `${formatUsage(usage)} used (${formatUsage(remaining)} left)`));
    stack.appendChild(makeText(elapsed === null ? 'Time elapsed unavailable' : `${elapsed}% of time elapsed`));
    if (value?.resetLabel) stack.appendChild(makeText(`Resets ${value.resetLabel}`));
  };

  const appendAvailableReadouts = (stack, key) => {
    if (snapshot[key]) appendWindowReadout(stack, formatWindowLabel(snapshot[key], key === 'primary' ? '5-hour usage' : 'Weekly usage'), snapshot[key], false);
    const resetLabel = availableResets === null ? 'Resets unavailable'
      : `${availableResets} ${availableResets === 1 ? 'reset' : 'resets'} available`;
    stack.appendChild(makeText(resetLabel, 'codex-helper-usage-resets'));
    if (snapshot.primary && snapshot.secondary) stack.appendChild(makeText('Shared balance', 'codex-helper-usage-shared'));
  };

  const showFallbackTooltip = (instance) => {
    const unavailable = t3Renderer && !snapshot.primary && !snapshot.secondary;
    const key = unavailable ? 'primary' : snapshot[instance.activeWindow] ? instance.activeWindow : (snapshot.primary ? 'primary' : 'secondary');
    const label = key === 'primary' ? '5-hour usage' : 'Weekly usage';
    let tooltip = instance.tooltip;
    if (!tooltip) {
      tooltip = document.createElement('div');
      tooltip.dataset.codexHelperTooltip = 'usage-fallback';
      tooltip.setAttribute('role', 'tooltip');
      tooltip.id = `codex-helper-usage-tooltip-${instances.size}`;
      tooltip.className = 'z-50 w-fit select-none text-sm whitespace-normal break-words bg-token-dropdown-background text-token-foreground border-token-border rounded-lg border px-2 py-1';
      tooltip.style.position = 'fixed';
      tooltip.style.zIndex = '2147483647';
      tooltip.style.pointerEvents = 'none';
      if (t3Renderer) {
        tooltip.style.background = 'var(--color-popover, #1b1b1b)';
        tooltip.style.color = 'var(--color-popover-foreground, #eee)';
        tooltip.style.border = '1px solid var(--color-border, #444)';
        tooltip.style.borderRadius = '8px';
        tooltip.style.padding = '8px';
      }
      instance.tooltip = tooltip;
      instance.bank.setAttribute('aria-describedby', tooltip.id);
      document.body.appendChild(tooltip);
    }
    for (const dial of [instance.primary, instance.secondary]) dial.removeAttribute('aria-describedby');
    instance[key].setAttribute('aria-describedby', tooltip.id);
    tooltip.replaceChildren();
    const stack = document.createElement('div');
    stack.className = 'flex w-38 flex-col gap-0.5 text-center';
    const preview = document.createElement('div');
    preview.className = 'codex-helper-usage-preview';
    if (snapshot[key]) {
      const dial = makeDial(key, label, true);
      dial.removeAttribute('tabindex');
      // The original 20-unit canvas includes padding; crop it so the visible
      // ring (radius 7 plus its 1-unit half-stroke) is exactly 76px across.
      dial.querySelector('svg').setAttribute('viewBox', '2 2 16 16');
      renderDial(dial, snapshot[key]);
      preview.appendChild(dial);
    }
    if (unavailable) {
      stack.appendChild(makeText(`${providerLabel} usage`));
      stack.appendChild(makeText(quotaUnavailableReason));
    } else {
      stack.appendChild(preview);
      appendAvailableReadouts(stack, key);
    }
    tooltip.appendChild(stack);
    const bankRect = instance[key].getBoundingClientRect();
    const tooltipRect = tooltip.getBoundingClientRect();
    const margin = 4;
    const viewportWidth = window.innerWidth || document.documentElement?.clientWidth || tooltipRect.width + margin * 2;
    const viewportHeight = window.innerHeight || document.documentElement?.clientHeight || tooltipRect.height + margin * 2;
    const left = Math.min(Math.max(margin, bankRect.left + (bankRect.width - tooltipRect.width) / 2), Math.max(margin, viewportWidth - tooltipRect.width - margin));
    const below = bankRect.bottom + 6;
    const top = below + tooltipRect.height <= viewportHeight - margin
      ? below
      : Math.max(margin, bankRect.top - tooltipRect.height - 6);
    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
  };

  const hideFallbackTooltip = (instance) => {
    if (!instance.tooltip) return;
    instance.tooltip.remove();
    instance.tooltip = null;
    instance.bank.removeAttribute('aria-describedby');
    for (const dial of [instance.primary, instance.secondary]) dial.removeAttribute('aria-describedby');
  };

  const status = () => ({
    fallbackStrategy: controllerVersion,
    attachedCount: [...instances.values()].filter((instance) => instance.bank.isConnected).length,
    anchorCount: findAnchors().length,
    primary: snapshot.primary || null,
    secondary: snapshot.secondary || null,
    availableResets,
    ...(t3Renderer ? { renderHealth: {
      state: !document.querySelector('[data-testid="composer-editor"]') ? 'not-applicable'
        : [...instances.values()].some(instance => instance.bank.isConnected && instance.bank.getBoundingClientRect().width > 0)
          && (snapshot.primary || snapshot.secondary) ? 'ready' : 'waiting',
      detail: quotaUnavailableReason || ([...instances.values()].some(instance => instance.bank.isConnected)
        ? `${providerLabel} usage is visible.` : 'Usage Dials could not attach to the visible composer.'),
    } } : {}),
  });

  const update = (nextSnapshot) => {
    fallbackSnapshot = nextSnapshot || {};
    reconcile();
    return status();
  };

  const queueReconcile = () => {
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      reconcile();
    });
  };
  const observer = new MutationObserver((mutations) => {
    if (mutations.some((mutation) => !mutation.target.closest?.('[data-codex-helper-tooltip]'))) {
      queueReconcile();
    }
  });
  observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-label'] });
  const interval = setInterval(reconcile, 2000);

  const cleanup = () => {
    disposed = true;
    observer.disconnect();
    unsubscribeAccount?.();
    accountClient = null;
    clearInterval(interval);
    cancelAnimationFrame(frame);
    for (const instance of instances.values()) destroyInstance(instance);
    instances.clear();
    document.querySelectorAll('[data-codex-helper-tooltip]').forEach((section) => section.remove());
    document.querySelectorAll('[data-codex-helper-context-slot="usage-dials"]').forEach((slot) => slot.remove());
    style.remove();
    delete window.__codexHelperUsageDials;
    return true;
  };

  window.__codexHelperUsageDials = { version: controllerVersion, update, cleanup, status };
  const result = update(initialSnapshot);
  if (typeof wingman !== 'undefined') wingman.reportRenderHealth?.(result.renderHealth);
  return result;
})()
