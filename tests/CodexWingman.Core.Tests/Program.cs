using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using CodexWingman.Core;
using CodexWingman.Core.Helpers;
using Jint;

if (args.Contains("--live-quota", StringComparer.Ordinal))
{
    try
    {
        var liveRoots = SessionQuotaSource.DefaultRoots();
        Console.Error.WriteLine($"Quota roots: {string.Join("; ", liveRoots)}");
        var live = await new SessionQuotaSource(liveRoots).ReadAsync();
        Console.WriteLine(JsonSerializer.Serialize(live));
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"Live quota probe failed cleanly: {error.Message}");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Contains("--tray-visibility", StringComparer.Ordinal))
{
    TestTrayVisibilityRecovery();
    Console.WriteLine("CodexWingman tray visibility tests passed");
    return;
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

static void TestTrayVisibilityRecovery()
{
    var recoveryType = typeof(WingmanIdentity).Assembly.GetType("CodexWingman.Core.TrayVisibilityRecovery")
        ?? throw new InvalidOperationException("tray visibility recovery policy type is missing");
    var visibilityChanges = new List<bool>();

    var ensureVisible = recoveryType.GetMethod("EnsureVisible")
        ?? throw new InvalidOperationException("tray visibility watchdog policy is missing");
    visibilityChanges.Clear();
    ensureVisible.Invoke(null, [false, (Action<bool>)visibilityChanges.Add]);
    Equal("False,True", string.Join(',', visibilityChanges), "periodic tray watchdog re-registers a lost shell icon");

    visibilityChanges.Clear();
    ensureVisible.Invoke(null, [true, (Action<bool>)visibilityChanges.Add]);
    Equal(0, visibilityChanges.Count, "periodic tray watchdog does not restore the icon during explicit exit");
}

try
{
Equal("CodexWingman.Desktop.Singleton.v1", WingmanIdentity.SingleInstanceName, "stable single-instance identity");
Equal("CodexWingman.Desktop.Activate.v1", WingmanIdentity.ActivationEventName, "stable activation identity");
Equal("Codex Wingman", WingmanIdentity.RunningMenuText, "concise Wingman menu heading");
Equal("Close Wingman (leave Codex open)", WingmanIdentity.CloseMenuText, "non-destructive close menu text");
Equal(
    Path.GetFullPath(Path.Combine("C:\\Apps\\CodexWingman-verified", "Helpers")),
    WingmanIdentity.ResolveHelpersRoot("C:\\Apps\\CodexWingman-verified\\CodexWingman.exe"),
    "Helpers root is derived from the executable directory");
Equal(true, WingmanIdentity.IsCanonicalPackageDirectory("C:\\Apps\\CodexWingman-verified"), "verified package root is canonical");
Equal(false, WingmanIdentity.IsCanonicalPackageDirectory("C:\\Apps\\CodexWingman"), "legacy package root is rejected");

var longDiagnostic = "Needs attention: " + new string('x', 240);
Equal("Starting", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Starting, "booting").Label, "starting tray label");
Equal("Working", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Working, "working").Label, "working tray label");
Equal("Active", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Active, "active").Label, "active tray label");
Equal("Suspended", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Suspended, "suspended").Label, "suspended tray label");
Equal("Removed", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Removed, "removed").Label, "removed tray label");
Equal("Needs attention", TrayStatusPolicy.ForLifecycle(TrayLifecycleState.Error, longDiagnostic).Label, "error tray label");
Equal(longDiagnostic, TrayStatusPolicy.ForError(longDiagnostic).DiagnosticText, "long error diagnostic is preserved");
Equal("Codex not running", TrayStatusPolicy.ForCodexState(CodexHookState.NotOpen, "not open").Label, "not-open Codex status label");
Equal("Codex needs helper restart", TrayStatusPolicy.ForCodexState(CodexHookState.OpenButCannotHook, "cannot hook").Label, "unhookable Codex status label");
Equal("Codex connected", TrayStatusPolicy.ForCodexState(CodexHookState.Hooked, "hooked").Label, "hooked Codex status label");
Equal("Codex status unknown", TrayStatusPolicy.ForCodexState(CodexHookState.UnknownProblem, "unknown").Label, "unknown Codex status label");

var closedHealth = ConnectionHealthPolicy.Evaluate(CodexHookState.NotOpen, null, helpersSuspended: false);
Equal(ConnectionHealthState.CodexClosed, closedHealth.State, "closed Codex health state");
Equal("Status: Codex closed", closedHealth.Label, "closed Codex health label");
Equal("No Codex windows open", closedHealth.WindowSummary, "closed Codex window summary");

var unhookableHealth = ConnectionHealthPolicy.Evaluate(CodexHookState.OpenButCannotHook, null, helpersSuspended: false);
Equal(ConnectionHealthState.NeedsRepair, unhookableHealth.State, "unhookable Codex needs repair");
Equal(true, unhookableHealth.CanRepair, "unhookable Codex exposes repair");

var zeroTargetHealth = ConnectionHealthPolicy.Evaluate(
    CodexHookState.Hooked,
    new HelperHostReport(0, 0, 0, 0, []),
    helpersSuspended: false);
Equal(ConnectionHealthState.NeedsRepair, zeroTargetHealth.State, "zero-target endpoint needs repair");
Equal("Status: Needs repair", zeroTargetHealth.Label, "zero-target endpoint label");

var degradedHealth = ConnectionHealthPolicy.Evaluate(
    CodexHookState.Hooked,
    new HelperHostReport(7, 4, 27, 1, [new HelperDiagnostic("fixture", "fixture failed")]),
    helpersSuspended: false);
Equal(ConnectionHealthState.Degraded, degradedHealth.State, "failed reconciliation is degraded");
Equal("Status: Needs attention", degradedHealth.Label, "degraded status label");
Equal("Altering 4 of 4 windows", degradedHealth.WindowSummary, "degraded window coverage stays separate from Helper health");
Equal(false, degradedHealth.DiagnosticText.Contains("fixture", StringComparison.Ordinal), "degraded user details omit technical target diagnostics");
Equal(true, degradedHealth.DiagnosticText.Contains("What to do:", StringComparison.Ordinal), "degraded user details give a concrete next action");
Equal(true,
    degradedHealth.DiagnosticText.IndexOf("reload Helpers", StringComparison.OrdinalIgnoreCase)
        < degradedHealth.DiagnosticText.IndexOf("Repair Codex and Helpers", StringComparison.Ordinal),
    "degraded Helper health recommends reload before Codex repair");

var workingHealth = ConnectionHealthPolicy.Evaluate(
    CodexHookState.Hooked,
    new HelperHostReport(7, 4, 28, 0, []),
    helpersSuspended: false);
Equal(ConnectionHealthState.Working, workingHealth.State, "successful reconciliation is working");
Equal("Status: OK", workingHealth.Label, "working status label");
Equal("Altering 4 of 4 windows", workingHealth.WindowSummary, "working window coverage has its own menu line");
Equal(true, workingHealth.CanOpenWindow, "working endpoint can open another window");

var partialWindowHealth = ConnectionHealthPolicy.Evaluate(
    CodexHookState.Hooked,
    new HelperHostReport(7, 2, 14, 0, []),
    helpersSuspended: false,
    totalWindows: 4);
Equal("Altering 2 of 4 windows", partialWindowHealth.WindowSummary, "window summary distinguishes altered and total Codex windows");

var pausedHealth = ConnectionHealthPolicy.Evaluate(
    CodexHookState.Hooked,
    new HelperHostReport(0, 4, 0, 0, []),
    helpersSuspended: true);
Equal(ConnectionHealthState.HelpersPaused, pausedHealth.State, "suspended Helpers have distinct health");
Equal("Status: Helpers paused", pausedHealth.Label, "paused status label");
var pausedWithoutReport = ConnectionHealthPolicy.Evaluate(CodexHookState.Hooked, null, helpersSuspended: true);
Equal(ConnectionHealthState.HelpersPaused, pausedWithoutReport.State, "paused Helpers do not become a false repair failure during polling");

Equal(HookReadyWindowPlan.OpenNativeChild,
    HookReadyWindowPolicy.Decide(endpointReady: true, codexRunning: true),
    "hook-ready action reuses an existing endpoint");
Equal(HookReadyWindowPlan.LaunchNormalProfile,
    HookReadyWindowPolicy.Decide(endpointReady: false, codexRunning: false),
    "hook-ready action launches when Codex is absent");
Equal(HookReadyWindowPlan.ConfirmRestart,
    HookReadyWindowPolicy.Decide(endpointReady: false, codexRunning: true),
    "hook-ready action requests confirmation before restarting a running non-hookable Codex");

var automaticRefreshGate = new TrayRefreshGate();
Equal(true, automaticRefreshGate.TryEnter(busy: false, exiting: false), "automatic refresh starts when idle");
Equal(false, automaticRefreshGate.TryEnter(busy: false, exiting: false), "automatic refresh does not overlap itself");
automaticRefreshGate.Exit();
Equal(false, automaticRefreshGate.TryEnter(busy: true, exiting: false), "automatic refresh never takes the interactive busy path");
Equal(false, automaticRefreshGate.TryEnter(busy: false, exiting: true), "automatic refresh does not run during exit");
Equal(true, automaticRefreshGate.TryEnter(busy: false, exiting: false), "automatic refresh can run again after exit");
automaticRefreshGate.Exit();

var successfulReport = TrayStatusPolicy.ForReport(
    TrayLifecycleState.Active,
    new HelperHostReport(2, 4, 4, 0, []));
Equal("Active", successfulReport.Label, "successful report tray label");
Equal("Active: 4 succeeded, 0 failed across 4 windows", successfulReport.DiagnosticText, "successful report diagnostic");

var failedReport = TrayStatusPolicy.ForReport(
    TrayLifecycleState.Active,
    new HelperHostReport(2, 4, 3, 1, [new HelperDiagnostic("fixture", longDiagnostic)]));
Equal("Needs attention", failedReport.Label, "failed report tray label");
Equal($"Needs attention: 3 succeeded, 1 failed across 4 windows; {longDiagnostic}", failedReport.DiagnosticText, "failed report diagnostic");

var reset = DateTimeOffset.FromUnixTimeSeconds(10_000);
var now = reset.AddHours(-2.5);

Equal(QuotaVisualState.Neutral,
    QuotaPolicy.Evaluate(new QuotaWindow(51, 300, reset.ToUnixTimeSeconds()), now),
    "usage exactly one point ahead stays neutral");
Equal(QuotaVisualState.Warning,
    QuotaPolicy.Evaluate(new QuotaWindow(51.01, 300, reset.ToUnixTimeSeconds()), now),
    "usage over one point ahead warns");
Equal(QuotaVisualState.Warning,
    QuotaPolicy.Evaluate(new QuotaWindow(95, 300, reset.ToUnixTimeSeconds()), now),
    "95 percent is warning");
Equal(QuotaVisualState.Danger,
    QuotaPolicy.Evaluate(new QuotaWindow(95.01, 300, reset.ToUnixTimeSeconds()), now),
    "over 95 percent while ahead is danger");
Equal(QuotaVisualState.Neutral,
    QuotaPolicy.Evaluate(new QuotaWindow(99, null, null), now),
    "missing reset metadata stays neutral");
Equal(QuotaVisualState.Unavailable, QuotaPolicy.Evaluate(null, now), "missing window is unavailable");

using var camel = JsonDocument.Parse("""
{
  "rateLimitsByLimitId": {
    "codex": {
      "primary": { "usedPercent": 12.5, "windowDurationMins": 300, "resetsAt": 10000 },
      "secondary": { "usedPercent": 45, "windowDurationMins": 10080, "resetsAt": 20000 }
    }
  }
}
""");
var camelSnapshot = QuotaPayloadNormalizer.Normalize(camel.RootElement);
Equal(12.5, camelSnapshot.Primary?.UsedPercent, "camel primary usage");
Equal(300, camelSnapshot.Primary?.WindowMinutes, "camel primary window");
Equal(45d, camelSnapshot.Secondary?.UsedPercent, "camel secondary usage");

using var snake = JsonDocument.Parse("""
{
  "rate_limits": {
    "primary": { "used_percent": 7, "window_minutes": 300, "resets_at": 12000 },
    "secondary": { "used_percent": 9, "window_minutes": 10080, "resets_at": 22000 }
  }
}
""");
var snakeSnapshot = QuotaPayloadNormalizer.Normalize(snake.RootElement);
Equal(7d, snakeSnapshot.Primary?.UsedPercent, "snake primary usage");
Equal(9d, snakeSnapshot.Secondary?.UsedPercent, "snake secondary usage");

using var empty = JsonDocument.Parse("{}");
var emptySnapshot = QuotaPayloadNormalizer.Normalize(empty.RootElement);
Equal<QuotaWindow?>(null, emptySnapshot.Primary, "empty primary");
Equal<QuotaWindow?>(null, emptySnapshot.Secondary, "empty secondary");

var sessionNow = new DateTimeOffset(2026, 7, 11, 15, 0, 0, TimeSpan.Zero);
var sessionRoot = Path.Combine(Path.GetTempPath(), $"codex-wingman-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(sessionRoot);
try
{
    var oldSession = Path.Combine(sessionRoot, "old.jsonl");
    await File.WriteAllTextAsync(oldSession, """
{"timestamp":"2026-07-11T13:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":11,"window_minutes":300,"resets_at":1783796400},"secondary":{"used_percent":21,"window_minutes":10080,"resets_at":1784401200}}}}
""");
    File.SetLastWriteTimeUtc(oldSession, sessionNow.AddMinutes(-5).UtcDateTime);

    var newSession = Path.Combine(sessionRoot, "new.jsonl");
    await File.WriteAllTextAsync(newSession, """
{"timestamp":"2026-07-11T14:00:00Z","type":"event_msg","payload":{"type":"other_event"}}
{"timestamp":"2026-07-11T14:30:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":42,"window_minutes":300,"resets_at":1783803600},"secondary":{"used_percent":67,"window_minutes":10080,"resets_at":1784401200}}}}
{"timestamp":"2026-07-11T14:31:00Z","type":"event_msg","payload":{"type":"other_event"}}
""");
    File.SetLastWriteTimeUtc(newSession, sessionNow.UtcDateTime);

    var sessionSnapshot = await new SessionQuotaSource([sessionRoot], () => sessionNow).ReadAsync();
    Equal(42d, sessionSnapshot.Primary?.UsedPercent, "newest session event supplies primary usage");
    Equal(67d, sessionSnapshot.Secondary?.UsedPercent, "newest session event supplies weekly usage");

    var staleSession = Path.Combine(sessionRoot, "stale.jsonl");
    var expiredReset = sessionNow.AddMinutes(-1).ToUnixTimeSeconds();
    var weeklyReset = sessionNow.AddDays(4).ToUnixTimeSeconds();
    var staleLine = """
{"timestamp":"2026-07-11T14:59:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":99,"window_minutes":300,"resets_at":__EXPIRED__},"secondary":{"used_percent":68,"window_minutes":10080,"resets_at":__WEEKLY__}}}}
"""
        .Replace("__EXPIRED__", expiredReset.ToString(), StringComparison.Ordinal)
        .Replace("__WEEKLY__", weeklyReset.ToString(), StringComparison.Ordinal);
    await File.WriteAllTextAsync(staleSession, staleLine);
    File.SetLastWriteTimeUtc(staleSession, sessionNow.AddMinutes(1).UtcDateTime);
    var freshened = await new SessionQuotaSource([sessionRoot], () => sessionNow).ReadAsync();
    Equal<QuotaWindow?>(null, freshened.Primary, "expired five-hour window is not displayed as current");
    Equal(68d, freshened.Secondary?.UsedPercent, "unexpired weekly window survives stale-window filtering");

    var unavailable = await new SessionQuotaSource([Path.Combine(sessionRoot, "missing")], () => sessionNow).ReadAsync();
    Equal(QuotaSnapshot.Unavailable, unavailable, "missing session roots fail closed without launching a process");
}
finally
{
    Directory.Delete(sessionRoot, recursive: true);
}

var portableDefaults = SessionQuotaSource.DefaultRoots(@"X:\Profiles\Example");
Equal(false, HelperSettings.Default.IsHelperEnabled("json-debug", true), "developer JSON overlay is off by default for public installs");
Equal(2, portableDefaults.Count, "portable defaults include only current-user session roots");
Equal(true, portableDefaults.All(path => path.StartsWith(@"X:\Profiles\Example\.codex", StringComparison.OrdinalIgnoreCase)), "portable defaults stay inside the supplied user profile");

var defaultSettings = HelperSettings.Default;
Equal(true, defaultSettings.UsageDialsEnabled, "usage dials default enabled");
Equal(0, defaultSettings.AdditionalSessionRoots.Count, "additional roots default empty");
Equal(false, defaultSettings.ForceHighPerformanceGpu, "high-performance GPU launch defaults off");

var settingsRoot = Path.Combine(Path.GetTempPath(), $"codex-wingman-settings-{Guid.NewGuid():N}");
var settingsPath = Path.Combine(settingsRoot, "settings.json");
try
{
    var configured = new HelperSettings(
        false,
        [@"R:\shared-codex\sessions", @"R:\shared-codex\sessions", @"R:\shared-codex\archived_sessions"],
        ForceHighPerformanceGpu: true);
    HelperSettingsStore.Save(settingsPath, configured);
    var loaded = HelperSettingsStore.Load(settingsPath);
    Equal(false, loaded.UsageDialsEnabled, "settings round trip feature state");
    Equal(true, loaded.ForceHighPerformanceGpu, "settings round trip high-performance GPU launch preference");
    var personalConfig = JsonSerializer.SerializeToElement(new { mappings = new[] { new { sourcePrefix = "C:/Notes/", vault = "Notes" } }, uriAction = "open" });
    HelperSettingsStore.Save(settingsPath, configured with { HelperConfig = new Dictionary<string, JsonElement> { ["obsidian-links"] = personalConfig } });
    var personalized = HelperSettingsStore.Load(settingsPath).WithHelperEnabled("usage-dials", true);
    Equal("Notes", personalized.ConfigFor("obsidian-links", null)!.Value.GetProperty("mappings")[0].GetProperty("vault").GetString(), "local helper config survives save load and tray toggle");
    Equal(personalConfig.GetRawText(), personalized.ConfigFor("unconfigured-helper", personalConfig)!.Value.GetRawText(), "missing override keeps package config");
    var invalidConfig = personalized with { HelperConfig = new Dictionary<string, JsonElement> { ["obsidian-links"] = JsonSerializer.SerializeToElement(false) } };
    Equal("{}", invalidConfig.ConfigFor("obsidian-links", personalConfig)!.Value.GetRawText(), "malformed override never falls back to another user's mapping");
    Equal(3, loaded.AdditionalSessionRoots.Count, "settings store preserves declared roots");
    var composed = loaded.ComposeSessionRoots(@"X:\Profiles\Example");
    Equal(4, composed.Count, "composed roots deduplicate additional paths");
    Equal(1, composed.Count(path => path.Equals(@"R:\shared-codex\sessions", StringComparison.OrdinalIgnoreCase)), "duplicate additional root removed");

    await File.WriteAllTextAsync(settingsPath, "{not-json");
    Equal(HelperSettings.Default, HelperSettingsStore.Load(settingsPath), "malformed settings fail closed to defaults");
}
finally
{
    if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, recursive: true);
}

