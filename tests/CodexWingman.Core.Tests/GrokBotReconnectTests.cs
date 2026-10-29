using CodexWingman.Core;

/// <summary>
/// Grok Bot's self-update (its installer reopens it with no debugging port): Wingman offers one click to reconnect and
/// never closes or reopens Grok Bot by itself.
/// </summary>
internal static class GrokBotReconnectTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Present = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Away = TimeSpan.FromMinutes(20);
    private const int First = 100, Second = 200, Third = 300;

    public static void Run()
    {
        UpdateWhileAwayPromptsOnceOnReturn();
        NeverConnectedNeverPrompts();
        OwnRelaunchWithThePortDoesNotPrompt();
        ClosedIsNotAnUpdate();
        PromptOncePerProcess();
        ReconnectRearms();
        PromptAppearsWithinSecondsOfThePortGoing();
        TitleSaysUpdatedOnlyWithProof();
        ClickCountsOnlyForThisPromptAndProcess();
        BusyClickIsKeptThenRechecked();
        CommandLineSplitting();
        Console.WriteLine("Grok Bot reconnect tests passed");
    }

    private sealed class Facts(string? version = "1.0.0", Func<int, string?>? commandLine = null) : IGrokBotFacts
    {
        public string? Version = version;
        public int VersionReads;
        public string? CommandLine(int processId) => commandLine?.Invoke(processId);
        public string? ExecutableVersion() { VersionReads++; return Version; }
    }

    private static GrokBotReconnectOffer?[] Play(GrokBotReconnectPolicy policy, IGrokBotFacts facts,
        params (double Seconds, int? Pid, bool Connected, TimeSpan Idle)[] ticks) =>
        ticks.Select(tick => policy.Observe(new(tick.Pid, tick.Connected, tick.Idle), T0.AddSeconds(tick.Seconds), facts)).ToArray();

    private static GrokBotReconnectOffer?[] Play(GrokBotReconnectPolicy policy, params (double Seconds, int? Pid, bool Connected, TimeSpan Idle)[] ticks) =>
        Play(policy, new Facts(), ticks);

    private static int Prompts(IEnumerable<GrokBotReconnectOffer?> offers) => offers.Count(offer => offer is not null);

    /// <summary>Connected, Grok Bot updates while the user is away, its installer reopens it unconnected, the user returns.</summary>
    private static void UpdateWhileAwayPromptsOnceOnReturn()
    {
        var policy = new GrokBotReconnectPolicy();
        var offers = Play(policy,
            (0, First, true, Away),        // connected by Wingman
            (2, null, false, Away),        // Grok Bot quits to install
            (20, Second, false, Away),     // installer reopened it: no port
            (40, Second, false, Away),     // still away: nobody to see a prompt
            (600, Second, false, Present)); // the user is back
        Equal(true, offers[^1] is not null, "prompt when the user returns to an unconnected Grok Bot");
        Equal(Second, offers[^1]!.ProcessId, "the prompt is tied to the reopened process");
        Equal(1, Prompts(offers), "exactly one prompt");
        Equal(0, Prompts(Play(policy, (602, Second, false, Present), (900, Second, false, Present))), "not repeated for the same Grok Bot");
    }

    private static void NeverConnectedNeverPrompts()
    {
        // Grok Bot was already running unconnected when Wingman started (left as it is, with no question).
        var offers = Play(new GrokBotReconnectPolicy(), (0, First, false, Present), (30, First, false, Present), (300, First, false, Present));
        Equal(0, Prompts(offers), "no prompt unless Wingman had Grok Bot connected in this session");
    }

    private static void OwnRelaunchWithThePortDoesNotPrompt()
    {
        // Grok Bot's own relaunch keeps its command line, so the port listens again within a couple of seconds.
        var offers = Play(new GrokBotReconnectPolicy(),
            (0, First, true, Present), (2, null, false, Present), (4, Second, false, Present), (6, Second, true, Present));
        Equal(0, Prompts(offers), "a window that connects within the settle time is not prompted");
    }

    private static void ClosedIsNotAnUpdate()
    {
        var offers = Play(new GrokBotReconnectPolicy(), (0, First, true, Present), (2, null, false, Present), (60, null, false, Present));
        Equal(0, Prompts(offers), "Grok Bot closed: no prompt");
    }

    private static void PromptOncePerProcess()
    {
        var offers = Play(new GrokBotReconnectPolicy(),
            (0, First, true, Present), (2, Second, false, Present), (6, Second, false, Present), (30, Second, false, Present),
            (40, null, false, Present), (50, Third, false, Present), (54, Third, false, Present));
        Equal(2, Prompts(offers), "one prompt for each unconnected Grok Bot process, never a stream");
        Equal(true, offers[2] is not null, "first prompt after the settle time");
    }

    private static void ReconnectRearms()
    {
        var offers = Play(new GrokBotReconnectPolicy(),
            (0, First, true, Present), (2, Second, false, Present), (6, Second, false, Present), // prompted
            (30, Third, true, Present),                                                        // clicked: reconnected
            (5000, null, false, Away), (5020, 400, false, Away), (6000, 400, false, Present));  // next update
        Equal(2, Prompts(offers), "after a reconnect, the next update prompts again");
    }

    /// <summary>
    /// The tray's own check runs every CheckInterval (not on the up-to-60-second helper refresh), so with the user present
    /// the prompt lands within Settle plus one check of the unconnected window appearing.
    /// </summary>
    private static void PromptAppearsWithinSecondsOfThePortGoing()
    {
        Equal(true, GrokBotReconnectPolicy.CheckInterval <= TimeSpan.FromSeconds(2), "fast check interval");
        var policy = new GrokBotReconnectPolicy();
        var step = GrokBotReconnectPolicy.CheckInterval.TotalSeconds;
        var ticks = new List<(double, int?, bool, TimeSpan)> { (0, First, true, Present) };
        for (var seconds = step; seconds <= 30; seconds += step) ticks.Add((seconds, Second, false, Present));
        var offers = Play(policy, ticks.ToArray());
        var shown = offers.Select((offer, index) => (offer, index)).First(pair => pair.offer is not null);
        var after = (shown.offer!.ShownAt - T0).TotalSeconds - step; // the window first seen unconnected at one step
        Equal(true, after <= (GrokBotReconnectPolicy.Settle + GrokBotReconnectPolicy.CheckInterval).TotalSeconds,
            $"prompt {after}s after the unconnected window appeared");
    }

    private static void TitleSaysUpdatedOnlyWithProof()
    {
        GrokBotReconnectOffer? OfferWith(Facts facts, Action? betweenConnectedAndPrompt = null)
        {
            var policy = new GrokBotReconnectPolicy();
            policy.Observe(new(First, true, Present), T0, facts);
            betweenConnectedAndPrompt?.Invoke();
            policy.Observe(new(Second, false, Present), T0.AddSeconds(2), facts);
            return policy.Observe(new(Second, false, Present), T0.AddSeconds(10), facts);
        }
        var plain = OfferWith(new Facts("1.0.0", _ => "\"C:\\Apps\\Grok Bot\\Grok Bot.exe\""));
        Equal(false, plain!.Updated, "no --updated and same version: not proven");
        Equal(GrokBotReconnectPolicy.ReopenedTitle, plain.Title, "neutral title without proof");

        var flagged = OfferWith(new Facts("1.0.0", _ => "\"C:\\Apps\\Grok Bot\\Grok Bot.exe\" --updated"));
        Equal(true, flagged!.Updated, "--updated on the new process proves an update");
        Equal(GrokBotReconnectPolicy.UpdatedTitle, flagged.Title, "updated title with proof");

        var facts = new Facts("1.0.0");
        var versioned = OfferWith(facts, () => facts.Version = "1.1.0");
        Equal(true, versioned!.Updated, "a changed Grok Bot.exe version proves an update");

        var unknown = new Facts(null);
        Equal(false, OfferWith(unknown)!.Updated, "unknown versions prove nothing");

        var reads = new Facts("1.0.0");
        var policy = new GrokBotReconnectPolicy();
        for (var second = 0; second < 20; second += 2) policy.Observe(new(First, true, Present), T0.AddSeconds(second), reads);
        Equal(1, reads.VersionReads, "the version is read once per connected process, not every check");
        Equal(false, GrokBotReconnectPolicy.UpdatedTitle == GrokBotReconnectPolicy.ReopenedTitle, "two distinct titles");
    }

    private static void ClickCountsOnlyForThisPromptAndProcess()
    {
        var offer = new GrokBotReconnectOffer(Second, false, T0);
        var same = new GrokBotObservation(Second, false, Present);
        Equal(GrokBotReconnectAction.ReconnectNow, GrokBotReconnectPolicy.Validate(offer, false, T0.AddSeconds(5), same), "click on the live prompt");
        Equal(GrokBotReconnectAction.AskFirst, GrokBotReconnectPolicy.Validate(offer, true, T0.AddSeconds(5), same), "prompt closed or replaced");
        Equal(GrokBotReconnectAction.AskFirst,
            GrokBotReconnectPolicy.Validate(offer, false, T0 + GrokBotReconnectPolicy.PromptLifetime + TimeSpan.FromSeconds(1), same), "click after the lifetime");
        Equal(GrokBotReconnectAction.AskFirst, GrokBotReconnectPolicy.Validate(offer, false, T0.AddSeconds(5), new(Third, false, Present)),
            "a different Grok Bot process since the prompt");
        Equal(GrokBotReconnectAction.AskFirst, GrokBotReconnectPolicy.Validate(offer, false, T0.AddSeconds(5), new(null, false, Present)),
            "Grok Bot closed since the prompt");
        Equal(GrokBotReconnectAction.AskFirst, GrokBotReconnectPolicy.Validate(null, false, T0.AddSeconds(5), same), "no prompt on record");
        Equal(GrokBotReconnectAction.AlreadyConnected, GrokBotReconnectPolicy.Validate(offer, false, T0.AddSeconds(5), new(Second, true, Present)),
            "already connected again");
    }

    private static void BusyClickIsKeptThenRechecked()
    {
        var offer = new GrokBotReconnectOffer(Second, true, T0);
        var same = new GrokBotObservation(Second, false, Present);
        var clicks = new GrokBotReconnectClicks();
        Equal(null, clicks.Drain(same), "nothing kept: nothing to drain");
        Equal(GrokBotReconnectAction.ReconnectNow, clicks.Click(offer, false, T0.AddSeconds(3), same, busy: false), "free: act now");
        Equal(false, clicks.Pending, "nothing kept when acted on");

        Equal(GrokBotReconnectAction.WaitUntilFree, clicks.Click(offer, false, T0.AddSeconds(3), same, busy: true), "busy: kept");
        Equal(true, clicks.Pending, "the busy click is kept, not dropped");
        // Busy for longer than the prompt's lifetime: the consent was valid when given and holds for the same process.
        Equal(GrokBotReconnectAction.ReconnectNow, clicks.Drain(same), "drained once free: reconnect");
        Equal(false, clicks.Pending, "drained once");

        clicks.Click(offer, false, T0.AddSeconds(3), same, busy: true);
        Equal(GrokBotReconnectAction.AskFirst, clicks.Drain(new(Third, false, Present)), "another Grok Bot by the time Wingman is free: ask");
        clicks.Click(offer, false, T0.AddSeconds(3), same, busy: true);
        Equal(GrokBotReconnectAction.AlreadyConnected, clicks.Drain(new(Second, true, Present)), "connected meanwhile: nothing to do");

        Equal(GrokBotReconnectAction.WaitUntilFree, clicks.Click(offer, true, T0.AddSeconds(3), same, busy: true), "stale click while busy: kept too");
        Equal(GrokBotReconnectAction.AskFirst, clicks.Drain(same), "a stale click becomes the Yes/No question, never a silent relaunch");
    }

    private static void CommandLineSplitting()
    {
        Equal(true, GrokBotReconnectPolicy.HasUpdatedFlag("\"C:\\Program Files\\Grok Bot\\Grok Bot.exe\" --updated"), "quoted exe then flag");
        Equal(true, GrokBotReconnectPolicy.HasUpdatedFlag("C:\\GrokBot\\GrokBot.exe --force-run --updated"), "flag among others");
        Equal(false, GrokBotReconnectPolicy.HasUpdatedFlag("\"C:\\x --updated\\Grok Bot.exe\""), "inside the quoted exe path");
        Equal(false, GrokBotReconnectPolicy.HasUpdatedFlag("\"C:\\Grok Bot.exe\" --updated-later"), "a longer flag");
        Equal(false, GrokBotReconnectPolicy.HasUpdatedFlag(null), "unreadable command line");
        Equal(false, GrokBotReconnectPolicy.HasUpdatedFlag("--updated"), "the program name itself is not an argument");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}
