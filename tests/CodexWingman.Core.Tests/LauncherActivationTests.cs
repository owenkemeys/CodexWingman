using CodexWingman.Core;

/// <summary>
/// Launcher activation (an app's launcher opens that app; a launch naming no app opens the ticked apps, the same
/// set as at startup), the settings fail-safe and the rolling log.
/// </summary>
internal static class LauncherActivationTests
{
    public static async Task RunAsync()
    {
        ActivationPolicy();
        await GeneralActivationDuringGrokRelaunchAsync();
        await AppActivationsWaitAndKeepOrderAsync();
        SettingsFailSafe();
        RollingLog();
    }

    private static readonly WingmanLaunchTarget[] AllApps =
        [WingmanLaunchTarget.Codex, WingmanLaunchTarget.T3Code, WingmanLaunchTarget.T3Web, WingmanLaunchTarget.GrokBot];

    private static HelperSettings Ticked(params WingmanLaunchTarget[] apps) =>
        AllApps.Aggregate(HelperSettings.Default, (settings, app) => settings.WithOpenOnLaunch(app, apps.Contains(app)));

    private static string AppsFor(WingmanLaunchTarget target, HelperSettings settings) =>
        string.Join(",", LauncherActivationPolicy.AppsFor(target, settings));

    private static void ActivationPolicy()
    {
        var onlyGrok = Ticked(WingmanLaunchTarget.GrokBot);
        Equal("GrokBot", AppsFor(WingmanLaunchTarget.Preferences, onlyGrok), "no-app launch opens only the ticked app");
        Equal("", AppsFor(WingmanLaunchTarget.Preferences, Ticked()), "nothing ticked: a no-app launch opens nothing");
        Equal("T3Web,GrokBot,T3Code,Codex", AppsFor(WingmanLaunchTarget.Preferences, Ticked(AllApps)), "every ticked app, startup order");
        Equal("Codex", AppsFor(WingmanLaunchTarget.Preferences, HelperSettings.Default), "first-run default ticks Codex only");
        foreach (var app in AllApps)
        {
            Equal(app.ToString(), AppsFor(app, onlyGrok), $"{app} launcher opens {app} only");
            Equal(app.ToString(), AppsFor(app, Ticked()), $"{app} launcher ignores the ticks");
        }
        foreach (var target in Enum.GetValues<WingmanLaunchTarget>())
            foreach (var app in AllApps)
                Equal(LauncherActivationPolicy.AppsFor(target, onlyGrok).Contains(app),
                    new WingmanLaunchOptions(target).OpenOnStartup(app, onlyGrok),
                    $"startup and a later launch share one policy ({target}, {app})");
        Throws<ArgumentOutOfRangeException>(() => LauncherActivationPolicy.AppsFor((WingmanLaunchTarget)99, Ticked(AllApps)),
            "an unknown target never falls through to an app");
    }

    /// <summary>
    /// The reported case: only Grok Bot ticked; the plain Wingman entry clicked while Wingman relaunches Grok Bot.
    /// Codex never opens and Grok Bot is relaunched once.
    /// </summary>
    private static async Task GeneralActivationDuringGrokRelaunchAsync()
    {
        var busy = false;
        var hooked = false;
        var opened = new List<WingmanLaunchTarget>();
        var relaunches = 0;
        var relaunch = new TaskCompletionSource();
        var queue = new LauncherActivationQueue(() => busy, () => Ticked(WingmanLaunchTarget.GrokBot), async app =>
        {
            opened.Add(app);
            if (app != WingmanLaunchTarget.GrokBot || hooked) return;
            busy = true;
            relaunches++;
            await relaunch.Task;
            hooked = true;
            busy = false;
        });
        queue.Enqueue(WingmanLaunchTarget.GrokBot);
        Equal(true, busy, "Grok Bot relaunch in progress");
        queue.Enqueue(WingmanLaunchTarget.Preferences);
        Equal(0, queue.Pending, "Grok Bot, already being opened, is not queued again");
        relaunch.SetResult();
        await queue.DrainAsync();
        Equal(0, opened.Count(app => app == WingmanLaunchTarget.Codex), "Codex not opened");
        Equal(1, relaunches, "Grok Bot relaunched once");
        Equal(1, opened.Count(app => app == WingmanLaunchTarget.GrokBot), "Grok Bot handled once");

        queue.Enqueue(WingmanLaunchTarget.Preferences);
        await queue.DrainAsync();
        Equal(1, relaunches, "a later no-app launch with Grok Bot connected brings it forward, no relaunch");
        Equal(0, opened.Count(app => app == WingmanLaunchTarget.Codex), "still no Codex");

        var nothing = new List<WingmanLaunchTarget>();
        var none = new LauncherActivationQueue(() => false, () => Ticked(), app => { nothing.Add(app); return Task.CompletedTask; });
        none.Enqueue(WingmanLaunchTarget.Preferences);
        await none.DrainAsync();
        Equal(0, nothing.Count, "nothing ticked: a no-app launch opens nothing");
    }

