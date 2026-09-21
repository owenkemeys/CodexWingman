using System.Text.Json;

namespace CodexWingman.Core.Helpers;

public sealed class HelperHost
{
    private static readonly TimeSpan DefaultNativeWindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultNativeWindowPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly string helpersRoot;
    private readonly string? overrideRoot;
    private readonly string settingsPath;
    private readonly ICodexTargetSource targetSource;
    private readonly ICdpEvaluator evaluator;
    private readonly IHostActionDispatcher actionDispatcher;
    private readonly Func<DateTimeOffset> currentTime;
    private readonly Action<string>? backendLog;
    private readonly Func<IEnumerable<string>, HelperBackend> backendFactory;
    private readonly TimeSpan nativeWindowTimeout;
    private readonly TimeSpan nativeWindowPollInterval;
    private readonly Func<string> childOperationTokenFactory;
    private readonly HelperCatalog catalog = new();
    private HelperBackend backend;
    private readonly HelperRenderer renderer = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, CachedBackendState> backendStates = new(StringComparer.Ordinal);
    private readonly HashSet<HelperPackage> pendingCleanup = [];
    private HelperCatalogReport catalogReport = new([], []);
    private HelperSettings settings;
    private IReadOnlyList<HelperSummary> summaries = [];
    private bool suspended;

    public HelperHost(
        string bundledRoot,
        string userRoot,
        string settingsPath,
        ICodexTargetSource targetSource,
        ICdpEvaluator evaluator,
        IHostActionDispatcher actionDispatcher,
        IEnumerable<string> sessionRoots,
        Func<DateTimeOffset>? currentTime = null,
        Action<string>? log = null,
        Func<IEnumerable<string>, HelperBackend>? backendFactory = null,
        TimeSpan? nativeWindowTimeout = null,
        TimeSpan? nativeWindowPollInterval = null,
        Func<string>? childOperationTokenFactory = null)
    {
        helpersRoot = bundledRoot;
        overrideRoot = userRoot;
        this.settingsPath = settingsPath;
        this.targetSource = targetSource;
        this.evaluator = evaluator;
        this.actionDispatcher = actionDispatcher;
        this.currentTime = currentTime ?? (() => DateTimeOffset.UtcNow);
        backendLog = log;
        this.backendFactory = backendFactory ?? (roots => new(roots, this.currentTime, backendLog));
        this.nativeWindowTimeout = nativeWindowTimeout ?? DefaultNativeWindowTimeout;
        this.nativeWindowPollInterval = nativeWindowPollInterval ?? DefaultNativeWindowPollInterval;
        this.childOperationTokenFactory = childOperationTokenFactory ?? (() => Guid.NewGuid().ToString("N"));
        backend = this.backendFactory(sessionRoots);
        settings = HelperSettingsStore.Load(settingsPath);
        ReloadCore();
    }

    public HelperHost(
        string helpersRoot,
        string settingsPath,
        ICodexTargetSource targetSource,
        ICdpEvaluator evaluator,
        IHostActionDispatcher actionDispatcher,
        IEnumerable<string> sessionRoots,
        Func<DateTimeOffset>? currentTime = null,
        Action<string>? log = null,
        Func<IEnumerable<string>, HelperBackend>? backendFactory = null,
        TimeSpan? nativeWindowTimeout = null,
        TimeSpan? nativeWindowPollInterval = null,
        Func<string>? childOperationTokenFactory = null)
    {
        this.helpersRoot = helpersRoot;
        overrideRoot = null;
        this.settingsPath = settingsPath;
        this.targetSource = targetSource;
        this.evaluator = evaluator;
        this.actionDispatcher = actionDispatcher;
        this.currentTime = currentTime ?? (() => DateTimeOffset.UtcNow);
        backendLog = log;
        this.backendFactory = backendFactory ?? (roots => new(roots, this.currentTime, backendLog));
        this.nativeWindowTimeout = nativeWindowTimeout ?? DefaultNativeWindowTimeout;
        this.nativeWindowPollInterval = nativeWindowPollInterval ?? DefaultNativeWindowPollInterval;
        this.childOperationTokenFactory = childOperationTokenFactory ?? (() => Guid.NewGuid().ToString("N"));
        backend = this.backendFactory(sessionRoots);
        settings = HelperSettingsStore.Load(settingsPath);
        ReloadCore();
    }