var migrationRoot = Path.Combine(Path.GetTempPath(), $"codex-wingman-migration-{Guid.NewGuid():N}");
try
{
    var legacyDirectory = Path.Combine(migrationRoot, "CodexHelper");
    Directory.CreateDirectory(legacyDirectory);
    var legacyPath = Path.Combine(legacyDirectory, "settings.json");
    var legacySettings = new HelperSettings(false, [@"R:\legacy-codex\sessions"]);
    HelperSettingsStore.Save(legacyPath, legacySettings);

    var initialized = HelperSettingsStore.InitializeDefault(migrationRoot);
    Equal(Path.Combine(migrationRoot, "CodexWingman", "settings.json"), initialized.Path, "new default settings path");
    Equal(legacySettings.UsageDialsEnabled, initialized.Settings.UsageDialsEnabled, "legacy feature state imported into new identity");
    Equal(legacySettings.AdditionalSessionRoots.Single(), initialized.Settings.AdditionalSessionRoots.Single(), "legacy session root imported into new identity");
    Equal(true, File.Exists(initialized.Path), "migrated settings persisted at new path");
    Equal(true, File.Exists(legacyPath), "legacy settings remains untouched");

    var preferred = new HelperSettings(true, [@"R:\new-codex\sessions"]);
    HelperSettingsStore.Save(initialized.Path, preferred);
    var reloaded = HelperSettingsStore.InitializeDefault(migrationRoot).Settings;
    Equal(preferred.UsageDialsEnabled, reloaded.UsageDialsEnabled, "new feature state takes precedence over legacy file");
    Equal(preferred.AdditionalSessionRoots.Single(), reloaded.AdditionalSessionRoots.Single(), "new session root takes precedence over legacy file");
}
finally
{
    if (Directory.Exists(migrationRoot)) Directory.Delete(migrationRoot, recursive: true);
}

var selectedTargets = CodexTargetCatalog.SelectPages([
    new CodexTarget("a", "page", "app://-/index.html", "ws://a"),
    new CodexTarget("b", "page", "app://-/index.html?initialRoute=%2Flocal%2F1", "ws://b"),
    new CodexTarget("b", "page", "app://-/index.html?initialRoute=%2Flocal%2F1", "ws://duplicate"),
    new CodexTarget("worker", "worker", "", "ws://worker"),
    new CodexTarget("web", "page", "https://example.com", "ws://web"),
]);
Equal(2, selectedTargets.Count, "all unique Codex pages selected");
Equal("a", selectedTargets[0].Id, "first selected target");
Equal("b", selectedTargets[1].Id, "second selected target");
var openedTarget = new CodexTarget("c", "page", "app://-/index.html", "ws://c");
Equal(true, CodexTargetCatalog.PreservesPages(selectedTargets, [.. selectedTargets, openedTarget]), "new window preserves existing pages");
Equal(1, CodexTargetCatalog.FindNewPages(selectedTargets, [.. selectedTargets, openedTarget]).Count, "new window produces one new page");
Equal("c", CodexTargetCatalog.FindNewPages(selectedTargets, [.. selectedTargets, openedTarget]).Single().Id, "new page identity is retained");
Equal(false, CodexTargetCatalog.PreservesPages(selectedTargets, [selectedTargets[1], openedTarget]), "new window rejects a disappeared page");

var browserTargetSocket = new ReplyCdpWebSocket("""
    {"id":1,"result":{"targetInfos":[
      {"targetId":"browser-page","type":"page","title":"Codex","url":"app://-/index.html?initialRoute=%2Flocal%2F019f70bf-2c52-7003-99f1-b54e7b10dd37","attached":false},
      {"targetId":"browser-tab","type":"tab","title":"Codex","url":"app://-/index.html","attached":false}
    ]}}
    """);
using (var browserFallbackHttp = new HttpClient(new StaticHttpMessageHandler(request =>
{
    var body = request.RequestUri?.AbsolutePath switch
    {
        "/json/list" => "[]",
        "/json/version" => "{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:9223/devtools/browser/browser-fixture\"}",
        _ => throw new InvalidOperationException($"Unexpected discovery request {request.RequestUri}"),
    };
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
})))
{
    var browserFallbackSource = new HttpCodexTargetSource(browserFallbackHttp, 9223, () => browserTargetSocket);
    var browserFallbackTargets = await browserFallbackSource.ListAsync();
    Equal(1, browserFallbackTargets.Count, "browser target registry recovers pages when json list is empty");
    Equal("browser-page", browserFallbackTargets.Single().Id, "browser target registry preserves exact page identity");
    Equal("ws://127.0.0.1:9223/devtools/page/browser-page", browserFallbackTargets.Single().WebSocketDebuggerUrl, "browser target registry builds direct page websocket URL");
    Equal("ws://127.0.0.1:9223/devtools/browser/browser-fixture", browserTargetSocket.ConnectedUri?.ToString(), "browser fallback connects only to the advertised browser endpoint");
    Equal(true, browserTargetSocket.SentText.Contains("Target.getTargets", StringComparison.Ordinal), "browser fallback requests the target registry");
}

var eventMessage = CdpEvaluationResponseParser.Parse(Encoding.UTF8.GetBytes("{\"method\":\"Runtime.consoleAPICalled\",\"params\":{}}"), 1);
Equal(false, eventMessage.IsResponse, "CDP event before response is ignored");
var arrayResponseJson = "{\"id\":1,\"result\":{\"result\":{\"type\":\"object\",\"value\":[{\"helperId\":\"fixture\"}]}}}";
var arrayResponseBytes = Encoding.UTF8.GetBytes(arrayResponseJson);
using (var accumulator = new CdpMessageAccumulator())
{
    Equal<ReadOnlyMemory<byte>?>(null, accumulator.Append(arrayResponseBytes.AsSpan(0, 17), endOfMessage: false), "fragmented CDP response waits for final frame");
    var completePayload = accumulator.Append(arrayResponseBytes.AsSpan(17), endOfMessage: true);
    Equal(true, completePayload.HasValue, "fragmented CDP response completes on final frame");
    var parsedArray = CdpEvaluationResponseParser.Parse(completePayload!.Value.Span, 1);
    Equal(true, parsedArray.IsResponse, "matching CDP response recognized");
    Equal(JsonValueKind.Array, parsedArray.Value?.ValueKind, "CDP JSON array return-by-value parsed");
    Equal("fixture", parsedArray.Value?.EnumerateArray().Single().GetProperty("helperId").GetString(), "CDP array value content preserved");
}
Throws<InvalidOperationException>(
    () => CdpEvaluationResponseParser.Parse(Encoding.UTF8.GetBytes("{\"id\":1,\"error\":{\"message\":\"protocol fixture\"}}"), 1),
    "CDP protocol errors surface");
Throws<InvalidOperationException>(
    () => CdpEvaluationResponseParser.Parse(Encoding.UTF8.GetBytes("{\"id\":1,\"result\":{\"exceptionDetails\":{\"text\":\"renderer fixture\"}}}"), 1),
    "CDP exceptionDetails surface");
var missingValueResponse = CdpEvaluationResponseParser.Parse(Encoding.UTF8.GetBytes("{\"id\":1,\"result\":{\"result\":{\"type\":\"undefined\"}}}"), 1);
Equal(true, missingValueResponse.IsResponse, "CDP missing-value reply still completes matching request");
Equal<JsonElement?>(null, missingValueResponse.Value, "CDP missing value maps to null");

var neverReplySocket = new NeverReplyCdpWebSocket();
var boundedEvaluator = new WebSocketCdpEvaluator(TimeSpan.FromMilliseconds(50), () => neverReplySocket);
var boundedEvaluation = Stopwatch.StartNew();
await ThrowsAsync<TimeoutException>(
    () => boundedEvaluator.EvaluateValueAsync(selectedTargets[0], "window.__fixture", CancellationToken.None),
    "production CDP evaluator times out target that never replies");
boundedEvaluation.Stop();
Equal(true, boundedEvaluation.Elapsed < TimeSpan.FromSeconds(1), "production CDP timeout promptly returns control");
Equal(true, neverReplySocket.AbortCalled, "timed-out CDP socket is aborted before disposal");

var targetSource = new FakeTargetSource(selectedTargets);
var evaluator = new FakeEvaluator("b");
var controller = new CodexInjectionController(targetSource, evaluator, new InjectionScriptBuilder());
var injected = await controller.InjectAllAsync(camelSnapshot);
Equal(2, injected.Discovered, "controller discovered every Codex page");
Equal(1, injected.Succeeded, "controller counts successful window injection");
Equal(1, injected.Failed, "controller isolates failed window injection");
Equal(2, evaluator.Calls.Count, "controller attempted every window");

evaluator.FailTargetId = null;
var removed = await controller.RemoveAllAsync();
Equal(2, removed.Succeeded, "cleanup reaches every window");
Equal(true, evaluator.Calls.TakeLast(2).All((call) => call.Expression.Contains("cleanup", StringComparison.Ordinal)), "cleanup expression broadcast");

var builtScript = new InjectionScriptBuilder().Build(camelSnapshot, now);
Equal(false, builtScript.Contains("__CODEX_HELPER_SNAPSHOT__", StringComparison.Ordinal), "snapshot placeholder replaced");
Equal(true, builtScript.Contains("aria-label^=\"Context usage:\"", StringComparison.Ordinal), "semantic context selector retained");
Equal(true, builtScript.Contains("insertAdjacentElement('afterend'", StringComparison.Ordinal), "native sibling insertion retained");
Equal(false, builtScript.Contains("position: fixed", StringComparison.Ordinal), "injected bank is not fixed-positioned");
Equal(true, builtScript.Contains("windowMinutes", StringComparison.Ordinal), "generated snapshot includes window duration for elapsed-time copy");
Equal(true, builtScript.Contains("resetsAtUnixSeconds", StringComparison.Ordinal), "generated snapshot includes raw reset time for elapsed-time copy");

Equal("--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223", CodexLaunchPolicy.DebugArguments(9223), "Codex launch arguments default to the existing behavior");
Equal("--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223 --force_high_performance_gpu", CodexLaunchPolicy.DebugArguments(9223, forceHighPerformanceGpu: true), "Codex launch arguments include the opted-in high-performance GPU override");
Equal(true, CodexLaunchPolicy.ProcessNames.SequenceEqual(["codex", "ChatGPT"], StringComparer.OrdinalIgnoreCase), "restart recognizes current and legacy Codex process names");
Equal(true, CodexLaunchPolicy.ShouldLaunchAtStartup(isCodexRunning: false), "Wingman startup launches Codex when absent");
Equal(false, CodexLaunchPolicy.ShouldLaunchAtStartup(isCodexRunning: true), "Wingman startup preserves a running Codex instance");

using var heldPort = new TcpListener(IPAddress.Loopback, 0);
heldPort.Start();
var occupiedPort = ((IPEndPoint)heldPort.LocalEndpoint).Port;
var selectedPort = CodexLaunchPolicy.FindAvailablePort(occupiedPort, maxAttempts: 3);
Equal(false, selectedPort == occupiedPort, "debug port allocator skips an occupied port");
using (var selectedListener = new TcpListener(IPAddress.Loopback, selectedPort))
{
    selectedListener.Start();
}

Equal(9224, CodexLaunchPolicy.TryGetPortOverride(["--cdp-port=9224"]), "explicit CDP port override parses");
Equal<int?>(null, CodexLaunchPolicy.TryGetPortOverride(["--unrelated"]), "missing CDP port override stays unset");

var runtimeStatePath = Path.Combine(Path.GetTempPath(), $"codex-wingman-runtime-{Guid.NewGuid():N}.json");
try
{
    CodexRuntimeStateStore.SavePort(runtimeStatePath, selectedPort);
    Equal(selectedPort, CodexRuntimeStateStore.LoadPort(runtimeStatePath), "runtime state round trips active CDP port");
    await File.WriteAllTextAsync(runtimeStatePath, "{not-json");
    Equal<int?>(null, CodexRuntimeStateStore.LoadPort(runtimeStatePath), "malformed runtime state fails closed");
}
finally
{
    if (File.Exists(runtimeStatePath)) File.Delete(runtimeStatePath);
}

var fakeQuota = new FakeQuotaSource(camelSnapshot);
var fakeController = new FakeInjectionController();
var coordinator = new HelperCoordinator(fakeQuota, fakeController);
await coordinator.RefreshAsync();
Equal(1, fakeQuota.ReadCount, "refresh reads quota once");
Equal(1, fakeController.InjectCount, "refresh injects once");
await coordinator.ShutdownAsync();
Equal(1, fakeController.RemoveCount, "shutdown removes all injections before exit");

var enableCount = 0;
var disableCount = 0;
var persistedStates = new List<bool>();
var featureToggle = new HelperFeatureToggle(
    "Usage dials",
    enabled: false,
    _ => { enableCount++; return Task.CompletedTask; },
    _ => { disableCount++; return Task.CompletedTask; },
    persistedStates.Add);
await featureToggle.SetEnabledAsync(true);
Equal(true, featureToggle.Enabled, "feature toggle enables after apply succeeds");
Equal(1, enableCount, "feature toggle applies exactly once");
Equal(true, persistedStates.Single(), "feature toggle persists enabled state");
await featureToggle.SetEnabledAsync(true);
Equal(1, enableCount, "setting same feature state is idempotent");
await featureToggle.SetEnabledAsync(false);
Equal(false, featureToggle.Enabled, "feature toggle disables after removal succeeds");
Equal(1, disableCount, "feature toggle removes exactly once");
Equal(false, persistedStates.Last(), "feature toggle persists disabled state");

