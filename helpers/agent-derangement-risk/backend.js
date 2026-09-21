// Change this one line to select the deployed algorithm: existing, milestones, smooth-age, or paired.
var DERANGEMENT_RISK_ALGORITHM = 'smooth-age';
var DERANGEMENT_RISK_ALGORITHMS = {
  existing: {
    description: 'Current fill, compaction, and cumulative-load formula.',
    calculate: function (inputs) {
      return clamp(0.55 * inputs.fill
        + 0.25 * Math.min(inputs.compactions, 3) / 3
        + 0.20 * Math.min(Math.log(1 + inputs.cumulativeRatio) / Math.log(5), 1));
    }
  },
  milestones: {
    description: 'Stepwise points at compaction, fresh-processing, and fill milestones.',
    calculate: function (inputs) {
      return Math.min(100,
        20 * (inputs.compactions >= 4) + 20 * (inputs.compactions >= 8)
        + 10 * (inputs.compactions >= 16) + 20 * (inputs.freshRatio >= 2)
        + 20 * (inputs.freshRatio >= 5) + 10 * (inputs.freshRatio >= 15)
        + 10 * (inputs.fill >= 0.60) + 10 * (inputs.fill >= 0.80)) / 100;
    }
  },
  'smooth-age': {
    description: 'Smooth history score weighted by compactions, fresh processing, and current fill.',
    calculate: function (inputs) {
      return clamp(0.50 * Math.min(inputs.compactions / 8, 1)
        + 0.40 * Math.min(inputs.freshRatio / 6, 1) + 0.10 * inputs.fill);
    }
  },
  paired: {
    description: 'Corroborating history score using the geometric mean of compactions and fresh processing.',
    calculate: function (inputs) {
      return clamp(0.85 * Math.sqrt(Math.min(inputs.compactions / 8, 1)
        * Math.min(inputs.freshRatio / 6, 1)) + 0.15 * inputs.fill);
    }
  }
};

function clamp(value) {
  return Math.max(0, Math.min(1, value));
}

function calculateDerangementRisk(inputs, algorithmName) {
  var name = algorithmName === undefined ? DERANGEMENT_RISK_ALGORITHM : algorithmName;
  var algorithm = Object.prototype.hasOwnProperty.call(DERANGEMENT_RISK_ALGORITHMS, name)
    ? DERANGEMENT_RISK_ALGORITHMS[name] : null;
  if (!algorithm || !inputs || !Number.isFinite(inputs.fill) || inputs.fill < 0 || inputs.fill > 1
    || !Number.isSafeInteger(inputs.compactions) || inputs.compactions < 0
    || !Number.isFinite(inputs.cumulativeRatio) || inputs.cumulativeRatio < 0
    || (name !== 'existing' && (!Number.isFinite(inputs.freshRatio) || inputs.freshRatio < 0))) {
    return null;
  }
  return clamp(algorithm.calculate(inputs));
}

function bandForRisk(risk) {
  if (risk === null || !Number.isFinite(risk)) return 'unavailable';
  return risk < 0.60 ? 'healthy' : risk < 0.85 ? 'watch' : 'handoff';
}

