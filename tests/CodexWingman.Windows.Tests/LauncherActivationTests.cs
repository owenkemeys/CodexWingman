using System.Collections.Concurrent;
using CodexWingman;
using CodexWingman.Core;

/// <summary>
/// Real named Windows events, signalled the way a second Wingman signals the running one. With only Grok Bot
/// ticked, the plain Wingman entry clicked while Wingman relaunches Grok Bot opens no Codex and no second relaunch.
/// </summary>
internal static class LauncherActivationTests
{
    public static void Run()
    {
        var prefix = "CodexWingman.Tests.Activate." + Guid.NewGuid().ToString("N");
        using var general = new EventWaitHandle(false, EventResetMode.AutoReset, prefix);
        using var codex = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + WingmanLaunchOptions.IdentitySuffix(WingmanLaunchTarget.Codex));
        using var grok = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + WingmanLaunchOptions.IdentitySuffix(WingmanLaunchTarget.GrokBot));
        var ui = new BlockingCollection<Action>();
        var busy = false;
        var hooked = false;
        var relaunches = 0;
        var generalReceived = false;
        var opened = new List<WingmanLaunchTarget>();
        var sources = new List<LaunchSource>();
        // Only Grok Bot is ticked Open when Wingman starts.
        var onlyGrok = HelperSettings.Default
            .WithOpenOnLaunch(WingmanLaunchTarget.Codex, false)
            .WithOpenOnLaunch(WingmanLaunchTarget.GrokBot, true);

        void Pump(Func<bool> until, string name)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!until())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(name);
                if (ui.TryTake(out var work, 50)) work();
            }
        }

        void SignalAsSecondWingman(string name)
        {
            using var selected = EventWaitHandle.OpenExisting(name);
            selected.Set();
        }

        var queue = new LauncherActivationQueue(() => busy, () => onlyGrok, (app, source) =>
        {
            opened.Add(app);
            sources.Add(source);
            if (app == WingmanLaunchTarget.GrokBot && !hooked)
            {
                busy = true;
                relaunches++;
                // While Grok Bot is being closed and reopened, the user clicks the plain Wingman entry.
                SignalAsSecondWingman(prefix);
                Pump(() => generalReceived, "general activation reaches Wingman during the Grok Bot relaunch");
                hooked = true;
                busy = false;
            }
            return Task.CompletedTask;
        });
        using var router = new LauncherActivationRouter(ui.Add, target =>
        {
            if (target == WingmanLaunchTarget.Preferences) generalReceived = true;
            queue.Enqueue(target);
        });
        router.Register(general, WingmanLaunchTarget.Preferences);
        router.Register(codex, WingmanLaunchTarget.Codex);
        router.Register(grok, WingmanLaunchTarget.GrokBot);

        SignalAsSecondWingman(prefix + WingmanLaunchOptions.IdentitySuffix(WingmanLaunchTarget.GrokBot));
        Pump(() => opened.Count >= 1 && !busy, "Grok Bot launcher reaches Wingman");
        Thread.Sleep(500);
        while (ui.TryTake(out var late)) late();
        queue.DrainAsync().GetAwaiter().GetResult();
        Equal(0, opened.Count(app => app == WingmanLaunchTarget.Codex), "no-app launch during the Grok Bot relaunch opens no Codex");
        Equal(1, relaunches, "Grok Bot relaunched once");
        Equal(1, opened.Count(app => app == WingmanLaunchTarget.GrokBot), "Grok Bot handled once");

        generalReceived = false;
        SignalAsSecondWingman(prefix);
        Pump(() => generalReceived && opened.Count == 2, "a later no-app launch reaches Wingman");
        Equal(WingmanLaunchTarget.GrokBot, opened[1], "a later no-app launch opens the ticked app");
        Equal(1, relaunches, "Grok Bot already connected: no second relaunch");
        Equal(LaunchSource.AppShortcut, sources[0], "the Grok Bot launcher opens as the launcher");
        Equal(LaunchSource.PlainEntry, sources[1], "the general event opens as a plain launch (bring forward, never a new window)");

        SignalAsSecondWingman(prefix + WingmanLaunchOptions.IdentitySuffix(WingmanLaunchTarget.Codex));
        Pump(() => opened.Count == 3, "Codex launcher reaches Wingman");
        Equal(WingmanLaunchTarget.Codex, opened[2], "the Codex launcher still opens Codex");
        Equal(LaunchSource.AppShortcut, sources[2], "the Codex launcher keeps its own behaviour");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}
