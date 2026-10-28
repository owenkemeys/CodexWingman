namespace CodexWingman.Core;

public static class LauncherActivationPolicy
{
    /// <summary>Order in which a launch that names no app opens the ticked apps (same as Wingman startup).</summary>
    public static IReadOnlyList<WingmanLaunchTarget> Apps { get; } =
        [WingmanLaunchTarget.T3Web, WingmanLaunchTarget.GrokBot, WingmanLaunchTarget.T3Code, WingmanLaunchTarget.Codex];

    /// <summary>
    /// The apps one launch opens, used by Wingman startup and by a launch that reaches an already running Wingman.
    /// A launch that names an app opens that app only. A launch that names no app (the general Wingman entry)
    /// opens exactly the apps whose Open when Wingman starts is ticked, and nothing when none is. No default
    /// branch: an unknown target is a programming error, never an app.
    /// </summary>
    public static IReadOnlyList<WingmanLaunchTarget> AppsFor(WingmanLaunchTarget target, HelperSettings settings) => target switch
    {
        WingmanLaunchTarget.Preferences => Apps.Where(settings.OpensOnLaunch).ToArray(),
        WingmanLaunchTarget.Codex or WingmanLaunchTarget.T3Code or WingmanLaunchTarget.T3Web or WingmanLaunchTarget.GrokBot => [target],
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown launch target."),
    };
}

/// <summary>
/// Launches that reach the running Wingman, in arrival order. Opening an app waits while Wingman is busy (one
/// operation at a time). An app already being opened or already waiting is not queued again, so a second
/// click during a relaunch can't start a second relaunch.
/// </summary>
public sealed class LauncherActivationQueue(
    Func<bool> blocked,
    Func<HelperSettings> settings,
    Func<WingmanLaunchTarget, Task> open)
{
    private readonly List<WingmanLaunchTarget> pending = [];
    private WingmanLaunchTarget? opening;
    private bool draining;

    public int Pending => pending.Count;

    public void Enqueue(WingmanLaunchTarget target)
    {
        var apps = LauncherActivationPolicy.AppsFor(target, settings());
        WingmanLog.Write("activation", ("target", target), ("apps", apps.Count == 0 ? "none" : string.Join("+", apps)));
        foreach (var app in apps)
        {
            if (app == opening || pending.Contains(app))
            {
                WingmanLog.Write("activation-skip", ("app", app), ("reason", app == opening ? "opening" : "queued"));
                continue;
            }
            pending.Add(app);
        }
        _ = DrainAsync();
    }

    public async Task DrainAsync()
    {
        if (draining || blocked()) return;
        draining = true;
        try
        {
            while (pending.Count > 0 && !blocked())
            {
                var app = pending[0];
                pending.RemoveAt(0);
                opening = app;
                WingmanLog.Write("activation-open", ("app", app));
                try { await open(app); }
                finally { opening = null; }
            }
        }
        finally { draining = false; }
    }
}