function refresh(wingman) {
  var sessions = wingman.files.codexSessions;
  var aliases = {
    used: ['usedtokens', 'used_tokens', 'contextusedtokens', 'context_used_tokens', 'inputtokens', 'input_tokens'],
    max: ['maxtokens', 'max_tokens', 'contextmaxtokens', 'context_max_tokens', 'modelcontextwindow', 'model_context_window'],
    compact: ['compactions', 'compactioncount', 'compaction_count'],
    cumulative: ['cumtokens', 'cum_tokens', 'cumulativetokens', 'cumulative_tokens', 'totaltokens', 'total_tokens']
  };

  function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
  }

  function keyName(value) {
    return String(value).toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  function findNumber(root, names, integer) {
    var wanted = names.map(keyName);
    var seen = [];
    function visit(value, depth) {
      if (!isObject(value) || depth > 5 || seen.indexOf(value) >= 0) return null;
      seen.push(value);
      var keys = Object.keys(value);
      for (var i = 0; i < keys.length; i++) {
        var candidate = value[keys[i]];
        if (wanted.indexOf(keyName(keys[i])) >= 0 && typeof candidate === 'number'
          && Number.isFinite(candidate) && (!integer || Number.isSafeInteger(candidate))) return candidate;
      }
      for (var j = 0; j < keys.length; j++) {
        var result = visit(value[keys[j]], depth + 1);
        if (result !== null) return result;
      }
      return null;
    }
    return visit(root, 0);
  }

  function readPathNumber(root, path) {
    var value = root;
    for (var i = 0; i < path.length; i++) {
      if (!isObject(value)) return null;
      value = value[path[i]];
    }
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }

  function calculate(outer, compactionOverride) {
    var payload = isObject(outer && outer.payload) ? outer.payload : outer;
    var used = readPathNumber(payload, ['info', 'last_token_usage', 'input_tokens']);
    if (used === null) used = findNumber(payload, aliases.used, false);
    var max = readPathNumber(payload, ['info', 'model_context_window']);
    if (max === null) max = findNumber(payload, aliases.max, false);
    if (!(used >= 0) || !(max > 0)) return null;

    var discovered = findNumber(payload, aliases.compact, true);
    var overridePresent = compactionOverride !== undefined;
    if (overridePresent && (!Number.isSafeInteger(compactionOverride) || compactionOverride < 0)
      && DERANGEMENT_RISK_ALGORITHM !== 'existing') {
      return { usedTokens: used, maxTokens: max, compactions: null, cumTokens: null, risk: null, band: 'unavailable' };
    }
    var compactions = overridePresent ? compactionOverride : discovered;
    var exactCumulative = readPathNumber(payload, ['info', 'total_token_usage', 'total_tokens']);
    var cumulative = exactCumulative;
    if (cumulative === null) cumulative = findNumber(payload, aliases.cumulative, false);
    if (typeof cumulative !== 'number' || !Number.isFinite(cumulative) || cumulative < 0) cumulative = used;
    var fill = clamp(used / max);
    var cached = readPathNumber(payload, ['info', 'total_token_usage', 'cached_input_tokens']);
    var freshRatio = exactCumulative === null || cached === null
      ? null : Math.max(exactCumulative - cached, 0) / max;
    if (compactions === null) compactions = 0;
    if (DERANGEMENT_RISK_ALGORITHM !== 'existing'
      && ((!overridePresent && discovered === null) || !Number.isSafeInteger(compactions)
        || compactions < 0 || freshRatio === null)) {
      return { usedTokens: used, maxTokens: max,
        compactions: Number.isSafeInteger(compactionOverride) ? compactionOverride : null,
        cumTokens: cumulative, risk: null, band: 'unavailable' };
    }
    var risk = calculateDerangementRisk({ fill: fill, compactions: compactions,
      cumulativeRatio: cumulative / max, freshRatio: freshRatio === null ? 0 : freshRatio });
    return { usedTokens: used, maxTokens: max, compactions: compactions, cumTokens: cumulative,
      risk: risk, band: bandForRisk(risk) };
  }

  function calculateSnapshots(value) {
    if (!isObject(value)) return null;
    var scoped = {};
    Object.keys(value).forEach(function (id) {
      var entry = value[id];
      var metrics = calculate(entry && entry.event ? entry.event : entry, entry && entry.compactions);
      if (metrics) scoped[id] = metrics;
    });
    return Object.keys(scoped).length ? { byThreadId: scoped, latest: null } : null;
  }

  function unavailable() {
    return { usedTokens: null, maxTokens: null, compactions: null, cumTokens: null, risk: null, band: 'unavailable' };
  }

  var targetSession = wingman.files.codexTargetSession;
  if (targetSession && targetSession.snapshot) {
    var target = targetSession.snapshot();
    var targetMetrics = target ? calculate(target.event || target, target.compactions) : null;
    return Object.assign({ threadId: target && target.threadId || null }, targetMetrics || unavailable());
  }
  try {
    var hostSnapshots = sessions.snapshots ? calculateSnapshots(sessions.snapshots()) : null;
    if (hostSnapshots) return hostSnapshots;
  } catch (error) {
    wingman.log('Agent Derangement Risk snapshot lookup failed: ' + String(error && error.message || error));
  }

  function sessionId(event) {
    var payload = isObject(event && event.payload) ? event.payload : null;
    if (!payload || (payload.type !== 'session_meta' && event.type !== 'session_meta')) return null;
    var id = payload.id || payload.session_id || event.session_id || event.id;
    return typeof id === 'string' && id ? id : null;
  }

  var newest = null;
  var byThreadId = {};
  var roots = sessions.roots();
  roots.forEach(function (root) {
    var candidates;
    try { candidates = sessions.listFiles(root); } catch (error) { return; }
    if (!Array.isArray(candidates)) {
      wingman.log('Agent Derangement Risk received a non-array session file list; skipping root.');
      return;
    }
    var files = candidates.filter(function (path) { return /\.jsonl$/i.test(String(path)); }).slice(0, 16);
    files.forEach(function (file) {
      try {
        var lines = String(sessions.readText(file) || '').split(/\r?\n/);
        var threadId = null;
        for (var i = 0; i < Math.min(lines.length, 8); i++) {
          try { threadId = sessionId(JSON.parse(lines[i])); } catch (_) {}
          if (threadId) break;
        }
        var metric = null;
        for (var j = lines.length - 1; j >= Math.max(0, lines.length - 256); j--) {
          if (!/(?:used[\s_-]*tokens|max[\s_-]*tokens|context[\s_-]*(?:window|used|max)|input[\s_-]*tokens|model[\s_-]*context[\s_-]*window|total[\s_-]*token[\s_-]*usage)/i.test(lines[j])) continue;
          var event = JSON.parse(lines[j]);
          var metrics = calculate(event);
          if (metrics && Number.isFinite(Date.parse(event.timestamp || ''))) {
            metric = { timestamp: Date.parse(event.timestamp), metrics: metrics };
            break;
          }
        }
        if (metric && threadId && (!byThreadId[threadId] || metric.timestamp > byThreadId[threadId].timestamp)) byThreadId[threadId] = metric;
        else if (metric && (!newest || metric.timestamp > newest.timestamp)) newest = metric;
      } catch (_) {}
    });
  });
  var ids = Object.keys(byThreadId);
  if (ids.length) {
    var scoped = {};
    ids.forEach(function (id) { scoped[id] = byThreadId[id].metrics; });
    return { byThreadId: scoped, latest: newest ? newest.metrics : null };
  }
  return newest ? newest.metrics : unavailable();
}