    private static async Task AppActivationsWaitAndKeepOrderAsync()
    {
        var blocked = true;
        var opened = new List<WingmanLaunchTarget>();
        var queue = new LauncherActivationQueue(() => blocked, () => Ticked(WingmanLaunchTarget.Codex, WingmanLaunchTarget.T3Code),
            app => { opened.Add(app); return Task.CompletedTask; });
        queue.Enqueue(WingmanLaunchTarget.GrokBot);
        queue.Enqueue(WingmanLaunchTarget.Preferences);
        queue.Enqueue(WingmanLaunchTarget.Codex);
        Equal(3, queue.Pending, "openings wait while Wingman is busy or starting; a waiting app is not queued twice");
        blocked = false;
        await queue.DrainAsync();
        Equal("GrokBot,T3Code,Codex", string.Join(",", opened), "openings run in arrival order");
    }

    private static void SettingsFailSafe()
    {
        var root = Path.Combine(Path.GetTempPath(), "wingman-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "settings.json");
            Equal(true, HelperSettingsStore.Load(path).OpenCodexOnLaunch, "first run (no file) keeps the Codex default");

            File.WriteAllText(path, "{ not json");
            var unreadable = HelperSettingsStore.Load(path);
            foreach (var app in new[] { WingmanLaunchTarget.Codex, WingmanLaunchTarget.T3Code, WingmanLaunchTarget.T3Web, WingmanLaunchTarget.GrokBot })
                Equal(false, unreadable.OpensOnLaunch(app), $"unreadable settings open nothing ({app})");
            Equal(false, new WingmanLaunchOptions().OpenOnStartup(WingmanLaunchTarget.Codex, unreadable),
                "unreadable settings never open Codex at startup");

            var chosen = HelperSettings.Default with { OpenCodexOnLaunch = false, OpenGrokBotOnLaunch = true };
            HelperSettingsStore.Save(path, chosen);
            Equal(false, HelperSettingsStore.Load(path).OpenCodexOnLaunch, "saved choice reads back");
            File.WriteAllText(path, "{ \"openCodexOnLaunch\": ");
            var lastGood = HelperSettingsStore.Load(path);
            Equal(false, lastGood.OpenCodexOnLaunch, "a later read failure keeps the last good Codex choice");
            Equal(true, lastGood.OpenGrokBotOnLaunch, "a later read failure keeps the last good Grok Bot choice");

            var other = Path.Combine(root, "other.json");
            File.WriteAllText(other, "{\"usageDialsEnabled\":true,\"additionalSessionRoots\":[],\"openCodexOnLaunch\":true}");
            Equal(true, HelperSettingsStore.Load(other).OpenCodexOnLaunch, "readable Codex choice is used");
            File.WriteAllText(other, "");
            Equal(true, HelperSettingsStore.Load(other).OpenCodexOnLaunch, "last good is per file, and keeps a chosen On");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void RollingLog()
    {
        var root = Path.Combine(Path.GetTempPath(), "wingman-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var line = WingmanLog.Format(new DateTimeOffset(2026, 10, 28, 16, 12, 51, TimeSpan.FromHours(4)), "activation",
                ("target", WingmanLaunchTarget.Preferences), ("action", "show\nwingman"), ("pid", 12), ("ok", true));
            Equal("2026-10-28T16:12:51.000+04:00 activation target=Preferences action=show_wingman pid=12 ok=true\n", line, "one plain line");
            WingmanLog.Configure(root);
            for (var index = 0; index < 6000; index++) WingmanLog.Write("activation", ("target", WingmanLaunchTarget.GrokBot), ("n", index));
            var current = new FileInfo(Path.Combine(root, WingmanLog.FileName));
            var rolled = new FileInfo(Path.Combine(root, WingmanLog.RolledFileName));
            Equal(true, current.Exists && rolled.Exists, "log rolls once");
            Equal(true, current.Length <= WingmanLog.MaxBytes && rolled.Length <= WingmanLog.MaxBytes, "each log file stays under the cap");
            Equal(2, Directory.GetFiles(root).Length, "two files at most");
        }
        finally
        {
            WingmanLog.Configure(null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }
}