var helperRoot = Path.Combine(Path.GetTempPath(), $"codex-wingman-helper-host-{Guid.NewGuid():N}");
var bundledRoot = Path.Combine(helperRoot, "bundled");
var userRoot = Path.Combine(helperRoot, "user");
var helperSettingsPath = Path.Combine(helperRoot, "settings", "settings.json");
Directory.CreateDirectory(bundledRoot);
Directory.CreateDirectory(userRoot);
try
{
    WritePackage(bundledRoot, "valid", "valid-helper", capabilities: ["codex.openNewChatWindow"], refreshSeconds: 0);
    WritePackage(bundledRoot, "overridden", "override-me", version: "1.0.0");
    WritePackage(userRoot, "override", "override-me", version: "2.0.0");
    WritePackage(bundledRoot, "future", "future-helper", schemaVersion: 2);
    WritePackage(bundledRoot, "traversal", "traversal-helper", apply: "../outside.js");
    WritePackage(bundledRoot, "duplicate-a", "duplicate-helper");
    WritePackage(bundledRoot, "duplicate-b", "duplicate-helper");

    var catalog = new HelperCatalog().Discover(bundledRoot, userRoot);
    Equal(2, catalog.Packages.Count, "catalog accepts valid packages and user overrides");
    Equal("2.0.0", catalog.Packages.Single(package => package.Manifest.Id == "override-me").Manifest.Version, "user package overrides bundled package by helper id");
    Equal(false, catalog.Packages.Any(package => package.Manifest.Id == "future-helper"), "unknown schema version rejected");
    Equal(false, catalog.Packages.Any(package => package.Manifest.Id == "traversal-helper"), "entrypoint traversal rejected");
    Equal(false, catalog.Packages.Any(package => package.Manifest.Id == "duplicate-helper"), "duplicates within one root are rejected together");
    Equal(true, catalog.Diagnostics.Count >= 3, "invalid catalog entries produce diagnostics");

    Equal("About CodexWingman v1.2.3", TrayAboutText.For(new Version(1, 2, 3, 0)), "About row uses the application assembly version");

    var childWindowRoot = Path.Combine(helperRoot, "child-window-bundled");
    var childWindowUserRoot = Path.Combine(helperRoot, "child-window-user");
    Directory.CreateDirectory(childWindowRoot);
    Directory.CreateDirectory(childWindowUserRoot);
    WritePackage(childWindowRoot, "child-helper", "child-helper", refreshSeconds: 0);
    var originalPage = new CodexTarget("original", "page", "app://-/index.html", "ws://original");
    var childPage = new CodexTarget("child", "page", "app://-/index.html", "ws://child");
    var childTargetSource = new SequencedTargetSource([[originalPage], [originalPage, childPage]]);
    var childDispatcher = new OpeningHostActionDispatcher(childTargetSource.Advance);
    var childEvaluator = new HostFakeEvaluator();
    var childHost = new HelperHost(
        childWindowRoot,
        childWindowUserRoot,
        Path.Combine(helperRoot, "child-window-settings", "settings.json"),
        childTargetSource,
        childEvaluator,
        childDispatcher,
        []);
    var childReport = await childHost.OpenNativeNewWindowAsync();
    Equal(2, childReport.TargetsDiscovered, "native child window reconciliation sees both pages");
    Equal(HostActionDispatcher.OpenNewChatWindow, childDispatcher.Requests.Single().Action, "native child window uses the verified Host Action");
    Equal(originalPage.Id, childDispatcher.Requests.Single().SourceTarget?.Id, "native child window dispatches from the original page");
    Equal(2, childEvaluator.Expressions.Count(expression => expression.Contains("const helperId=\"child-helper\"", StringComparison.Ordinal)), "child Helper applies to both pages");

    var productionNativeWindowTargets = new SequencedTargetSource([[originalPage], [originalPage, childPage]]);
    var productionNativeWindowEvaluator = new HostFakeEvaluator();
    var productionNativeWindowHost = new HelperHost(
        childWindowRoot,
        childWindowUserRoot,
        Path.Combine(helperRoot, "production-native-window-settings", "settings.json"),
        productionNativeWindowTargets,
        productionNativeWindowEvaluator,
        new HostActionDispatcher(new AdvancingEvaluator(productionNativeWindowEvaluator, productionNativeWindowTargets.Advance)),
        []);
    await productionNativeWindowHost.OpenNativeNewWindowAsync();
    Equal(true, productionNativeWindowEvaluator.Expressions.Any(expression => expression.Contains("path:'/'", StringComparison.Ordinal)), "production dispatcher preserves system-owned native-window root open");

    var bootstrapRoot = Path.Combine(helperRoot, "bootstrap-bundled");
    var bootstrapUserRoot = Path.Combine(helperRoot, "bootstrap-user");
    Directory.CreateDirectory(bootstrapRoot);
    Directory.CreateDirectory(bootstrapUserRoot);
    WritePackage(bootstrapRoot, "owner", "owner", capabilities: ["codex.openNewChatWindow"], refreshSeconds: 0);
    WritePackage(bootstrapRoot, "other", "other", refreshSeconds: 0);
    var bootstrapSource = new CodexTarget("bootstrap-source", "page", "app://-/index.html", "ws://bootstrap-source");
    var bootstrapChild = new CodexTarget("bootstrap-child", "page", "app://-/index.html", "ws://bootstrap-child");
    var bootstrapTargets = new SequencedTargetSource([[bootstrapSource], [bootstrapSource, bootstrapChild]]);
    var bootstrapDispatcher = new OpeningHostActionDispatcher(bootstrapTargets.Advance);
    var bootstrapEvaluator = new HostFakeEvaluator();
    var bootstrapAckPolls = 0;
    bootstrapEvaluator.ValueResultFactory = (target, _) => target.Id == bootstrapChild.Id
        ? ++bootstrapAckPolls < 2
            ? Json("""
[
  {"helperId":"other","action":"wingman.completeChild","payload":{"token":"token-success"}},
  {"helperId":"owner","action":"wingman.completeChild","payload":{"token":"wrong-token"}}
]
""")
            : Json("[{\"helperId\":\"owner\",\"action\":\"wingman.completeChild\",\"payload\":{\"token\":\"token-success\"}}]")
        : null;
    bootstrapEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/","bootstrap":{"mode":"native-new-chat","controlLabel":"Start new task in Example Project"}}}]
"""));
    var bootstrapHost = new HelperHost(
        bootstrapRoot,
        bootstrapUserRoot,
        Path.Combine(helperRoot, "bootstrap-settings", "settings.json"),
        bootstrapTargets,
        bootstrapEvaluator,
        bootstrapDispatcher,
        [],
        childOperationTokenFactory: () => "token-success");
    var bootstrapReport = await bootstrapHost.ReconcileAsync();
    Equal(0, bootstrapReport.Failed, "exact-child open and bootstrap succeeds");
    Equal(HostActionDispatcher.SystemHelperId, bootstrapDispatcher.Requests.Single().HelperId, "validated root open uses Host-owned bridge authority");
    Equal(null, bootstrapDispatcher.Requests.Single().Payload, "validated root open uses the system root contract only after bootstrap validation");
    Equal(true, bootstrapEvaluator.Calls.Any(call => call.Target.Id == "bootstrap-child" && call.Expression.Contains("const helperId=\"owner\"", StringComparison.Ordinal) && call.Expression.Contains("\"controlLabel\":\"Start new task in Example Project\"", StringComparison.Ordinal)), "bootstrap reaches owning Helper in exact child");
    Equal(false, bootstrapEvaluator.Calls.Any(call => call.Target.Id != "bootstrap-child" && call.Expression.Contains("\"controlLabel\":\"Start new task in Example Project\"", StringComparison.Ordinal)), "bootstrap never reaches source target");
    Equal(false, bootstrapEvaluator.Calls.Any(call => call.Expression.Contains("const helperId=\"other\"", StringComparison.Ordinal) && call.Expression.Contains("\"controlLabel\":\"Start new task in Example Project\"", StringComparison.Ordinal)), "bootstrap never reaches another Helper");
    var bootstrapAckDrainIndex = bootstrapEvaluator.Calls.FindLastIndex(call => call.Target.Id == bootstrapChild.Id && call.Expression.Contains("splice(0", StringComparison.Ordinal));
    var bootstrapFocusIndexes = bootstrapEvaluator.Calls.Select((call, index) => (call, index)).Where(item => item.call.Target.Id == bootstrapChild.Id && item.call.Expression == "window.focus()").Select(item => item.index).ToArray();
    Equal(true, bootstrapFocusIndexes.First() < bootstrapAckDrainIndex, "exact child is focused as soon as its bootstrap is installed");
    Equal(true, bootstrapFocusIndexes.Last() > bootstrapAckDrainIndex, "exact child is focused again after matching acknowledgement");
    Equal(false, bootstrapEvaluator.Calls.Any(call => call.Target.Id != bootstrapChild.Id && call.Expression == "window.focus()"), "focus never targets source or sibling renderers");
    bootstrapEvaluator.Calls.Clear();
    bootstrapEvaluator.DrainResults.Enqueue(Json("[]"));
    await bootstrapHost.ReconcileAsync();
    Equal(false, bootstrapEvaluator.Calls.Any(call => call.Expression.Contains("\"controlLabel\":\"Start new task in Example Project\"", StringComparison.Ordinal)), "bootstrap is consumed one shot");
    Equal(true, bootstrapAckPolls >= 2, "wrong owner/token are ignored until delayed matching acknowledgement succeeds");

    var productionRootTargets = new SequencedTargetSource([[bootstrapSource], [bootstrapSource, bootstrapChild]]);
    var productionRootEvaluator = new HostFakeEvaluator();
    productionRootEvaluator.ValueResultFactory = (target, _) => target.Id == bootstrapChild.Id
        ? Json("[{\"helperId\":\"owner\",\"action\":\"wingman.completeChild\",\"payload\":{\"token\":\"token-production\"}}]")
        : null;
    productionRootEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/","bootstrap":{"mode":"native-new-chat","controlLabel":"Start new task in Production Project"}}}]
"""));
    var productionRootHost = new HelperHost(
        bootstrapRoot,
        bootstrapUserRoot,
        Path.Combine(helperRoot, "production-root-settings", "settings.json"),
        productionRootTargets,
        productionRootEvaluator,
        new HostActionDispatcher(new AdvancingEvaluator(productionRootEvaluator, productionRootTargets.Advance)),
        [],
        childOperationTokenFactory: () => "token-production");
    var productionRootReport = await productionRootHost.ReconcileAsync();
    Equal(0, productionRootReport.Failed, "production dispatcher accepts root only through validated exact-child bootstrap");
    Equal(true, productionRootEvaluator.Calls.Any(call => call.Target.Id == "bootstrap-child" && call.Expression.Contains("Start new task in Production Project", StringComparison.Ordinal)), "production root child receives exact label bootstrap");

    var failedAckTargets = new SequencedTargetSource([[bootstrapSource], [bootstrapSource, bootstrapChild]]);
    var failedAckEvaluator = new HostFakeEvaluator();
    failedAckEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/","bootstrap":{"mode":"native-new-chat","controlLabel":"Start new task in Failed Project"}}}]
"""));
    failedAckEvaluator.ValueResultFactory = (target, _) => target.Id == bootstrapChild.Id ? Json("[]") : null;
    var failedAckHost = new HelperHost(
        bootstrapRoot,
        bootstrapUserRoot,
        Path.Combine(helperRoot, "failed-ack-settings", "settings.json"),
        failedAckTargets,
        failedAckEvaluator,
        new OpeningHostActionDispatcher(failedAckTargets.Advance),
        [],
        nativeWindowTimeout: TimeSpan.FromMilliseconds(20),
        nativeWindowPollInterval: TimeSpan.FromMilliseconds(1),
        childOperationTokenFactory: () => "token-failed");
    var failedAckReport = await failedAckHost.ReconcileAsync();
    Equal(true, failedAckReport.Failed > 0, "missing acknowledgement times out instead of counting success");
    Equal(true, failedAckEvaluator.Calls.Any(call => call.Target.Id == bootstrapChild.Id && call.Expression.Contains("window.close()", StringComparison.Ordinal)), "unacknowledged exact child is closed on timeout");

    var genericNoAckTargets = new SequencedTargetSource([[bootstrapSource], [bootstrapSource, bootstrapChild]]);
    var genericNoAckEvaluator = new HostFakeEvaluator();
    genericNoAckEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/","bootstrap":{"mode":"native-new-chat","controlLabel":"New task","controlKind":"sidebar-row"}}}]
"""));
    var genericNoAckHost = new HelperHost(
        bootstrapRoot,
        bootstrapUserRoot,
        Path.Combine(helperRoot, "generic-no-ack-settings", "settings.json"),
        genericNoAckTargets,
        genericNoAckEvaluator,
        new OpeningHostActionDispatcher(genericNoAckTargets.Advance),
        [],
        nativeWindowTimeout: TimeSpan.FromMilliseconds(20),
        nativeWindowPollInterval: TimeSpan.FromMilliseconds(1));
    var genericNoAckReport = await genericNoAckHost.ReconcileAsync();
    Equal(0, genericNoAckReport.Failed, "generic exact child does not require an acknowledgement that native navigation can erase");
    Equal(false, genericNoAckEvaluator.Calls.Any(call => call.Target.Id == bootstrapChild.Id && call.Expression.Contains("window.close()", StringComparison.Ordinal)), "generic exact child is never closed by acknowledgement cleanup");

    var ambiguousSource = new CodexTarget("ambiguous-source", "page", "app://-/index.html", "ws://ambiguous-source");
    var ambiguousTargets = new SequencedTargetSource([
        [ambiguousSource],
        [ambiguousSource, new CodexTarget("ambiguous-a", "page", "app://-/index.html", "ws://ambiguous-a"), new CodexTarget("ambiguous-b", "page", "app://-/index.html", "ws://ambiguous-b")]
    ]);
    var ambiguousEvaluator = new HostFakeEvaluator();
    ambiguousEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/local/thread-ambiguous","bootstrap":{"mode":"native-new-chat"}}}]
"""));
    var ambiguousHost = new HelperHost(
        bootstrapRoot, bootstrapUserRoot, Path.Combine(helperRoot, "ambiguous-settings", "settings.json"),
        ambiguousTargets, ambiguousEvaluator, new OpeningHostActionDispatcher(ambiguousTargets.Advance), []);
    var ambiguousReport = await ambiguousHost.ReconcileAsync();
    Equal(true, ambiguousReport.Failed > 0, "ambiguous child correlation fails safely");
    Equal(false, ambiguousEvaluator.Expressions.Any(expression => expression.Contains("const bootstrap={\"mode\":\"native-new-chat\"}", StringComparison.Ordinal)), "ambiguous child receives no bootstrap");

    var timeoutEvaluator = new HostFakeEvaluator();
    timeoutEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/local/thread-timeout","bootstrap":{"mode":"native-new-chat"}}}]
"""));
    var timeoutHost = new HelperHost(
        bootstrapRoot, bootstrapUserRoot, Path.Combine(helperRoot, "open-child-timeout-settings", "settings.json"),
        new FakeTargetSource([bootstrapSource]), timeoutEvaluator, new RecordingHostActionDispatcher(), [],
        nativeWindowTimeout: TimeSpan.FromMilliseconds(20), nativeWindowPollInterval: TimeSpan.FromMilliseconds(1));
    var timeoutReport = await timeoutHost.ReconcileAsync();
    Equal(true, timeoutReport.Failed > 0, "timed-out child correlation fails safely");
    Equal(false, timeoutEvaluator.Expressions.Any(expression => expression.Contains("const bootstrap={\"mode\":\"native-new-chat\"}", StringComparison.Ordinal)), "timed-out child receives no bootstrap");

    var serialChildA = new CodexTarget("serial-a", "page", "app://-/index.html", "ws://serial-a");
    var serialChildB = new CodexTarget("serial-b", "page", "app://-/index.html", "ws://serial-b");
    var serialTargets = new SequencedTargetSource([[bootstrapSource], [bootstrapSource, serialChildA], [bootstrapSource, serialChildA, serialChildB]]);
    using var firstDispatchEntered = new ManualResetEventSlim();
    using var releaseFirstDispatch = new ManualResetEventSlim();
    var serialDispatcher = new BlockingOpeningHostActionDispatcher(serialTargets.Advance, firstDispatchEntered, releaseFirstDispatch);
    var serialEvaluator = new HostFakeEvaluator();
    serialEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/local/thread-a","bootstrap":{"sequence":"a"}}}]
"""));
    serialEvaluator.DrainResults.Enqueue(Json("""
[{"helperId":"owner","action":"wingman.openChild","payload":{"path":"/local/thread-b","bootstrap":{"sequence":"b"}}}]
"""));
    serialEvaluator.DrainResults.Enqueue(Json("[]"));
    serialEvaluator.ValueResultFactory = (target, _) => target.Id is "serial-a" or "serial-b"
        ? Json("""
