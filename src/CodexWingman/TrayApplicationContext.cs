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
    private readonly ToolStripMenuItem codexOpenOnLaunchItem;
    private readonly ToolStripMenuItem t3OpenOnLaunchItem;
    private readonly ToolStripMenuItem helpersItem;
    private readonly ToolStripMenuItem injectItem;
    private readonly ToolStripMenuItem repairItem;
    private readonly ToolStripMenuItem openWindowItem;
    private readonly ToolStripMenuItem t3RepairItem;
    private readonly ToolStripMenuItem t3OpenWindowItem;
    private readonly ToolStripMenuItem statusDetailsItem;
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
    private readonly HttpClient targetHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly HttpClient releaseHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string userHelpersRoot;
    private readonly string runtimeStatePath;
    private readonly string t3RuntimeStatePath;
    private readonly string settingsPath;
    private readonly int? configuredPortOverride;
    private readonly RegisteredWaitHandle? activationRegistration;
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

    public TrayApplicationContext(int? portOverride = null, EventWaitHandle? activationEvent = null)
    {
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        configuredPortOverride = portOverride;
        var initializedSettings = HelperSettingsStore.InitializeDefault();
        var settings = initializedSettings.Settings;
        var settingsDirectory = Path.GetDirectoryName(initializedSettings.Path)
            ?? throw new InvalidOperationException("Settings directory is unavailable");
        settingsPath = initializedSettings.Path;
        runtimeStatePath = Path.Combine(settingsDirectory, "runtime.json");
        t3RuntimeStatePath = Path.Combine(settingsDirectory, "t3-runtime.json");
        activePort = configuredPortOverride
            ?? CodexRuntimeStateStore.LoadPort(runtimeStatePath)
            ?? CodexLaunchPolicy.PreferredPort;
        var targetSource = new HttpCodexTargetSource(targetHttp, () => activePort);
        var t3TargetSource = new T3CodeTargetSource(targetHttp, GetT3Port);
        var evaluator = new WebSocketCdpEvaluator();
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

        codexMenuItem = new ToolStripMenuItem("Codex: Checking");
        t3CodeMenuItem = new ToolStripMenuItem("T3 Code: Not ready");
        codexOpenOnLaunchItem = new ToolStripMenuItem("Open on launch")
        {
            CheckOnClick = true,
            Checked = settings.OpenCodexOnLaunch,
        };
        codexOpenOnLaunchItem.CheckedChanged += CodexOpenOnLaunchChanged;
        t3OpenOnLaunchItem = new ToolStripMenuItem("Open on launch")
        {
            CheckOnClick = true,
            Checked = settings.OpenT3CodeOnLaunch,
        };
        t3OpenOnLaunchItem.CheckedChanged += T3OpenOnLaunchChanged;
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
        repairItem = new ToolStripMenuItem("Repair Codex", null, RepairClicked);
        openWindowItem = new ToolStripMenuItem("Open a hooked window", null, OpenWindowClicked);
        statusDetailsItem = new ToolStripMenuItem("View status details...", null, StatusDetailsClicked);
        codexMenuItem.DropDownItems.AddRange([
            openWindowItem,
            repairItem,
            statusDetailsItem,
            new ToolStripSeparator(),
            codexOpenOnLaunchItem,
        ]);
        t3OpenWindowItem = new ToolStripMenuItem("Open a hooked window", null, T3OpenWindowClicked);
        t3RepairItem = new ToolStripMenuItem("Repair T3 Code", null, T3RepairClicked);
        t3CodeMenuItem.DropDownItems.AddRange([
            t3OpenWindowItem,
            t3RepairItem,
            new ToolStripMenuItem("View status details...", null, T3StatusDetailsClicked),
            new ToolStripSeparator(),
            t3OpenOnLaunchItem,
        ]);
        SetT3MenuStatus();
        aboutItem = new ToolStripMenuItem("About", null, AboutClicked);
        checkUpdatesItem = new ToolStripMenuItem("Check for Updates", null, CheckUpdatesClicked);
        exitItem = new ToolStripMenuItem(WingmanIdentity.CloseMenuText, null, ExitClicked);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            codexMenuItem,
            t3CodeMenuItem,
            new ToolStripSeparator(),
            injectItem,
            helpersItem,
            new ToolStripSeparator(),
            aboutItem,
            checkUpdatesItem,
            exitItem,
        ]);

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
            activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) => uiContext.Post(async _ => await LauncherActivatedAsync(), null),
                null,
                Timeout.Infinite,
                false);
        }

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
            await InitializeAsync();
            await CheckForUpdatesAsync(showCurrentResult: false);
        };
        startTimer.Start();
    }

    private async Task InitializeAsync()
    {
        if (busy || exiting) return;
        SetBusy(true, "Starting Codex...");
        try
        {
            var t3StartupFailed = false;
            if (t3OpenOnLaunchItem.Checked)
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
            if (startupState == CodexHookState.OpenButCannotHook
                || (startupState == CodexHookState.NotOpen && !codexOpenOnLaunchItem.Checked))
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

    private void CodexOpenOnLaunchChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            var settings = HelperSettingsStore.Load(settingsPath);
            HelperSettingsStore.Save(settingsPath, settings with { OpenCodexOnLaunch = codexOpenOnLaunchItem.Checked });
        }
        catch (Exception error)
        {
            codexOpenOnLaunchItem.CheckedChanged -= CodexOpenOnLaunchChanged;
            codexOpenOnLaunchItem.Checked = HelperSettingsStore.Load(settingsPath).OpenCodexOnLaunch;
            codexOpenOnLaunchItem.CheckedChanged += CodexOpenOnLaunchChanged;
            MessageBox.Show($"Wingman could not save the startup choice: {error.Message}",
                "Wingman settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void T3OpenOnLaunchChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            var settings = HelperSettingsStore.Load(settingsPath);
            HelperSettingsStore.Save(settingsPath, settings with { OpenT3CodeOnLaunch = t3OpenOnLaunchItem.Checked });
        }
        catch (Exception error)
        {
            t3OpenOnLaunchItem.CheckedChanged -= T3OpenOnLaunchChanged;
            t3OpenOnLaunchItem.Checked = HelperSettingsStore.Load(settingsPath).OpenT3CodeOnLaunch;
            t3OpenOnLaunchItem.CheckedChanged += T3OpenOnLaunchChanged;
            MessageBox.Show($"Wingman could not save the T3 Code startup choice: {error.Message}",
                "Wingman settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private IEnumerable<HelperSummary> AllHelpers() => helperHost.Helpers.Concat(t3HelperHost.Helpers);

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
        injectItem.Checked = !helperHost.IsSuspended && !t3HelperHost.IsSuspended;
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

    private async Task LauncherActivatedAsync()
    {
        if (exiting) return;
        if (busy)
        {
            ShowCurrentStatusBalloon();
            return;
        }
        await EnsureCodexReadyAsync(openWindowWhenHealthy: true, "Opening Codex...");
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
        if (!automaticRefreshGate.TryEnter(busy, exiting)) return;
        try
        {
            await ReconcileT3Async();
            if (await CodexLauncher.GetHookStatusAsync(runtimeStatePath, configuredPortOverride)
                != CodexHookState.Hooked)
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
            await helperHost.DrainHostActionsAsync();
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
            if (repair)
            {
                if (!ConfirmT3Restart()) return;
                await T3CodeLauncher.RestartWithDebuggingAsync(t3RuntimeStatePath, deadline.Token);
            }
            else
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

    private static bool ConfirmT3Restart() => MessageBox.Show(
        "Wingman will close this session's T3 Code windows and relaunch T3 Code with Helpers. Save your work first. Codex will be left alone. Continue?",
        "Repair T3 Code", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void StatusDetailsClicked(object? sender, EventArgs eventArgs)
    {
        MessageBox.Show(
            $"{currentStatus.Label}\n{currentStatus.WindowSummary}\n\n{currentStatus.DiagnosticText}",
            "Codex Wingman status",
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
                t3HelperHost.IsSuspended);
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
        t3CodeMenuItem.ToolTipText = status.Details;
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
            var release = await client.CheckAsync("owenkemeys/CodexWingman", "Wingman", version);
            if (exiting) return;
            if (release is null)
            {
                if (showCurrentResult)
                    MessageBox.Show("Wingman is up to date.", "Wingman updates",
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
        tray.Visible = false;
        ExitThread();
    }

    private void SetBusy(bool value, string? text = null)
    {
        busy = value;
        injectItem.Enabled = !value;
        repairItem.Enabled = !value;
        openWindowItem.Enabled = !value;
        t3RepairItem.Enabled = !value;
        t3OpenWindowItem.Enabled = !value;
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
            "Repair Codex and Helpers",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private void SetStatus(TrayStatus status)
    {
        currentStatus = status;
        codexMenuItem.ToolTipText = status.DiagnosticText;
        tray.Icon = trayIcons.For(status.IconState);
        tray.Text = TrayIconPresentation.TextFor(status.IconState);
    }

    private void SetCodexMenu(ConnectionHealthSnapshot health, bool forceProblem = false)
    {
        var label = forceProblem ? "Problem" : health.State switch
        {
            ConnectionHealthState.Working => "OK",
            ConnectionHealthState.CodexClosed => "Closed",
            ConnectionHealthState.HelpersPaused => "Paused",
            _ => "Problem",
        };
        codexMenuItem.Text = $"Codex: {label} ({health.Windows}/{health.VisibleWindows})";
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
            activationRegistration?.Unregister(null);
            tray.Dispose();
            trayIcons.Dispose();
            targetHttp.Dispose();
            releaseHttp.Dispose();
        }
        base.Dispose(disposing);
    }

}
