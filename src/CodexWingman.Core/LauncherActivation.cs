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

/// <summary>Where one app opening came from.</summary>
public enum LaunchSource
{
    /// <summary>The app's own launcher: opens that app as its shortcut always has (for Codex, another window).</summary>
    AppShortcut,
    /// <summary>The plain Wingman entry (no app named): idempotent, see <see cref="PlainLaunchPolicy"/>.</summary>
    PlainEntry,
}

/// <summary>What an app is doing when a plain launch reaches it.</summary>
public enum AppState
{
    NotRunning,
    /// <summary>Running without Wingman's connection (for example after the app updated itself).</summary>
    RunningNotConnected,
    /// <summary>Connected, but no window is open (running in the background only).</summary>
    ConnectedNoWindow,
    ConnectedWithWindow,
}

public enum PlainLaunchStep
{
    /// <summary>Start the app (it isn't running).</summary>
    Launch,
    /// <summary>Bring the existing window forward; never a new window.</summary>
    BringForward,
    /// <summary>Connected but windowless: show its one window.</summary>
    ShowFirstWindow,
    /// <summary>Running without Wingman's connection: leave it alone, no relaunch and no question.</summary>
    LeaveAsIs,
}

/// <summary>
/// A plain launch is idempotent per app: it only starts apps that aren't running and never opens a second window.
/// An app's own launcher keeps its behaviour (it may relaunch after asking, and Codex's opens another window).
/// </summary>
public static class PlainLaunchPolicy
{
    public static PlainLaunchStep Decide(AppState presence) => presence switch
    {
        AppState.NotRunning => PlainLaunchStep.Launch,
        AppState.RunningNotConnected => PlainLaunchStep.LeaveAsIs,
        AppState.ConnectedNoWindow => PlainLaunchStep.ShowFirstWindow,
        AppState.ConnectedWithWindow => PlainLaunchStep.BringForward,
        _ => throw new ArgumentOutOfRangeException(nameof(presence), presence, "Unknown app state."),
    };
}

/// <summary>
/// Launches that reach the running Wingman, in arrival order. Opening an app waits while Wingman is busy (one
/// operation at a time). An app already being opened or already waiting is not queued again, so a second
/// click during a relaunch can't start a second relaunch. If an app waits for a plain launch and its own launcher
/// is clicked, it waits once, as the launcher's opening.
/// </summary>
public sealed class LauncherActivationQueue(
    Func<bool> blocked,
    Func<HelperSettings> settings,
    Func<WingmanLaunchTarget, LaunchSource, Task> open)
{
    private readonly List<(WingmanLaunchTarget App, LaunchSource Source)> pending = [];
    private WingmanLaunchTarget? opening;
    private bool draining;

    public int Pending => pending.Count;

    public void Enqueue(WingmanLaunchTarget target)
    {
        var apps = LauncherActivationPolicy.AppsFor(target, settings());
        var source = target == WingmanLaunchTarget.Preferences ? LaunchSource.PlainEntry : LaunchSource.AppShortcut;
        WingmanLog.Write("activation", ("target", target), ("apps", apps.Count == 0 ? "none" : string.Join("+", apps)));
        foreach (var app in apps)
        {
            var waiting = pending.FindIndex(item => item.App == app);
            if (waiting >= 0 && source == LaunchSource.AppShortcut && pending[waiting].Source == LaunchSource.PlainEntry)
            {
                pending[waiting] = (app, source);
                WingmanLog.Write("activation-skip", ("app", app), ("reason", "queued-now-app-shortcut"));
                continue;
            }
            if (app == opening || waiting >= 0)
            {
                WingmanLog.Write("activation-skip", ("app", app), ("reason", app == opening ? "opening" : "queued"));
                continue;
            }
            pending.Add((app, source));
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
                var (app, source) = pending[0];
                pending.RemoveAt(0);
                opening = app;
                WingmanLog.Write("activation-open", ("app", app), ("source", source));
                try { await open(app, source); }
                finally { opening = null; }
            }
        }
        finally { draining = false; }
    }
}