[
  {"helperId":"owner","action":"wingman.completeChild","payload":{"token":"token-a"}},
  {"helperId":"owner","action":"wingman.completeChild","payload":{"token":"token-b"}}
]
""")
        : null;
    var serialTokenIndex = 0;
    var serialHost = new HelperHost(
        bootstrapRoot, bootstrapUserRoot, Path.Combine(helperRoot, "serial-settings", "settings.json"),
        serialTargets, serialEvaluator, serialDispatcher, [],
        childOperationTokenFactory: () => ++serialTokenIndex == 1 ? "token-a" : "token-b");
    var firstSerialOpen = serialHost.ReconcileAsync();
    Equal(true, firstDispatchEntered.Wait(TimeSpan.FromSeconds(1)), "first concurrent open reaches native dispatch");
    var secondSerialOpen = serialHost.ReconcileAsync();
    await Task.Delay(20);
    Equal(1, serialDispatcher.Requests.Count, "second concurrent open waits behind first correlation");
    releaseFirstDispatch.Set();
    await Task.WhenAll(firstSerialOpen, secondSerialOpen);
    Equal(2, serialDispatcher.Requests.Count, "serialized concurrent opens both complete");
    Equal(true, serialEvaluator.Calls.Any(call => call.Target.Id == "serial-a" && call.Expression.Contains("const bootstrap={\"sequence\":\"a\"}", StringComparison.Ordinal)), "first bootstrap correlates to first child");
    Equal(true, serialEvaluator.Calls.Any(call => call.Target.Id == "serial-b" && call.Expression.Contains("const bootstrap={\"sequence\":\"b\"}", StringComparison.Ordinal)), "second bootstrap correlates to second child");

    var shadowBundledRoot = Path.Combine(helperRoot, "shadow-bundled");
    var shadowUserRoot = Path.Combine(helperRoot, "shadow-user");
    Directory.CreateDirectory(shadowBundledRoot);
    Directory.CreateDirectory(shadowUserRoot);
    WritePackage(shadowBundledRoot, "shadowed", "shadowed-helper");
    var invalidOverrideRoot = WritePackage(shadowUserRoot, "shadowed", "shadowed-helper");
    File.Delete(Path.Combine(invalidOverrideRoot, "apply.js"));
    var shadowCatalog = new HelperCatalog().Discover(shadowBundledRoot, shadowUserRoot);
    Equal(false, shadowCatalog.Packages.Any(package => package.Manifest.Id == "shadowed-helper"), "identified invalid user override shadows bundled helper and remains inactive");
    Equal(true, shadowCatalog.Diagnostics.Any(diagnostic => diagnostic.HelperId == "shadowed-helper"), "invalid identified user override retains helper-owned diagnostic");
    var unidentifiedInvalidRoot = Path.Combine(shadowUserRoot, "unidentified-invalid");
    Directory.CreateDirectory(unidentifiedInvalidRoot);
    await File.WriteAllTextAsync(Path.Combine(unidentifiedInvalidRoot, "wingman.json"), "{not-json");
    var diagnosticHost = new HelperHost(
        shadowBundledRoot,
        shadowUserRoot,
        Path.Combine(helperRoot, "diagnostic-settings", "settings.json"),
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        []);
    var identifiedDiagnosticSummary = diagnosticHost.Helpers.Single(helper => helper.Id == "shadowed-helper");
    Equal(false, identifiedDiagnosticSummary.CanToggle, "identified rejected override is a non-toggleable Host summary");
    Equal(false, identifiedDiagnosticSummary.Enabled, "identified rejected override remains disabled");
    Equal(true, !string.IsNullOrWhiteSpace(identifiedDiagnosticSummary.Diagnostic), "identified rejected override exposes diagnostic text");
    Equal(true, diagnosticHost.Helpers.Any(helper => !helper.CanToggle && helper.Id.StartsWith("diagnostic-", StringComparison.Ordinal)), "unidentified rejected package gets visible non-toggleable diagnostic summary");

    var personalBundledRoot = Path.Combine(helperRoot, "personal-default-bundled");
    var personalUserRoot = Path.Combine(helperRoot, "personal-default-user");
    Directory.CreateDirectory(personalBundledRoot);
    Directory.CreateDirectory(personalUserRoot);
    WritePackage(personalUserRoot, "Personal helper v1", "personal-helper", name: "Different manifest name");
    var personalEvaluator = new HostFakeEvaluator();
    var personalHost = new HelperHost(
        personalBundledRoot,
        personalUserRoot,
        Path.Combine(helperRoot, "personal-default-settings", "settings.json"),
        new FakeTargetSource([new CodexTarget("personal-target", "page", "app://-/index.html", "ws://personal")]),
        personalEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    Equal(false, personalHost.Helpers.Single(helper => helper.Id == "personal-helper").Enabled, "new personal helper defaults disabled until explicitly enabled");
    Equal("Personal helper v1", personalHost.Helpers.Single(helper => helper.Id == "personal-helper").Name, "personal helper menu name comes from its folder");
    await personalHost.ReconcileAsync();
    Equal(false, personalEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "new personal helper does not execute during reload reconciliation");

    var executableHelpersRoot = Path.Combine(helperRoot, "single-executable-helper-root");
    Directory.CreateDirectory(executableHelpersRoot);
    WritePackage(executableHelpersRoot, "New bundled helper", "new-bundled-helper");
    var executableRootHost = new HelperHost(
        executableHelpersRoot,
        Path.Combine(helperRoot, "single-executable-settings", "settings.json"),
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        []);
    Equal(true, executableRootHost.Helpers.Single(helper => helper.Id == "new-bundled-helper").Enabled, "a new helper shipped in the executable Helpers root defaults enabled");

    var overrideDefaultBundledRoot = Path.Combine(helperRoot, "override-default-bundled");
    var overrideDefaultUserRoot = Path.Combine(helperRoot, "override-default-user");
    Directory.CreateDirectory(overrideDefaultBundledRoot);
    Directory.CreateDirectory(overrideDefaultUserRoot);
    WritePackage(overrideDefaultBundledRoot, "established", "established-helper", version: "1.0.0");
    WritePackage(overrideDefaultUserRoot, "established", "established-helper", version: "2.0.0");
    var overrideDefaultSettingsPath = Path.Combine(helperRoot, "override-default-settings", "settings.json");
    var overrideDefaultHost = new HelperHost(
        overrideDefaultBundledRoot,
        overrideDefaultUserRoot,
        overrideDefaultSettingsPath,
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        []);
    Equal(true, overrideDefaultHost.Helpers.Single().Enabled, "personal override of bundled ID preserves established default-enabled state");
    HelperSettingsStore.Save(overrideDefaultSettingsPath, HelperSettings.Default.WithHelperEnabled("established-helper", false));
    await overrideDefaultHost.ReloadAsync();
    Equal(false, overrideDefaultHost.Helpers.Single().Enabled, "personal override preserves explicit per-ID disabled state");

    var capabilityValidationRoot = Path.Combine(helperRoot, "capability-validation");
    var capabilityValidationUser = Path.Combine(helperRoot, "capability-validation-user");
    Directory.CreateDirectory(capabilityValidationRoot);
    Directory.CreateDirectory(capabilityValidationUser);
    var nullCapabilityRoot = WritePackage(capabilityValidationRoot, "null-capability", "null-capability");
    await File.WriteAllTextAsync(Path.Combine(nullCapabilityRoot, "wingman.json"), """
{"schemaVersion":1,"id":"null-capability","name":"Null capability","version":"1.0.0","description":"fixture","refreshSeconds":0,"capabilities":[null],"entrypoints":{"apply":"apply.js","remove":"remove.js"}}
""");
    var blankCapabilityRoot = WritePackage(capabilityValidationRoot, "blank-capability", "blank-capability");
    await File.WriteAllTextAsync(Path.Combine(blankCapabilityRoot, "wingman.json"), """
{"schemaVersion":1,"id":"blank-capability","name":"Blank capability","version":"1.0.0","description":"fixture","refreshSeconds":0,"capabilities":["  "],"entrypoints":{"apply":"apply.js","remove":"remove.js"}}
""");
    WritePackage(capabilityValidationRoot, "unknown-capability", "unknown-capability", capabilities: ["third.party.unknown"]);
    var capabilityValidationCatalog = new HelperCatalog().Discover(capabilityValidationRoot, capabilityValidationUser);
    Equal(false, capabilityValidationCatalog.Packages.Any(package => package.Manifest.Id == "null-capability"), "manifest null capability is rejected");
    Equal(false, capabilityValidationCatalog.Packages.Any(package => package.Manifest.Id == "blank-capability"), "manifest blank capability is rejected");
    Equal(true, capabilityValidationCatalog.Packages.Any(package => package.Manifest.Id == "unknown-capability"), "unknown nonblank capability remains valid");
    Equal(true, capabilityValidationCatalog.Diagnostics.Any(diagnostic => diagnostic.HelperId == "null-capability"), "null capability rejection retains diagnostic identity");
    var capabilityValidationHost = new HelperHost(
        capabilityValidationRoot,
        capabilityValidationUser,
        Path.Combine(helperRoot, "capability-validation-settings", "settings.json"),
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        []);
    Equal(true, capabilityValidationHost.Helpers.Any(helper => helper.Id == "null-capability" && !helper.CanToggle), "null capability is visible only as disabled diagnostic summary");
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds(capabilityValidationHost.Helpers), "malformed capability cannot crash or accelerate tray scheduling");

    var externalPackageRoot = WritePackage(Path.Combine(helperRoot, "external-root"), "external", "linked-helper");
    var reparseDiscoveryRoot = Path.Combine(helperRoot, "reparse-discovery");
    Directory.CreateDirectory(reparseDiscoveryRoot);
    var linkedPackageRoot = Path.Combine(reparseDiscoveryRoot, "linked");
    var linkedPackageCreated = false;
    try
    {
        Directory.CreateSymbolicLink(linkedPackageRoot, externalPackageRoot);
        linkedPackageCreated = true;
        var reparseCatalog = new HelperCatalog().Discover(reparseDiscoveryRoot, Path.Combine(helperRoot, "empty-reparse-user"));
        Equal(false, reparseCatalog.Packages.Any(package => package.Manifest.Id == "linked-helper"), "package-root reparse point cannot escape discovery root");
    }
    catch (Exception error) when (!linkedPackageCreated && error is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
    {
        Console.Error.WriteLine($"Package-root reparse regression skipped: {error.GetType().Name}");
    }
    finally
    {
        if (linkedPackageCreated && Directory.Exists(linkedPackageRoot)) Directory.Delete(linkedPackageRoot);
    }

    var persisted = new HelperSettings(false, [@"R:\legacy-codex\sessions"])
        .WithHelperEnabled("valid-helper", false)
        .WithHelperEnabled("another-helper", true);
    HelperSettingsStore.Save(helperSettingsPath, persisted);
    var persistedReloaded = HelperSettingsStore.Load(helperSettingsPath);
    Equal(false, persistedReloaded.IsHelperEnabled("valid-helper", defaultEnabled: true), "per-helper disabled state persists");
    Equal(true, persistedReloaded.IsHelperEnabled("another-helper", defaultEnabled: false), "per-helper enabled state persists");
    Equal(false, persistedReloaded.IsHelperEnabled("usage-dials", defaultEnabled: true), "legacy UsageDialsEnabled remains the usage-dials fallback");

    var sessionCapabilityRoot = Path.Combine(helperRoot, "sessions");
    Directory.CreateDirectory(sessionCapabilityRoot);
    var sessionFile = Path.Combine(sessionCapabilityRoot, "sample.jsonl");
    await File.WriteAllTextAsync(sessionFile, "fixture-session-text");
    WritePackage(
        bundledRoot,
        "backend-valid",
        "backend-valid",
        capabilities: ["files.codexSessions"],
        backend: "backend.js",
        backendSource: "function refresh(wingman) { const roots = wingman.files.codexSessions.roots(); const files = wingman.files.codexSessions.listFiles(roots[0]); return { now: wingman.currentTime(), text: wingman.files.codexSessions.readText(files[0]), count: files.length }; }");
    var backendCatalog = new HelperCatalog().Discover(bundledRoot, userRoot);
    var backendPackage = backendCatalog.Packages.Single(package => package.Manifest.Id == "backend-valid");
    var backend = new HelperBackend([sessionCapabilityRoot], () => new DateTimeOffset(2026, 7, 11, 12, 34, 56, TimeSpan.Zero));
    using var backendState = await backend.RefreshAsync(backendPackage);
    Equal("2026-07-11T12:34:56.0000000+00:00", backendState.RootElement.GetProperty("now").GetString(), "backend receives deterministic current time");
    Equal("fixture-session-text", backendState.RootElement.GetProperty("text").GetString(), "backend reads text only through declared session capability");
    Equal(1, backendState.RootElement.GetProperty("count").GetInt32(), "backend lists files below configured session roots");

    WritePackage(
        bundledRoot,
        "backend-no-capability",
        "backend-no-capability",
        backend: "backend.js",
        backendSource: "function refresh(wingman) { return wingman.files.codexSessions.roots(); }");
    var noCapabilityPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "backend-no-capability");
    await ThrowsAsync<Exception>(() => backend.RefreshAsync(noCapabilityPackage), "undeclared backend capability is inaccessible");

    WritePackage(
        bundledRoot,
        "backend-runaway",
        "backend-runaway",
        backend: "backend.js",
        backendSource: "function refresh(wingman) { while (true) {} }");
    var runawayPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "backend-runaway");
    await ThrowsAsync<Exception>(() => backend.RefreshAsync(runawayPackage), "backend statement limit interrupts runaway scripts");

    WritePackage(
        bundledRoot,
        "backend-blocking",
        "backend-blocking",
        backend: "backend.js",
        backendSource: "function refresh(wingman) { wingman.log('entered'); return { completed: true }; }");
    var blockingPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "backend-blocking");
    using var blockingEntered = new ManualResetEventSlim();
    using var blockingRelease = new ManualResetEventSlim();
    var safetyRelease = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        blockingRelease.Set();
    });
    var previousContext = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    try
    {
        var blockingBackend = new HelperBackend([], log: _ =>
        {
            blockingEntered.Set();
            blockingRelease.Wait();
        });
        var blockingRefresh = blockingBackend.RefreshAsync(blockingPackage);
        Equal(true, blockingEntered.Wait(TimeSpan.FromSeconds(1)), "blocking backend reaches worker fixture");
        Equal(false, blockingRefresh.IsCompleted, "backend execution returns without blocking caller synchronization context");
        blockingRelease.Set();
        using var completedState = blockingRefresh.GetAwaiter().GetResult();
        Equal(true, completedState.RootElement.GetProperty("completed").GetBoolean(), "background backend returns serialized state");
    }
    finally
    {
        blockingRelease.Set();
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }
    await safetyRelease;

    WritePackage(
        bundledRoot,
        "backend-blocking-capability",
        "backend-blocking-capability",
        capabilities: ["files.codexSessions"],
        backend: "backend.js",
        backendSource: "function refresh(wingman) { const sessions=wingman.files.codexSessions; const files=sessions.listFiles(sessions.roots()[0]); return { text: sessions.readText(files[0]) }; }");
    var blockingCapabilityPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "backend-blocking-capability");
    using var capabilityEntered = new ManualResetEventSlim();
    using var capabilityRelease = new ManualResetEventSlim();
    var blockingSessionFiles = new BlockingSessionFiles(sessionCapabilityRoot, capabilityEntered, capabilityRelease);
    var deadlineBackend = new HelperBackend(
        [sessionCapabilityRoot],
        durationLimit: TimeSpan.FromMilliseconds(100),
        sessionFiles: blockingSessionFiles);
    var capabilityStopwatch = Stopwatch.StartNew();
    var blockedCapabilityRefresh = deadlineBackend.RefreshAsync(blockingCapabilityPackage);
    Equal(true, capabilityEntered.Wait(TimeSpan.FromSeconds(1)), "blocking file capability begins on backend worker");
    await ThrowsAsync<TimeoutException>(() => blockedCapabilityRefresh, "outer backend deadline bounds synchronous file capability");
    capabilityStopwatch.Stop();
    Equal(true, capabilityStopwatch.Elapsed < TimeSpan.FromSeconds(1), "Host-facing backend await regains control promptly after capability deadline");
    capabilityRelease.Set();

    var blockingHostBundled = Path.Combine(helperRoot, "blocking-host-bundled");
    var blockingHostUser = Path.Combine(helperRoot, "blocking-host-user");
    Directory.CreateDirectory(blockingHostBundled);
    Directory.CreateDirectory(blockingHostUser);
    WritePackage(
        blockingHostBundled,
        "blocking-host",
        "blocking-host",
        capabilities: ["files.codexSessions"],
        backend: "backend.js",
        backendSource: "function refresh(wingman) { const sessions=wingman.files.codexSessions; const files=sessions.listFiles(sessions.roots()[0]); return { text: sessions.readText(files[0]) }; }");
    using var blockingHostEntered = new ManualResetEventSlim();
    using var blockingHostRelease = new ManualResetEventSlim();
    var blockingHostFiles = new BlockingSessionFiles(sessionCapabilityRoot, blockingHostEntered, blockingHostRelease);
    var blockingHost = new HelperHost(
        blockingHostBundled,
        blockingHostUser,
        Path.Combine(helperRoot, "blocking-host-settings", "settings.json"),
        new FakeTargetSource([new CodexTarget("blocking-host-target", "page", "app://-/index.html", "ws://blocking-host")]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        [sessionCapabilityRoot],
        backendFactory: _ => new HelperBackend(
            [sessionCapabilityRoot],
            durationLimit: TimeSpan.FromMilliseconds(100),
            sessionFiles: blockingHostFiles));
    var blockingHostStopwatch = Stopwatch.StartNew();
    var blockingHostReconcile = blockingHost.ReconcileAsync();
    Equal(true, blockingHostEntered.Wait(TimeSpan.FromSeconds(1)), "Host reconciliation enters blocking capability fixture");
    var blockingHostReport = await blockingHostReconcile;
    blockingHostStopwatch.Stop();
    Equal(true, blockingHostStopwatch.Elapsed < TimeSpan.FromSeconds(1), "Host gate is released promptly after backend outer deadline");
    Equal(true, blockingHostReport.Failed > 0, "Host reports timed-out backend without accepting abandoned state");
    var postTimeoutCleanup = await blockingHost.RemoveAllAsync();
    Equal(0, postTimeoutCleanup.Failed, "Host lifecycle operation converges after backend timeout releases gate");
    blockingHostRelease.Set();
    await Task.Delay(100);
    var retryAfterAbandonedState = await blockingHost.ReconcileAsync();
    Equal(0, retryAfterAbandonedState.Failed, "Host retries backend after abandoned worker eventually completes");
    Equal(true, blockingHostFiles.ReadCount >= 2, "late abandoned backend result never populates Host state cache");

    WritePackage(
        bundledRoot,
        "backend-non-json",
        "backend-non-json",
        backend: "backend.js",
        backendSource: "function refresh(wingman) { return { invalid: function () {} }; }");
    var nonJsonPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "backend-non-json");
    await ThrowsAsync<InvalidOperationException>(() => backend.RefreshAsync(nonJsonPackage), "backend rejects values that JSON serialization would silently omit");

    var renderer = new HelperRenderer();
    using var rendererState = JsonDocument.Parse("{\"enabled\":true}");
    var snapshotBundledRoot = Path.Combine(helperRoot, "snapshot-bundled");
    var snapshotUserRoot = Path.Combine(helperRoot, "snapshot-user");
    Directory.CreateDirectory(snapshotBundledRoot);
    Directory.CreateDirectory(snapshotUserRoot);
    WritePackage(snapshotBundledRoot, "snapshot", "snapshot");
    var snapshotPackage = new HelperCatalog().Discover(snapshotBundledRoot, snapshotUserRoot).Packages.Single();
    await File.WriteAllTextAsync(snapshotPackage.ApplyPath, "window.__changedBeforeReload=true;");
    Equal(false, renderer.BuildApply(snapshotPackage, rendererState.RootElement, "snapshot-target").Contains("__changedBeforeReload", StringComparison.Ordinal), "catalog snapshots scripts until explicit reload");
    var targetApplyMethod = typeof(HelperRenderer).GetMethod(
        nameof(HelperRenderer.BuildApply),
        [typeof(HelperPackage), typeof(JsonElement), typeof(string), typeof(JsonElement?), typeof(string)]);
    Equal(true, targetApplyMethod is not null, "renderer requires an explicit target ID when building apply expressions");
    var targetApplyExpression = (string)targetApplyMethod!.Invoke(
        renderer,
        [backendPackage, rendererState.RootElement, "renderer-target-id", null, null])!;
    Equal(true, targetApplyExpression.Contains("target:Object.freeze({\"id\":\"renderer-target-id\"})", StringComparison.Ordinal), "renderer serializes the supplied target ID into a frozen wingman.target descriptor");
    var targetCapturePackageRoot = WritePackage(bundledRoot, "target-id-capture", "target-id-capture");
    await File.WriteAllTextAsync(
        Path.Combine(targetCapturePackageRoot, "apply.js"),
        "window.__capturedTargetId=wingman.target.id;");
    var targetCapturePackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(
        package => package.Manifest.Id == "target-id-capture");
    const string adversarialTargetId = "quote\" backslash\\ newline\n');window.__rendererInjected=true;//";
    var adversarialTargetExpression = renderer.BuildApply(
        targetCapturePackage,
        rendererState.RootElement,
        adversarialTargetId);
    var targetEngine = new Engine();
    targetEngine.Execute("globalThis.window={};");
    targetEngine.Execute(adversarialTargetExpression);
    Equal(
        adversarialTargetId,
        targetEngine.Evaluate("window.__capturedTargetId").AsString(),
        "renderer preserves an adversarial CDP target ID as exact inert wingman.target.id data");
    Equal(
        true,
        targetEngine.Evaluate("window.__rendererInjected").IsUndefined(),
        "renderer target ID JavaScript syntax cannot escape its serialized string");
    var applyExpression = renderer.BuildApply(backendPackage, rendererState.RootElement, "renderer-target");
    Equal(true, applyExpression.Contains("const state={\"enabled\":true}", StringComparison.Ordinal), "renderer receives serialized backend state");
    WritePackage(
        bundledRoot,
        "manifest-config",
        "manifest-config",
        config: new { pathPrefix = "/mnt/example-data/obsidian/" });
    var configuredPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "manifest-config");
    const string expectedHelperConfig = "const helperConfig=Object.freeze({\"pathPrefix\":\"/mnt/example-data/obsidian/\"});";
    Equal(true, renderer.BuildApply(configuredPackage, rendererState.RootElement, "renderer-target").Contains(expectedHelperConfig, StringComparison.Ordinal), "renderer injects exact frozen manifest config into apply scripts");
    Equal(true, renderer.BuildRemove(configuredPackage).Contains(expectedHelperConfig, StringComparison.Ordinal), "renderer injects exact frozen manifest config into remove scripts");
    foreach (var invalidConfig in new JsonElement?[] { null, Json("null"), Json("[]"), Json("true"), Json("\"scalar\"") })
    {
        var fallbackPackage = configuredPackage with { Manifest = configuredPackage.Manifest with { Config = invalidConfig } };
        const string expectedEmptyHelperConfig = "const helperConfig=Object.freeze({});";
        Equal(true, renderer.BuildApply(fallbackPackage, rendererState.RootElement, "renderer-target").Contains(expectedEmptyHelperConfig, StringComparison.Ordinal), "renderer normalizes missing or non-object manifest config to an empty object in apply scripts");
        Equal(true, renderer.BuildRemove(fallbackPackage).Contains(expectedEmptyHelperConfig, StringComparison.Ordinal), "renderer normalizes missing or non-object manifest config to an empty object in remove scripts");
    }
    Equal(true, applyExpression.Contains("const bootstrap=null", StringComparison.Ordinal), "ordinary renderer apply receives null bootstrap");
    Equal(true, applyExpression.Contains("openChild(path,bootstrap=null)", StringComparison.Ordinal), "renderer bridge exposes one-way openChild");
    Equal(true, applyExpression.Contains("request(action,payload=null)", StringComparison.Ordinal), "renderer bridge exposes wingman.request action and payload");
    Equal(true, applyExpression.Contains("helperId:\"backend-valid\"", StringComparison.Ordinal), "renderer requests retain helper ownership");
    WritePackage(bundledRoot, "agent-derangement-risk", "agent-derangement-risk");
    var riskPackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "agent-derangement-risk");
    const string riskLeaseRefresh = "window.__codexHelperAgentDerangementRiskLeaseExpiresAt=Date.now()+6000;";
    var riskApplyExpression = renderer.BuildApply(riskPackage, rendererState.RootElement, "risk-target");
    Equal(true, riskApplyExpression.Contains(riskLeaseRefresh, StringComparison.Ordinal) && riskApplyExpression.IndexOf(riskLeaseRefresh, StringComparison.Ordinal) < riskApplyExpression.IndexOf("window.__fixtureApply", StringComparison.Ordinal), "Agent Derangement Risk apply refreshes its liveness lease before helper source runs");
    Equal(false, applyExpression.Contains(riskLeaseRefresh, StringComparison.Ordinal), "non-risk helper apply does not receive the Agent Derangement Risk lease");
    var removeExpression = renderer.BuildRemove(new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "valid-helper"));
    Equal(false, removeExpression.Contains("__codexHelperUsageDials", StringComparison.Ordinal), "non-usage helper cleanup remains ownership scoped");
    WritePackage(bundledRoot, "usage-dials", "usage-dials");
    var usagePackage = new HelperCatalog().Discover(bundledRoot, userRoot).Packages.Single(package => package.Manifest.Id == "usage-dials");
    Equal(true, renderer.BuildRemove(usagePackage).Contains("__codexHelperUsageDials", StringComparison.Ordinal), "Usage Dials removal retains the legacy cleanup global");
    Equal(true, renderer.BuildRemove(usagePackage).Contains("queue.splice(0,queue.length,...queue.filter", StringComparison.Ordinal), "removal mutates the shared action queue without stranding other helper closures");
    Equal(true, renderer.BuildDrainActions().Contains("splice(0", StringComparison.Ordinal), "host action drain removes returned JSON values from the shared queue");
    using var childBootstrap = JsonDocument.Parse("{\"mode\":\"native-new-chat\"}");
    var bootstrappedApply = renderer.BuildApply(backendPackage, rendererState.RootElement, "child-target", childBootstrap.RootElement, "renderer-token");
    Equal(true, bootstrappedApply.Contains("const bootstrap={\"mode\":\"native-new-chat\"}", StringComparison.Ordinal), "targeted renderer apply receives serialized bootstrap");
    Equal(true, bootstrappedApply.Contains("completeChild()", StringComparison.Ordinal) && bootstrappedApply.Contains("renderer-token", StringComparison.Ordinal), "renderer completion bridge is bound to Host operation token");
    using var validNewWindowPayload = JsonDocument.Parse("{\"path\":\"/local/thread-123?view=chat\"}");
    Equal("window.electronBridge.sendMessageFromView({type:'open-in-new-window',path:\"/local/thread-123?view=chat\"})", CodexOpenNewChatWindowAdapter.BuildExpression(validNewWindowPayload.RootElement), "new-window adapter transports the validated renderer route");
    foreach (var invalidPayload in new[] { "null", "{}", "{\"path\":\"/\"}", "{\"path\":\"//evil.example/path\"}", "{\"path\":\"/local/../settings\"}", "{\"path\":\"/local/%2e%2e/settings\"}", "{\"path\":\"/local/%252e%252e/settings\"}", "{\"path\":\"/local%2f..%2fsettings\"}", "{\"path\":\"/local%252f..%252fsettings\"}", "{\"path\":\"/local/%5cthread\"}", "{\"path\":\"/local/thread\",\"projectId\":\"synthetic\"}" })
    {
        using var invalidNewWindowPayload = JsonDocument.Parse(invalidPayload);
        Throws<InvalidOperationException>(() => CodexOpenNewChatWindowAdapter.BuildExpression(invalidNewWindowPayload.RootElement), $"new-window adapter rejects invalid payload {invalidPayload}");
    }
    using var validOpenChildPayload = JsonDocument.Parse("{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"Start new task in Example Project\"}}");
    var parsedOpenChild = ExactChildBootstrapRequest.Parse("valid-helper", validOpenChildPayload.RootElement);
    Equal("valid-helper", parsedOpenChild.HelperId, "trusted queue Helper ID owns child bootstrap");
    Equal("/", parsedOpenChild.Path, "openChild accepts root only for validated exact-child bootstrap");
    Equal("native-new-chat", parsedOpenChild.Bootstrap.GetProperty("mode").GetString(), "openChild preserves valid JSON bootstrap");
    using var unicodeApostrophePayload = JsonDocument.Parse("{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"Start new task in O’Reilly\"}}");
    Equal("Start new task in O’Reilly", ExactChildBootstrapRequest.Parse("valid-helper", unicodeApostrophePayload.RootElement).Bootstrap.GetProperty("controlLabel").GetString(), "Host root gate accepts exact U+2019 label shared with renderer");
    using var genericRootPayload = JsonDocument.Parse("{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"New chat\",\"controlKind\":\"semantic\"}}");
    Equal("New chat", ExactChildBootstrapRequest.Parse("valid-helper", genericRootPayload.RootElement).Bootstrap.GetProperty("controlLabel").GetString(), "Host root gate accepts exact verified generic New Chat label");
    foreach (var invalidPayload in new[]
    {
        "null", "{}", "{\"path\":\"/\",\"bootstrap\":{}}", "{\"path\":\"//evil\",\"bootstrap\":{}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"\"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\" Start new task in Project\"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"Start  new task in Project\"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"Start new task in Project \"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"native-new-chat\",\"controlLabel\":\"Start new task in Oâ€™Reilly\"}}",
        "{\"path\":\"/\",\"bootstrap\":{\"mode\":\"other\",\"controlLabel\":\"Start new task in Project\"}}",
        "{\"path\":\"/local/%2e%2e/settings\",\"bootstrap\":{}}",
        "{\"path\":\"/local/%252e%252e/settings\",\"bootstrap\":{}}",
        "{\"path\":\"/local%2f..%2fsettings\",\"bootstrap\":{}}",
        "{\"path\":\"/local%252f..%252fsettings\",\"bootstrap\":{}}",
        "{\"path\":\"/local/%5cthread\",\"bootstrap\":{}}",
        "{\"path\":\"/local/thread\",\"helperId\":\"other\",\"bootstrap\":{}}",
        $"{{\"path\":\"/local/thread\",\"bootstrap\":\"{new string('x', 16 * 1024)}\"}}"
    })
    {
        using var invalidOpenChildPayload = JsonDocument.Parse(invalidPayload);
        Throws<InvalidOperationException>(() => ExactChildBootstrapRequest.Parse("valid-helper", invalidOpenChildPayload.RootElement), "openChild rejects invalid payload");
    }

    HelperSettingsStore.Save(helperSettingsPath, persistedReloaded.WithHelperEnabled("valid-helper", true));
    var hostTargets = new[] { new CodexTarget("host-window", "page", "app://-/index.html", "ws://host-window") };
    var hostEvaluator = new HostFakeEvaluator();
    hostEvaluator.DrainResults.Enqueue(Json("""
