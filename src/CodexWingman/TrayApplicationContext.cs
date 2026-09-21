using System.Diagnostics;
using CodexWingman.Core;
using CodexWingman.Core.Helpers;

namespace CodexWingman;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TrayHoverText = "Codex Wingman";
    private readonly TrayIconSet trayIcons;
    private readonly NotifyIcon tray;
    private readonly ToolStripLabel statusLabel;
    private readonly ToolStripLabel windowStatusLabel;
    private readonly ToolStripMenuItem helpersItem;
    private readonly ToolStripMenuItem injectItem;
    private readonly ToolStripMenuItem repairItem;
    private readonly ToolStripMenuItem openWindowItem;
    private readonly ToolStripMenuItem statusDetailsItem;
    private readonly ToolStripMenuItem openHelpersFolderItem;
    private readonly ToolStripMenuItem reloadHelpersItem;
    private readonly ToolStripMenuItem aboutItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly System.Windows.Forms.Timer actionTimer;
    private readonly System.Windows.Forms.Timer trayRecoveryTimer;
    private readonly HelperHost helperHost;
    private readonly string userHelpersRoot;
    private readonly string runtimeStatePath;
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
    private HelperHostReport? lastReport;
    private TrayStatus currentStatus = new(
        "Status: Checking",
        "Wingman is checking Codex and its Helpers.");

    public TrayApplicationContext(int? portOverride = null, EventWaitHandle? activationEvent = null)
    {
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        configuredPortOverride = portOverride;
        var initializedSettings = HelperSettingsStore.InitializeDefault();
        var settings = initializedSettings.Settings;
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var settingsDirectory = Path.GetDirectoryName(initializedSettings.Path)
            ?? throw new InvalidOperationException("Settings directory is unavailable");
        runtimeStatePath = Path.Combine(settingsDirectory, "runtime.json");
        activePort = configuredPortOverride
            ?? CodexRuntimeStateStore.LoadPort(runtimeStatePath)
            ?? CodexLaunchPolicy.PreferredPort;
        var targetSource = new HttpCodexTargetSource(http, () => activePort);
        var evaluator = new WebSocketCdpEvaluator();
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

        statusLabel = new ToolStripLabel
        {
            Padding = new Padding(4, 2, 4, 1),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.MenuText,
        };
        windowStatusLabel = new ToolStripLabel
        {
            Padding = new Padding(4, 1, 4, 2),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.MenuText,
        };
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
        repairItem = new ToolStripMenuItem("Repair Codex and Helpers...", null, RepairClicked);
        openWindowItem = new ToolStripMenuItem("Open another Codex window", null, OpenWindowClicked);
        statusDetailsItem = new ToolStripMenuItem("View status details...", null, StatusDetailsClicked);
        aboutItem = new ToolStripMenuItem("About", null, AboutClicked);
        exitItem = new ToolStripMenuItem(WingmanIdentity.CloseMenuText, null, ExitClicked);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            statusLabel,
            windowStatusLabel,
            new ToolStripSeparator(),
            repairItem,
            openWindowItem,
            statusDetailsItem,
            new ToolStripSeparator(),
            injectItem,
            helpersItem,
            new ToolStripSeparator(),
            aboutItem,
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

        refreshTimer = new System.Windows.Forms.Timer { Interval = HelperRefreshSchedule.IntervalMilliseconds(helperHost.Helpers) };
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
        };
        startTimer.Start();
    }

    private async Task InitializeAsync()
    {
        if (busy || exiting) return;
        SetBusy(true, "Starting Codex...");
        try
        {
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

    private void RebuildHelpersMenu()
    {
        synchronizingMenu = true;
        helpersItem.DropDownItems.Clear();
        helperMenuItems.Clear();
        foreach (var helper in helperHost.Helpers)
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
        if (helperHost.Helpers.Count == 0)
            helpersItem.DropDownItems.Add(new ToolStripMenuItem("No Helpers found") { Enabled = false });
        helpersItem.DropDownItems.Add(new ToolStripSeparator());
        helpersItem.DropDownItems.Add(openHelpersFolderItem);
        helpersItem.DropDownItems.Add(reloadHelpersItem);
        synchronizingMenu = false;
    }

    private void SynchronizeHelpersMenu()
    {
        synchronizingMenu = true;
        injectItem.Checked = !helperHost.IsSuspended;
        foreach (var helper in helperHost.Helpers)
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
        actionTimer.Enabled = !exiting && HelperRefreshSchedule.RequiresHostActionPolling(helperHost.Helpers);
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
            var report = item.Checked
                ? await helperHost.ResumeAndReconcileAsync()
                : await helperHost.SuspendAsync();
            SynchronizeHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(helperHost.Helpers);
            await SetConnectionHealthFromReportAsync(report);
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
            var report = await helperHost.SetEnabledAsync(helperId, item.Checked);
            SynchronizeHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(helperHost.Helpers);
            await SetConnectionHealthFromReportAsync(report);
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

    private void StatusDetailsClicked(object? sender, EventArgs eventArgs)
    {
        MessageBox.Show(
            $"{currentStatus.Label}\n{currentStatus.WindowSummary}\n\n{currentStatus.DiagnosticText}",
            "Codex Wingman status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenHelpersFolderClicked(object? sender, EventArgs eventArgs)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", userHelpersRoot) { UseShellExecute = true });
    }

    private static void AboutClicked(object? sender, EventArgs eventArgs)
    {
        var version = typeof(TrayApplicationContext).Assembly.GetName().Version ?? new Version(1, 0, 0);
        MessageBox.Show(
            $"CodexWingman v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}\n\nUnofficial Windows tray companion for the Codex desktop app.",
            "About CodexWingman",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
            await helperHost.ReloadAsync();
            RebuildHelpersMenu();
            refreshTimer.Interval = HelperRefreshSchedule.IntervalMilliseconds(helperHost.Helpers);
            var report = await helperHost.ReconcileAsync();
            SynchronizeHelpersMenu();
            await SetConnectionHealthFromReportAsync(report);
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
        SetBusy(true, "Cleaning every Codex window before exit...");
        try { await helperHost.RemoveAllAsync(); } catch { }
        tray.Visible = false;
        ExitThread();
    }

    private void SetBusy(bool value, string? text = null)
    {
        busy = value;
        injectItem.Enabled = !value;
        repairItem.Enabled = !value;
        openWindowItem.Enabled = !value;
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
        operationDeadline.CancelAfter(TimeSpan.FromSeconds(60));
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
            SetStatus(new TrayStatus(health.Label, diagnosticOverride ?? health.DiagnosticText, health.WindowSummary, health.IconState));
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[CodexWingman Status] {error}");
            SetStatus(new TrayStatus(
                "Status: Needs attention",
                "Wingman could not finish checking Codex.\n\nWhat to do: Choose Repair Codex and Helpers.",
                lastReport is { TargetsDiscovered: > 0 } report
                    ? ConnectionHealthPolicy.FormatWindowSummary(
                        report.TargetsDiscovered,
                        CodexLauncher.GetOpenWindowCount())
                    : "Window status unavailable",
                TrayIconState.Attention));
        }
    }

    private async Task SetConnectionErrorStatusAsync(string diagnosticText)
    {
        Debug.WriteLine($"[CodexWingman Status] {diagnosticText}");
        try
        {
            var state = await CodexLauncher.GetHookStatusAsync(
                runtimeStatePath,
                configuredPortOverride);
            var health = ConnectionHealthPolicy.Evaluate(state, lastReport, helperHost.IsSuspended);
            if (health.State == ConnectionHealthState.CodexClosed)
                SetStatus(new TrayStatus(health.Label, health.DiagnosticText, health.WindowSummary, health.IconState));
            else
                SetStatus(new TrayStatus(
                    "Status: Needs attention",
                    "Wingman could not complete the last operation.\n\nWhat to do: Choose Repair Codex and Helpers. If the problem remains, reload Helpers.",
                    health.WindowSummary,
                    TrayIconState.Attention));
        }
        catch (Exception probeError)
        {
            Debug.WriteLine($"[CodexWingman Status] Status check failed: {probeError}");
            SetStatus(new TrayStatus(
                "Status: Needs attention",
                "Wingman could not check Codex.\n\nWhat to do: Choose Repair Codex and Helpers.",
                "Window status unavailable",
                TrayIconState.Attention));
        }
    }

    private static bool ConfirmCodexRestart()
    {
        var result = MessageBox.Show(
            "Codex is already running without a verified helper endpoint. Wingman must close all Codex windows and relaunch Codex with hooks using your normal profile. Save any work first. Continue?",
            "Repair Codex and Helpers",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private void SetStatus(TrayStatus status)
    {
        currentStatus = status;
        statusLabel.Text = status.Label;
        windowStatusLabel.Text = status.WindowSummary;
        statusLabel.ToolTipText = status.DiagnosticText;
        windowStatusLabel.ToolTipText = status.DiagnosticText;
        tray.Icon = trayIcons.For(status.IconState);
        tray.Text = TrayIconPresentation.TextFor(status.IconState);
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
        }
        base.Dispose(disposing);
    }

}
