using System.Diagnostics;
using CodexWingman.Core;
using CodexWingman.Core.Helpers;
using CodexApp.ReleaseKit;

namespace CodexWingman;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TrayHoverText = "Wingman";
    private readonly TrayIconSet trayIcons;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem codexMenuItem;
    private readonly ToolStripMenuItem t3CodeMenuItem;
    private readonly AppFlyoutMenu codexFlyout;
    private readonly AppFlyoutMenu t3Flyout;
    private readonly ToolStripMenuItem helpersItem;
    private readonly ToolStripMenuItem injectItem;
    private readonly ToolStripMenuItem openHelpersFolderItem;
    private readonly ToolStripMenuItem reloadHelpersItem;
    private readonly ToolStripMenuItem aboutItem;
    private readonly ToolStripMenuItem checkUpdatesItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly System.Windows.Forms.Timer actionTimer;
    private readonly System.Windows.Forms.Timer trayRecoveryTimer;
    private readonly HelperHost helperHost;
    private readonly HelperHost t3HelperHost;
    private readonly T3CodeConnectionProbe t3ConnectionProbe;
    private readonly T3WebLauncher t3WebLauncher;
    private readonly GrokBotLauncher grokLauncher;
    private readonly AppTargetController t3Web;
    private readonly AppTargetController grok;
    private readonly ContextMenuStrip menu;
    private readonly ICdpEvaluator evaluator;
    private readonly CdpConnectionPool cdpPool = CdpConnectionPool.Shared;
    private bool codexHooked;
    private readonly string settingsDirectory;
    private bool pairingInFlight;
    private DateTime nextPairingCheckUtc = DateTime.MinValue;
    private readonly HttpClient targetHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly HttpClient releaseHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string userHelpersRoot;
    private readonly string runtimeStatePath;
    private readonly string t3RuntimeStatePath;
    private readonly string settingsPath;
    private readonly int? configuredPortOverride;
    private readonly List<RegisteredWaitHandle> activationRegistrations = [];
    private readonly Queue<WingmanLaunchTarget> pendingLaunches = new();
    private readonly WingmanLaunchOptions launchOptions;
    private bool drainingLaunches;
    private bool initializing = true;
    private readonly SynchronizationContext uiContext;
    private readonly TrayRefreshGate automaticRefreshGate = new();
    private readonly TrayRefreshGate actionDrainGate = new();
    private readonly Dictionary<string, ToolStripMenuItem> helperMenuItems = new(StringComparer.Ordinal);
    private CancellationTokenSource? activeInteractiveOperation;
    private int activePort = CodexLaunchPolicy.PreferredPort;
    private bool busy;
    private bool exiting;
    private bool reloadPending;
    private bool synchronizingMenu;
    private bool checkingRelease;
    private HelperHostReport? lastReport;
    private HelperHostReport? lastT3Report;
    private T3CodeConnectionReport? lastT3Connections;
    private string? lastOperationError;
    private bool t3CheckFailed;
    private TrayStatus currentStatus = new(
        "Status: Checking",
        "Wingman is checking Codex and its Helpers.");

    public TrayApplicationContext(int? portOverride = null, EventWaitHandle? activationEvent = null,
        WingmanLaunchOptions? launchOptions = null, EventWaitHandle? codexActivation = null, EventWaitHandle? t3Activation = null,
        EventWaitHandle? t3WebActivation = null, EventWaitHandle? grokActivation = null)
    {
        this.launchOptions = launchOptions ?? new WingmanLaunchOptions();
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        configuredPortOverride = portOverride;
        var initializedSettings = HelperSettingsStore.InitializeDefault(this.launchOptions.LocalAppData);
        var settings = initializedSettings.Settings;
        settingsDirectory = Path.GetDirectoryName(initializedSettings.Path)
            ?? throw new InvalidOperationException("Settings directory is unavailable");
        settingsPath = initializedSettings.Path;
        runtimeStatePath = Path.Combine(settingsDirectory, "runtime.json");
        t3RuntimeStatePath = Path.Combine(settingsDirectory, "t3-runtime.json");
        activePort = configuredPortOverride
            ?? CodexRuntimeStateStore.LoadPort(runtimeStatePath)
            ?? CodexLaunchPolicy.PreferredPort;
        // One persistent DevTools connection per endpoint, shared by every app target and the action poll.
        var targetSource = HttpCodexTargetSource.Pooled(targetHttp, () => activePort, CodexTargetCatalog.SelectPages, cdpPool);
        var t3TargetSource = new T3CodeTargetSource(targetHttp, GetT3Port, cdpPool);
        evaluator = new PooledCdpEvaluator(cdpPool);
        t3ConnectionProbe = new T3CodeConnectionProbe(t3TargetSource, evaluator);
        userHelpersRoot = WingmanIdentity.ResolveHelpersRoot(Application.ExecutablePath);
        Directory.CreateDirectory(userHelpersRoot);
        helperHost = new HelperHost(
            userHelpersRoot,
            initializedSettings.Path,
            targetSource,
            evaluator,
            new HostActionDispatcher(evaluator),
            settings.ComposeSessionRoots(),
            log: message => Debug.WriteLine($"[CodexWingman Helper] {message}"));
        t3HelperHost = new HelperHost(
            userHelpersRoot,
            initializedSettings.Path,
            t3TargetSource,
            evaluator,
            new HostActionDispatcher(evaluator),
            [],
            log: message => Debug.WriteLine($"[T3 Code Helper] {message}"),
            appId: HelperAppIds.T3Code);
        t3WebLauncher = new T3WebLauncher(settingsDirectory, () => HelperSettingsStore.Load(settingsPath), ReservedPorts);
        grokLauncher = new GrokBotLauncher(settingsDirectory, ReservedPorts);
        AppTargetController? t3WebReference = null;
        t3Web = t3WebReference = new AppTargetController(TrayMenuLayout.AppName(WingmanLaunchTarget.T3Web), WingmanLaunchTarget.T3Web, userHelpersRoot, initializedSettings.Path,
            new T3WebTargetSource(targetHttp, t3WebLauncher.Port, t3WebLauncher.Origin, cdpPool), evaluator, HelperAppIds.T3Web,
            // Live: Closed as soon as nothing listens on Wingman's T3 Code (web) port, not the last check's page count.
            t3WebLauncher.Port, () => AppPresence.EndpointWindows(AppPresence.IsListening(t3WebLauncher.Port()), t3WebReference?.Connections));
        grok = new AppTargetController(TrayMenuLayout.AppName(WingmanLaunchTarget.GrokBot), WingmanLaunchTarget.GrokBot, userHelpersRoot, initializedSettings.Path,
            new GrokBotTargetSource(targetHttp, grokLauncher.Port, cdpPool), evaluator, HelperAppIds.GrokBot,
            grokLauncher.Port, () => grokLauncher.OpenWindowCount(),
            roots => new HelperBackend(roots, log: message => Debug.WriteLine($"[Grok Bot Helper backend] {message}"),
                grokBotUserData: grokLauncher.UserData));

        codexMenuItem = new ToolStripMenuItem("Codex: Checking");
        t3CodeMenuItem = new ToolStripMenuItem("T3 Code: Not ready");
        helpersItem = new ToolStripMenuItem("Manage helpers");
        injectItem = new ToolStripMenuItem("Helpers enabled")
        {
            CheckOnClick = true,
            Checked = true,
        };
        injectItem.CheckedChanged += InjectCheckedChanged;
        openHelpersFolderItem = new ToolStripMenuItem("Open Helpers folder", null, OpenHelpersFolderClicked);
        reloadHelpersItem = new ToolStripMenuItem("Reload helpers", null, ReloadHelpersClicked);
        RebuildHelpersMenu();
        // Every app line gets the same flyout (TrayMenuLayout): Open window, Repair, Status details...,
        // separator, Open on launch. What each action does is app-specific.
        codexFlyout = new AppFlyoutMenu(codexMenuItem, OpenWindowClicked, RepairClicked, StatusDetailsClicked,
            settings.OpensOnLaunch(WingmanLaunchTarget.Codex));
        t3Flyout = new AppFlyoutMenu(t3CodeMenuItem, T3OpenWindowClicked, T3RepairClicked, T3StatusDetailsClicked,
            settings.OpensOnLaunch(WingmanLaunchTarget.T3Code));
        SetT3MenuStatus();
        t3Web.Flyout = new AppFlyoutMenu(t3Web.Menu, async (_, _) => await EnsureT3WebReadyAsync(), async (_, _) => await RepairT3WebAsync(),
            (_, _) => t3Web.ShowStatusDetails(), settings.OpensOnLaunch(WingmanLaunchTarget.T3Web));
        t3Web.SetMenuStatus();
        grok.Flyout = new AppFlyoutMenu(grok.Menu, async (_, _) => await EnsureGrokBotReadyAsync(), async (_, _) => await RepairGrokBotAsync(),
            (_, _) => grok.ShowStatusDetails(), settings.OpensOnLaunch(WingmanLaunchTarget.GrokBot));
        grok.SetMenuStatus();
        foreach (var (app, flyout) in new[] { (WingmanLaunchTarget.Codex, codexFlyout), (WingmanLaunchTarget.T3Code, t3Flyout),
                     (WingmanLaunchTarget.T3Web, t3Web.Flyout), (WingmanLaunchTarget.GrokBot, grok.Flyout) })
            flyout.OpenOnLaunchItem.CheckedChanged += (_, _) => OpenOnLaunchChanged(app, flyout.OpenOnLaunchItem);
        aboutItem = new ToolStripMenuItem("About", null, AboutClicked);
        checkUpdatesItem = new ToolStripMenuItem("Check for Updates", null, CheckUpdatesClicked);
        exitItem = new ToolStripMenuItem(WingmanIdentity.CloseMenuText, null, ExitClicked);
        menu = new ContextMenuStrip();
        menu.Items.AddRange([
            codexMenuItem,
            t3CodeMenuItem,
            t3Web.Menu,
            grok.Menu,
            new ToolStripSeparator(),
            injectItem,
            helpersItem,
            new ToolStripSeparator(),
            aboutItem,
            checkUpdatesItem,
            exitItem,
        ]);
        SynchronizeAppLines();
        menu.Opening += (_, _) => { SynchronizeAppLines(); RefreshAppLinePresence(); };

        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        trayIcons = new TrayIconSet(extractedIcon ?? SystemIcons.Application);
        extractedIcon?.Dispose();
        tray = new NotifyIcon
        {
            Text = TrayHoverText,
            Icon = trayIcons.For(currentStatus.IconState),
            ContextMenuStrip = menu,
            Visible = true,
        };
        SetStatus(currentStatus);
        tray.MouseClick += TrayMouseClick;
        if (activationEvent is not null)
        {
            activationRegistrations.Add(ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) => uiContext.Post(_ => QueueLauncherActivation(WingmanLaunchTarget.Codex), null),
                null,
                Timeout.Infinite,
                false));
        }
        RegisterLauncherActivation(codexActivation, WingmanLaunchTarget.Codex);
        RegisterLauncherActivation(t3Activation, WingmanLaunchTarget.T3Code);
        RegisterLauncherActivation(t3WebActivation, WingmanLaunchTarget.T3Web);
        RegisterLauncherActivation(grokActivation, WingmanLaunchTarget.GrokBot);
        if (DevelopmentChannel.IsDevelopmentBuild(AppContext.BaseDirectory)) RegisterDevelopmentCommands();

        refreshTimer = new System.Windows.Forms.Timer { Interval = HelperRefreshSchedule.IntervalMilliseconds(AllHelpers()) };
        refreshTimer.Tick += async (_, _) => await RefreshTimerAsync();
        refreshTimer.Start();
        actionTimer = new System.Windows.Forms.Timer { Interval = HelperRefreshSchedule.HostActionIntervalMilliseconds };
        actionTimer.Tick += async (_, _) => await ActionTimerAsync();
        SynchronizeActionTimer();
        trayRecoveryTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        trayRecoveryTimer.Tick += (_, _) => TrayVisibilityRecovery.EnsureVisible(
            exiting,
            visible => tray.Visible = visible);
        trayRecoveryTimer.Start();

        var startTimer = new System.Windows.Forms.Timer { Interval = 250 };
        startTimer.Tick += async (_, _) =>
        {
            startTimer.Stop();
            startTimer.Dispose();
            try { await InitializeAsync(); }
            finally
            {
                initializing = false;
                await DrainLauncherActivationsAsync();
            }
            await CheckForUpdatesAsync(showCurrentResult: false);
        };
        startTimer.Start();
    }

    private async Task InitializeAsync()
    {
        if (launchOptions.Target == WingmanLaunchTarget.T3Code)
        {
            await EnsureT3ReadyAsync(repair: false);
            return;
        }
        if (launchOptions.Target == WingmanLaunchTarget.Codex)
        {
            await EnsureCodexReadyAsync(openWindowWhenHealthy: true, "Opening Codex...");
            return;
        }
        if (launchOptions.Target == WingmanLaunchTarget.T3Web)
        {
            await EnsureT3WebReadyAsync();
            return;
        }
        if (launchOptions.Target == WingmanLaunchTarget.GrokBot)
        {
            await EnsureGrokBotReadyAsync();
            return;
        }
        await ReconcileAppTargetsAsync();
        var startupSettings = HelperSettingsStore.Load(settingsPath);
        // "Open when Wingman starts" never shows a dialog: an app that is already running without Wingman's
        // connection is left alone and its line reads Needs repair; a failure shows on the line too.
        if (launchOptions.OpenOnStartup(WingmanLaunchTarget.T3Web, startupSettings) && t3WebLauncher.IsAvailable())
            await EnsureT3WebReadyAsync(atStartup: true);
        if (launchOptions.OpenOnStartup(WingmanLaunchTarget.GrokBot, startupSettings) && grokLauncher.IsInstalled())
            await EnsureGrokBotReadyAsync(atStartup: true);
        if (busy || exiting) return;
        SetBusy(true, "Starting Codex...");
        try
        {
            var t3StartupFailed = false;
            if (t3Flyout.OpenOnLaunchItem.Checked)
            {
                try
                {
                    await T3CodeLauncher.EnsureRunningWithDebuggingAsync(t3RuntimeStatePath, activePort);
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"[T3 Code startup] {error}");
                    t3StartupFailed = true;
                    t3CheckFailed = true;
                    SetT3MenuStatus();
                }
            }
            if (!t3StartupFailed) await ReconcileT3Async();
            var startupState = await CodexLauncher.GetHookStatusAsync(runtimeStatePath, configuredPortOverride);
            codexHooked = startupState == CodexHookState.Hooked;
            if (startupState == CodexHookState.OpenButCannotHook
                || (startupState == CodexHookState.NotOpen && !codexFlyout.OpenOnLaunchItem.Checked))
            {
                await RefreshConnectionHealthAsync();
                return;
            }
            activePort = await CodexLauncher.EnsureRunningWithDebuggingAsync(runtimeStatePath, configuredPortOverride);
            var report = await helperHost.ReconcileAsync();
            SynchronizeHelpersMenu();
            await SetConnectionHealthFromReportAsync(report);
        }
        catch (Exception error)
        {
            await SetConnectionErrorStatusAsync($"Startup needs attention: {error.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool synchronizingOpenOnLaunch;

    private void OpenOnLaunchChanged(WingmanLaunchTarget app, ToolStripMenuItem item)
    {
        if (synchronizingOpenOnLaunch) return;
        try
        {
            HelperSettingsStore.Save(settingsPath, HelperSettingsStore.Load(settingsPath).WithOpenOnLaunch(app, item.Checked));
        }
        catch (Exception error)
        {
            synchronizingOpenOnLaunch = true;
            try { item.Checked = HelperSettingsStore.Load(settingsPath).OpensOnLaunch(app); }
            catch (Exception) { item.Checked = !item.Checked; }
            finally { synchronizingOpenOnLaunch = false; }
            MessageBox.Show($"Wingman could not save the startup choice: {error.Message}",
                "Wingman settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Cheap live re-check of each app line: windows are counted now, so a closed app reads Closed at once.
    /// The full Helper check (every refresh while Wingman is not busy) still decides OK and Needs repair.
    /// </summary>
    private void RefreshAppLinePresence()
    {
        if (exiting) return;
        try
        {
            SetT3MenuStatus();
            t3Web.SetMenuStatus();
            grok.SetMenuStatus();
            if (CodexLauncher.GetOpenWindowCount() == 0)
                codexMenuItem.Text = TrayMenuLayout.LineText(WingmanLaunchTarget.Codex, "Closed");
        }
        catch (Exception error) { Debug.WriteLine($"[App line presence] {error.Message}"); }
    }

    /// <summary>App lines show only for apps on this PC (or already running with Wingman).</summary>
    private void SynchronizeAppLines()
    {
        bool Safe(Func<bool> check, string app)
        {
            try { return check(); }
            catch (Exception error) { Debug.WriteLine($"[{app} install check] {error.Message}"); return false; }
        }
        t3CodeMenuItem.Visible = TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.T3Code,
            GetT3Port() is not null || Safe(T3CodeLauncher.IsInstalled, "T3 Code"));
        t3Web.Menu.Visible = TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.T3Web,
            t3Web.HasEndpoint || Safe(t3WebLauncher.IsAvailable, "T3 Code (web)"));
        grok.Menu.Visible = TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.GrokBot,
            grok.HasEndpoint || Safe(grokLauncher.IsInstalled, "Grok Bot"));
    }

    private IEnumerable<HelperSummary> AllHelpers() => helperHost.Helpers.Concat(t3HelperHost.Helpers)
        .Concat(t3Web.Host.Helpers).Concat(grok.Host.Helpers);

    private IEnumerable<AppTargetController> AppTargets() => [t3Web, grok];

    private IEnumerable<int> ReservedPorts()
    {
        yield return activePort;
        if (GetT3Port() is { } t3) yield return t3;
        if (t3WebLauncher?.Port() is { } web) yield return web;
        if (grokLauncher?.Port() is { } grokPort) yield return grokPort;
    }

    private async Task ReconcileAppTargetsAsync(Func<AppTargetController, Func<Task<HelperHostReport>>?>? operation = null)
    {
        foreach (var target in AppTargets())
            await target.ReconcileAsync(operation?.Invoke(target));
    }

    private IReadOnlyList<HelperSummary> ManagedHelpers() => AllHelpers()
        .GroupBy(helper => helper.Id, StringComparer.Ordinal)
        .Select(group =>
        {
            var first = group.First();
            var diagnostics = group.Select(helper => helper.Diagnostic)
                .Where(message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal);
            return first with
            {
                Enabled = group.All(helper => helper.Enabled),
                CanToggle = group.All(helper => helper.CanToggle),
                Diagnostic = string.Join(" | ", diagnostics) is { Length: > 0 } detail ? detail : null,
            };
        }).ToArray();

    private void RebuildHelpersMenu()
    {
        synchronizingMenu = true;
        helpersItem.DropDownItems.Clear();
        helperMenuItems.Clear();
        var managedHelpers = ManagedHelpers();
        foreach (var helper in managedHelpers)
        {
            var item = new ToolStripMenuItem(helper.Diagnostic is null ? helper.Name : $"{helper.Name} (!)")
            {
                CheckOnClick = helper.CanToggle,
                Checked = helper.CanToggle && helper.Enabled,
                Enabled = helper.CanToggle,
                ToolTipText = helper.Diagnostic ?? $"{helper.Name} {helper.Version}",
                Tag = helper.Id,
            };
            if (helper.CanToggle)
            {
                item.CheckedChanged += HelperCheckedChanged;
                helperMenuItems.Add(helper.Id, item);
            }
            helpersItem.DropDownItems.Add(item);
        }
        if (managedHelpers.Count == 0)
            helpersItem.DropDownItems.Add(new ToolStripMenuItem("No Helpers found") { Enabled = false });
        helpersItem.DropDownItems.Add(new ToolStripSeparator());
        helpersItem.DropDownItems.Add(openHelpersFolderItem);
        helpersItem.DropDownItems.Add(reloadHelpersItem);
        synchronizingMenu = false;
    }

    private void SynchronizeHelpersMenu()
    {
        synchronizingMenu = true;
        injectItem.Checked = !helperHost.IsSuspended && !t3HelperHost.IsSuspended
            && AppTargets().All(target => !target.Host.IsSuspended);
        foreach (var helper in ManagedHelpers())
        {
            if (!helperMenuItems.TryGetValue(helper.Id, out var item)) continue;
            item.Checked = helper.Enabled;
            item.Text = helper.Diagnostic is null ? helper.Name : $"{helper.Name} (!)";
            item.ToolTipText = helper.Diagnostic ?? $"{helper.Name} {helper.Version}";
        }
        synchronizingMenu = false;
        SynchronizeActionTimer();
    }

    private void SynchronizeActionTimer()
    {
        actionTimer.Enabled = !exiting && HelperRefreshSchedule.RequiresHostActionPolling(AllHelpers());
    }

    private void TrayMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left) return;
        tray.BalloonTipTitle = TrayHoverText;
        tray.BalloonTipText = currentStatus.DiagnosticText;
        tray.ShowBalloonTip(2500);
    }

    private void RegisterLauncherActivation(EventWaitHandle? handle, WingmanLaunchTarget target)
    {
        if (handle is null) return;
        activationRegistrations.Add(ThreadPool.RegisterWaitForSingleObject(handle,
            (_, _) => uiContext.Post(_ => QueueLauncherActivation(target), null), null, Timeout.Infinite, false));
    }

    private readonly List<EventWaitHandle> commandEvents = [];

    /// <summary>
    /// Development builds only: <c>--t3-web-renew-now</c> renews T3 web's pairing now (same path as renewal before
    /// expiry, no prompt), and <c>--close</c> closes Wingman the same way as its tray Close item.
    /// </summary>
    private void RegisterDevelopmentCommands()
    {
        foreach (var command in WingmanLaunchOptions.Commands)
        {
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, WingmanLaunchOptions.CommandEventName(command));
            commandEvents.Add(handle);
            activationRegistrations.Add(ThreadPool.RegisterWaitForSingleObject(handle,
                (_, _) => uiContext.Post(async _ => await RunDevelopmentCommandAsync(command), null), null, Timeout.Infinite, false));
        }
    }

    private async Task RunDevelopmentCommandAsync(WingmanCommand command)
    {
        if (exiting) return;
        switch (command)
        {
            case WingmanCommand.Close:
                await ExitAsync();
                return;
            case WingmanCommand.DumpMenu:
                DumpMenu();
                return;
            case WingmanCommand.GrokBotPin:
                ToggleGrokBotPin();
                return;
            case WingmanCommand.T3WebCheckSignIn:
                // Sign-in renewal is automatic; this is the old "Check T3 sign-in now" for development.
                nextPairingCheckUtc = DateTime.MinValue;
                await CheckT3WebPairingAsync(interactive: true);
                return;
            default:
                nextPairingCheckUtc = DateTime.MinValue;
                await CheckT3WebPairingAsync(interactive: false, forceRenew: true);
                return;
        }
    }

    /// <summary>Development command --dump-menu: writes the live tray menu tree and its contract check to a file.</summary>
    private void DumpMenu()
    {
        try
        {
            SynchronizeAppLines();
            RefreshAppLinePresence();
            var nodes = AppFlyoutMenu.Snapshot(menu.Items);
            var dump = new
            {
                writtenAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                version = Application.ProductVersion,
                problems = TrayMenuCheck.Problems(nodes),
                menu = nodes,
            };
            var path = WingmanLaunchOptions.MenuDumpPath(settingsDirectory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(dump, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[menu dump] {error}");
        }
    }

    private void QueueLauncherActivation(WingmanLaunchTarget target)
    {
        if (exiting) return;
        pendingLaunches.Enqueue(target);
        _ = DrainLauncherActivationsAsync();
    }

    private async Task DrainLauncherActivationsAsync()
    {
        if (initializing || drainingLaunches || busy || exiting) return;
        drainingLaunches = true;
        try
        {
            while (pendingLaunches.Count > 0 && !busy && !exiting)
            {
                var target = pendingLaunches.Dequeue();
                if (target == WingmanLaunchTarget.T3Code) await EnsureT3ReadyAsync(repair: false);
                else if (target == WingmanLaunchTarget.T3Web) await EnsureT3WebReadyAsync();
                else if (target == WingmanLaunchTarget.GrokBot) await EnsureGrokBotReadyAsync();
                else await EnsureCodexReadyAsync(openWindowWhenHealthy: true, "Opening Codex...");
            }
        }
        finally { drainingLaunches = false; }
    }

    private async void InjectCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (synchronizingMenu || sender is not ToolStripMenuItem item) return;
        if (busy || exiting)
        {
            SynchronizeHelpersMenu();
            return;
        }

        SetBusy(true);
        try
        {
            HelperHostReport? report = null;
            Exception? codexFailure = null;
            try
            {
                report = item.Checked
                    ? await helperHost.ResumeAndReconcileAsync()
                    : await helperHost.SuspendAsync();
            }
            catch (Exception error) { codexFailure = error; }
            await ReconcileT3Async(() => item.Checked
                ? t3HelperHost.ResumeAndReconcileAsync()
                : t3HelperHost.SuspendAsync());
            await ReconcileAppTargetsAsync(target => () => item.Checked
                ? target.Host.ResumeAndReconcileAsync()
                : target.Host.SuspendAsync());
            SynchronizeHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(AllHelpers());
            if (codexFailure is not null)
                await SetConnectionErrorStatusAsync($"Codex Helper setting needs attention: {codexFailure.Message}");
            else await SetConnectionHealthFromReportAsync(report!);
        }
        catch (Exception error)
        {
            SynchronizeHelpersMenu();
            await SetConnectionErrorStatusAsync($"Helper injection needs attention: {error.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshTimerAsync()
    {
        // App lines drop to Closed when their app closes even while the full check below is skipped
        // (Wingman busy with an operation or showing a dialog).
        RefreshAppLinePresence();
        if (!automaticRefreshGate.TryEnter(busy, exiting)) return;
        try
        {
            await ReconcileT3Async();
            await ReconcileAppTargetsAsync();
            await RefreshT3WebWindowsAsync();
            codexHooked = await CodexLauncher.GetHookStatusAsync(runtimeStatePath, configuredPortOverride)
                == CodexHookState.Hooked;
            if (!codexHooked)
            {
                await RefreshConnectionHealthAsync();
                return;
            }
            var report = await helperHost.ReconcileAsync();
            await SetConnectionHealthFromReportAsync(report);
            // Automatic reconciliation must not mutate the tray menu or interactive enabled state.
            // The first row may update only between the four stable Codex state labels.
        }
        catch (Exception error)
        {
            await SetConnectionErrorStatusAsync($"Automatic Helper refresh failed: {error.Message}");
        }
        finally
        {
            automaticRefreshGate.Exit();
        }
    }

    private async Task ActionTimerAsync()
    {
        if (!actionDrainGate.TryEnter(busy, exiting)) return;
        try
        {
            // Codex's action queue only exists while Codex is hooked; the 2-second refresh re-checks that.
            if (codexHooked) await helperHost.DrainHostActionsAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[CodexWingman Host Action poll] {error.Message}");
        }
        try
        {
            if (GetT3Port() is not null)
                await t3HelperHost.DrainHostActionsAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 Code Host Action poll] {error.Message}");
            t3CheckFailed = true;
            SetT3MenuStatus();
        }
        finally
        {
            foreach (var target in AppTargets()) await target.DrainHostActionsAsync();
            actionDrainGate.Exit();
        }
    }

    private async void HelperCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (synchronizingMenu || sender is not ToolStripMenuItem { Tag: string helperId } item) return;
        if (busy || exiting)
        {
            SynchronizeHelpersMenu();
            return;
        }

        SetBusy(true, item.Checked ? $"Enabling {item.Text}..." : $"Disabling {item.Text}...");
        try
        {
            HelperHostReport? report = null;
            Exception? codexFailure = null;
            if (helperHost.Helpers.Any(helper => helper.Id == helperId))
            {
                try { report = await helperHost.SetEnabledAsync(helperId, item.Checked); }
                catch (Exception error) { codexFailure = error; }
            }
            if (t3HelperHost.Helpers.Any(helper => helper.Id == helperId))
                await ReconcileT3Async(() => t3HelperHost.SetEnabledAsync(helperId, item.Checked));
            foreach (var target in AppTargets().Where(target => target.Host.Helpers.Any(helper => helper.Id == helperId)))
                await target.ReconcileAsync(() => target.Host.SetEnabledAsync(helperId, item.Checked));
            SynchronizeHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(AllHelpers());
            if (codexFailure is not null)
                await SetConnectionErrorStatusAsync($"Codex Helper setting needs attention: {codexFailure.Message}");
            else if (report is null) await RefreshConnectionHealthAsync();
            else await SetConnectionHealthFromReportAsync(report);
        }
        catch (Exception error)
        {
            SynchronizeHelpersMenu();
            await SetConnectionErrorStatusAsync($"Helper setting needs attention: {error.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RepairClicked(object? sender, EventArgs eventArgs)
    {
        await EnsureCodexReadyAsync(openWindowWhenHealthy: false, "Repairing Codex and Helpers...");
    }

    private async void OpenWindowClicked(object? sender, EventArgs eventArgs)
    {
        await EnsureCodexReadyAsync(openWindowWhenHealthy: true, "Opening another Codex window...");
    }

    private async void T3OpenWindowClicked(object? sender, EventArgs eventArgs)
    {
        await EnsureT3ReadyAsync(repair: false);
    }

    private async void T3RepairClicked(object? sender, EventArgs eventArgs)
    {
        await EnsureT3ReadyAsync(repair: true);
    }

    private async Task EnsureT3ReadyAsync(bool repair)
    {
        if (busy || exiting) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        activeInteractiveOperation = deadline;
        SetBusy(true, repair ? "Repairing T3 Code..." : "Opening T3 Code...");
        try
        {
            // Repair, like every app's: with Wingman's connection, re-attach quietly; ask before closing
            // T3 Code only when windows are open that Wingman cannot reach (a restart is the only fix).
            var quietRepair = false;
            if (repair && GetT3Port() is not null)
            {
                await ReconcileT3Async(() => t3HelperHost.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
                var windows = T3CodeLauncher.GetOpenWindowCount();
                var reached = t3CheckFailed || lastT3Connections is null ? 0
                    : Math.Min(lastT3Connections.Connected, lastT3Report?.TargetsDiscovered ?? 0);
                quietRepair = T3CodeRepairPolicy.Decide(windows, reached, t3CheckFailed) switch
                {
                    T3CodeRepairStep.Reattached => true,
                    T3CodeRepairStep.RestartForUnreachedWindows => ConfirmT3Restart()
                        ? await RestartT3ForRepairAsync(deadline.Token)
                        : throw new OperationCanceledException(),
                    _ => false,
                };
            }
            if (!quietRepair)
            {
                var acquisition = await T3CodeLauncher.OpenHookReadyWindowAsync(
                    t3RuntimeStatePath, activePort, deadline.Token);
                if (acquisition.Mode == HookReadyAcquisitionMode.RestartRequired)
                {
                    if (!ConfirmT3Restart()) return;
                    await T3CodeLauncher.RestartWithDebuggingAsync(t3RuntimeStatePath, deadline.Token);
                }
            }
            await ReconcileT3Async(() => t3HelperHost.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
            if (t3CheckFailed)
                throw new InvalidOperationException("T3 Code started, but its Helpers could not be checked.");
            SynchronizeHelpersMenu();
        }
        catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
        {
            SetT3MenuStatus();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 Code launch or repair] {error}");
            t3CheckFailed = true;
            SetT3MenuStatus();
            MessageBox.Show(error.Message, "T3 Code needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(activeInteractiveOperation, deadline)) activeInteractiveOperation = null;
            SetBusy(false);
        }
    }

    private async Task<bool> RestartT3ForRepairAsync(CancellationToken cancellationToken)
    {
        await T3CodeLauncher.RestartWithDebuggingAsync(t3RuntimeStatePath, cancellationToken);
        return true;
    }

    private static bool ConfirmT3Restart() => MessageBox.Show(
        "Wingman will close this session's T3 Code windows and relaunch T3 Code with Helpers. Save your work first. Codex will be left alone. Continue?",
        "Repair T3 Code", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private T3PairingLink? pendingT3WebLink;

    private async Task EnsureT3WebReadyAsync(bool atStartup = false)
    {
        if (busy || exiting) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        activeInteractiveOperation = deadline;
        SetBusy(true, "Opening T3 Code (web)...");
        try
        {
            var (_, prefetched) = await t3WebLauncher.OpenAsync(deadline.Token);
            if (prefetched is not null) pendingT3WebLink = prefetched;
            t3Web.Menu.Visible = true;
            var pagesDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while ((await t3WebLauncher.PagesAsync(deadline.Token)).Count == 0 && DateTime.UtcNow < pagesDeadline)
                await Task.Delay(500, deadline.Token);
            await t3Web.ReconcileAsync(() => t3Web.Host.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
            await t3WebLauncher.ApplyWindowIdentityAsync(Application.ExecutablePath, LauncherShortcuts.CurrentProfileOverride(launchOptions), deadline.Token);
            SynchronizeHelpersMenu();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 web launch] {error}");
            t3Web.CheckFailed = true;
            t3Web.Note = "Last attempt to open it failed: " + error.Message;
            t3Web.SetMenuStatus();
            if (!atStartup)
                MessageBox.Show(error.Message, "T3 Code (web) needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(activeInteractiveOperation, deadline)) activeInteractiveOperation = null;
            SetBusy(false);
        }
        nextPairingCheckUtc = DateTime.MinValue;
        await CheckT3WebPairingAsync(interactive: false);
    }

    /// <summary>
    /// Keeps Wingman's own T3 web Chrome folder signed in: pairs a signed-out window and renews a few
    /// days before T3's fixed 30-day session end, using one-time links from the pairing provider.
    /// Runs in the background and never holds the tray busy.
    /// </summary>
    private async Task CheckT3WebPairingAsync(bool interactive, bool forceRenew = false)
    {
        if (pairingInFlight || exiting || DateTime.UtcNow < nextPairingCheckUtc || t3WebLauncher.Port() is null) return;
        pairingInFlight = true;
        T3PairingOutcome? outcome = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var pages = await t3WebLauncher.PagesAsync(deadline.Token);
            if (pages.Count == 0)
            {
                nextPairingCheckUtc = DateTime.UtcNow + TimeSpan.FromMinutes(1);
                if (interactive) MessageBox.Show("Choose Open window in the T3 Code (web) menu first.", "T3 Code (web) sign-in", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var provider = (IT3PairingLinkProvider?)t3WebLauncher.PairingProvider() ?? new UnavailablePairingProvider();
            var coordinator = new T3PairingCoordinator(provider, t3WebLauncher.PairingStatePath);
            var link = Interlocked.Exchange(ref pendingT3WebLink, null);
            outcome = await coordinator.EnsurePairedAsync(new CdpT3WebPage(evaluator, pages[0]), t3WebLauncher.Origin(),
                t3WebLauncher.PairingLabel(), deadline.Token, link, forceRenew);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 web pairing] {error}");
            outcome = new(T3PairingResult.Failed, "Wingman could not check the T3 Code (web) sign-in: " + error.Message);
        }
        finally
        {
            pairingInFlight = false;
        }
        var ends = outcome.ExpiresAtUtc is { } expires
            ? " Session ends " + expires.ToLocalTime().ToString("MMM d, h:mm tt") + " (" + LocalZoneLabel(expires) + ")."
            : string.Empty;
        t3Web.Note = "Sign-in: " + outcome.Message + ends;
        nextPairingCheckUtc = DateTime.UtcNow + (outcome.Result == T3PairingResult.Failed ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(30));
        if (outcome.Result is T3PairingResult.Paired or T3PairingResult.Renewed)
            await t3Web.ReconcileAsync();
        t3Web.SetMenuStatus();
        if (outcome.Result == T3PairingResult.Failed || interactive || forceRenew)
        {
            tray.BalloonTipTitle = "T3 Code (web) sign-in";
            tray.BalloonTipText = outcome.Message;
            tray.ShowBalloonTip(4000);
        }
    }

    private static string LocalZoneLabel(DateTimeOffset value) =>
        TimeZoneInfo.Local.IsDaylightSavingTime(value) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName;

    private sealed class UnavailablePairingProvider : IT3PairingLinkProvider
    {
        public string Name => "this PC";
        public Task<T3PairingLink> CreateLinkAsync(T3PairingLinkRequest request, CancellationToken cancellationToken) =>
            throw new DirectoryNotFoundException("no T3 pairing folder is set up and the the sync share share was not found.");
        public Task RevokePreviousAsync(Uri origin, string label, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private async Task RefreshT3WebWindowsAsync()
    {
        if (t3WebLauncher.Port() is null) return;
        try { await t3WebLauncher.ApplyWindowIdentityAsync(Application.ExecutablePath, LauncherShortcuts.CurrentProfileOverride(launchOptions)); }
        catch (Exception error) { Debug.WriteLine($"[T3 web window identity] {error.Message}"); }
        _ = CheckT3WebPairingAsync(interactive: false);
    }

    /// <summary>
    /// Repair for T3 web. Wingman owns this Chrome window, so repair never closes anything: with no
    /// Wingman window it opens one; otherwise it re-attaches the Helpers and checks the sign-in now
    /// (pairing a signed-out window), which is the manual form of the automatic renewal.
    /// </summary>
    private async Task RepairT3WebAsync()
    {
        if (busy || exiting) return;
        if (t3WebLauncher.Port() is null || (await t3WebLauncher.PagesAsync()).Count == 0)
        {
            await EnsureT3WebReadyAsync();
            return;
        }
        using (var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
        {
            activeInteractiveOperation = deadline;
            SetBusy(true, "Repairing T3 Code (web)...");
            try
            {
                await t3Web.ReconcileAsync(() => t3Web.Host.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
                if (t3Web.CheckFailed)
                    throw new InvalidOperationException("T3 Code (web) is open, but its Helpers could not be checked.");
                SynchronizeHelpersMenu();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"[T3 web repair] {error}");
                t3Web.CheckFailed = true;
                t3Web.SetMenuStatus();
                MessageBox.Show(error.Message, "T3 Code (web) needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (ReferenceEquals(activeInteractiveOperation, deadline)) activeInteractiveOperation = null;
                SetBusy(false);
            }
        }
        // Sign-in is checked now too; like the automatic check, it says something only when it failed.
        nextPairingCheckUtc = DateTime.MinValue;
        await CheckT3WebPairingAsync(interactive: false);
    }

    /// <summary>
    /// Repair for Grok Bot. Hooked: re-attach the Helpers. Running without Wingman's debugging port, or
    /// closed: the same asked-for relaunch as Open window (Grok Bot keeps its own sign-in and data).
    /// </summary>
    private async Task RepairGrokBotAsync()
    {
        if (busy || exiting) return;
        if (grokLauncher.Port() is null || (await grokLauncher.PagesAsync()).Count == 0)
        {
            await EnsureGrokBotReadyAsync();
            return;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        activeInteractiveOperation = deadline;
        SetBusy(true, "Repairing Grok Bot...");
        try
        {
            await grok.ReconcileAsync(() => grok.Host.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
            if (grok.CheckFailed)
                throw new InvalidOperationException("Grok Bot is open, but its Helpers could not be checked.");
            SynchronizeHelpersMenu();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[Grok Bot repair] {error}");
            grok.CheckFailed = true;
            grok.SetMenuStatus();
            MessageBox.Show(error.Message, "Grok Bot needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(activeInteractiveOperation, deadline)) activeInteractiveOperation = null;
            SetBusy(false);
        }
    }

    private async Task EnsureGrokBotReadyAsync(bool atStartup = false)
    {
        if (busy || exiting) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        activeInteractiveOperation = deadline;
        SetBusy(true, "Opening Grok Bot...");
        try
        {
            // At startup Wingman never asks to relaunch: a Grok Bot running without Wingman's port stays as it is.
            if (!await grokLauncher.OpenAsync(atStartup ? () => false : ConfirmGrokBotRelaunch, deadline.Token))
            {
                grok.SetMenuStatus();
                return;
            }
            grok.Menu.Visible = true;
            await grok.ReconcileAsync(() => grok.Host.ResumeAndReconcileAsync(deadline.Token), deadline.Token);
            if (grok.CheckFailed)
                throw new InvalidOperationException("Grok Bot started, but its Helpers could not be checked.");
            SynchronizeHelpersMenu();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[Grok Bot launch] {error}");
            grok.CheckFailed = true;
            grok.SetMenuStatus();
            if (!atStartup)
                MessageBox.Show(error.Message, "Grok Bot needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(activeInteractiveOperation, deadline)) activeInteractiveOperation = null;
            SetBusy(false);
        }
    }

    private static bool ConfirmGrokBotRelaunch() => MessageBox.Show(
        "Wingman will close Grok Bot and open it again with Helpers. Grok Bot keeps its own sign-in and data, and Wingman changes none of its files. Finish anything in progress first. Continue?",
        "Grok Bot", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>
    /// Development command --grok-bot-pin: make the existing Grok Bot taskbar pin open Grok Bot through
    /// Wingman, or put it back. Setup only, so it is not a tray item; it asks before changing anything.
    /// </summary>
    private void ToggleGrokBotPin()
    {
        try
        {
            var pin = GrokBotPinRetarget.FindPin(grokLauncher.Executable, Application.ExecutablePath);
            if (pin is null)
            {
                MessageBox.Show("No Grok Bot taskbar pin was found. Wingman never adds pins.", "Grok Bot pin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (pin.Value.ThroughWingman)
            {
                if (MessageBox.Show("Put your Grok Bot taskbar pin back the way it was?", "Grok Bot pin",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    GrokBotPinRetarget.Restore(grokLauncher.Executable, Application.ExecutablePath, settingsDirectory);
            }
            else if (MessageBox.Show(
                         "Make your existing Grok Bot taskbar pin open Grok Bot through Wingman, so the usage dial is there? The pin keeps Grok Bot's icon and grouping. Wingman keeps a backup and puts it back when you run this command again.",
                         "Grok Bot pin", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                var backup = GrokBotPinRetarget.Apply(grokLauncher.Executable, Application.ExecutablePath, settingsDirectory,
                    WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.GrokBot, LauncherShortcuts.CurrentProfileOverride(launchOptions)));
                MessageBox.Show("Done. Backup: " + backup, "Grok Bot pin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Grok Bot pin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StatusDetailsClicked(object? sender, EventArgs eventArgs)
    {
        MessageBox.Show(
            $"{currentStatus.Label}\n{currentStatus.WindowSummary}\n\n{currentStatus.DiagnosticText}",
            "Wingman status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void T3StatusDetailsClicked(object? sender, EventArgs eventArgs)
    {
        var status = CurrentT3MenuStatus();
        MessageBox.Show(status.Details, "T3 Code status", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private int? GetT3Port()
    {
        var port = CodexRuntimeStateStore.LoadPort(t3RuntimeStatePath);
        return port is not null && port != CodexLaunchPolicy.PreferredPort && port != activePort
            ? port : null;
    }

    private T3CodeMenuStatus CurrentT3MenuStatus()
    {
        try
        {
            return T3CodeMenuPolicy.Evaluate(
                T3CodeLauncher.GetOpenWindowCount(),
                GetT3Port() is not null,
                lastT3Report,
                lastT3Connections,
                t3CheckFailed,
                t3HelperHost.IsSuspended,
                t3HelperHost.Helpers.Any(helper => helper.Enabled));
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 Code window discovery] {error}");
            return T3CodeMenuPolicy.Evaluate(null, GetT3Port() is not null,
                lastT3Report, lastT3Connections, true);
        }
    }

    private void SetT3MenuStatus()
    {
        var status = CurrentT3MenuStatus();
        t3CodeMenuItem.Text = status.Label;
    }

    private async Task ReconcileT3Async(
        Func<Task<HelperHostReport>>? operation = null,
        CancellationToken cancellationToken = default)
    {
        // Without a saved, app-isolated endpoint, leave existing T3 windows untouched.
        if (operation is null && GetT3Port() is null)
        {
            lastT3Report = null;
            lastT3Connections = null;
            t3CheckFailed = false;
            SetT3MenuStatus();
            return;
        }
        try
        {
            lastT3Report = await (operation?.Invoke() ?? t3HelperHost.ReconcileAsync());
            lastT3Connections = await t3ConnectionProbe.ProbeAsync(cancellationToken);
            t3CheckFailed = false;
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[T3 Code Helper] {error}");
            lastT3Connections = null;
            t3CheckFailed = true;
        }
        SetT3MenuStatus();
    }

    private void OpenHelpersFolderClicked(object? sender, EventArgs eventArgs)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", userHelpersRoot) { UseShellExecute = true });
    }

    private static void AboutClicked(object? sender, EventArgs eventArgs)
    {
        string content;
        try
        {
            content = ReleaseAbout.TextFor(typeof(TrayApplicationContext).Assembly);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[Wingman About] {error}");
            var version = typeof(TrayApplicationContext).Assembly.GetName().Version;
            content = $"Wingman v{version?.ToString(3) ?? "unknown"}\n\nRelease details are unavailable in this copy.";
        }
        MessageBox.Show(
            content,
            "About Wingman",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async void CheckUpdatesClicked(object? sender, EventArgs eventArgs) =>
        await CheckForUpdatesAsync(showCurrentResult: true);

    private async Task CheckForUpdatesAsync(bool showCurrentResult)
    {
        if (checkingRelease || exiting || busy) return;
        checkingRelease = true;
        checkUpdatesItem.Enabled = false;
        var updateAccepted = false;
        try
        {
            var version = typeof(TrayApplicationContext).Assembly.GetName().Version
                ?? throw new InvalidOperationException("Wingman version is unavailable.");
            var client = new AppReleaseClient(releaseHttp, "Wingman");
            var check = await client.CheckLatestAsync("owenkemeys/CodexWingman", "Wingman", version);
            var release = check.Release;
            if (exiting) return;
            if (release is null)
            {
                if (showCurrentResult)
                    MessageBox.Show(check.Message, "Wingman updates",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var choice = MessageBox.Show(
                $"Wingman {release.Tag} is available.\n\n{release.Summary}\n\nDownload and install this update now?",
                "Wingman update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes) return;
            updateAccepted = true;
            if (!WingmanIdentity.IsCanonicalPackageDirectory(AppContext.BaseDirectory))
                throw new InvalidOperationException("This copy is not running from the installed Wingman folder.");
            SetBusy(true, "Downloading Wingman update...");
            try
            {
                var (package, script) = await new AppReleasePackageStager(
                    releaseHttp,
                    "https://github.com/owenkemeys/CodexWingman",
                    "codexwingman.release.v1",
                    "CodexWingman.exe",
                    "CodexWingman-verified",
                    "Helpers/").StageAsync(release);
                ProcessStartInfo CreateUpdaterStartInfo(bool preflight)
                {
                    var startInfo = new ProcessStartInfo("powershell.exe")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    foreach (var argument in new[]
                    {
                        "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                        "-Package", package,
                        "-Installed", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                        "-WaitingPid", Environment.ProcessId.ToString(),
                        "-ExpectedVersion", $"{release.Version.Major}.{release.Version.Minor}.{release.Version.Build}",
                        "-ExpectedRepository", "https://github.com/owenkemeys/CodexWingman",
                        "-ExpectedSchema", "codexwingman.release.v1",
                        "-ExecutableName", "CodexWingman.exe",
                        "-StableDirectoryName", "CodexWingman-verified",
                        "-ExtensionsDirectoryName", "Helpers",
                    }) startInfo.ArgumentList.Add(argument);
                    var relaunchArguments = WingmanLaunchOptions.RelaunchArguments(
                        LauncherShortcuts.CurrentProfileOverride(launchOptions));
                    if (relaunchArguments.Length > 0)
                    {
                        startInfo.ArgumentList.Add("-RelaunchArguments");
                        startInfo.ArgumentList.Add(relaunchArguments);
                    }
                    if (preflight) startInfo.ArgumentList.Add("-PreflightOnly");
                    return startInfo;
                }
                using (var preflight = Process.Start(CreateUpdaterStartInfo(preflight: true))
                    ?? throw new InvalidOperationException("Could not preflight the Wingman updater."))
                {
                    await preflight.WaitForExitAsync();
                    if (preflight.ExitCode != 0)
                        throw new InvalidOperationException("The Windows updater preflight failed; Wingman was left unchanged.");
                }
                var start = CreateUpdaterStartInfo(preflight: false);
                using var updater = Process.Start(start)
                    ?? throw new InvalidOperationException("Could not start the Wingman updater.");
                if (updater.WaitForExit(250) && updater.ExitCode != 0)
                    throw new InvalidOperationException("Wingman could not prepare the update.");
                SetBusy(false);
                await ExitAsync();
            }
            finally
            {
                if (!exiting) SetBusy(false);
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[Wingman update] {error}");
            if (!exiting && (showCurrentResult || updateAccepted))
                MessageBox.Show($"Wingman could not update: {error.Message}", "Wingman updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            checkingRelease = false;
            if (!exiting) checkUpdatesItem.Enabled = true;
        }
    }

    private async void ReloadHelpersClicked(object? sender, EventArgs eventArgs)
    {
        if (exiting) return;
        if (busy)
        {
            reloadPending = true;
            activeInteractiveOperation?.Cancel();
            SetStatus(new TrayStatus(
                "Status: Reload queued",
                "Wingman is stopping the current operation, then it will reload Helpers.",
                currentStatus.WindowSummary));
            return;
        }
        await ReloadHelpersAsync();
    }

    private async Task ReloadHelpersAsync()
    {
        if (busy || exiting) return;
        SetBusy(true, "Reloading Helpers...");
        try
        {
            Exception? codexFailure = null;
            try { await helperHost.ReloadAsync(); }
            catch (Exception error) { codexFailure = error; }
            try
            {
                await t3HelperHost.ReloadAsync();
                await ReconcileT3Async();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"[T3 Code Helper reload] {error}");
                t3CheckFailed = true;
                SetT3MenuStatus();
            }
            foreach (var target in AppTargets())
            {
                try
                {
                    await target.Host.ReloadAsync();
                    await target.ReconcileAsync();
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"[{target.Name} Helper reload] {error}");
                    target.CheckFailed = true;
                    target.SetMenuStatus();
                }
            }
            RebuildHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(AllHelpers());
            SynchronizeHelpersMenu();
            if (codexFailure is not null)
                await SetConnectionErrorStatusAsync($"Codex Helper reload needs attention: {codexFailure.Message}");
            else
            {
                var report = await helperHost.ReconcileAsync();
                await SetConnectionHealthFromReportAsync(report);
            }
        }
        catch (Exception error)
        {
            await SetConnectionErrorStatusAsync($"Reload needs attention: {error.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ExitClicked(object? sender, EventArgs eventArgs) => await ExitAsync();

    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        refreshTimer.Stop();
        actionTimer.Stop();
        trayRecoveryTimer.Stop();
        SetBusy(true, "Removing Helpers from connected windows...");
        try { await helperHost.RemoveAllAsync(); } catch { }
        try { await t3HelperHost.RemoveAllAsync(); } catch { }
        foreach (var target in AppTargets())
        {
            try { await target.Host.RemoveAllAsync(); } catch { }
        }
        tray.Visible = false;
        ExitThread();
    }

    private void SetBusy(bool value, string? text = null)
    {
        busy = value;
        injectItem.Enabled = !value;
        codexFlyout.SetBusy(value);
        t3Flyout.SetBusy(value);
        foreach (var target in AppTargets()) target.SetBusy(value);
        checkUpdatesItem.Enabled = !value && !checkingRelease;
        openHelpersFolderItem.Enabled = !value;
        reloadHelpersItem.Enabled = !exiting;
        foreach (var item in helperMenuItems.Values) item.Enabled = !value;
        if (value && !string.IsNullOrWhiteSpace(text))
            SetStatus(new TrayStatus($"Status: {text.TrimEnd('.')}", text, currentStatus.WindowSummary, currentStatus.IconState));
        if (!value && reloadPending && !exiting)
        {
            reloadPending = false;
            uiContext.Post(async _ => await ReloadHelpersAsync(), null);
        }
        if (!value && !exiting && pendingLaunches.Count > 0)
            uiContext.Post(async _ => await DrainLauncherActivationsAsync(), null);
    }

    private async Task EnsureCodexReadyAsync(bool openWindowWhenHealthy, string busyText)
    {
        if (busy || exiting) return;
        using var operationDeadline = new CancellationTokenSource();
        operationDeadline.CancelAfter(TimeSpan.FromSeconds(90));
        activeInteractiveOperation = operationDeadline;
        var cancellationToken = operationDeadline.Token;
        SetBusy(true, busyText);
        try
        {
            var state = await CodexLauncher.GetHookStatusAsync(runtimeStatePath, configuredPortOverride, cancellationToken);
            HelperHostReport report;
            if (state == CodexHookState.Hooked)
            {
                report = openWindowWhenHealthy
                    ? await helperHost.OpenNativeNewWindowAsync(cancellationToken)
                    : await helperHost.ResumeAndReconcileAsync(cancellationToken);
            }
            else
            {
                var acquisition = await CodexLauncher.OpenHookReadyWindowAsync(
                    runtimeStatePath,
                    configuredPortOverride,
                    cancellationToken);
                if (acquisition.Mode == HookReadyAcquisitionMode.RestartRequired)
                {
                    if (!ConfirmCodexRestart())
                    {
                        await RefreshConnectionHealthAsync("No changes made; existing Codex windows were left open.");
                        return;
                    }
                    activePort = await CodexLauncher.RestartWithDebuggingAsync(
                        runtimeStatePath,
                        configuredPortOverride,
                        cancellationToken);
                }
                else
                {
                    activePort = acquisition.Port
                        ?? throw new InvalidOperationException("Codex launch did not return a Helper connection.");
                }
                report = await helperHost.ResumeAndReconcileAsync(cancellationToken);
            }
            SynchronizeHelpersMenu();
            await SetConnectionHealthFromReportAsync(report);
        }
        catch (OperationCanceledException) when (operationDeadline.IsCancellationRequested)
        {
            await SetConnectionErrorStatusAsync(
                reloadPending
                    ? "The current operation was cancelled so Helpers can reload."
                    : "The Codex and Helper operation took too long and was stopped.");
        }
        catch (Exception error)
        {
            await SetConnectionErrorStatusAsync($"Codex and Helper repair failed: {error.Message}");
        }
        finally
        {
            if (ReferenceEquals(activeInteractiveOperation, operationDeadline))
                activeInteractiveOperation = null;
            SetBusy(false);
        }
    }

    private async Task SetConnectionHealthFromReportAsync(HelperHostReport report)
    {
        lastReport = report;
        lastOperationError = null;
        await RefreshConnectionHealthAsync();
    }

    private async Task RefreshConnectionHealthAsync(string? diagnosticOverride = null)
    {
        try
        {
            var state = await CodexLauncher.GetHookStatusAsync(
                runtimeStatePath,
                configuredPortOverride);
            codexHooked = state == CodexHookState.Hooked;
            var health = ConnectionHealthPolicy.Evaluate(
                state,
                lastReport,
                helperHost.IsSuspended,
                CodexLauncher.GetOpenWindowCount());
            var diagnostic = diagnosticOverride ?? health.DiagnosticText;
            if (lastOperationError is not null)
                diagnostic += $"\n\nLast failed operation: {lastOperationError}";
            SetCodexMenu(health);
            SetStatus(new TrayStatus(health.Label, diagnostic, health.WindowSummary, health.IconState));
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[CodexWingman Status] {error}");
            codexMenuItem.Text = "Codex: Status unavailable";
            SetStatus(new TrayStatus(
                "Status: Needs attention",
                lastOperationError is null
                    ? $"Wingman could not finish checking Codex: {error.Message}"
                    : $"Last failed operation: {lastOperationError}\n\nStatus check also failed: {error.Message}",
                "Window status unavailable",
                TrayIconState.Attention));
        }
    }

    private async Task SetConnectionErrorStatusAsync(string diagnosticText)
    {
        lastOperationError = diagnosticText;
        Debug.WriteLine($"[CodexWingman Status] {diagnosticText}");
        try
        {
            var state = await CodexLauncher.GetHookStatusAsync(
                runtimeStatePath,
                configuredPortOverride);
            var health = ConnectionHealthPolicy.Evaluate(
                state, lastReport, helperHost.IsSuspended, CodexLauncher.GetOpenWindowCount());
            SetCodexMenu(health, forceProblem: true);
            SetStatus(new TrayStatus(
                "Status: Needs attention",
                diagnosticText,
                health.WindowSummary,
                TrayIconState.Attention));
        }
        catch (Exception probeError)
        {
            Debug.WriteLine($"[CodexWingman Status] Status check failed: {probeError}");
            codexMenuItem.Text = "Codex: Status unavailable";
            SetStatus(new TrayStatus(
                "Status: Needs attention",
                $"{diagnosticText}\n\nStatus check also failed: {probeError.Message}",
                "Window status unavailable",
                TrayIconState.Attention));
        }
    }

    private static bool ConfirmCodexRestart()
    {
        var result = MessageBox.Show(
            "Codex is already running without a verified helper endpoint. Wingman will close its windows, then stop remaining processes from that same app before relaunching with hooks. Save any work first. Other ChatGPT installations will be left alone. Continue?",
            "Repair Codex",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private void SetStatus(TrayStatus status)
    {
        currentStatus = status;
        tray.Icon = trayIcons.For(status.IconState);
        tray.Text = TrayIconPresentation.TextFor(status.IconState);
    }

    private void SetCodexMenu(ConnectionHealthSnapshot health, bool forceProblem = false)
    {
        var label = forceProblem ? "Needs repair" : health.State switch
        {
            ConnectionHealthState.Working => "OK",
            ConnectionHealthState.CodexClosed => "Closed",
            ConnectionHealthState.HelpersPaused => "Paused",
            _ => "Needs repair",
        };
        // The app and its state only; the window counts are in Status details....
        codexMenuItem.Text = TrayMenuLayout.LineText(WingmanLaunchTarget.Codex, label);
    }

    private void ShowCurrentStatusBalloon()
    {
        tray.BalloonTipTitle = TrayHoverText;
        tray.BalloonTipText = currentStatus.DiagnosticText;
        tray.ShowBalloonTip(2500);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
            actionTimer.Dispose();
            trayRecoveryTimer.Dispose();
            foreach (var registration in activationRegistrations) registration.Unregister(null);
            foreach (var handle in commandEvents) handle.Dispose();
            tray.Dispose();
            trayIcons.Dispose();
            targetHttp.Dispose();
            releaseHttp.Dispose();
        }
        base.Dispose(disposing);
    }

}