[
  {"helperId":"valid-helper","action":"codex.openNewChatWindow","payload":{"source":"fixture"}},
  {"helperId":"override-me","action":"codex.openNewChatWindow","payload":null},
  {"helperId":"missing-helper","action":"codex.openNewChatWindow","payload":null}
]
"""));
    hostEvaluator.FailExpressionsContaining = "override-me";
    var actionDispatcher = new RecordingHostActionDispatcher();
    var host = new HelperHost(
        bundledRoot,
        userRoot,
        helperSettingsPath,
        new FakeTargetSource(hostTargets),
        hostEvaluator,
        actionDispatcher,
        [sessionCapabilityRoot],
        () => new DateTimeOffset(2026, 7, 11, 12, 34, 56, TimeSpan.Zero));
    await host.ReloadAsync();
    Equal(true, host.Helpers.Any(summary => summary.Id == "valid-helper"), "host exposes dynamic helper summaries");

    var deletedPackageRoot = Path.Combine(helperRoot, "deleted-package-root");
    var deletedPackageFolder = WritePackage(deletedPackageRoot, "Deleted package", "deleted-package");
    var deletionHost = new HelperHost(
        deletedPackageRoot,
        Path.Combine(helperRoot, "deleted-package-settings", "settings.json"),
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        []);
    Equal(true, deletionHost.Helpers.Any(summary => summary.Id == "deleted-package"), "catalog initially exposes package before deletion");
    Directory.Delete(deletedPackageFolder, recursive: true);
    await deletionHost.ReloadAsync();
    Equal(false, deletionHost.Helpers.Any(summary => summary.Id == "deleted-package"), "reload drops a deleted package from summaries");

    var hostReport = await host.ReconcileAsync();
    Equal(true, hostReport.Succeeded > 0, "one helper can reconcile successfully");
    Equal(true, hostReport.Failed > 0, "failed helper reconciliation is reported");
    Equal(1, actionDispatcher.Requests.Count, "only declared actions from known enabled helpers dispatch");
    Equal("valid-helper", actionDispatcher.Requests.Single().HelperId, "dispatched action retains helper ownership");
    Equal("host-window", actionDispatcher.Requests.Single().SourceTarget?.Id, "dispatched action retains its originating Codex target");
    Equal(true, hostReport.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("missing-helper", StringComparison.Ordinal)), "unknown action ownership is diagnosed");
    Equal(true, hostReport.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("override-me", StringComparison.Ordinal)), "undeclared action capability is diagnosed");

    hostEvaluator.DrainResults.Enqueue(Json("""
