using System.Diagnostics;
using CodexWingman.Core;
using CodexWingman.Core.Helpers;

namespace CodexWingman;

/// <summary>
/// One extra Helper target in the tray (T3 web or Grok Bot): its own Helper host on its own local
/// debugging port, a status row, and an "Open" action. Native Codex and T3 desktop are not involved.
/// </summary>
internal sealed class AppTargetController
{
    private readonly Func<int?> port;
    private readonly Func<int?> visibleWindows;
    private readonly AppTargetConnectionProbe probe;

    public AppTargetController(
        string name,
        WingmanLaunchTarget target,
        string helpersRoot,
        string settingsPath,
        ICodexTargetSource targetSource,
        ICdpEvaluator evaluator,
        string appId,
        Func<int?> port,
        Func<int?> visibleWindows,
        Func<IEnumerable<string>, HelperBackend>? backendFactory = null)
    {
        Name = name;
        Target = target;
        this.port = port;
        this.visibleWindows = visibleWindows;
        Host = new HelperHost(helpersRoot, settingsPath, targetSource, evaluator, new HostActionDispatcher(evaluator), [],
            log: message => Debug.WriteLine($"[{name} Helper] {message}"), backendFactory: backendFactory, appId: appId);
        probe = new AppTargetConnectionProbe(targetSource, evaluator);
        Menu = new ToolStripMenuItem($"{name}: Not ready");
        OpenItem = new ToolStripMenuItem($"Open {name} with Wingman");
        DetailsItem = new ToolStripMenuItem("View status details...", null, (_, _) =>
            MessageBox.Show(Status().Details, $"{name} status", MessageBoxButtons.OK, MessageBoxIcon.Information));
        Menu.DropDownItems.AddRange([OpenItem, DetailsItem]);
    }

    public string Name { get; }
    public WingmanLaunchTarget Target { get; }
    public HelperHost Host { get; }
    public ToolStripMenuItem Menu { get; }
    public ToolStripMenuItem OpenItem { get; }
    public ToolStripMenuItem DetailsItem { get; }
    public HelperHostReport? Report { get; private set; }
    public T3CodeConnectionReport? Connections { get; private set; }
    public bool CheckFailed { get; set; }
    public string? Note { get; set; }
    public bool HasEndpoint => port() is not null;

    public async Task ReconcileAsync(Func<Task<HelperHostReport>>? operation = null, CancellationToken cancellationToken = default)
    {
        // Without Wingman's own endpoint for this app, nothing is touched.
        if (operation is null && port() is null)
        {
            Report = null;
            Connections = null;
            CheckFailed = false;
            SetMenuStatus();
            return;
        }
        try
        {
            Report = await (operation?.Invoke() ?? Host.ReconcileAsync(cancellationToken));
            Connections = await probe.ProbeAsync(cancellationToken);
            CheckFailed = false;
        }
        catch (Exception error)
        {
            Debug.WriteLine($"[{Name} Helper] {error}");
            Connections = null;
            CheckFailed = true;
        }
        SetMenuStatus();
    }

    public async Task DrainHostActionsAsync()
    {
        if (port() is null) return;
        try { await Host.DrainHostActionsAsync(); }
        catch (Exception error)
        {
            Debug.WriteLine($"[{Name} Host Action poll] {error.Message}");
            CheckFailed = true;
            SetMenuStatus();
        }
    }

    public T3CodeMenuStatus Status()
    {
        int? windows;
        try { windows = visibleWindows(); }
        catch (Exception error)
        {
            Debug.WriteLine($"[{Name} window discovery] {error}");
            windows = null;
        }
        var status = AppTargetMenuPolicy.Evaluate(Name, windows, port() is not null, Report, Connections, CheckFailed || windows is null,
            Host.IsSuspended, Host.Helpers.Any(helper => helper.Enabled));
        return Note is null ? status : status with { Details = status.Details + "\n\n" + Note };
    }

    public void SetMenuStatus()
    {
        var status = Status();
        Menu.Text = status.Label;
        Menu.ToolTipText = status.Details;
    }

    public void SetBusy(bool value) => OpenItem.Enabled = !value;
}