    public IReadOnlyList<HelperSummary> Helpers => summaries;
    public bool IsSuspended => suspended;

    public async Task<HelperHostReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var report = await ReconcileCoreAsync(cancellationToken);
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> DrainHostActionsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var targets = CodexTargetCatalog.SelectPages(await targetSource.ListAsync(cancellationToken));
            return suspended
                ? new(0, targets.Count, 0, 0, [])
                : await DrainHostActionsCoreAsync(targets, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> SetEnabledAsync(
        string helperId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var package = catalogReport.Packages.FirstOrDefault(candidate => candidate.Manifest.Id.Equals(helperId, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Unknown Helper '{helperId}'");
            var nextSettings = settings.WithHelperEnabled(helperId, enabled);
            HelperSettingsStore.Save(settingsPath, nextSettings);
            settings = nextSettings;
            if (enabled) suspended = false;

            HelperHostReport report;
            if (enabled)
            {
                report = await ReconcileCoreAsync(cancellationToken);
            }
            else
            {
                pendingCleanup.Add(package);
                var targets = await targetSource.ListAsync(cancellationToken);
                report = await RetryPendingCleanupAsync(targets, cancellationToken);
            }
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            pendingCleanup.UnionWith(catalogReport.Packages);
            var targets = await targetSource.ListAsync(cancellationToken);
            var report = await RetryPendingCleanupAsync(targets, cancellationToken);
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> SuspendAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            suspended = true;
            pendingCleanup.UnionWith(catalogReport.Packages);
            var targets = await targetSource.ListAsync(cancellationToken);
            var report = await RetryPendingCleanupAsync(targets, cancellationToken);
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> ResumeAndReconcileAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            suspended = false;
            var report = await ReconcileCoreAsync(cancellationToken);
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HelperHostReport> OpenNativeNewWindowAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            suspended = false;
            var before = CodexTargetCatalog.SelectPages(await targetSource.ListAsync(cancellationToken));
            var source = before.FirstOrDefault()
                ?? throw new InvalidOperationException("No Codex page is available to open a new hookable window.");

            await actionDispatcher.DispatchAsync(
                new HostActionRequest(HostActionDispatcher.SystemHelperId, HostActionDispatcher.OpenNewChatWindow, null, source),
                cancellationToken);
            await WaitForNativeNewWindowAsync(before, cancellationToken);

            var report = await ReconcileCoreAsync(cancellationToken);
            UpdateSummaries(report.Diagnostics);
            return report;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pendingCleanup.UnionWith(catalogReport.Packages);
            try
            {
                var targets = await targetSource.ListAsync(cancellationToken);
                _ = await RetryPendingCleanupAsync(targets, cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Reload must still discover package changes when Codex/CDP is unavailable.
            }
            settings = HelperSettingsStore.Load(settingsPath);
            backend = backendFactory(settings.ComposeSessionRoots());
            ReloadCore();
        }
        finally
        {
            gate.Release();
        }
    }

    private void ReloadCore()
    {
        backendStates.Clear();
        catalogReport = overrideRoot is null
            ? catalog.Discover(helpersRoot)
            : catalog.Discover(helpersRoot, overrideRoot);
        catalogReport = catalogReport with
        {
            Packages = catalogReport.Packages.Select(package => package with
            {
                Manifest = package.Manifest with { Config = settings.ConfigFor(package.Manifest.Id, package.Manifest.Config) },
            }).ToArray(),
        };
        UpdateSummaries(catalogReport.Diagnostics);
    }

    private async Task<IReadOnlyList<CodexTarget>> WaitForNativeNewWindowAsync(
        IReadOnlyList<CodexTarget> before,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + nativeWindowTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = CodexTargetCatalog.SelectPages(await targetSource.ListAsync(cancellationToken));
            if (!CodexTargetCatalog.PreservesPages(before, current))
                throw new InvalidOperationException("Codex opened a new window but an existing page disappeared before reconciliation.");

            var newPages = CodexTargetCatalog.FindNewPages(before, current);
            if (newPages.Count > 1)
                throw new InvalidOperationException($"Codex opened {newPages.Count} new pages; Wingman expected exactly one.");
            if (newPages.Count == 1) return current;
            await Task.Delay(nativeWindowPollInterval, cancellationToken);
        }

        throw new TimeoutException($"Codex did not expose exactly one new page target within {nativeWindowTimeout.TotalSeconds:g} seconds.");
    }

    private async Task<HelperHostReport> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<HelperDiagnostic>(catalogReport.Diagnostics);
        if (suspended && pendingCleanup.Count == 0)
            return new(0, 0, 0, 0, diagnostics);
        var targets = await targetSource.ListAsync(cancellationToken);
        var attempted = 0;
        var succeeded = 0;
        var failed = 0;

        var pendingAtStart = pendingCleanup.ToHashSet();
        if (pendingAtStart.Count > 0)
        {
            var cleanup = await RetryPendingCleanupAsync(targets, cancellationToken);
            attempted += cleanup.HelpersAttempted;
            succeeded += cleanup.Succeeded;
            failed += cleanup.Failed;
            diagnostics.AddRange(cleanup.Diagnostics);
        }

        if (suspended)
            return new(attempted, targets.Count, succeeded, failed, diagnostics);

        var disabledPackages = catalogReport.Packages
            .Where(package => !IsEnabled(package) && !pendingAtStart.Contains(package))
            .ToArray();
        if (disabledPackages.Length > 0)
        {
            var cleanup = await RemovePackagesAsync(disabledPackages, targets, cancellationToken);
            attempted += cleanup.Report.HelpersAttempted;
            succeeded += cleanup.Report.Succeeded;
            failed += cleanup.Report.Failed;
            diagnostics.AddRange(cleanup.Report.Diagnostics);
        }

        foreach (var package in catalogReport.Packages)
        {
            if (!IsEnabled(package)) continue;
            if (IsTargetScoped(package))
            {
                foreach (var target in targets)
                {
                    attempted++;
                    try
                    {
                        var threadId = await GetTargetThreadIdAsync(target, cancellationToken);
                        using var state = await GetBackendStateAsync(package, target, threadId, cancellationToken);
                        var targetExpression = renderer.BuildApply(package, state.RootElement, target.Id);
                        await evaluator.EvaluateAsync(target, targetExpression, cancellationToken);
                        succeeded++;
                    }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                        diagnostics.Add(new(package.Manifest.Id, $"{package.Manifest.Id}: target {target.Id} failed: {Concise(error)}"));
                    }
                }
                continue;
            }
            attempted++;
            JsonDocument backendState;
            try
            {
                backendState = await GetBackendStateAsync(package, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                diagnostics.Add(new(package.Manifest.Id, $"{package.Manifest.Id}: backend failed: {Concise(error)}"));
                continue;
            }

            using (backendState)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        var expression = renderer.BuildApply(package, backendState.RootElement, target.Id);
                        await evaluator.EvaluateAsync(target, expression, cancellationToken);
                        succeeded++;
                    }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                        diagnostics.Add(new(package.Manifest.Id, $"{package.Manifest.Id}: renderer failed in {target.Id}: {Concise(error)}"));
                    }
                }
            }
        }

        var actionReport = await DrainHostActionsCoreAsync(targets, cancellationToken);
        succeeded += actionReport.Succeeded;
        failed += actionReport.Failed;
        diagnostics.AddRange(actionReport.Diagnostics);

        return new(attempted, targets.Count, succeeded, failed, diagnostics);
    }

    private async Task<HelperHostReport> DrainHostActionsCoreAsync(
        IReadOnlyList<CodexTarget> targets,
        CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var failed = 0;
        var diagnostics = new List<HelperDiagnostic>();
        foreach (var target in targets)
        {
            try
            {
                var drained = await evaluator.EvaluateValueAsync(target, renderer.BuildDrainActions(), cancellationToken);
                if (drained is not { ValueKind: JsonValueKind.Array }) continue;
                foreach (var item in drained.Value.EnumerateArray())
                {
                    if (await DispatchActionAsync(item, target, diagnostics, cancellationToken)) succeeded++;
                    else failed++;
                }
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                diagnostics.Add(new(null, $"Host Action drain failed in {target.Id}: {Concise(error)}"));
            }
        }
        return new(0, targets.Count, succeeded, failed, diagnostics);
    }

    private async Task<JsonDocument> GetBackendStateAsync(
        HelperPackage package,
        CancellationToken cancellationToken)
    {
        if (package.BackendSource is null)
            return JsonDocument.Parse("{}");

        var now = currentTime();
        if (package.Manifest.RefreshSeconds > 0
            && backendStates.TryGetValue(package.Manifest.Id, out var cached)
            && now < cached.RefreshAfter)
            return JsonDocument.Parse(cached.Json);

        var refreshed = await backend.RefreshAsync(package, cancellationToken);
        if (package.Manifest.RefreshSeconds > 0)
        {
            backendStates[package.Manifest.Id] = new(
                refreshed.RootElement.GetRawText(),
                now.AddSeconds(package.Manifest.RefreshSeconds));
        }
        return refreshed;
    }

    private async Task<JsonDocument> GetBackendStateAsync(
        HelperPackage package,
        CodexTarget target,
        string? threadId,
        CancellationToken cancellationToken)
    {
        if (package.BackendSource is null)
            return JsonDocument.Parse("{}");

        var cacheKey = $"{package.Manifest.Id}\0{target.Id}\0{threadId ?? "(none)"}";
        var now = currentTime();
        if (package.Manifest.RefreshSeconds > 0
            && backendStates.TryGetValue(cacheKey, out var cached)
            && now < cached.RefreshAfter)
            return JsonDocument.Parse(cached.Json);

        var refreshed = await backend.RefreshAsync(package, cancellationToken, threadId);
        if (package.Manifest.RefreshSeconds > 0)
        {
            backendStates[cacheKey] = new(
                refreshed.RootElement.GetRawText(),
                now.AddSeconds(package.Manifest.RefreshSeconds));
        }
        return refreshed;
    }

    private async Task<string?> GetTargetThreadIdAsync(
        CodexTarget target,
        CancellationToken cancellationToken)
    {
        const string expression = """
            (() => {
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
              return ids.size === 0 ? '__codex-wingman-missing-rendered-thread__'
                : ids.size === 1 ? [...ids][0] : null;
            })()
            """;
        try
        {
            var value = await evaluator.EvaluateValueAsync(target, expression, cancellationToken);
            if (value is { ValueKind: JsonValueKind.String })
            {
                var candidate = value.Value.GetString();
                if (Guid.TryParse(candidate, out var activeId)) return activeId.ToString();
                if (string.Equals(candidate, "__codex-wingman-missing-rendered-thread__", StringComparison.Ordinal))
                    return CodexTargetCatalog.TryGetThreadId(target.Url);
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            backendLog?.Invoke($"Could not read visible Codex conversation for target {target.Id}: {error.Message}");
        }
        return null;
    }

    private async Task<bool> DispatchActionAsync(
        JsonElement item,
        CodexTarget target,
        List<HelperDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var helperId = item.TryGetProperty("helperId", out var helperIdValue) ? helperIdValue.GetString() : null;
        var action = item.TryGetProperty("action", out var actionValue) ? actionValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(helperId) || string.IsNullOrWhiteSpace(action))
        {
            diagnostics.Add(new(helperId, "Malformed Host Action request"));
            return false;
        }

        var package = catalogReport.Packages.FirstOrDefault(candidate => candidate.Manifest.Id.Equals(helperId, StringComparison.Ordinal));
        if (package is null || !IsEnabled(package))
        {
            diagnostics.Add(new(helperId, $"Host Action rejected for unknown or disabled Helper '{helperId}'"));
            return false;
        }

        string capability;
        try
        {
            capability = HostActionDispatcher.RequiredCapability(action);
        }
        catch (InvalidOperationException error)
        {
            diagnostics.Add(new(helperId, $"{helperId}: {error.Message}"));
            return false;
        }
        if (!package.Manifest.Capabilities.Any(
            declared => string.Equals(declared, capability, StringComparison.Ordinal)))
        {
            diagnostics.Add(new(helperId, $"{helperId}: undeclared capability '{capability}' for Host Action '{action}'"));
            return false;
        }

        JsonElement? payload = item.TryGetProperty("payload", out var payloadValue) ? payloadValue.Clone() : null;
        try
        {
            if (string.Equals(action, HostActionDispatcher.OpenChild, StringComparison.Ordinal))
            {
                await OpenChildAsync(package, target, payload, cancellationToken);
            }
            else
            {
                await actionDispatcher.DispatchAsync(new(helperId, action, payload, target), cancellationToken);
            }
            return true;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new(helperId, $"{helperId}: Host Action '{action}' failed: {Concise(error)}"));
            return false;
        }
    }

    private async Task OpenChildAsync(
        HelperPackage owner,
        CodexTarget source,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        var request = ExactChildBootstrapRequest.Parse(
            owner.Manifest.Id,
            payload ?? throw new InvalidOperationException("wingman.openChild requires a payload"));
        var before = CodexTargetCatalog.SelectPages(await targetSource.ListAsync(cancellationToken));
        if (!before.Any(candidate => candidate.Id == source.Id))
            throw new InvalidOperationException("wingman.openChild source target is no longer available");

        if (request.Path == "/")
        {
            await actionDispatcher.DispatchAsync(
                new(HostActionDispatcher.SystemHelperId, HostActionDispatcher.OpenNewChatWindow, null, source),
                cancellationToken);
        }
        else
        {
            using var bridgePayload = JsonDocument.Parse(JsonSerializer.Serialize(new { path = request.Path }));
            await actionDispatcher.DispatchAsync(
                new(owner.Manifest.Id, HostActionDispatcher.OpenNewChatWindow, bridgePayload.RootElement.Clone(), source),
                cancellationToken);
        }

        var current = await WaitForNativeNewWindowAsync(before, cancellationToken);
        var child = CodexTargetCatalog.FindNewPages(before, current).Single();
        var operationToken = childOperationTokenFactory();
        try
        {
            using var state = await GetBackendStateAsync(owner, cancellationToken);
            await evaluator.EvaluateAsync(
                child,
                renderer.BuildApply(owner, state.RootElement, child.Id, request.Bootstrap, operationToken),
                cancellationToken);
            await evaluator.EvaluateAsync(child, "window.focus()", cancellationToken);
            if (request.RequiresCompletionAcknowledgement)
            {
                await WaitForChildCompletionAsync(child, owner.Manifest.Id, operationToken, cancellationToken);
                await evaluator.EvaluateAsync(child, "window.focus()", cancellationToken);
            }
        }
        catch
        {
            try { await evaluator.EvaluateAsync(child, "window.close()", cancellationToken); }
            catch when (!cancellationToken.IsCancellationRequested) { }
            throw;
        }
    }

    private async Task WaitForChildCompletionAsync(
        CodexTarget child,
        string helperId,
        string operationToken,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + nativeWindowTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var drained = await evaluator.EvaluateValueAsync(child, renderer.BuildDrainActions(), cancellationToken);
            if (drained is { ValueKind: JsonValueKind.Array }
                && drained.Value.EnumerateArray().Any(item => IsMatchingChildCompletion(item, helperId, operationToken)))
                return;
            await Task.Delay(nativeWindowPollInterval, cancellationToken);
        }
        throw new TimeoutException("Exact child did not acknowledge successful Helper setup.");
    }

    private static bool IsMatchingChildCompletion(JsonElement item, string helperId, string operationToken) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("helperId", out var itemHelper)
        && itemHelper.ValueKind == JsonValueKind.String
        && itemHelper.GetString() == helperId
        && item.TryGetProperty("action", out var action)
        && action.ValueKind == JsonValueKind.String
        && action.GetString() == "wingman.completeChild"
        && item.TryGetProperty("payload", out var payload)
        && payload.ValueKind == JsonValueKind.Object
        && payload.EnumerateObject().Count() == 1
        && payload.TryGetProperty("token", out var token)
        && token.ValueKind == JsonValueKind.String
        && token.GetString() == operationToken;

    private async Task<HelperHostReport> RetryPendingCleanupAsync(
        IReadOnlyList<CodexTarget> targets,
        CancellationToken cancellationToken)
    {
        var attempt = await RemovePackagesAsync(pendingCleanup.ToArray(), targets, cancellationToken);
        pendingCleanup.ExceptWith(attempt.CompletedPackages);
        return attempt.Report;
    }

    private async Task<CleanupAttempt> RemovePackagesAsync(
        IReadOnlyList<HelperPackage> packages,
        IReadOnlyList<CodexTarget> targets,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<HelperDiagnostic>();
        var completedPackages = new List<HelperPackage>();
        var succeeded = 0;
        var failed = 0;
        foreach (var package in packages)
        {
            var packageFailed = false;
            string expression;
            try
            {
                expression = renderer.BuildRemove(package);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                packageFailed = true;
                diagnostics.Add(new(package.Manifest.Id, $"{package.Manifest.Id}: cleanup script failed to load: {Concise(error)}"));
                continue;
            }

            foreach (var target in targets)
            {
                try
                {
                    await evaluator.EvaluateAsync(target, expression, cancellationToken);
                    succeeded++;
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    failed++;
                    packageFailed = true;
                    diagnostics.Add(new(package.Manifest.Id, $"{package.Manifest.Id}: cleanup failed in {target.Id}: {Concise(error)}"));
                }
            }
            if (!packageFailed) completedPackages.Add(package);
        }
        return new(
            new(packages.Count, targets.Count, succeeded, failed, diagnostics),
            completedPackages);
    }

    private void UpdateSummaries(IReadOnlyList<HelperDiagnostic> diagnostics)
    {
        var packageSummaries = catalogReport.Packages
            .Select(package => new HelperSummary(
                package.Manifest.Id,
                package.Source == HelperPackageSource.User ? Path.GetFileName(package.DirectoryPath) : package.Manifest.Name,
                package.Manifest.Version,
                IsEnabled(package),
                diagnostics.FirstOrDefault(diagnostic => diagnostic.HelperId == package.Manifest.Id)?.Message,
                package.Manifest.RefreshSeconds,
                CanToggle: true,
                package.Manifest.Capabilities,
                package.Source))
            .ToList();

        var selectedIds = catalogReport.Packages.Select(package => package.Manifest.Id).ToHashSet(StringComparer.Ordinal);
        var identifiedFailures = catalogReport.Diagnostics
            .Where(diagnostic => diagnostic.HelperId is { } id && !selectedIds.Contains(id))
            .GroupBy(diagnostic => diagnostic.HelperId!, StringComparer.Ordinal);
        foreach (var failure in identifiedFailures)
        {
            packageSummaries.Add(new(
                failure.Key,
                failure.Key,
                string.Empty,
                Enabled: false,
                string.Join(" | ", failure.Select(diagnostic => diagnostic.Message)),
                RefreshSeconds: 0,
                CanToggle: false,
                Capabilities: [],
                Source: null));
        }

        var unidentifiedIndex = 0;
        foreach (var failure in catalogReport.Diagnostics.Where(diagnostic => diagnostic.HelperId is null))
        {
            unidentifiedIndex++;
            packageSummaries.Add(new(
                $"diagnostic-{unidentifiedIndex}",
                "Rejected Helper",
                string.Empty,
                Enabled: false,
                failure.Message,
                RefreshSeconds: 0,
                CanToggle: false,
                Capabilities: [],
                Source: null));
        }
        summaries = packageSummaries;
    }

    private static string Concise(Exception error)
    {
        var message = error.Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 160 ? message : $"{message[..157]}...";
    }

    private bool IsEnabled(HelperPackage package) => settings.IsHelperEnabled(
        package.Manifest.Id,
        defaultEnabled: overrideRoot is null || package.Source == HelperPackageSource.Bundled || package.OverridesBundled);

    private static bool IsTargetScoped(HelperPackage package) => package.Manifest.Capabilities
        .Any(capability => string.Equals(capability, "files.codexTargetSession", StringComparison.Ordinal));

    private sealed record CachedBackendState(string Json, DateTimeOffset RefreshAfter);

    private sealed record CleanupAttempt(
        HelperHostReport Report,
        IReadOnlyList<HelperPackage> CompletedPackages);
}