[
  {"helperId":"valid-helper","action":"codex.openNewChatWindow","payload":{"source":"fast-poll"}}
]
"""));
    var fastActionReport = await host.DrainHostActionsAsync();
    Equal(0, fastActionReport.HelpersAttempted, "fast Host Action polling does not reapply Helpers");
    Equal(2, actionDispatcher.Requests.Count, "fast Host Action polling dispatches a queued renderer action");

    var disabledReport = await host.SetEnabledAsync("valid-helper", false);
    Equal(false, host.Helpers.Single(summary => summary.Id == "valid-helper").Enabled, "SetEnabled updates helper summary");
    Equal(false, HelperSettingsStore.Load(helperSettingsPath).IsHelperEnabled("valid-helper", true), "SetEnabled persists per-helper state");
    Equal(true, disabledReport.Succeeded > 0, "disabling removes helper effects from current targets");

    hostEvaluator.FailExpressionsContaining = null;
    var removeAllReport = await host.RemoveAllAsync();
    Equal(true, removeAllReport.Failed == 0, "explicit cleanup removes every loaded helper independently");
    Equal(true, hostEvaluator.Expressions.Any(expression => expression.Contains("__codexHelperUsageDials", StringComparison.Ordinal)), "explicit cleanup broadcasts legacy Usage Dials cleanup");

    var adapterEvaluator = new HostFakeEvaluator();
    await new HostActionDispatcher(adapterEvaluator).DispatchAsync(
        new HostActionRequest("valid-helper", HostActionDispatcher.OpenNewChatWindow, validNewWindowPayload.RootElement, hostTargets[0]));
    Equal(CodexOpenNewChatWindowAdapter.BuildExpression(validNewWindowPayload.RootElement), adapterEvaluator.Expressions.Single(), "new-window Host Action dispatches the verified native bridge expression on its source target");
    var openedObsidianUris = new List<string>();
    using (var legacyObsidianPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { uri = "obsidian://open?vault=Jarvis&file=Existing.md" })))
    {
        await new HostActionDispatcher(adapterEvaluator, (uri, _) => { openedObsidianUris.Add(uri); return Task.CompletedTask; }).DispatchAsync(
            new HostActionRequest("obsidian-links", HostActionDispatcher.OpenObsidianUri, legacyObsidianPayload.RootElement, hostTargets[0]));
    }
    Equal("obsidian://open?vault=Jarvis&file=Existing.md", openedObsidianUris.Single(), "Obsidian Host Action preserves legacy open URIs");

    var openedFilePaths = new List<string>();
    using (var filePathPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { path = "R:/codex/Projects/ExampleWorkspace/Example Project/home.html" })))
    {
        await new HostActionDispatcher(
            adapterEvaluator,
            openFilePath: (path, _) => { openedFilePaths.Add(path); return Task.CompletedTask; }).DispatchAsync(
            new HostActionRequest("clickable-file-links", HostActionDispatcher.OpenFilePath, filePathPayload.RootElement, hostTargets[0]));
    }
    Equal("R:/codex/Projects/ExampleWorkspace/Example Project/home.html", openedFilePaths.Single(), "File path Host Action preserves a normalized J path for the Windows shell");
    openedFilePaths.Clear();
    using (var localWindowsPathPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { path = "C:/Users/Example/Documents/Tools/Example Tool.exe" })))
    {
        await new HostActionDispatcher(
            adapterEvaluator,
            openFilePath: (path, _) => { openedFilePaths.Add(path); return Task.CompletedTask; }).DispatchAsync(
            new HostActionRequest("clickable-file-links", HostActionDispatcher.OpenFilePath, localWindowsPathPayload.RootElement, hostTargets[0]));
    }
    Equal("C:/Users/Example/Documents/Tools/Example Tool.exe", openedFilePaths.Single(), "path Host Action preserves a normalized absolute local Windows path without an extension allowlist");
    var foregroundHandles = new Queue<IntPtr>(new[] { IntPtr.Zero, new IntPtr(42) });
    var foregroundActivations = new List<IntPtr>();
    var foregroundDelays = 0;
    var foregroundActivated = await WindowsForegroundActivator.WaitAndActivateAsync(
        () => foregroundHandles.Dequeue(),
        handle => { foregroundActivations.Add(handle); return true; },
        (_, _) => { foregroundDelays++; return Task.CompletedTask; },
        attempts: 3,
        CancellationToken.None);
    Equal(true, foregroundActivated, "file launching activates the first available application window");
    Equal(1, foregroundDelays, "file launching waits while the associated application creates its window");
    Equal(new IntPtr(42), foregroundActivations.Single(), "file launching targets the associated application window");

    foreach (var invalidPath in new[]
    {
        "/mnt/example-data/projects/file.html",
        "R:/codex/../Secret.txt",
        "R:\\codex\\file.html",
        "R://codex/file.html",
    })
    {
        using var invalidFilePathPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { path = invalidPath }));
        Throws<InvalidOperationException>(() => FilePathOpenAdapter.Parse(invalidFilePathPayload.RootElement), $"File path Host Action rejects {invalidPath}");
    }

    openedObsidianUris.Clear();
    using (var coldStartObsidianPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        uri = "obsidian://wait-for-note?vault=Jarvis&file=Notifications%2FTelegram%20concierge.md%23Review%20or%20reminder",
    })))
    using (var coldStartCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
    {
        await ThrowsAsync<OperationCanceledException>(
            () => new HostActionDispatcher(adapterEvaluator, (uri, _) => { openedObsidianUris.Add(uri); return Task.CompletedTask; }).DispatchAsync(
                new HostActionRequest("obsidian-links", HostActionDispatcher.OpenObsidianUri, coldStartObsidianPayload.RootElement, hostTargets[0]),
                coldStartCancellation.Token),
            "sync-aware Obsidian Host Action gives a closed vault time to load its community plugin");
    }
    Equal("obsidian://open?vault=Jarvis", openedObsidianUris.Single(), "sync-aware Obsidian Host Action opens the requested vault before invoking the plugin action");

    openedObsidianUris.Clear();
    using (var syncAwareObsidianPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        uri = "obsidian://wait-for-note?vault=Jarvis&file=Notifications%2FTelegram%20concierge.md%23Review%20or%20reminder",
    })))
    {
        await new HostActionDispatcher(adapterEvaluator, (uri, _) => { openedObsidianUris.Add(uri); return Task.CompletedTask; }).DispatchAsync(
            new HostActionRequest("obsidian-links", HostActionDispatcher.OpenObsidianUri, syncAwareObsidianPayload.RootElement, hostTargets[0]));
    }
    Equal(
        "obsidian://open?vault=Jarvis|obsidian://wait-for-note?vault=Jarvis&file=Notifications%2FTelegram%20concierge.md%23Review%20or%20reminder",
        string.Join('|', openedObsidianUris),
        "sync-aware Obsidian Host Action invokes the plugin only after opening its vault");
    foreach (var invalidUri in new[]
    {
        "https://example.test",
        "obsidian://new?vault=Jarvis&file=Note.md",
        "obsidian://wait-for-note?vault=Jarvis&file=../Secret.md",
        "obsidian://wait-for-note?vault=Jarvis&file=Note.txt",
        "obsidian://wait-for-note?vault=Jarvis&file=Note.md&extra=nope",
        "obsidian://wait-for-note?vault=Jarvis&vault=Other&file=Note.md",
    })
    {
        using var invalidObsidianPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { uri = invalidUri }));
        Throws<InvalidOperationException>(() => ObsidianUriOpenAdapter.Parse(invalidObsidianPayload.RootElement), $"Obsidian Host Action rejects {invalidUri}");
    }

    hostEvaluator.Expressions.Clear();
    await host.ReloadAsync();
    Equal(true, hostEvaluator.Expressions.Any(expression => expression.Contains("delete window.__fixtureApply", StringComparison.Ordinal)), "reload removes effects using the old catalog before replacing packages");

    var retryBundledRoot = Path.Combine(helperRoot, "retry-bundled");
    var retryUserRoot = Path.Combine(helperRoot, "retry-user");
    Directory.CreateDirectory(retryBundledRoot);
    Directory.CreateDirectory(retryUserRoot);
    WritePackage(retryBundledRoot, "retry-cleanup", "retry-cleanup");
    var retryEvaluator = new HostFakeEvaluator
    {
        FailExpressionsContaining = "delete window.__fixtureApply",
        FailuresRemaining = 1,
    };
    var retryHost = new HelperHost(
        retryBundledRoot,
        retryUserRoot,
        Path.Combine(helperRoot, "retry-settings", "settings.json"),
        new FakeTargetSource(hostTargets),
        retryEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    var failedDisable = await retryHost.SetEnabledAsync("retry-cleanup", false);
    Equal(1, failedDisable.Failed, "disable reports transient cleanup failure");
    retryEvaluator.Expressions.Clear();
    var retryReport = await retryHost.ReconcileAsync();
    Equal(true, retryEvaluator.Expressions.Any(expression => expression.Contains("delete window.__fixtureApply", StringComparison.Ordinal)), "next reconcile retries cleanup for disabled helper");
    Equal(0, retryReport.Failed, "recovered disabled cleanup converges without re-enabling helper");
    Equal(false, retryHost.Helpers.Single().Enabled, "cleanup retry leaves helper disabled");

    var suspendedBundledRoot = Path.Combine(helperRoot, "suspended-bundled");
    var suspendedUserRoot = Path.Combine(helperRoot, "suspended-user");
    Directory.CreateDirectory(suspendedBundledRoot);
    Directory.CreateDirectory(suspendedUserRoot);
    WritePackage(suspendedBundledRoot, "suspended-helper", "suspended-helper");
    var suspendedEvaluator = new HostFakeEvaluator();
    var suspendedHost = new HelperHost(
        suspendedBundledRoot,
        suspendedUserRoot,
        Path.Combine(helperRoot, "suspended-settings", "settings.json"),
        new FakeTargetSource(hostTargets),
        suspendedEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    await suspendedHost.ReconcileAsync();
    Equal(true, suspendedEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "active Host initially applies enabled helper");
    await suspendedHost.SuspendAsync();
    Equal(true, suspendedHost.IsSuspended, "manual remove enters explicit suspended state");
    suspendedEvaluator.Expressions.Clear();
    await suspendedHost.ReconcileAsync();
    Equal(false, suspendedEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "timer reconciliation does not reapply helpers while suspended");
    suspendedEvaluator.Expressions.Clear();
    await suspendedHost.ResumeAndReconcileAsync();
    Equal(false, suspendedHost.IsSuspended, "manual inject refresh resumes Host");
    Equal(true, suspendedEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "manual inject refresh reapplies enabled helpers");
    await suspendedHost.SuspendAsync();
    suspendedEvaluator.Expressions.Clear();
    await suspendedHost.SetEnabledAsync("suspended-helper", true);
    Equal(false, suspendedHost.IsSuspended, "enabling helper resumes suspended Host");
    Equal(true, suspendedEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "enabling helper reapplies after suspension");

    var cdpTimeoutBundledRoot = Path.Combine(helperRoot, "cdp-timeout-bundled");
    var cdpTimeoutUserRoot = Path.Combine(helperRoot, "cdp-timeout-user");
    Directory.CreateDirectory(cdpTimeoutBundledRoot);
    Directory.CreateDirectory(cdpTimeoutUserRoot);
    WritePackage(cdpTimeoutBundledRoot, "cdp-timeout", "cdp-timeout");
    var timedOutSockets = new List<NeverReplyCdpWebSocket>();
    var cdpTimeoutEvaluator = new WebSocketCdpEvaluator(TimeSpan.FromMilliseconds(30), () =>
    {
        var socket = new NeverReplyCdpWebSocket();
        timedOutSockets.Add(socket);
        return socket;
    });
    var cdpTimeoutHost = new HelperHost(
        cdpTimeoutBundledRoot,
        cdpTimeoutUserRoot,
        Path.Combine(helperRoot, "cdp-timeout-settings", "settings.json"),
        new FakeTargetSource([new CodexTarget("cdp-timeout-target", "page", "app://-/index.html", "ws://cdp-timeout")]),
        cdpTimeoutEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    var cdpLifecycleStopwatch = Stopwatch.StartNew();
    var cdpTimeoutReport = await cdpTimeoutHost.ReconcileAsync();
    Equal(true, cdpTimeoutReport.Failed > 0, "Host isolates non-responsive CDP target as reconciliation failure");
    var cleanupAfterCdpTimeout = await cdpTimeoutHost.RemoveAllAsync();
    cdpLifecycleStopwatch.Stop();
    Equal(true, cleanupAfterCdpTimeout.Failed > 0, "cleanup reports non-responsive CDP target without wedging lifecycle");
    Equal(true, cdpLifecycleStopwatch.Elapsed < TimeSpan.FromSeconds(1), "Host gate and lifecycle converge across production CDP timeouts");
    Equal(true, timedOutSockets.All(socket => socket.AbortCalled), "every non-responsive production CDP socket is aborted");

    var listFailureBundledRoot = Path.Combine(helperRoot, "list-failure-bundled");
    var listFailureUserRoot = Path.Combine(helperRoot, "list-failure-user");
    Directory.CreateDirectory(listFailureBundledRoot);
    Directory.CreateDirectory(listFailureUserRoot);
    var listFailurePackageRoot = WritePackage(listFailureBundledRoot, "old-list-cleanup", "old-list-cleanup");
    await File.WriteAllTextAsync(Path.Combine(listFailurePackageRoot, "remove.js"), "window.__oldListCleanup=true;");
    var flakyTargetSource = new FlakyTargetSource(hostTargets);
    var listFailureEvaluator = new HostFakeEvaluator();
    var listFailureHost = new HelperHost(
        listFailureBundledRoot,
        listFailureUserRoot,
        Path.Combine(helperRoot, "list-failure-settings", "settings.json"),
        flakyTargetSource,
        listFailureEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    Directory.Delete(listFailurePackageRoot, recursive: true);
    flakyTargetSource.FailuresRemaining = 1;
    await listFailureHost.ReloadAsync();
    listFailureEvaluator.Expressions.Clear();
    await listFailureHost.ReconcileAsync();
    Equal(true, listFailureEvaluator.Expressions.Any(expression => expression.Contains("__oldListCleanup", StringComparison.Ordinal)), "reload preserves old remove snapshot across target-list failure and package deletion");

    var targetFailureBundledRoot = Path.Combine(helperRoot, "target-failure-bundled");
    var targetFailureUserRoot = Path.Combine(helperRoot, "target-failure-user");
    Directory.CreateDirectory(targetFailureBundledRoot);
    Directory.CreateDirectory(targetFailureUserRoot);
    var targetFailurePackageRoot = WritePackage(targetFailureBundledRoot, "old-target-cleanup", "old-target-cleanup");
    await File.WriteAllTextAsync(Path.Combine(targetFailurePackageRoot, "remove.js"), "window.__oldTargetCleanup=true;");
    var targetFailureEvaluator = new HostFakeEvaluator
    {
        FailExpressionsContaining = "__oldTargetCleanup",
        FailuresRemaining = 1,
    };
    var targetFailureHost = new HelperHost(
        targetFailureBundledRoot,
        targetFailureUserRoot,
        Path.Combine(helperRoot, "target-failure-settings", "settings.json"),
        new FakeTargetSource(hostTargets),
        targetFailureEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    Directory.Delete(targetFailurePackageRoot, recursive: true);
    await targetFailureHost.ReloadAsync();
    targetFailureEvaluator.Expressions.Clear();
    await targetFailureHost.ReconcileAsync();
    Equal(true, targetFailureEvaluator.Expressions.Any(expression => expression.Contains("__oldTargetCleanup", StringComparison.Ordinal)), "reload retries old remove snapshot after per-target cleanup failure and package deletion");

    var transactionalBundledRoot = Path.Combine(helperRoot, "transactional-bundled");
    var transactionalUserRoot = Path.Combine(helperRoot, "transactional-user");
    Directory.CreateDirectory(transactionalBundledRoot);
    Directory.CreateDirectory(transactionalUserRoot);
    WritePackage(transactionalBundledRoot, "transactional", "transactional");
    var blockedSettingsParent = Path.Combine(helperRoot, "blocked-settings-parent");
    await File.WriteAllTextAsync(blockedSettingsParent, "not-a-directory");
    var transactionalEvaluator = new HostFakeEvaluator();
    var transactionalHost = new HelperHost(
        transactionalBundledRoot,
        transactionalUserRoot,
        Path.Combine(blockedSettingsParent, "settings.json"),
        new FakeTargetSource(hostTargets),
        transactionalEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    await ThrowsAsync<Exception>(() => transactionalHost.SetEnabledAsync("transactional", false), "failed settings save surfaces to caller");
    transactionalEvaluator.Expressions.Clear();
    await transactionalHost.ReconcileAsync();
    Equal(true, transactionalEvaluator.Expressions.Any(expression => expression.Contains("const state={}", StringComparison.Ordinal)), "failed settings save leaves prior enabled behavior active");
    Equal(true, transactionalHost.Helpers.Single().Enabled, "failed settings save leaves in-memory summary unchanged");

    var rootReloadBundled = Path.Combine(helperRoot, "root-reload-bundled");
    var rootReloadUser = Path.Combine(helperRoot, "root-reload-user");
    var rootReloadA = Path.Combine(helperRoot, "root-reload-a");
    var rootReloadB = Path.Combine(helperRoot, "root-reload-b");
    Directory.CreateDirectory(rootReloadBundled);
    Directory.CreateDirectory(rootReloadUser);
    Directory.CreateDirectory(rootReloadA);
    Directory.CreateDirectory(rootReloadB);
    WritePackage(
        rootReloadBundled,
        "root-reload",
        "root-reload",
        capabilities: ["files.codexSessions"],
        backend: "backend.js",
        backendSource: "function refresh(wingman) { return { roots: wingman.files.codexSessions.roots() }; }");
    var rootReloadSettingsPath = Path.Combine(helperRoot, "root-reload-settings", "settings.json");
    HelperSettingsStore.Save(rootReloadSettingsPath, new HelperSettings(true, [rootReloadA]));
    var rootReloadEvaluator = new HostFakeEvaluator();
    var rootReloadHost = new HelperHost(
        rootReloadBundled,
        rootReloadUser,
        rootReloadSettingsPath,
        new FakeTargetSource(hostTargets),
        rootReloadEvaluator,
        new RecordingHostActionDispatcher(),
        [rootReloadA]);
    await rootReloadHost.ReconcileAsync();
    Equal(true, rootReloadEvaluator.Expressions.Any(expression => expression.Contains("root-reload-a", StringComparison.Ordinal)), "initial backend exposes startup session roots");
    HelperSettingsStore.Save(rootReloadSettingsPath, new HelperSettings(true, [rootReloadB]));
    await rootReloadHost.ReloadAsync();
    rootReloadEvaluator.Expressions.Clear();
    await rootReloadHost.ReconcileAsync();
    Equal(true, rootReloadEvaluator.Expressions.Any(expression => expression.Contains("root-reload-b", StringComparison.Ordinal)), "reload rebuilds backend capability with new settings session roots");
    Equal(false, rootReloadEvaluator.Expressions.Any(expression => expression.Contains("root-reload-a", StringComparison.Ordinal)), "reload retires stale backend session roots");

    var cadenceBundledRoot = Path.Combine(helperRoot, "cadence-bundled");
    var cadenceUserRoot = Path.Combine(helperRoot, "cadence-user");
    Directory.CreateDirectory(cadenceBundledRoot);
    Directory.CreateDirectory(cadenceUserRoot);
    WritePackage(
        cadenceBundledRoot,
        "cadence",
        "cadence",
        refreshSeconds: 300,
        backend: "backend.js",
        backendSource: "function refresh(wingman) { wingman.log('refreshed'); return { ok: true }; }");
    var refreshLogCount = 0;
    var cadenceHost = new HelperHost(
        cadenceBundledRoot,
        cadenceUserRoot,
        Path.Combine(helperRoot, "cadence-settings.json"),
        new FakeTargetSource([]),
        new HostFakeEvaluator(),
        new RecordingHostActionDispatcher(),
        [],
        () => new DateTimeOffset(2026, 7, 11, 12, 34, 56, TimeSpan.Zero),
        _ => refreshLogCount++);
    await cadenceHost.ReconcileAsync();
    await cadenceHost.ReconcileAsync();
    Equal(1, refreshLogCount, "backend state is reused until manifest refreshSeconds elapses");

    var slowSummary = new HelperSummary("slow", "Slow", "1.0.0", true, null, 60);
    var shortDisabledSummary = new HelperSummary("short", "Short", "1.0.0", false, null, 2);
    var shortEnabledSummary = shortDisabledSummary with { Enabled = true };
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds([slowSummary, shortDisabledSummary]), "disabled short-cadence helper does not accelerate polling");
    Equal(2_000, HelperRefreshSchedule.IntervalMilliseconds([slowSummary, shortEnabledSummary]), "enabling short-cadence helper accelerates polling");
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds([slowSummary, shortEnabledSummary with { Enabled = false }]), "disabling short-cadence helper restores normal polling");
    var thirdPartyActionSummary = new HelperSummary(
        "third-party-action",
        "Third-party action",
        "1.0.0",
        true,
        null,
        RefreshSeconds: 0,
        CanToggle: true,
        Capabilities: [HostActionDispatcher.OpenNewChatWindow],
        Source: HelperPackageSource.User);
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds([thirdPartyActionSummary]), "renderer-only Host Action helper does not accelerate full reconciliation");
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds([thirdPartyActionSummary with { RefreshSeconds = 300 }]), "lightweight Host Action polling remains independent of slow backend refresh cadence");
    Equal(60_000, HelperRefreshSchedule.IntervalMilliseconds([thirdPartyActionSummary with { Capabilities = ["unknown.action"] }]), "unknown capability does not silently accelerate polling");
    Equal(true, HelperRefreshSchedule.RequiresHostActionPolling([thirdPartyActionSummary]), "known Host Action capability enables the lightweight action timer");
    Equal(false, HelperRefreshSchedule.RequiresHostActionPolling([thirdPartyActionSummary with { Enabled = false }]), "disabled Host Action helper does not keep the lightweight action timer active");
    Equal(250, HelperRefreshSchedule.HostActionIntervalMilliseconds, "Host Actions use a responsive lightweight poll interval");

    var projectRoot = FindProjectRoot();
    var nativeLookupRoot = Path.Combine(helperRoot, "bounded-lookup", "sessions");
    var nativeLookupId = "01a0a096-910f-7193-8f53-71c79230a69e";
    var nativeLookupDay = Path.Combine(nativeLookupRoot, "2026", "09", "14");
    Directory.CreateDirectory(nativeLookupDay);
    await File.WriteAllTextAsync(Path.Combine(nativeLookupDay, $"rollout-{nativeLookupId}.jsonl"), """
{"timestamp":"2026-09-14T11:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":42}}}}
""");
    var searchedNativeDays = new List<string>();
    var boundedLookup = new FileSystemHelperSessionFiles([nativeLookupRoot], (root, pattern, options) => {
        if (root == nativeLookupRoot && options.RecurseSubdirectories)
            throw new InvalidOperationException("Regression: recursive network-wide session lookup");
        searchedNativeDays.Add(root);
        return Directory.EnumerateFiles(root, pattern, options);
    });
    Equal(false, boundedLookup.ReadSessionSnapshotJson(nativeLookupId, CancellationToken.None) is null, "native UUIDv7 lookup resolves its dated rollout without scanning all history");
    Equal(true, searchedNativeDays.Contains(nativeLookupDay), "native lookup probes the UUID creation day");
    var adjacentLookupDay = Path.Combine(nativeLookupRoot, "2026", "09", "13");
    Directory.CreateDirectory(adjacentLookupDay);
    File.Move(Path.Combine(nativeLookupDay, $"rollout-{nativeLookupId}.jsonl"), Path.Combine(adjacentLookupDay, $"rollout-{nativeLookupId}.jsonl"));
    Equal(false, boundedLookup.ReadSessionSnapshotJson(nativeLookupId, CancellationToken.None) is null, "native lookup handles the adjacent timezone date");
    var legacyLookupId = "019f61c0-1111-4111-8111-111111111111";
    var legacyLookupFolder = Path.Combine(nativeLookupRoot, "imported-history");
    Directory.CreateDirectory(legacyLookupFolder);
    File.Copy(Path.Combine(adjacentLookupDay, $"rollout-{nativeLookupId}.jsonl"), Path.Combine(legacyLookupFolder, $"rollout-{legacyLookupId}.jsonl"));
    Equal(false, new FileSystemHelperSessionFiles([nativeLookupRoot]).ReadSessionSnapshotJson(legacyLookupId, CancellationToken.None) is null, "legacy IDs retain recursive discovery");
    var actualHelpersRoot = Path.Combine(projectRoot, "helpers");
    var actualUsagePackagePath = Path.Combine(actualHelpersRoot, "usage-dials", "wingman.json");
    Equal(true, File.Exists(actualUsagePackagePath), "actual Usage Dials package is present for Host integration");
    var actualSessionRoot = Path.Combine(helperRoot, "actual-usage-sessions");
    Directory.CreateDirectory(actualSessionRoot);
    var actualSessionText = new StringBuilder();
    for (var ignoredLine = 0; ignoredLine < 20_000; ignoredLine++)
        actualSessionText.AppendLine("{\"timestamp\":\"2026-07-11T12:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"other\"}}");
    actualSessionText.AppendLine("""
{"timestamp":"2026-07-11T12:30:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":37,"window_minutes":300,"resets_at":1783803600},"secondary":{"used_percent":61,"window_minutes":10080,"resets_at":1784401200}}}}
""");
    await File.WriteAllTextAsync(Path.Combine(actualSessionRoot, "actual.jsonl"), actualSessionText.ToString());
    var actualSettingsPath = Path.Combine(helperRoot, "actual-usage-settings", "settings.json");
    var actualSettings = new HelperSettings(true, [actualSessionRoot])
        .WithHelperEnabled("usage-dials", true)
        .WithHelperEnabled("sidebar-on-demand", false)
        .WithHelperEnabled("new-chat-window", false);
    HelperSettingsStore.Save(actualSettingsPath, actualSettings);
    var actualUsageEvaluator = new HostFakeEvaluator();
    var actualUsageHost = new HelperHost(
        actualHelpersRoot,
        Path.Combine(helperRoot, "actual-usage-user"),
        actualSettingsPath,
        new FakeTargetSource(hostTargets),
        actualUsageEvaluator,
        new RecordingHostActionDispatcher(),
        [actualSessionRoot],
        () => new DateTimeOffset(2026, 7, 11, 12, 34, 56, TimeSpan.Zero));
    Equal(true, actualUsageHost.Helpers.Any(helper => helper.Id == "usage-dials"), "Host discovers actual Usage Dials manifest");
    var actualUsageReport = await actualUsageHost.ReconcileAsync();
    Equal(0, actualUsageReport.Failed, "actual Usage Dials Host reconciliation succeeds through real Jint");
    Equal(true, actualUsageEvaluator.Expressions.Any(expression =>
        expression.Contains("helperId=\"usage-dials\"", StringComparison.Ordinal)
        && expression.Contains("getQueryState(['rate-limit-status'])", StringComparison.Ordinal)), "actual Usage Dials native account adapter reaches renderer wrapper");
    var actualUsagePackage = new HelperCatalog().Discover(actualHelpersRoot).Packages.Single(package => package.Manifest.Id == "usage-dials");
    var forbiddenUsageFiles = new ForbiddenUsageSessionFiles();
    using var accountOnlyState = await new HelperBackend([], sessionFiles: forbiddenUsageFiles).RefreshAsync(actualUsagePackage);
    Equal(0, forbiddenUsageFiles.AccessCount, "Usage Dials activation never touches slow or unavailable session storage");

    var visibleSessionId = Guid.Parse("019f61c0-1111-7111-8111-111111111111").ToString();
    var staleRouteSessionId = Guid.Parse("019f5d7d-2222-7222-8222-222222222222").ToString();
    var visibleSessionRoot = Path.Combine(helperRoot, "visible-target-sessions");
    Directory.CreateDirectory(visibleSessionRoot);
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-{visibleSessionId}.jsonl"), """
{"timestamp":"2026-07-14T14:59:59Z","type":"session_meta","payload":{"id":"__SESSION__"}}
{"timestamp":"2026-07-14T15:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":61000},"model_context_window":258400}}}
""".Replace("__SESSION__", visibleSessionId, StringComparison.Ordinal));
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-{staleRouteSessionId}.jsonl"), """
{"timestamp":"2026-07-14T15:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":93000},"model_context_window":258400}}}
""");
    var metadataSessionId = Guid.Parse("019f61c0-3333-7333-8333-333333333333").ToString();
    var firstMetadataTurnId = Guid.Parse("019f61c0-4444-7444-8444-444444444444").ToString();
    var secondMetadataTurnId = Guid.Parse("019f61c0-5555-7555-8555-555555555555").ToString();
    var childMetadataThreadId = Guid.Parse("019f61c0-6666-7666-8666-666666666666").ToString();
    var metadataSessionText = """
{"timestamp":"2026-07-14T16:00:00Z","type":"session_meta","payload":{"id":"__SESSION__","model_provider":"openai"}}
{malformed
{"timestamp":"2026-07-14T16:01:00Z","type":"event_msg","payload":{"type":"task_started","turn_id":"__TURN_ONE__"}}
{"timestamp":"2026-07-14T16:01:01Z","type":"turn_context","payload":{"turn_id":"__TURN_ONE__","model":"gpt-5.6-sol","model_provider":"openrouter"}}
{"timestamp":"2026-07-14T16:01:04Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":101,"cached_input_tokens":11,"output_tokens":17,"reasoning_output_tokens":3,"total_tokens":121}}}}
{"timestamp":"2026-07-14T16:01:05Z","type":"response_item","payload":{"type":"subAgentActivity","agentThreadId":"__CHILD__","model":"gpt-5.6-luna","reasoningEffort":"high","status":"completed","internal_chat_message_metadata_passthrough":{"turn_id":"__TURN_ONE__"}}}
{"timestamp":"2026-07-14T16:01:06Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"__TURN_ONE__"}}
{"timestamp":"2026-07-14T16:02:00Z","type":"turn_context","payload":{"turnId":"__TURN_TWO__","model":"gpt-5.6-terra"}}
{"timestamp":"2026-07-14T16:02:03Z","type":"event_msg","payload":{"type":"token_count","info":{"lastTokenUsage":{"inputTokens":50,"cachedInputTokens":5,"outputTokens":8,"reasoningOutputTokens":2,"totalTokens":60}}}}
"""
        .Replace("__SESSION__", metadataSessionId, StringComparison.Ordinal)
        .Replace("__TURN_ONE__", firstMetadataTurnId, StringComparison.Ordinal)
        .Replace("__TURN_TWO__", secondMetadataTurnId, StringComparison.Ordinal)
        .Replace("__CHILD__", childMetadataThreadId, StringComparison.Ordinal);
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-{metadataSessionId}.jsonl"), metadataSessionText);
    var metadataFiles = new FileSystemHelperSessionFiles([visibleSessionRoot]);
    var metadataJson = metadataFiles.ReadTurnMetadataJson(metadataSessionId, CancellationToken.None);
    Equal(false, metadataJson is null, "target session returns structured Turn Metadata");
    using (var metadataDocument = JsonDocument.Parse(metadataJson!))
    {
        var metadataRoot = metadataDocument.RootElement;
        Equal(metadataSessionId, metadataRoot.GetProperty("threadId").GetString(), "Turn Metadata keeps the validated thread ID");
        var firstRecord = metadataRoot.GetProperty("records").GetProperty(firstMetadataTurnId);
        Equal(firstMetadataTurnId, firstRecord.GetProperty("turnId").GetString(), "Turn Metadata is keyed by the exact turn ID");
        Equal("gpt-5.6-sol", firstRecord.GetProperty("model").GetString(), "Turn Metadata records the exact model");
        Equal("openrouter", firstRecord.GetProperty("providerId").GetString(), "per-turn provider overrides the session provider");
        Equal("OpenRouter", firstRecord.GetProperty("providerLabel").GetString(), "known provider IDs map to human labels");
        Equal(6_000L, firstRecord.GetProperty("durationMs").GetInt64(), "duration derives from task lifecycle timestamps");
        Equal(11L, firstRecord.GetProperty("tokenUsage").GetProperty("cachedInputTokens").GetInt64(), "cached input remains separate");
        Equal(3L, firstRecord.GetProperty("tokenUsage").GetProperty("reasoningOutputTokens").GetInt64(), "reasoning output remains separate");
        Equal(childMetadataThreadId, firstRecord.GetProperty("subagents")[0].GetProperty("threadId").GetString(), "known child thread ID is retained");
        Equal("complete", firstRecord.GetProperty("completeness").GetString(), "fully recorded turn is complete");
        var secondRecord = metadataRoot.GetProperty("records").GetProperty(secondMetadataTurnId);
        Equal("OpenAI", secondRecord.GetProperty("providerLabel").GetString(), "session provider supplies a known fallback");
        Equal(50L, secondRecord.GetProperty("tokenUsage").GetProperty("inputTokens").GetInt64(), "camelCase usage fields are accepted");
        Equal("partial", secondRecord.GetProperty("completeness").GetString(), "missing lifecycle completion marks a record partial");
        Equal(false, metadataJson!.Contains("malformed", StringComparison.Ordinal), "raw session content is not exposed");
    }
    Equal(null, metadataFiles.ReadTurnMetadataJson("not-a-guid", CancellationToken.None), "invalid target identity returns unavailable Turn Metadata");

    var hookTraceSessionId = Guid.Parse("019f70bd-1111-7111-8111-111111111111").ToString();
    var hookTraceTurnId = Guid.Parse("019f70bd-2222-7222-8222-222222222222").ToString();
    var otherHookTraceTurnId = Guid.Parse("019f70bd-3333-7333-8333-333333333333").ToString();
    var hookTraceSessionText = """
{"timestamp":"2026-07-17T15:00:00Z","type":"session_meta","payload":{"id":"__SESSION__"}}
{"timestamp":"2026-07-17T15:00:01Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Startup developer instruction"}]}}
{malformed
{"timestamp":"2026-07-17T15:01:00Z","type":"turn_context","payload":{"turn_id":"__TURN__"}}
{"timestamp":"2026-07-17T15:01:00.100Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Permissions supplied before the visible message"}]}}
{"timestamp":"2026-07-17T15:01:00.200Z","type":"response_item","payload":{"type":"message","role":"system","content":[{"type":"input_text","text":"System context supplied before the visible message"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Please inspect this"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:02Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Prompt hook one"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:03Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Prompt hook two\nwith its complete second line"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:04Z","type":"response_item","payload":{"type":"reasoning","internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:05Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Check command policy"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:06Z","type":"response_item","payload":{"type":"custom_tool_call","name":"exec","call_id":"call-one","internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:07Z","type":"response_item","payload":{"type":"custom_tool_call_output","call_id":"call-one","output":[],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:08Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Record the durable result"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:09Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Finished"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:01:10Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Continue after stop"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T15:02:00Z","type":"turn_context","payload":{"turn_id":"__OTHER__"}}
{"timestamp":"2026-07-17T15:02:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Other prompt"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__OTHER__"}}}
{"timestamp":"2026-07-17T15:02:02Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Other prompt hook"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__OTHER__"}}}
"""
        .Replace("__SESSION__", hookTraceSessionId, StringComparison.Ordinal)
        .Replace("__TURN__", hookTraceTurnId, StringComparison.Ordinal)
        .Replace("__OTHER__", otherHookTraceTurnId, StringComparison.Ordinal);
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-{hookTraceSessionId}.jsonl"), hookTraceSessionText);
    var hookTraceJson = metadataFiles.ReadHookTraceJson(hookTraceSessionId, CancellationToken.None);
    Equal(false, hookTraceJson is null, "target session returns structured Hook Trace");
    using (var hookTraceDocument = JsonDocument.Parse(hookTraceJson!))
    {
        var hookTraceRoot = hookTraceDocument.RootElement;
        Equal(hookTraceSessionId, hookTraceRoot.GetProperty("threadId").GetString(), "Hook Trace keeps the validated thread ID");
        Equal("available", hookTraceRoot.GetProperty("status").GetString(), "a readable exact rollout is distinguished from an unavailable session");
        var records = hookTraceRoot.GetProperty("records").GetProperty(hookTraceTurnId);
        Equal(7, records.GetArrayLength(), "Hook Trace keeps every model-visible insertion for the exact turn, including context before the visible message");
        Equal("user", records[0].GetProperty("side").GetString(), "prompt context belongs to the user side");
        Equal("modelContext", records[0].GetProperty("kind").GetString(), "saved model context is not falsely claimed as a proven hook run");
        Equal("Before your message", records[0].GetProperty("label").GetString(), "context before the visible user message is labelled honestly");
        Equal("Permissions supplied before the visible message", records[0].GetProperty("text").GetString(), "pre-message developer context remains complete");
        Equal("System context supplied before the visible message", records[1].GetProperty("text").GetString(), "pre-message system context remains complete");
        Equal("Prompt-submission context", records[2].GetProperty("label").GetString(), "prompt context receives user-facing copy");
        Equal("Prompt hook two\nwith its complete second line", records[3].GetProperty("text").GetString(), "injected text remains complete and untruncated");
        Equal("Before tool use", records[4].GetProperty("label").GetString(), "developer context immediately before a tool call is classified before tool use");
        Equal(0, records[4].GetProperty("activityIndex").GetInt32(), "before-tool context retains its exact tool ordinal");
        Equal("After tool use", records[5].GetProperty("label").GetString(), "developer context after a tool result is classified after tool use");
        Equal(0, records[5].GetProperty("activityIndex").GetInt32(), "after-tool context retains its exact tool ordinal");
        Equal("Stop context", records[6].GetProperty("label").GetString(), "developer context after an assistant response is classified as stop context");
        Equal("Source not recorded by Codex", records[0].GetProperty("sourceLabel").GetString(), "unknown provenance is labelled without guessing");
        Equal(false, hookTraceJson!.Contains("Startup developer instruction", StringComparison.Ordinal), "thread-start developer instructions without a turn are not exposed as turn hooks");
        Equal(1, hookTraceRoot.GetProperty("records").GetProperty(otherHookTraceTurnId).GetArrayLength(), "separate turns remain isolated");
    }
    var subagentHookTraceRolloutId = Guid.Parse("019f70bd-4444-7444-8444-444444444444").ToString();
    var subagentHookTraceTurnId = Guid.Parse("019f70bd-5555-7555-8555-555555555555").ToString();
    var subagentHookTraceText = """
{"timestamp":"2026-07-17T16:00:00Z","type":"session_meta","payload":{"session_id":"__SESSION__","id":"__ROLLOUT__","parent_thread_id":"__SESSION__","source":{"subagent":{"thread_spawn":{"parent_thread_id":"__SESSION__"}}}}}
{"timestamp":"2026-07-17T16:01:00Z","type":"turn_context","payload":{"turn_id":"__TURN__"}}
{"timestamp":"2026-07-17T16:01:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Subagent prompt"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T16:01:02Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Subagent prompt hook"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
"""
        .Replace("__SESSION__", hookTraceSessionId, StringComparison.Ordinal)
        .Replace("__ROLLOUT__", subagentHookTraceRolloutId, StringComparison.Ordinal)
        .Replace("__TURN__", subagentHookTraceTurnId, StringComparison.Ordinal);
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-{subagentHookTraceRolloutId}.jsonl"), subagentHookTraceText);
    var isolatedHookTraceJson = metadataFiles.ReadHookTraceJson(hookTraceSessionId, CancellationToken.None);
    using (var isolatedHookTraceDocument = JsonDocument.Parse(isolatedHookTraceJson!))
    {
        Equal(false, isolatedHookTraceDocument.RootElement.GetProperty("records").TryGetProperty(subagentHookTraceTurnId, out _), "Hook Trace never merges a linked subagent rollout into the visible parent task");
    }
    var foreignHookTraceSessionId = Guid.Parse("019f70bd-6666-7666-8666-666666666666").ToString();
    var foreignHookTraceTurnId = Guid.Parse("019f70bd-7777-7777-8777-777777777777").ToString();
    var misleadingForeignText = """
{"timestamp":"2026-07-17T17:00:00Z","type":"session_meta","payload":{"id":"__FOREIGN__"}}
{"timestamp":"2026-07-17T17:01:00Z","type":"turn_context","payload":{"turn_id":"__TURN__"}}
{"timestamp":"2026-07-17T17:01:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Foreign prompt"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
{"timestamp":"2026-07-17T17:01:02Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Foreign hook text"}],"internal_chat_message_metadata_passthrough":{"turn_id":"__TURN__"}}}
"""
        .Replace("__FOREIGN__", foreignHookTraceSessionId, StringComparison.Ordinal)
        .Replace("__TURN__", foreignHookTraceTurnId, StringComparison.Ordinal);
    await File.WriteAllTextAsync(Path.Combine(visibleSessionRoot, $"rollout-copy-{hookTraceSessionId}-{foreignHookTraceSessionId}.jsonl"), misleadingForeignText);
    var filenameIsolatedJson = metadataFiles.ReadHookTraceJson(hookTraceSessionId, CancellationToken.None);
    using (var filenameIsolatedDocument = JsonDocument.Parse(filenameIsolatedJson!))
    {
        Equal(false, filenameIsolatedDocument.RootElement.GetProperty("records").TryGetProperty(foreignHookTraceTurnId, out _), "Hook Trace rejects a foreign rollout even when its filename contains the visible task ID");
    }
    Equal(null, metadataFiles.ReadHookTraceJson("not-a-guid", CancellationToken.None), "invalid target identity returns unavailable Hook Trace");
    var visibleTargetBundledRoot = Path.Combine(helperRoot, "visible-target-bundled");
    var visibleTargetUserRoot = Path.Combine(helperRoot, "visible-target-user");
    Directory.CreateDirectory(visibleTargetBundledRoot);
    Directory.CreateDirectory(visibleTargetUserRoot);
    WritePackage(
        visibleTargetBundledRoot,
        "visible-target-session",
        "visible-target-session",
        capabilities: ["files.codexTargetSession"],
        backend: "backend.js",
        refreshSeconds: 0,
        backendSource: "function refresh(wingman) { var snapshot = wingman.files.codexTargetSession.snapshot(); var metadata = wingman.files.codexTargetSession.turnMetadata(); var trace = wingman.files.codexTargetSession.hookTrace(); return { threadId: snapshot ? snapshot.threadId : null, metadataThreadId: metadata ? metadata.threadId : null, traceThreadId: trace ? trace.threadId : null }; }");
    var staleRouteTarget = new CodexTarget(
        "visible-target-window",
        "page",
        $"app://-/index.html?initialRoute=%2Flocal%2F{staleRouteSessionId}",
        "ws://visible-target-window");

    var visibleConversationNodes = new[]
    {
        new Dictionary<string, string> { ["data-above-composer-conversation-id"] = $"local:{visibleSessionId}" },
        new Dictionary<string, string> { ["data-request-user-input-auto-resolution-conversation-id"] = visibleSessionId },
    };
    var visibleTargetEvaluator = new HostFakeEvaluator();
    visibleTargetEvaluator.ValueResultFactory = (_, expression) =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal)
        && expression.Contains("data-request-user-input-auto-resolution-conversation-id", StringComparison.Ordinal)
            ? ExecuteTargetIdentityProbe(expression, visibleConversationNodes)
            : expression.Contains("data-app-action-sidebar-thread-id", StringComparison.Ordinal)
                ? Json($"\"{staleRouteSessionId}\"")
                : null;
    var visibleTargetHost = new HelperHost(
        visibleTargetBundledRoot,
        visibleTargetUserRoot,
        Path.Combine(helperRoot, "visible-target-settings", "settings.json"),
        new FakeTargetSource([staleRouteTarget]),
        visibleTargetEvaluator,
        new RecordingHostActionDispatcher(),
        [visibleSessionRoot]);
    var visibleTargetReport = await visibleTargetHost.ReconcileAsync();
    Equal(0, visibleTargetReport.Failed, "visible target-scoped Host reconciliation succeeds");
    var visibleTargetApply = visibleTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("helperId=\"visible-target-session\"", StringComparison.Ordinal));
    Equal(true, visibleTargetApply.Contains($"\"threadId\":\"{visibleSessionId}\"", StringComparison.Ordinal), "target-scoped state follows the rendered conversation identity");
    Equal(true, visibleTargetApply.Contains($"\"metadataThreadId\":\"{visibleSessionId}\"", StringComparison.Ordinal), "target-scoped Turn Metadata reaches the backend through the narrow interface");
    Equal(true, visibleTargetApply.Contains($"\"traceThreadId\":\"{visibleSessionId}\"", StringComparison.Ordinal), "target-scoped Hook Trace reaches the backend through the narrow interface");
    Equal(false, visibleTargetApply.Contains(staleRouteSessionId, StringComparison.Ordinal), "target-scoped state ignores stale sidebar and route identities");

    var missingTargetEvaluator = new HostFakeEvaluator();
    missingTargetEvaluator.ValueResultFactory = (_, expression) =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal)
        && expression.Contains("data-request-user-input-auto-resolution-conversation-id", StringComparison.Ordinal)
            ? ExecuteTargetIdentityProbe(expression, [])
            : expression.Contains("data-app-action-sidebar-thread-id", StringComparison.Ordinal)
                ? Json($"\"{staleRouteSessionId}\"")
                : null;
    var missingTargetHost = new HelperHost(
        visibleTargetBundledRoot,
        visibleTargetUserRoot,
        Path.Combine(helperRoot, "missing-target-settings", "settings.json"),
        new FakeTargetSource([staleRouteTarget]),
        missingTargetEvaluator,
        new RecordingHostActionDispatcher(),
        [visibleSessionRoot]);
    Equal(0, (await missingTargetHost.ReconcileAsync()).Failed, "missing visible identity remains a successful unavailable-state reconciliation");
    var missingTargetApply = missingTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("helperId=\"visible-target-session\"", StringComparison.Ordinal));
    Equal(true, missingTargetApply.Contains($"\"threadId\":\"{staleRouteSessionId}\"", StringComparison.Ordinal), "missing rendered identity uses the exact validated local route for historical read-only state");

    var conflictingConversationNodes = new[]
    {
        new Dictionary<string, string> { ["data-above-composer-conversation-id"] = visibleSessionId },
        new Dictionary<string, string> { ["data-request-user-input-auto-resolution-conversation-id"] = staleRouteSessionId },
    };
    var conflictingTargetEvaluator = new HostFakeEvaluator();
    conflictingTargetEvaluator.ValueResultFactory = (_, expression) =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal)
        && expression.Contains("data-request-user-input-auto-resolution-conversation-id", StringComparison.Ordinal)
            ? ExecuteTargetIdentityProbe(expression, conflictingConversationNodes)
            : expression.Contains("data-app-action-sidebar-thread-id", StringComparison.Ordinal)
                ? Json($"\"{staleRouteSessionId}\"")
                : null;
    var conflictingTargetHost = new HelperHost(
        visibleTargetBundledRoot,
        visibleTargetUserRoot,
        Path.Combine(helperRoot, "conflicting-target-settings", "settings.json"),
        new FakeTargetSource([staleRouteTarget]),
        conflictingTargetEvaluator,
        new RecordingHostActionDispatcher(),
        [visibleSessionRoot]);
    Equal(0, (await conflictingTargetHost.ReconcileAsync()).Failed, "conflicting visible identity remains a successful unavailable-state reconciliation");
    var conflictingTargetApply = conflictingTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("helperId=\"visible-target-session\"", StringComparison.Ordinal));
    Equal(true, conflictingTargetApply.Contains("\"threadId\":null", StringComparison.Ordinal), "conflicting visible identities supply unavailable target-scoped state");
    Equal(false, conflictingTargetApply.Contains(staleRouteSessionId, StringComparison.Ordinal), "conflicting visible identities do not fall back to the target URL");

    var invalidConversationNodes = new[]
    {
        new Dictionary<string, string> { ["data-above-composer-conversation-id"] = "not-a-guid" },
    };
    var invalidTargetEvaluator = new HostFakeEvaluator();
    invalidTargetEvaluator.ValueResultFactory = (_, expression) =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal)
            ? ExecuteTargetIdentityProbe(expression, invalidConversationNodes)
            : null;
    var invalidTargetHost = new HelperHost(
        visibleTargetBundledRoot,
        visibleTargetUserRoot,
        Path.Combine(helperRoot, "invalid-target-settings", "settings.json"),
        new FakeTargetSource([staleRouteTarget]),
        invalidTargetEvaluator,
        new RecordingHostActionDispatcher(),
        [visibleSessionRoot]);
    Equal(0, (await invalidTargetHost.ReconcileAsync()).Failed, "invalid visible identity remains a successful unavailable-state reconciliation");
    var invalidTargetApply = invalidTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("helperId=\"visible-target-session\"", StringComparison.Ordinal));
    Equal(true, invalidTargetApply.Contains("\"threadId\":null", StringComparison.Ordinal), "non-GUID visible identity supplies unavailable target-scoped state");
    Equal(false, invalidTargetApply.Contains(staleRouteSessionId, StringComparison.Ordinal), "non-GUID visible identity does not fall back to the target URL");

    var failedTargetEvaluator = new HostFakeEvaluator();
    failedTargetEvaluator.ValueResultFactory = (_, expression) =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal)
            ? throw new InvalidOperationException("visible identity fixture failure")
            : null;
    var failedTargetHost = new HelperHost(
        visibleTargetBundledRoot,
        visibleTargetUserRoot,
        Path.Combine(helperRoot, "failed-target-settings", "settings.json"),
        new FakeTargetSource([staleRouteTarget]),
        failedTargetEvaluator,
        new RecordingHostActionDispatcher(),
        [visibleSessionRoot]);
    Equal(0, (await failedTargetHost.ReconcileAsync()).Failed, "failed visible identity probe remains a successful unavailable-state reconciliation");
    var failedTargetApply = failedTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("helperId=\"visible-target-session\"", StringComparison.Ordinal));
    Equal(true, failedTargetApply.Contains("\"threadId\":null", StringComparison.Ordinal), "failed visible identity probe supplies unavailable target-scoped state");
    Equal(false, failedTargetApply.Contains(staleRouteSessionId, StringComparison.Ordinal), "failed visible identity probe does not fall back to the target URL");

    var targetIdentityProbe = visibleTargetEvaluator.Expressions.Single(expression =>
        expression.Contains("data-above-composer-conversation-id", StringComparison.Ordinal));
    Equal(true, targetIdentityProbe.Contains("data-request-user-input-auto-resolution-conversation-id", StringComparison.Ordinal), "Host identity probe uses the composer and request-input attributes");
    Equal(true, targetIdentityProbe.Contains("replace(/^local:/i, '')", StringComparison.Ordinal), "Host identity probe normalizes the optional local prefix");
    Equal(false, targetIdentityProbe.Contains("data-app-action-sidebar-thread-id", StringComparison.Ordinal), "Host identity probe has no sidebar fallback");

    var targetIdentityBundledRoot = Path.Combine(helperRoot, "target-identity-bundled");
    var targetIdentityUserRoot = Path.Combine(helperRoot, "target-identity-user");
    Directory.CreateDirectory(targetIdentityBundledRoot);
    Directory.CreateDirectory(targetIdentityUserRoot);
    WritePackage(targetIdentityBundledRoot, "target-identity", "target-identity", refreshSeconds: 0);
    var targetIdentityTargets = new[]
    {
        new CodexTarget("target-identity-a", "page", "app://-/index.html", "ws://target-identity-a"),
        new CodexTarget("target-identity-b", "page", "app://-/index.html?initialRoute=%2Flocal%2F1", "ws://target-identity-b"),
    };
    var targetIdentityEvaluator = new HostFakeEvaluator();
    var targetIdentityHost = new HelperHost(
        targetIdentityBundledRoot,
        targetIdentityUserRoot,
        Path.Combine(helperRoot, "target-identity-settings", "settings.json"),
        new FakeTargetSource(targetIdentityTargets),
        targetIdentityEvaluator,
        new RecordingHostActionDispatcher(),
        []);
    var targetIdentityReport = await targetIdentityHost.ReconcileAsync();
    Equal(0, targetIdentityReport.Failed, "target identity fixture reconciliation succeeds");
    foreach (var target in targetIdentityTargets)
    {
        Equal(true,
            targetIdentityEvaluator.Calls.Any(call => call.Target.Id == target.Id && call.Expression.Contains($"target:Object.freeze({{\"id\":\"{target.Id}\"}})", StringComparison.Ordinal)),
            $"host applies each expression with its exact CDP target ID ({target.Id})");
    }
}
finally
{
    if (Directory.Exists(helperRoot)) Directory.Delete(helperRoot, recursive: true);
}

Console.WriteLine("CodexWingman core tests passed");
}
catch (Exception error)
{
    Console.Error.WriteLine($"CodexWingman core tests failed cleanly: {error}");
    Environment.ExitCode = 1;
}

static string WritePackage(
    string root,
    string folder,
    string id,
    int schemaVersion = 1,
    string version = "1.0.0",
    IReadOnlyList<string>? capabilities = null,
    string? backend = null,
    int refreshSeconds = 60,
    string apply = "apply.js",
    string remove = "remove.js",
    string? backendSource = null,
    string? name = null,
    object? config = null)
{
    var packageRoot = Path.Combine(root, folder);
    Directory.CreateDirectory(packageRoot);
    File.WriteAllText(Path.Combine(packageRoot, "wingman.json"), JsonSerializer.Serialize(new
    {
        schemaVersion,
        id,
        name = name ?? id,
        version,
        description = $"Fixture {id}",
        refreshSeconds,
        capabilities = capabilities ?? [],
        entrypoints = new { backend, apply, remove },
        config,
    }));
    if (!apply.Contains("..", StringComparison.Ordinal))
        File.WriteAllText(Path.Combine(packageRoot, apply), "window.__fixtureApply=(window.__fixtureApply??0)+1;");
    if (!remove.Contains("..", StringComparison.Ordinal))
        File.WriteAllText(Path.Combine(packageRoot, remove), "delete window.__fixtureApply;");
    if (backend is not null)
        File.WriteAllText(Path.Combine(packageRoot, backend), backendSource ?? "function refresh(wingman) { return {}; }");
    File.WriteAllText(Path.Combine(packageRoot, "HELPER_INFO.md"), $"# {name ?? id}\n\nVersion: v{version.Split('.')[0]}\n");
    return packageRoot;
}

static JsonElement Json(string value)
{
    using var document = JsonDocument.Parse(value);
    return document.RootElement.Clone();
}

static JsonElement ExecuteTargetIdentityProbe(
    string expression,
    IEnumerable<Dictionary<string, string>> conversationNodes)
{
    var nodes = JsonSerializer.Serialize(conversationNodes);
    var engine = new Engine();
    engine.SetValue("__conversationNodesJson", nodes);
    engine.Execute("""
        const __conversationNodes = JSON.parse(__conversationNodesJson);
        globalThis.document = {
          querySelectorAll(selector) {
            const names = selector
              .split(',')
              .map((part) => part.trim().replace(/^\[|\]$/g, ''));
            return __conversationNodes
              .filter((attributes) => names.some((name) => Object.prototype.hasOwnProperty.call(attributes, name)))
              .map((attributes) => ({
                getAttribute(name) {
                  return Object.prototype.hasOwnProperty.call(attributes, name)
                    ? attributes[name]
                    : null;
                },
              }));
          },
        };
        """);

    var value = engine.Evaluate(expression);
    return value.IsNull()
        ? Json("null")
        : Json(JsonSerializer.Serialize(value.AsString()));
}

static string FindProjectRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "helpers", "usage-dials", "wingman.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Could not locate staging project root containing helpers/usage-dials/wingman.json");
}

static async Task ThrowsAsync<TException>(Func<Task> action, string name) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
}

static void Throws<TException>(Action action, string name) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
}

sealed class FakeTargetSource(IReadOnlyList<CodexTarget> targets) : ICodexTargetSource
{
    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(targets);
}

sealed class SequencedTargetSource(IReadOnlyList<IReadOnlyList<CodexTarget>> snapshots) : ICodexTargetSource
{
    private int index;

    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshots[Math.Min(index, snapshots.Count - 1)]);

    public void Advance() => index = Math.Min(index + 1, snapshots.Count - 1);
}

sealed class FlakyTargetSource(IReadOnlyList<CodexTarget> targets) : ICodexTargetSource
{
    public int FailuresRemaining { get; set; }

    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("fixture target-list failure");
        }
        return Task.FromResult(targets);
    }
}

sealed class FakeEvaluator(string? failTargetId) : ICdpEvaluator
{
    public string? FailTargetId { get; set; } = failTargetId;
    public List<(CodexTarget Target, string Expression)> Calls { get; } = [];

    public Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        Calls.Add((target, expression));
        return target.Id == FailTargetId
            ? Task.FromException(new InvalidOperationException("fixture failure"))
            : Task.CompletedTask;
    }
}

sealed class FakeQuotaSource(QuotaSnapshot snapshot) : IQuotaSource
{
    public int ReadCount { get; private set; }
    public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return Task.FromResult(snapshot);
    }
}

sealed class FakeInjectionController : ICodexInjectionController
{
    public int InjectCount { get; private set; }
    public int RemoveCount { get; private set; }
    public Task<InjectionStatus> InjectAllAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        InjectCount++;
        return Task.FromResult(new InjectionStatus(1, 1, 0));
    }
    public Task<InjectionStatus> RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        RemoveCount++;
        return Task.FromResult(new InjectionStatus(1, 1, 0));
    }
}

sealed class HostFakeEvaluator : ICdpEvaluator
{
    public string? FailExpressionsContaining { get; set; }
    public int FailuresRemaining { get; set; } = int.MaxValue;
    public Queue<JsonElement> DrainResults { get; } = new();
    public List<string> Expressions { get; } = [];
    public List<(CodexTarget Target, string Expression)> Calls { get; } = [];
    public Func<CodexTarget, string, JsonElement?>? ValueResultFactory { get; set; }

    public Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        Expressions.Add(expression);
        Calls.Add((target, expression));
        if (FailExpressionsContaining is { } value
            && expression.Contains(value, StringComparison.Ordinal)
            && FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("fixture renderer failure");
        }
        return Task.CompletedTask;
    }

    public Task<JsonElement?> EvaluateValueAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        Expressions.Add(expression);
        Calls.Add((target, expression));
        JsonElement? result = ValueResultFactory?.Invoke(target, expression);
        result ??= DrainResults.Count > 0 ? DrainResults.Dequeue() : EmptyArray();
        return Task.FromResult(result);
    }

    private static JsonElement EmptyArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }
}

sealed class AdvancingEvaluator(HostFakeEvaluator inner, Action advance) : ICdpEvaluator
{
    public async Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        await inner.EvaluateAsync(target, expression, cancellationToken);
        advance();
    }

    public Task<JsonElement?> EvaluateValueAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default) =>
        inner.EvaluateValueAsync(target, expression, cancellationToken);
}

sealed class RecordingHostActionDispatcher : IHostActionDispatcher
{
    public List<HostActionRequest> Requests { get; } = [];

    public Task DispatchAsync(HostActionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}

sealed class OpeningHostActionDispatcher(Action onDispatch) : IHostActionDispatcher
{
    public List<HostActionRequest> Requests { get; } = [];

    public Task DispatchAsync(HostActionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        onDispatch();
        return Task.CompletedTask;
    }
}

sealed class BlockingOpeningHostActionDispatcher(
    Action onDispatch,
    ManualResetEventSlim firstEntered,
    ManualResetEventSlim releaseFirst) : IHostActionDispatcher
{
    public List<HostActionRequest> Requests { get; } = [];

    public async Task DispatchAsync(HostActionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Requests.Count == 1)
        {
            firstEntered.Set();
            await Task.Run(() => releaseFirst.Wait(cancellationToken), cancellationToken);
        }
        onDispatch();
    }
}

sealed class NeverReplyCdpWebSocket : ICdpWebSocket
{
    public bool AbortCalled { get; private set; }
    public WebSocketState State { get; private set; } = WebSocketState.None;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable fixture receive");
    }

    public void Abort()
    {
        AbortCalled = true;
        State = WebSocketState.Aborted;
    }

    public void Dispose() => State = WebSocketState.Closed;
}

sealed class StaticHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responseFactory(request));
}

sealed class ReplyCdpWebSocket(string reply) : ICdpWebSocket
{
    private readonly byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
    private bool replied;
    public Uri? ConnectedUri { get; private set; }
    public string SentText { get; private set; } = string.Empty;
    public WebSocketState State { get; private set; } = WebSocketState.None;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedUri = uri;
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        SentText = Encoding.UTF8.GetString(payload.Span);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (replied) throw new InvalidOperationException("Fixture reply was already consumed");
        replied = true;
        replyBytes.CopyTo(buffer);
        return ValueTask.FromResult(new ValueWebSocketReceiveResult(replyBytes.Length, WebSocketMessageType.Text, true));
    }

    public void Abort() => State = WebSocketState.Aborted;
    public void Dispose() => State = WebSocketState.Closed;
}

sealed class BlockingSessionFiles(
    string root,
    ManualResetEventSlim entered,
    ManualResetEventSlim release) : IHelperSessionFiles
{
    private int readCount;
    public int ReadCount => Volatile.Read(ref readCount);
    public string[] Roots() => [root];
    public string[] ListFiles(string requestedRoot, CancellationToken cancellationToken) => [Path.Combine(root, "blocked.jsonl")];

    public string ReadText(string path, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref readCount);
        entered.Set();
        release.Wait();
        return "fixture";
    }
}

sealed class ForbiddenUsageSessionFiles : IHelperSessionFiles
{
    public int AccessCount { get; private set; }
    public string[] Roots() { AccessCount++; throw new IOException("Session storage must not be queried"); }
    public string[] ListFiles(string root, CancellationToken token) { AccessCount++; throw new IOException("Session storage must not be queried"); }
    public string ReadText(string path, CancellationToken token) { AccessCount++; throw new IOException("Session storage must not be queried"); }
}
