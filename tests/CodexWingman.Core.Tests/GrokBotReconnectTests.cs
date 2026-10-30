using CodexWingman.Core;

/// <summary>
/// Grok Bot's self-update (its installer reopens it with no debugging port): Wingman offers a prompt window to reconnect
/// and never closes or reopens Grok Bot by itself.
/// </summary>
internal static class GrokBotReconnectTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Present = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Away = TimeSpan.FromMinutes(20);
    private const int First = 100, Second = 200, Third = 300, FakeStarted = 900;

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
        ReusedProcessIdIsANewRun();
        PromptIsBoundToItsProcess();
        ReconnectRechecksBeforeActing();
        RelaunchWaitsForTheUsersQuit();
        AfterTheQuitThePromptNeverGivesUpSilently();
        AfterTheQuitWingmanWaitsThenSaysSo();
        NeverStartsDuringAnUpdate();
        AnUpdateTheQuitStartedIsWaitedFor();
        AReconnectAfterAnUpdateFinishesTheJob();
        OpenWithoutTheQuitsProofIsStillGuarded();
        OpenWindowsCloseWaitIsUpdateAware();
        TheAnswerTimeDoesntCount();
        DocCommentsStayOnTheirMember();
        AHandOverIsTheOnlyPromptForItsProcess();
        PromptWindowStaysOutOfTheWay();
        BringingForwardKeepsTheWindowAsItIs();
        TheQuitWaitHoldsTheProcessItself();
        ThePromptFollowsItsProcessAfterAnUninstall();
        AReopenDuringTheWaitStartsNothing();
        NoPromptOverAFullScreenApp();
        ACrashIsNotReopened();
        AnInstallersKillIsAnUpdateNotACrash();
        ACancelledShutdownKeepsThePrompt();
        NothingIsAwaitedBetweenTheBusyCheckAndTheStart();
        TheMainProcessIsGrokBotItself();
        NothingStartsWhileWindowsShutsDown();
        ThePromptClosesAfterItsOwnClick();
        TryAgainAsksForTheCloseAgain();
        AnErrorAfterWingmansStartKeepsThatWingmanOpenedIt();
        AGrokBotWingmanStartedIsNeverOfferedAsReopened();
        AStartReturnsTheGrokBotItStartedOrThrows();
        TheTitleNeverSaysWithoutWingmanWhileTheBodySaysWingmanOpenedIt();
        TheTitleAndTheMessageAgreeInEveryStateThePromptCanShow();
        AGrokBotThatAppearsWhileWingmanIsBusyStartsNothing();
        EveryResultEndsInTheSamePrompt();
        EveryResultTimesEveryStatePicksItsEnding();
        OpenWindowAfterTheYesEndsInThePrompt();
        TheRuntimeFileHasOneWriterAtATime();
        CommandLineSplitting();
        TheQuickCheckRunsOnlyForASetUpGrokBot();
        Console.WriteLine("Grok Bot reconnect tests passed");
    }

    private sealed class Facts(string? version = "1.0.0", Func<int, string?>? commandLine = null) : IGrokBotFacts
    {
        public string? Version = version;
        public int VersionReads;
        public string? CommandLine(int processId) => commandLine?.Invoke(processId);
        public string? ExecutableVersion() { VersionReads++; return Version; }
        public GrokBotStarts Starts { get; } = new();
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
        Equal(GrokBotReconnectPolicy.ReopenedTitle, new GrokBotReconnectPrompt(plain).Title, "neutral title without proof");

        var flagged = OfferWith(new Facts("1.0.0", _ => "\"C:\\Apps\\Grok Bot\\Grok Bot.exe\" --updated"));
        Equal(true, flagged!.Updated, "--updated on the new process proves an update");
        Equal(GrokBotReconnectPolicy.UpdatedTitle, new GrokBotReconnectPrompt(flagged).Title, "updated title with proof");

        var facts = new Facts("1.0.0");
        var versioned = OfferWith(facts, () => facts.Version = "1.1.0");
        Equal(true, versioned!.Updated, "a changed Grok Bot.exe version proves an update");

        var unknown = new Facts(null);
        Equal(false, OfferWith(unknown)!.Updated, "unknown versions prove nothing");

        // A Grok Bot the prompt follows (from the 2-second check or at a run's end) is titled by the same proof a new prompt
        // for it would get: the slot hands every prompt the policy's proof.
        foreach (var (name, make, change) in new (string, Func<Facts>, Action<Facts>?)[]
                 {
                     ("plain", () => new Facts("1.0.0", _ => "\"C:\\Apps\\Grok Bot\\Grok Bot.exe\""), null),
                     ("--updated", () => new Facts("1.0.0", pid => pid == Third ? "\"C:\\Apps\\Grok Bot\\Grok Bot.exe\" --updated" : null), null),
                     ("version changed", () => new Facts("1.0.0"), changed => changed.Version = "1.1.0"),
                 })
        {
            var seen = make();
            var policy2 = new GrokBotReconnectPolicy();
            policy2.Observe(new(First, true, Present), T0, seen);
            change?.Invoke(seen);
            GrokBotPromptSlot Slot() => new(process => policy2.UpdateProven(process, seen));
            var fresh = policy2.UpdateProven(new(Third, null), seen) ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle;
            foreach (var viaCheck in new[] { true, false })
            {
                var followed = Slot().Offer(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0))!;
                followed.Reconnect(busy: false);
                followed.Finished(new(GrokBotReconnectOutcome.UpdateTooLong), viaCheck ? new(null, false, Present) : new(Third, false, Present));
                if (viaCheck) Equal(GrokBotPromptStep.HandOver, followed.Observe(new(Third, false, Present), busy: false), name + ": the check follows the Grok Bot its installer opened");
                Equal((Third, fresh), (followed.Offer.ProcessId, followed.Title), name + (viaCheck ? " (check)" : " (run's end)") + ": titled as a new prompt for it would be");
            }
            var yes = Slot().AfterYes(new(Second, null), null, T0);
            yes.Finished(new(GrokBotReconnectOutcome.Reopened), new(Third, false, Present));
            Equal(fresh, yes.Title, name + ": the Yes prompt follows by the same proof");
        }
        Equal(true, File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"))
            .Contains("grokPrompts = new GrokBotPromptSlot(process => grokReconnect.UpdateProven(process, grokLauncher));", StringComparison.Ordinal),
            "the tray's prompts use the policy's proof, with Grok Bot's real facts");

        var reads = new Facts("1.0.0");
        var policy = new GrokBotReconnectPolicy();
        for (var second = 0; second < 20; second += 2) policy.Observe(new(First, true, Present), T0.AddSeconds(second), reads);
        Equal(1, reads.VersionReads, "the version is read once per connected process, not every check");
        Equal(false, GrokBotReconnectPolicy.UpdatedTitle == GrokBotReconnectPolicy.ReopenedTitle, "two distinct titles");
    }

    /// <summary>A reused process id with a new start time is a new Grok Bot run: it settles again and gets its own prompt.</summary>
    private static void ReusedProcessIdIsANewRun()
    {
        var policy = new GrokBotReconnectPolicy();
        var facts = new Facts();
        var early = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        GrokBotReconnectOffer? At(double seconds, int pid, bool connected, DateTime started) =>
            policy.Observe(new(pid, connected, Present, started), T0.AddSeconds(seconds), facts);
        At(0, First, true, early);
        Equal(true, At(6, Second, false, early) is null && At(10, Second, false, early) is { } offer && offer.Target == new GrokBotProcess(Second, early),
            "first run prompted, bound to its start time");
        var later = early.AddMinutes(5);
        Equal(true, At(12, Second, false, later) is null, "same id, new start time: settles again, no prompt yet");
        Equal(true, At(17, Second, false, later) is { ProcessStarted: var started } && started == later, "and then prompts once for the new run");
        Equal(true, At(30, Second, false, later) is null, "only once");
    }

    /// <summary>
    /// The prompt window is bound to its offer's exact process: it closes when that process exits or is replaced, when Grok
    /// Bot connects, and after Not now; Reconnect acts once, and while Wingman is busy it waits ("Working...") and acts when free.
    /// </summary>
    private static void PromptIsBoundToItsProcess()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started);
        GrokBotObservation Seen(int? pid, DateTime? start = null, bool connected = false) => new(pid, connected, Present, start ?? started);
        const GrokBotPromptStep None = GrokBotPromptStep.None;

        var reused = new GrokBotReconnectPrompt(offer);
        Equal(None, reused.Observe(Seen(Second, started.AddSeconds(1)), busy: false), "same id, other start");
        Equal((GrokBotPromptState.Closed, "gone"), (reused.State, reused.ClosedBecause), "another run of the same id closes it");
        Equal(false, reused.Reconnect(busy: false), "a closed prompt never acts");

        var exited = new GrokBotReconnectPrompt(offer);
        exited.Observe(Seen(null), busy: false);
        Equal(GrokBotPromptState.Closed, exited.State, "its process exited: closed");

        var connected = new GrokBotReconnectPrompt(offer);
        connected.Observe(Seen(Second, connected: true), busy: false);
        Equal((GrokBotPromptState.Closed, "connected"), (connected.State, connected.ClosedBecause), "Grok Bot connected: closed");

        var declined = new GrokBotReconnectPrompt(offer);
        declined.NotNow();
        Equal((GrokBotPromptState.Closed, "not-now"), (declined.State, declined.ClosedBecause), "Not now closes it");

        var stays = new GrokBotReconnectPrompt(offer);
        for (var tick = 0; tick < 1000; tick++) stays.Observe(Seen(Second), busy: false);
        Equal(GrokBotPromptState.Offered, stays.State, "no timeout while its process runs unconnected");
        Equal(true, stays.Reconnect(busy: false), "Reconnect acts at once when free");
        Equal(false, stays.Reconnect(busy: false), "a second press does nothing");

        var busy = new GrokBotReconnectPrompt(offer);
        Equal(false, busy.Reconnect(busy: true), "pressed while busy: not yet");
        Equal(GrokBotPromptState.Working, busy.State, "shows Working…");
        Equal(None, busy.Observe(Seen(Second), busy: true), "still busy: waits");
        Equal(GrokBotPromptStep.Reconnect, busy.Observe(Seen(Second), busy: false), "free: acts");
        Equal(None, busy.Observe(Seen(Second), busy: false), "acts once");
        busy.Finished(new(GrokBotReconnectOutcome.Busy), null);
        Equal(true, busy.State == GrokBotPromptState.Working && busy.Observe(Seen(Second), busy: false) == GrokBotPromptStep.Reconnect,
            "busy again by then: kept, acts when free");

        // Kept while busy for up to a minute on the tray's clock; then the prompt says Wingman was busy, with its button.
        var kept = new GrokBotReconnectPrompt(offer);
        kept.Reconnect(busy: true);
        Equal(None, kept.Observe(Seen(Second), busy: true, T0), "kept: the clock starts");
        Equal((None, GrokBotPromptState.Working), (kept.Observe(Seen(Second), busy: true, T0.AddSeconds(59)), kept.State), "kept for under a minute");
        Equal((None, GrokBotPromptState.CouldNotStart, "Wingman was busy, so Grok Bot is still open.", ("Try again", true, "Not now")),
            (kept.Observe(Seen(Second), busy: true, T0.AddSeconds(60)), kept.State, kept.Problem, kept.Buttons), "busy for a minute, its Grok Bot still open: says so, with Try again");
        Equal((None, GrokBotReconnectPolicy.BusyTooLongText, ("Open Grok Bot", true, "Not now")),
            (kept.Observe(Seen(null), busy: false), kept.Problem, kept.Buttons), "then it closes: Wingman didn't reopen it, with Open Grok Bot");
        var keptGone = new GrokBotReconnectPrompt(offer);
        keptGone.Reconnect(busy: true);
        keptGone.Observe(Seen(Second), busy: true, T0);
        Equal((GrokBotReconnectPolicy.BusyTooLongText, ("Open Grok Bot", true, "Not now")),
            (keptGone.Observe(Seen(null), busy: true, T0.AddSeconds(60)) == None ? keptGone.Problem : null, keptGone.Buttons), "busy for a minute, its Grok Bot gone: Open Grok Bot");
        var freed = new GrokBotReconnectPrompt(offer);
        freed.Reconnect(busy: true);
        freed.Observe(Seen(Second), busy: true, T0);
        Equal(GrokBotPromptStep.Reconnect, freed.Observe(Seen(Second), busy: false, T0.AddSeconds(30)), "free in time: acts");
        freed.Finished(new(GrokBotReconnectOutcome.Busy), null);
        Equal((None, GrokBotPromptState.Working), (freed.Observe(Seen(Second), busy: true, T0.AddSeconds(80)), freed.State), "kept again: a new minute");
        Equal(true, File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"))
            .Contains("prompt.Observe(observation, busy || initializing, DateTimeOffset.UtcNow)", StringComparison.Ordinal), "the tray passes its clock");

        var gone = new GrokBotReconnectPrompt(offer);
        gone.Reconnect(busy: true);
        // Pressed: the run (which re-checks the process and starts nothing) says what became of it; never a silent close.
        Equal(GrokBotPromptStep.Reconnect, gone.Observe(Seen(Third), busy: false), "replaced while waiting to act: the run re-checks");
        gone.Finished(new(GrokBotReconnectOutcome.Replaced), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReplacedText), (gone.State, gone.Problem), "and the prompt says it was replaced (not closed), with Open Grok Bot");
        var replacedNow = new GrokBotReconnectPrompt(offer);
        replacedNow.Reconnect(busy: false);
        replacedNow.Finished(new(GrokBotReconnectOutcome.Replaced), Seen(Third));
        var replacedText = (replacedNow.Offer.ProcessId, replacedNow.Problem, replacedNow.Buttons);
        for (var tick = 0; tick < 5; tick++) Equal(None, replacedNow.Observe(Seen(Third), busy: false), "the replacement is already followed: no second hand-over");
        Equal((Third, "Another Grok Bot replaced the one you chose, so Grok Bot is still open.", ("Try again", true, "Not now"), replacedText),
            (replacedNow.Offer.ProcessId, replacedNow.Problem, replacedNow.Buttons, (replacedNow.Offer.ProcessId, replacedNow.Problem, replacedNow.Buttons)),
            "replaced, the replacement open: the prompt follows it and keeps one steady text");
        replacedNow.Observe(Seen(null), busy: false);
        Equal((GrokBotReconnectPolicy.ReplacedText, ("Open Grok Bot", true, "Not now")), (replacedNow.Problem, replacedNow.Buttons), "and once it closes: didn't reopen it, Open Grok Bot");
        // Replaced, but Wingman couldn't look: the next check follows the replacement with the replaced words, not "opened again".
        var replacedBlind = new GrokBotReconnectPrompt(offer);
        replacedBlind.Reconnect(busy: false);
        replacedBlind.Finished(new(GrokBotReconnectOutcome.Replaced), null);
        Equal((GrokBotPromptStep.HandOver, Third, "Another Grok Bot replaced the one you chose, so Grok Bot is still open.", ("Try again", true, "Not now")),
            (replacedBlind.Observe(Seen(Third), busy: false), replacedBlind.Offer.ProcessId, replacedBlind.Problem, replacedBlind.Buttons),
            "replaced after a blind last look: the next check follows the replacement and still says it replaced the one you chose");
        // One hand-over rule: a run that ends while another Grok Bot already runs follows it at once, as the next check would.
        foreach (var ending in new[] { GrokBotReconnectOutcome.Stopped, GrokBotReconnectOutcome.Crashed, GrokBotReconnectOutcome.Gone, GrokBotReconnectOutcome.StillRunning,
                     GrokBotReconnectOutcome.UpdateTooLong, GrokBotReconnectOutcome.BusyTooLong, GrokBotReconnectOutcome.SessionEnding })
        {
            var follows = new GrokBotReconnectPrompt(offer);
            follows.Reconnect(busy: false);
            follows.Finished(new(ending), Seen(Third));
            var atEnd = (follows.Offer.ProcessId, follows.Problem, follows.Buttons);
            Equal((Third, GrokBotReconnectPolicy.ReopenedText, ("Try again", true, "Not now")), atEnd, ending + " while another Grok Bot runs: follows it at the run's end");
            Equal((None, atEnd), (follows.Observe(Seen(Third), busy: false), (follows.Offer.ProcessId, follows.Problem, follows.Buttons)), ending + ": the next check changes nothing");
        }
        // The title follows the Grok Bot the prompt holds: one opened without Wingman says Grok Bot updated with the update's
        // proof and reopened without Wingman without it, whatever the run's result; the Grok Bot Wingman opened gets the
        // neutral title.
        foreach (var outcome in new[] { GrokBotReconnectOutcome.Reopened, GrokBotReconnectOutcome.Replaced, GrokBotReconnectOutcome.UpdatedAndReopened })
        foreach (var proven in new[] { false, true })
        {
            var titled = GrokBotReconnectPrompt.AfterYes(new(Second, started), null, T0, process => proven && process.Id == Third);
            Equal(GrokBotReconnectPolicy.ApprovedTitle, titled.Title, outcome + ": the Yes prompt starts with its own title");
            titled.Finished(new(outcome), Seen(Third));
            Equal((Third, proven ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle), (titled.Offer.ProcessId, titled.Title),
                outcome + (proven ? ", update proven" : ", no proof") + ": following a Grok Bot opened without Wingman, the title says how it opened");
        }
        var proofFails = GrokBotReconnectPrompt.AfterYes(new(Second, started), null, T0, _ => throw new InvalidOperationException("access denied"));
        proofFails.Finished(new(GrokBotReconnectOutcome.Reopened), Seen(Third));
        Equal((Third, GrokBotReconnectPolicy.ReopenedTitle), (proofFails.Offer.ProcessId, proofFails.Title), "a proof that can't be read proves nothing; the prompt still follows");
        var keepsTitle = GrokBotReconnectPrompt.AfterYes(new(Second, started), null, T0);
        keepsTitle.Finished(new(GrokBotReconnectOutcome.NotConnected, Started: new(Third, started)), Seen(Third));
        Equal((Third, GrokBotReconnectPolicy.ApprovedTitle), (keepsTitle.Offer.ProcessId, keepsTitle.Title), "the Grok Bot Wingman opened gets the neutral title");
        // Wingman's Grok Bot, its connect failed while another was in front: back in front, it says what was true of it.
        foreach (var wasBusy in new[] { false, true })
        {
            var own = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started));
            own.Reconnect(busy: false);
            own.Finished(new(GrokBotReconnectOutcome.Relaunched, Started: new(Third, started)), Seen(Third));
            own.Finished(new(GrokBotReconnectOutcome.NotConnected) { WingmanBusy = wasBusy }, new GrokBotObservation(Third + 1, false, Present, started));
            own.Observe(Seen(Third), busy: false);
            Equal((Third, GrokBotReconnectPolicy.ApprovedTitle, GrokBotPromptReason.NotConnected(openedByWingman: true, wasBusy).WhileOpen), (own.Offer.ProcessId, own.Title, own.Problem),
                (wasBusy ? "busy" : "not busy") + ": Wingman's Grok Bot back in front after its connect failed says so");
        }
        // A failed connect to another Grok Bot open with Wingman's port: never called reopened without Wingman.
        foreach (var proven in new[] { false, true })
        {
            var portOwner = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started)) { UpdateProven = process => proven && process.Id == Third };
            portOwner.Reconnect(busy: false);
            portOwner.Finished(new(GrokBotReconnectOutcome.NotConnected), Seen(Third));
            Equal((Third, proven ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ApprovedTitle), (portOwner.Offer.ProcessId, portOwner.Title),
                (proven ? "update proven" : "no proof") + ": a failed connect to another Grok Bot titles it by the proof, else neutrally");
            // Wingman can't tell how that one opened, so the neutral title stays when the words are no longer Wingman's.
            portOwner.Open(busy: false);
            portOwner.WaitingForQuit();
            Equal((GrokBotReconnectPolicy.QuitText, proven ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ApprovedTitle), (portOwner.Message, portOwner.Title),
                (proven ? "update proven" : "no proof") + ": Try again on it asks for its close under the same title");
        }
        Equal((GrokBotReconnectOutcome.Gone, GrokBotReconnectOutcome.Replaced),
            (GrokBotReconnectRun.RunAsync(gone.Offer, new Steps(new GrokBotObservation(null, false, Present)), CancellationToken.None).GetAwaiter().GetResult().Outcome,
             GrokBotReconnectRun.RunAsync(gone.Offer, new Steps(Seen(Third)), CancellationToken.None).GetAwaiter().GetResult().Outcome),
            "at the press: no Grok Bot is gone; another one is replaced");
        Equal(true, gone.Open(busy: false), "Open Grok Bot without the quit's proof");
        gone.Finished(new(GrokBotReconnectOutcome.Busy), null);
        Equal(GrokBotPromptStep.Open, gone.Observe(Seen(null), busy: false), "busy then: Open Grok Bot is kept, not turned into Reconnect");

        var quitting = new GrokBotReconnectPrompt(offer);
        quitting.Reconnect(busy: false);
        quitting.WaitingForQuit();
        quitting.Observe(Seen(null), busy: false);
        Equal(GrokBotPromptState.WaitingForQuit, quitting.State, "waiting for the quit: its exit is expected, the run decides");
        quitting.Finished(new(GrokBotReconnectOutcome.Relaunched), null);
        Equal((GrokBotPromptState.Connecting, GrokBotReconnectPolicy.OpeningText), (quitting.State, quitting.Message), "reopened: still opening until the Helpers are connected");
        Equal(GrokBotPromptStep.None, quitting.Observe(Seen(Third, connected: true), busy: false), "the port coming up doesn't close it");
        quitting.Finished(new(GrokBotReconnectOutcome.Connected), null);
        Equal((GrokBotPromptState.Closed, "connected"), (quitting.State, quitting.ClosedBecause), "closed once the Helpers are connected");
        // Between runs, after a press: a Grok Bot that came up with Wingman's port isn't enough on its own either; the
        // prompt connects its Helpers and says so, and only a connect that worked closes it.
        var between = new GrokBotReconnectPrompt(offer);
        between.Reconnect(busy: false);
        between.Finished(new(GrokBotReconnectOutcome.Crashed), null);
        Equal(GrokBotPromptStep.None, between.Observe(Seen(Third, connected: true), busy: true), "connected while Wingman is busy: waits");
        Equal((GrokBotPromptStep.Connect, GrokBotPromptState.Connecting, GrokBotReconnectPolicy.ConnectingText),
            (between.Observe(Seen(Third, connected: true), busy: false), between.State, between.Message), "connected: Wingman connects its Helpers, and says so");
        between.Finished(new(GrokBotReconnectOutcome.NotConnected), Seen(Third, connected: true));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ConnectFailedText, GrokBotPromptStep.None),
            (between.State, between.Problem, between.Observe(Seen(Third, connected: true), busy: false)), "a failed connect says so, and isn't retried by itself");
    }

    /// <summary>
    /// Once the user has quit Grok Bot for the prompt, it never closes silently: it stays and says why Wingman couldn't open
    /// it, with Open Grok Bot. No quit within the limit: back to the offer, silently. An update running: closed.
    /// </summary>
    private static void AfterTheQuitThePromptNeverGivesUpSilently()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started);
        var quit = QuitProof(offer.Target);
        GrokBotObservation Seen(int? pid, DateTime? start = null, bool connected = false) => new(pid, connected, Present, start ?? started);

        GrokBotReconnectPrompt Waiting()
        {
            var prompt = new GrokBotReconnectPrompt(offer);
            prompt.Reconnect(busy: false);
            prompt.WaitingForQuit();
            return prompt;
        }

        foreach (var (outcome, text) in new[] { (GrokBotReconnectOutcome.StillRunning, GrokBotReconnectPolicy.StillRunningText),
            (GrokBotReconnectOutcome.BusyTooLong, GrokBotReconnectPolicy.BusyTooLongText) })
        {
            var prompt = Waiting();
            prompt.Finished(new(outcome, quit), null);
            Equal((GrokBotPromptState.CouldNotStart, text), (prompt.State, prompt.Problem), outcome + ": the prompt stays and says why");
            for (var tick = 0; tick < 100; tick++) prompt.Observe(Seen(null), busy: false);
            Equal(GrokBotPromptState.CouldNotStart, prompt.State, outcome + ": Grok Bot not running is expected now; it stays");
            Equal(false, prompt.Reconnect(busy: false), outcome + ": Reconnect isn't offered again");
            Equal(true, prompt.Open(busy: false), outcome + ": Open Grok Bot acts");
            Equal(GrokBotPromptState.Working, prompt.State, outcome + ": Working…");
            Equal(false, prompt.Open(busy: false), outcome + ": a second press does nothing");
        }

        var openBusy = Waiting();
        openBusy.Finished(new(GrokBotReconnectOutcome.StillRunning, quit), null);
        Equal(false, openBusy.Open(busy: true), "Open pressed while busy: not yet");
        Equal(GrokBotPromptStep.Open, openBusy.Observe(Seen(null), busy: false), "free: opens, without the offer's process");
        openBusy.Finished(new(GrokBotReconnectOutcome.Relaunched, quit), null);
        Equal(GrokBotPromptState.Connecting, openBusy.State, "opened: connecting");
        openBusy.Finished(new(GrokBotReconnectOutcome.Connected, quit), null);
        Equal(GrokBotPromptState.Closed, openBusy.State, "connected: closed");

        // After the quit another Grok Bot started: never a silent close. The same prompt follows it, still pressed, says
        // what happened and offers Open Grok Bot.
        var replaced = Waiting();
        replaced.Finished(new(GrokBotReconnectOutcome.BusyTooLong, quit), null);
        Equal(GrokBotPromptStep.HandOver, replaced.Observe(Seen(Third), busy: false), "another Grok Bot started: the prompt follows it");
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText, Third, true, (object?)null, ("Try again", true, "Not now")),
            (replaced.State, replaced.Problem, replaced.Offer.ProcessId, replaced.Pressed, (object?)replaced.Quit, replaced.Buttons), "and it says what happened, never Reconnect");
        for (var tick = 0; tick < 10; tick++) Equal(GrokBotPromptStep.None, replaced.Observe(Seen(Third), busy: false), "asked once for that Grok Bot");
        Equal((GrokBotPromptState.CouldNotStart, true), (replaced.State, replaced.Open(busy: false)), "still there, with Open Grok Bot");
        Equal(GrokBotPromptStep.None, replaced.Observe(Seen(400), busy: false), "while Open Grok Bot runs, the run sees a new Grok Bot itself");
        Equal(GrokBotPromptState.Working, replaced.State, "not closed or handed over mid-run");
        var pendingOpen = Waiting();
        pendingOpen.Finished(new(GrokBotReconnectOutcome.StillRunning, quit), null);
        pendingOpen.Open(busy: true);
        Equal(GrokBotPromptStep.HandOver, pendingOpen.Observe(Seen(Third), busy: true), "a kept Open Grok Bot press: handed over instead");
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText), (pendingOpen.State, pendingOpen.Problem), "and it says what happened");

        // A Try again or Open Grok Bot press kept while busy: "Working..." names the step it will take.
        var retry = Waiting();
        retry.Finished(new(GrokBotReconnectOutcome.NotClosed), Seen(Second));
        Equal((false, GrokBotPromptState.Working, GrokBotReconnectPolicy.PromptText), (retry.Open(busy: true), retry.State, retry.Message),
            "Try again kept while busy, its Grok Bot open: it will ask for the close, so no opening words");
        Equal((GrokBotPromptStep.None, GrokBotReconnectPolicy.OpeningText), (retry.Observe(Seen(null), busy: true), retry.Message), "that Grok Bot closes meanwhile: Wingman will open Grok Bot");
        var reopen = Waiting();
        reopen.Finished(new(GrokBotReconnectOutcome.StillRunning, quit), Seen(null));
        Equal((false, GrokBotReconnectPolicy.OpeningText), (reopen.Open(busy: true), reopen.Message), "Open Grok Bot kept while busy after the quit: Wingman will open it");

        var timedOut = Waiting();
        timedOut.Finished(new(GrokBotReconnectOutcome.QuitTimedOut), Seen(Second));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.QuitTimedOutText, (string?)null), (timedOut.State, timedOut.Problem, timedOut.ClosedBecause),
            "no quit within the limit: says so, never Reconnect again");
        Equal(true, timedOut.Open(busy: false), "and Open Grok Bot acts");

        var reconnects = new[] { GrokBotReconnectOutcome.Relaunched, GrokBotReconnectOutcome.Adopted, GrokBotReconnectOutcome.AlreadyConnected };
        // Once Reconnect is pressed, also without the quit's proof (before the quit, or Grok Bot hadn't finished closing as
        // its installer started), only a reconnect or the user's Cancel closes the prompt; every other outcome stays, says
        // why and offers Open Grok Bot.
        foreach (var waited in new[] { false, true })
            foreach (var outcome in Enum.GetValues<GrokBotReconnectOutcome>())
            {
                var prompt = new GrokBotReconnectPrompt(offer);
                prompt.Reconnect(busy: false);
                if (waited) prompt.WaitingForQuit();
                prompt.Finished(new(outcome), null);
                var name = outcome + (waited ? " while waiting for the quit" : " right after the press");
                if (outcome == GrokBotReconnectOutcome.Connected) Equal(GrokBotPromptState.Closed, prompt.State, name + ": closed");
                else if (reconnects.Contains(outcome)) Equal(GrokBotPromptState.Connecting, prompt.State, name + ": connecting the Helpers");
                else if (outcome == GrokBotReconnectOutcome.Busy) Equal(GrokBotPromptState.Working, prompt.State, name + ": Working…");
                else if (outcome is GrokBotReconnectOutcome.Cancelled or GrokBotReconnectOutcome.Stopped)
                    Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.StoppedText, true), (prompt.State, prompt.Problem, prompt.Open(busy: false)),
                        name + ": Wingman's own stop says so, and Open Grok Bot acts");
                else
                {
                    Equal((GrokBotPromptState.CouldNotStart, true), (prompt.State, !string.IsNullOrEmpty(prompt.Problem)), name + ": stays and says why");
                    for (var tick = 0; tick < 10; tick++) prompt.Observe(Seen(null), busy: false);
                    Equal(GrokBotPromptState.CouldNotStart, prompt.State, name + ": no Grok Bot doesn't close it either");
                    Equal(true, prompt.Open(busy: false), name + ": Open Grok Bot acts");
                }
            }
        // An error after the quit: the prompt stays, says so in plain words and offers Open Grok Bot, even with no Grok Bot.
        // The real path the tray takes (GrokBotPromptRun over GrokBotPromptSteps): the relaunch hands the prompt the quit's
        // proof the moment the process exits, and the start then throws, so the run never returns a proof.
        var failed = new GrokBotReconnectPrompt(offer);
        failed.Reconnect(busy: false);
        var throws = new FakeControl(new FakeProcess(offer.Target, []) { QuitsAfter = 2 }) { StartThrows = new FileNotFoundException("Grok Bot.exe") };
        var host = new FakeHost(throws, new GrokBotObservation(Second, false, Present, started), new GrokBotObservation(null, false, Present));
        var failedResult = Act(failed, GrokBotPromptStep.Reconnect, host);
        Equal((GrokBotReconnectOutcome.Failed, true, GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.FailedText, ("Open Grok Bot", true, "Not now")),
            (failedResult.Outcome, failed.Quit?.Process == offer.Target, failed.State, failed.Problem, failed.Buttons),
            "the start throws after the quit: the prompt has the proof, a plain reason and Open Grok Bot");
        Equal(true, host.Log.Contains("changed WaitingForQuit") && host.Log.Contains("changed Working"), "it stopped asking for the quit at the exit");
        // Open Grok Bot without the proof: the same, once the snapshot shows the process gone.
        var openFails = new GrokBotReconnectPrompt(offer);
        openFails.Reconnect(busy: false);
        openFails.Finished(new(GrokBotReconnectOutcome.ExitTooLong), null);
        openFails.Open(busy: false);
        var openHost = new FakeHost(new FakeControl() { StartThrows = new IOException("denied") }, new GrokBotObservation(null, false, Present));
        Equal((GrokBotReconnectOutcome.Failed, true, GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.FailedText),
            (Act(openFails, GrokBotPromptStep.Open, openHost).Outcome, openFails.Quit?.Process == offer.Target, openFails.State, openFails.Problem),
            "the prompt has the proof from Open Grok Bot too");
        for (var tick = 0; tick < 10; tick++) failed.Observe(Seen(null), busy: false);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.FailedText, true), (failed.State, failed.Problem, failed.Open(busy: false)),
            "an error after the quit: stays with a plain reason and Open Grok Bot");
        var trayText = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var act = trayText[trayText.IndexOf("private async Task ActOnGrokPromptAsync", StringComparison.Ordinal)..trayText.IndexOf("private sealed class TrayPromptHost", StringComparison.Ordinal)];
        Equal(true, !act.Contains("MessageBox", StringComparison.Ordinal) && act.Contains("new GrokBotPromptSteps(prompt, new TrayPromptHost(this, prompt, session, owned), GrokBotRelaunchLimits.Default)", StringComparison.Ordinal),
            "the tray runs the prompt through the same run and steps, with no message box");
        var connectPart = trayText[trayText.IndexOf("private sealed class TrayPromptHost", StringComparison.Ordinal)..trayText.IndexOf("private static bool ConfirmGrokBotRelaunch", StringComparison.Ordinal)];
        Equal((false, true, false), (connectPart.Contains("MessageBox", StringComparison.Ordinal),
            connectPart.Contains("=> tray.ConnectGrokBotAsync(started, owned);", StringComparison.Ordinal), trayText.Contains("ConnectRelaunchedGrokBotAsync", StringComparison.Ordinal)),
            "connecting the Helpers after the prompt's run shows no message box: a failure is the prompt's reason");
        Equal(true, connectPart.Contains("(\"error\", \"busy\"));\n                return exiting ? GrokBotConnectResult.Failed : GrokBotConnectResult.Busy;", StringComparison.Ordinal),
            "the tray's connect reports Wingman staying busy as Busy, so the prompt says Wingman was busy");
        var exitTooLong = Waiting();
        exitTooLong.Finished(new(GrokBotReconnectOutcome.ExitTooLong), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ExitTooLongText), (exitTooLong.State, exitTooLong.Problem),
            "an installer started but Grok Bot didn't finish closing: says so");

        // After the user's quit, every outcome either reconnected Grok Bot (or the user cancelled), or the prompt stays and
        // says something; none closes it silently.
        foreach (var outcome in Enum.GetValues<GrokBotReconnectOutcome>())
        {
            var prompt = Waiting();
            prompt.Finished(new(outcome, quit), null);
            if (outcome == GrokBotReconnectOutcome.Connected) Equal(GrokBotPromptState.Closed, prompt.State, outcome + " after the quit: closed");
            else if (reconnects.Contains(outcome)) Equal(GrokBotPromptState.Connecting, prompt.State, outcome + " after the quit: connecting");
            else if (outcome == GrokBotReconnectOutcome.Busy) Equal(GrokBotPromptState.Working, prompt.State, "busy after the quit: Working…");
            else Equal((GrokBotPromptState.CouldNotStart, true), (prompt.State, !string.IsNullOrEmpty(prompt.Problem)), outcome + " after the quit: stays and says why");
        }
        Equal(GrokBotReconnectPolicy.UpdateTooLongText, GrokBotPromptReason.For(GrokBotReconnectOutcome.UpdateTooLong).WhenGone, "the update limit is named");
        Equal(GrokBotReconnectPolicy.ReopenedText, GrokBotPromptReason.For(GrokBotReconnectOutcome.UpdatedAndReopened).WhileOpen, "reopened during the update");

        // The quit started an update: "Grok Bot is updating…" with Cancel, kept while another Grok Bot appears (the run
        // moves the prompt to it) and while none runs; Grok Bot connected closes it; Cancel closes it.
        var updating = Waiting();
        updating.Updating(quit);
        Equal((GrokBotPromptState.Updating, (object?)quit), (updating.State, (object?)updating.Quit), "updating, with the quit's proof");
        Equal("Grok Bot is updating... Wingman reopens it when the update has finished.", GrokBotReconnectPolicy.UpdatingText, "updating wording");
        for (var tick = 0; tick < 10; tick++) updating.Observe(Seen(null), busy: false);
        updating.Observe(Seen(Third), busy: false);
        Equal(GrokBotPromptState.Updating, updating.State, "updating: neither no Grok Bot nor a new one closes it");
        Equal(false, updating.Reconnect(busy: false) || updating.Open(busy: false), "no button acts while updating");
        updating.Finished(new(GrokBotReconnectOutcome.UpdateTooLong, quit), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.UpdateTooLongText), (updating.State, updating.Problem), "too long: says so");
        Equal(true, updating.Open(busy: false), "and offers Open Grok Bot");
        var connectedWhileUpdating = Waiting();
        connectedWhileUpdating.Updating(quit);
        connectedWhileUpdating.Observe(Seen(Third, connected: true), busy: false);
        Equal(GrokBotPromptState.Updating, connectedWhileUpdating.State, "connected while updating: the run sees it and connects the Helpers before the prompt closes");
        var cancelled = Waiting();
        cancelled.Updating(quit);
        cancelled.NotNow();
        Equal(GrokBotPromptState.Closed, cancelled.State, "Cancel while updating: closed");

        Equal("Helpers need Grok Bot reopened by Wingman.", GrokBotReconnectPolicy.PromptText, "prompt wording");
        Equal("Close Grok Bot's window. Wingman reopens it.", GrokBotReconnectPolicy.QuitText, "quit wording: on Windows closing the window quits Grok Bot");
        var window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotPromptWindow.cs"));
        Equal(true, window.Contains("message.Text = prompt.Message;", StringComparison.Ordinal) && window.Split("message.Text").Length == 2,
            "the window shows the prompt's own text, set in one place");
        // "Working..." never shows the reason from before the press.
        var reopening = Waiting();
        reopening.Finished(new(GrokBotReconnectOutcome.StillRunning, quit), null);
        Equal(GrokBotReconnectPolicy.StillRunningText, reopening.Message, "the reason, next to Open Grok Bot");
        reopening.Open(busy: true);
        Equal((GrokBotPromptState.Working, GrokBotReconnectPolicy.OpeningText), (reopening.State, reopening.Message), "Working…: Wingman is opening Grok Bot");
        var again = new GrokBotReconnectPrompt(offer);
        again.Reconnect(busy: false);
        again.Finished(new(GrokBotReconnectOutcome.Failed), null);
        Equal((GrokBotReconnectPolicy.FailedText, ("Open Grok Bot", true, "Not now")), (again.Message, again.Buttons), "the reason, next to Open Grok Bot (never Reconnect again)");
        again.Open(busy: true);
        Equal(GrokBotReconnectPolicy.OpeningText, again.Message, "Working… after Open Grok Bot: not the old reason");
        var waitingText = Waiting();
        Equal(GrokBotReconnectPolicy.QuitText, waitingText.Message, "waiting for the quit");
        Equal((true, true), (window.Contains("var (primaryText, primaryEnabled, secondaryText) = prompt.Buttons;", StringComparison.Ordinal), window.Contains("OpenClicked", StringComparison.Ordinal)),
            "the window shows the prompt's own buttons, and Open Grok Bot acts");
        var shown = Waiting();
        shown.Finished(new(GrokBotReconnectOutcome.Failed, quit), null);
        Equal((GrokBotPromptState.CouldNotStart, ("Open Grok Bot", true, "Not now")), (shown.State, shown.Buttons), "the window has the Open Grok Bot button");
        Equal(true, window.Contains("openMode = prompt.State == GrokBotPromptState.CouldNotStart;", StringComparison.Ordinal),
            "its button opens Grok Bot wherever it shows Open Grok Bot, also without the quit's proof");
        // Grok Bot 0.78.1 has no tray icon on Windows: nothing sends the user to one.
        foreach (var file in new[] { Path.Combine("src", "CodexWingman.Core", "GrokBotReconnectPolicy.cs"), Path.Combine("src", "CodexWingman", "GrokBotLauncher.cs"),
                     Path.Combine("docs", "grok-bot-after-update.md"), Path.Combine("docs", "wingman-2.md") })
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
            Equal(false, text.Contains("its tray icon", StringComparison.OrdinalIgnoreCase) || text.Contains("Quit Grok Bot", StringComparison.Ordinal)
                || text.Contains("hides to its tray", StringComparison.OrdinalIgnoreCase) || text.Contains("hides it to the tray", StringComparison.OrdinalIgnoreCase),
                file + " never sends the user to a Grok Bot tray icon");
        }
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var promptRun = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotPromptRun.cs"));
        Equal(2, promptRun.Split("WingmanLog.Write(\"grok-bot-reconnect-outcome\", (\"process\", prompt.Offer.ProcessId), (\"step\", step), (\"outcome\", result.Outcome));").Length - 1,
            "the real outcome is logged, and the connect's");
    }

    /// <summary>
    /// The structural guarantee, enumerated: once the user pressed Reconnect or Open Grok Bot, every way the run can end
    /// (each relaunch result, each result after an update, an error, Wingman's own stop, a shutdown, the connect working,
    /// failing or throwing, and what Grok Bot is doing by then) leaves the prompt either closed because the Helpers are
    /// connected or the user cancelled, or open with a plain reason and [Open Grok Bot] [Not now]; never [Reconnect] again,
    /// and Working only for a press kept while Wingman is busy.
    /// WINGMAN_OUTCOME_TABLE=1 prints every distinct ending.
    /// </summary>
    /// <summary>Words that say Wingman was due to reopen Grok Bot (and didn't).</summary>
    private static bool ClaimsAReopen(string text) =>
        text.Contains("reopen", StringComparison.OrdinalIgnoreCase) || text.Contains("closed before", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every result that leaves the prompt open, for every state of the Grok Bot it holds (still open, gone, not seen):
    /// the words and the button are <see cref="GrokBotReconnectPrompt.Ending"/>'s for that state, the same function for
    /// all; Try again exactly while it is open, with words that don't say a reopen was due.
    /// </summary>
    private static void EveryResultTimesEveryStatePicksItsEnding()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started);
        var looks = new (GrokBotHeldState Held, GrokBotObservation? Seen)[]
        {
            (GrokBotHeldState.Open, new(Second, false, Present, started)), (GrokBotHeldState.Gone, new(null, false, Present)), (GrokBotHeldState.Unknown, null),
        };
        var stays = Enum.GetValues<GrokBotReconnectOutcome>().Where(outcome => outcome is not (GrokBotReconnectOutcome.Busy or GrokBotReconnectOutcome.Connected
            or GrokBotReconnectOutcome.Relaunched or GrokBotReconnectOutcome.Adopted or GrokBotReconnectOutcome.AlreadyConnected)).ToList();
        Equal(true, stays.Count >= 16, "every staying result is enumerated");
        foreach (var outcome in stays)
        foreach (var (held, seen) in looks)
        foreach (var approved in new[] { false, true })
        {
            var prompt = approved ? GrokBotReconnectPrompt.AfterYes(offer.Target, null, T0) : new GrokBotReconnectPrompt(offer);
            if (!approved) prompt.Reconnect(busy: false);
            prompt.Finished(new(outcome), seen);
            var name = $"{outcome} / {held}{(approved ? " / after the Yes" : "")}";
            var why = outcome == GrokBotReconnectOutcome.NotConnected ? GrokBotPromptReason.NotConnected(false) : GrokBotPromptReason.For(outcome);
            var ending = GrokBotReconnectPrompt.Ending(why, held);
            Equal((GrokBotPromptState.CouldNotStart, held, ending.Text, (ending.Primary, true, "Not now")), (prompt.State, prompt.Held, prompt.Problem, prompt.Buttons),
                name + ": the words and the button are the one ending for that state");
            Equal(held == GrokBotHeldState.Open ? "Try again" : "Open Grok Bot", ending.Primary, name + ": Try again exactly while its Grok Bot is open");
            if (held == GrokBotHeldState.Open)
                Equal(false, ClaimsAReopen(ending.Text) || string.IsNullOrWhiteSpace(ending.Text), name + ": open: plain words that don't say a reopen was due");
            else
                Equal(false, string.IsNullOrWhiteSpace(ending.Text) || ending.Text.Contains("still open", StringComparison.Ordinal)
                    || ending.Text.Contains("left it running", StringComparison.Ordinal), name + ": gone or not seen: never says it is still open");
        }
        // The tray's stop right after the Yes passes its look too, so its button follows the held Grok Bot like every other.
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("prompt.Finished(new(GrokBotReconnectOutcome.Cancelled), seen);", StringComparison.Ordinal)
            && !File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotReconnectPolicy.cs")).Contains("GrokBotObservation? now = null", StringComparison.Ordinal),
            "every Finished passes Wingman's look at Grok Bot");
    }

    private static void EveryResultEndsInTheSamePrompt()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started);
        var owned = new GrokBotObservation(Second, false, Present, started);
        var finals = new (string Name, GrokBotObservation Seen)[]
        {
            ("its Grok Bot runs", owned), ("no Grok Bot", new(null, false, Present)), ("another Grok Bot", new(Third, false, Present, started.AddMinutes(1))),
            ("connected", new(Third, true, Present, started.AddMinutes(1))),
        };
        var kinds = Enum.GetValues<GrokBotRelaunchKind>();
        var table = new SortedSet<string>(StringComparer.Ordinal);
        var runs = 0;
        const string Approved = "Open window or Repair after the Yes";
        const string Replaced = "Reconnect after it was replaced";
        foreach (var path in new[] { "Reconnect", Replaced, "Open Grok Bot after the quit", "Open Grok Bot without the proof", "Open Grok Bot while it still runs", Approved })
        foreach (var kind in kinds)
        foreach (var afterUpdate in kind == GrokBotRelaunchKind.UpdateRunning ? kinds : [GrokBotRelaunchKind.Relaunched])
        foreach (var ending in new[] { "returns", "throws", "user cancels", "Wingman stops", "shutdown", "busy" })
        foreach (var connect in new[] { "connects", "doesn't connect", "connect throws" })
        foreach (var (finalName, final) in finals)
        {
            runs++;
            var prompt = path == Approved ? GrokBotReconnectPrompt.AfterYes(offer.Target, null, T0) : new GrokBotReconnectPrompt(offer);
            var quit = QuitProof(offer.Target);
            var steps = new Steps(path is "Reconnect" or "Open Grok Bot while it still runs" ? owned
                : path == Replaced ? new GrokBotObservation(Third, false, Present, started.AddMinutes(1)) : new GrokBotObservation(null, false, Present), final)
            {
                Result = kind, AfterUpdate = afterUpdate, OpenAfterPress = kind,
                RelaunchQuit = kind is GrokBotRelaunchKind.Relaunched or GrokBotRelaunchKind.UpdateRunning or GrokBotRelaunchKind.Reopened
                    or GrokBotRelaunchKind.StillRunning or GrokBotRelaunchKind.BusyTooLong or GrokBotRelaunchKind.UpdateTooLong ? quit : null,
                OpenAfterPressQuit = kind == GrokBotRelaunchKind.Relaunched ? quit : null,
                AfterClose = kind,
                AfterCloseQuit = kind is GrokBotRelaunchKind.NotClosed or GrokBotRelaunchKind.Cancelled or GrokBotRelaunchKind.ExitTooLong ? null : quit,
                Connects = connect == "connects",
                ConnectThrows = connect == "connect throws" ? new InvalidOperationException("The operation was canceled.") : null,
                IsBusy = ending == "busy",
                Ending = ending == "shutdown",
            };
            GrokBotPromptStep step;
            if (path == Approved)
            {
                Equal((GrokBotPromptState.Working, GrokBotReconnectPolicy.OpeningText, true, GrokBotReconnectPolicy.ApprovedTitle),
                    (prompt.State, prompt.Message, prompt.Pressed, prompt.Title), "after the Yes: the prompt is already working");
                step = GrokBotPromptStep.ApprovedClose;
            }
            else if (path is "Reconnect" or Replaced)
            {
                prompt.Reconnect(busy: false);
                step = GrokBotPromptStep.Reconnect;
            }
            else
            {
                prompt.Reconnect(busy: false);
                prompt.Finished(new(GrokBotReconnectOutcome.ExitTooLong, path == "Open Grok Bot after the quit" ? quit : null), null);
                Equal(true, prompt.Open(busy: false), path + ": pressed");
                step = GrokBotPromptStep.Open;
            }
            steps.Throws = ending switch
            {
                "throws" => new IOException("Access is denied."),
                "user cancels" or "Wingman stops" or "shutdown" => new OperationCanceledException(),
                _ => null,
            };
            if (ending == "user cancels") steps.During = prompt.NotNow;
            var name = $"{path} / {kind}{(kind == GrokBotRelaunchKind.UpdateRunning ? " then " + afterUpdate : "")} / {ending} / {connect} / {finalName}";
            var result = GrokBotPromptRun.ActAsync(prompt, step, steps, CancellationToken.None).GetAwaiter().GetResult();
            var message = prompt.State == GrokBotPromptState.Closed ? "" : prompt.Message;
            var shown = prompt.State == GrokBotPromptState.Closed ? "closed (" + prompt.ClosedBecause + ")" : $"{prompt.State}: \"{message}\" {prompt.Buttons}";
            table.Add($"{path} | {result.Outcome} | {shown}");
            if (path == Replaced)
                Equal((GrokBotReconnectOutcome.Replaced, true, false), (result.Outcome, prompt.State == GrokBotPromptState.CouldNotStart
                    && prompt.Problem == GrokBotReconnectPrompt.Ending(GrokBotPromptReason.For(GrokBotReconnectOutcome.Replaced), prompt.Held).Text
                    && (final.Process is not { } other || other == offer.Target || prompt.Offer.Target == other)
                    || prompt.ClosedBecause == "not-now", steps.Log.Any(line => line.StartsWith("relaunch", StringComparison.Ordinal) || line.StartsWith("start", StringComparison.Ordinal))),
                    name + ": replaced at the press: says so, starts nothing");
            Equal(false, shown.Contains('…') || prompt.Title.Contains('…'), name + ": plain ASCII dots, never the ellipsis character");
            Equal(false, prompt.State is GrokBotPromptState.WaitingForQuit or GrokBotPromptState.Updating or GrokBotPromptState.Connecting, name + ": the run's end leaves no waiting state");
            Equal(false, message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase) || message.Contains("denied", StringComparison.OrdinalIgnoreCase),
                name + ": never a raw error text");
            Equal(false, ending is "throws" or "Wingman stops" or "shutdown" && prompt.State != GrokBotPromptState.CouldNotStart
                && !(ending == "busy"), name + ": an error or a stop never closes the prompt or leaves it working");
            switch (prompt.State)
            {
                case GrokBotPromptState.Closed:
                    Equal(true, prompt.ClosedBecause == "connected" && result.Outcome == GrokBotReconnectOutcome.Connected && connect == "connects" && steps.Log.Any(line => line.StartsWith("connect ", StringComparison.Ordinal))
                        || prompt.ClosedBecause == "not-now" && ending == "user cancels",
                        name + ": closed only once the Helpers connected, or by the user");
                    break;
                case GrokBotPromptState.CouldNotStart:
                    // A Grok Bot replaced at the press ends the run before any wait a stop could interrupt.
                    Equal(true, ending is not ("Wingman stops" or "shutdown") || path == Replaced || new[] { GrokBotReconnectOutcome.Stopped, GrokBotReconnectOutcome.SessionEnding }.Any(stop =>
                            prompt.Problem == GrokBotPromptReason.For(stop).WhileOpen || prompt.Problem == GrokBotPromptReason.For(stop).WhenGone)
                        || prompt.Problem == GrokBotReconnectPolicy.ReopenedText && prompt.Offer.Target == steps.LastSeen?.Process,
                        name + ": a stop says Wingman stopped, or that another Grok Bot was opened, once the prompt follows it");
                    // One hand-over rule for the run's end and the next check: a check that sees what the run's last look saw
                    // changes neither the words nor the button.
                    if (steps.LastSeen is { Connected: false } lastLook)
                    {
                        var ended = (prompt.Problem, prompt.Buttons, prompt.Title, prompt.Offer.Target);
                        Equal((GrokBotPromptStep.None, ended), (prompt.Observe(lastLook, busy: false), (prompt.Problem, prompt.Buttons, prompt.Title, prompt.Offer.Target)),
                            name + ": the next check, seeing the same Grok Bot, changes nothing");
                    }
                    // The words and the button come from Wingman's last look at the Grok Bot the prompt holds, never from the result.
                    var held = steps.LastSeen is not { } last ? GrokBotHeldState.Unknown
                        : last.Process == prompt.Offer.Target ? GrokBotHeldState.Open : GrokBotHeldState.Gone;
                    Equal((true, held, (held == GrokBotHeldState.Open ? "Try again" : "Open Grok Bot", true, "Not now")),
                        (!string.IsNullOrWhiteSpace(prompt.Problem), prompt.Held, prompt.Buttons),
                        name + ": a plain reason; Try again while its Grok Bot is still open, Open Grok Bot once it is gone");
                    if (result.Outcome is GrokBotReconnectOutcome.NotClosed or GrokBotReconnectOutcome.QuitTimedOut && held == GrokBotHeldState.Gone)
                        Equal(GrokBotReconnectPolicy.GoneText, prompt.Problem, name + ": left running, then seen closed: says it closed");
                    if (result.Outcome == GrokBotReconnectOutcome.NotConnected)
                    {
                        Equal(finalName == "no Grok Bot", prompt.Problem == GrokBotReconnectPolicy.ClosedBeforeConnectText, name + ": the connect's reason says whether Grok Bot closed");
                        Equal((GrokBotPromptStep.None, false), (prompt.Observe(final with { Connected = false }, busy: false), prompt.Problem == GrokBotReconnectPolicy.ReopenedText),
                            name + ": the Grok Bot Wingman was connecting is never read as opened without Wingman");
                    }
                    if (held == GrokBotHeldState.Open)
                    {
                        Equal(false, ClaimsAReopen(prompt.Problem!), name + ": while its Grok Bot is open the words don't say a reopen was due");
                        var whileOpen = prompt.Problem;
                        Equal((GrokBotPromptStep.None, ("Open Grok Bot", true, "Not now")),
                            (prompt.Observe(new GrokBotObservation(null, false, Present), busy: false), prompt.Buttons),
                            name + ": once that Grok Bot closes, Open Grok Bot starts one");
                        Equal(true, prompt.Problem != whileOpen || prompt.Problem == GrokBotReconnectPolicy.OpenedNotConnectedText, name + ": and the words say it closed");
                    }
                    break;
                case GrokBotPromptState.Working:
                    var goesOn = result.Outcome == GrokBotReconnectOutcome.UpdatedAndReopened && prompt.Offer.ProcessId == Third;
                    Equal((true, ("Working...", false, "Not now")), (ending == "busy" && result.Outcome == GrokBotReconnectOutcome.Busy || goesOn, prompt.Buttons),
                        name + ": Working only for a press kept while busy, or going on to the quit of the Grok Bot opened during the update");
                    if (goesOn) Equal(GrokBotPromptStep.Reconnect, prompt.Observe(final with { Connected = false }, busy: false), name + ": asks for that Grok Bot's quit at once");
                    steps.IsBusy = false;
                    break;
                default:
                    throw new InvalidOperationException(name + ": ended in " + prompt.State);
            }
            if (prompt.State != GrokBotPromptState.CouldNotStart) continue;
            // Then, between runs: another Grok Bot opens, it closes again, a third one opens, and that one comes up with the
            // port. The same prompt follows each, stays pressed, never says Reconnect, and closes only once connected.
            var shownBefore = prompt.Message;
            Equal(GrokBotPromptStep.HandOver, prompt.Observe(new(401, false, Present, started.AddMinutes(5)), busy: false), name + ": follows a Grok Bot opened now");
            Equal((401, true, GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText, ("Try again", true, "Not now"), GrokBotReconnectPolicy.ReopenedTitle),
                (prompt.Offer.ProcessId, prompt.Pressed, prompt.State, prompt.Message, prompt.Buttons, prompt.Title), name + ": the same prompt, pressed, for that Grok Bot, titled for it");
            table.Add($"{path} | then another Grok Bot opens | {prompt.State}: \"{prompt.Message}\" {prompt.Buttons}");
            Equal(GrokBotPromptStep.None, prompt.Observe(new(null, false, Present), busy: false), name + ": that Grok Bot closes");
            Equal((true, GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.GoneText, ("Open Grok Bot", true, "Not now")),
                (prompt.Pressed, prompt.State, prompt.Message, prompt.Buttons), name + ": says it closed; never closed as gone");
            table.Add($"{path} | then that Grok Bot closes | {prompt.State}: \"{prompt.Message}\" {prompt.Buttons}");
            Equal(GrokBotPromptStep.HandOver, prompt.Observe(new(402, false, Present, started.AddMinutes(6)), busy: false), name + ": follows a second Grok Bot");
            Equal((402, true, GrokBotReconnectPolicy.ReopenedText, ("Try again", true, "Not now")), (prompt.Offer.ProcessId, prompt.Pressed, prompt.Message, prompt.Buttons),
                name + ": the second hand-over keeps the press too");
            table.Add($"{path} | then a second Grok Bot opens | {prompt.State}: \"{prompt.Message}\" {prompt.Buttons}");
            Equal((GrokBotPromptStep.Connect, GrokBotPromptState.Connecting), (prompt.Observe(new(402, true, Present, started.AddMinutes(6)), busy: false), prompt.State),
                name + ": it comes up with the port: Wingman connects its Helpers");
            prompt.Finished(new(GrokBotReconnectOutcome.Connected), null);
            Equal((GrokBotPromptState.Closed, "connected"), (prompt.State, prompt.ClosedBecause), name + ": closed once connected (before: " + shownBefore + ")");
        }
        Equal(true, runs > 5000, "every combination ran");
        var texts = typeof(GrokBotReconnectPolicy).GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!)
            .Append(GrokBotRelaunch.UpdateRunningText).ToList();
        Equal((true, false), (texts.Count > 15, texts.Any(text => text.Contains('…'))), "no prompt text has the ellipsis character");
        if (Environment.GetEnvironmentVariable("WINGMAN_OUTCOME_TABLE") == "1") foreach (var row in table) Console.WriteLine(row);
    }

    /// <summary>
    /// Open window or Repair after the Yes, on the real path (GrokBotPromptRun over GrokBotPromptSteps and the Core close
    /// wait): Grok Bot not closing in time, an update still running after 5 minutes, Reload Helpers during the wait, an
    /// error, and the connect failing each end in the prompt with a plain reason and [Open Grok Bot]; the update shows
    /// in the prompt while Wingman waits; only a connect that worked closes it. The tray shows that prompt before the run.
    /// </summary>
    private static void OpenWindowAfterTheYesEndsInThePrompt()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var none = new GrokBotObservation(null, false, Present);
        var reopenedWithPort = new GrokBotObservation(Third, true, Present, started.AddMinutes(1));
        var reopenedProcess = new GrokBotProcess(Third, started.AddMinutes(1));
        (GrokBotReconnectPrompt Prompt, FakeHost Host, GrokBotReconnectResult Result) Run(FakeControl control, CancellationToken token = default, bool connects = true,
            GrokBotObservation? seen = null)
        {
            var prompt = GrokBotReconnectPrompt.AfterYes(target, control.Stamp, T0);
            var host = new FakeHost(control, seen ?? none) { Connects = connects };
            return (prompt, host, Act(prompt, GrokBotPromptStep.ApprovedClose, host, token));
        }
        void Ends((GrokBotReconnectPrompt Prompt, FakeHost Host, GrokBotReconnectResult Result) run, GrokBotReconnectOutcome outcome, string text, string name,
            string primary = "Open Grok Bot") =>
            Equal((outcome, GrokBotPromptState.CouldNotStart, text, (primary, true, "Not now")), (run.Result.Outcome, run.Prompt.State, run.Prompt.Problem, run.Prompt.Buttons), name);

        Ends(Run(new FakeControl(new FakeProcess(target, []))), GrokBotReconnectOutcome.NotClosed, GrokBotReconnectPolicy.GoneText,
            "Grok Bot didn't close in time, but is gone by the end: the prompt says it closed, never that Wingman left it running");
        // Seen still open at the end: the button asks for its close again, never Open while it runs; once it closes, Open.
        var stuck = Run(new FakeControl(new FakeProcess(target, [])), seen: new GrokBotObservation(Second, false, Present, started));
        Equal((GrokBotReconnectOutcome.NotClosed, GrokBotReconnectPolicy.NotClosedText, ("Try again", true, "Not now")), (stuck.Result.Outcome, stuck.Prompt.Problem, stuck.Prompt.Buttons),
            "still open after it didn't close: Try again");
        stuck.Prompt.Observe(none, busy: false);
        Equal((GrokBotReconnectPolicy.GoneText, ("Open Grok Bot", true, "Not now")), (stuck.Prompt.Problem, stuck.Prompt.Buttons), "closed later: says so, Open Grok Bot");

        var updating = Run(new FakeControl(new FakeProcess(target, []) { Exited = true }) { InstallerAt = _ => true });
        Ends(updating, GrokBotReconnectOutcome.UpdateTooLong, GrokBotReconnectPolicy.UpdateTooLongText, "the update still runs after 5 minutes: the prompt says so");
        Equal(true, updating.Host.Log.Contains("changed Updating"), "while Wingman waits for the update, the prompt says Grok Bot is updating");
        // The tray status follows: updating only while it waits, then what Wingman does next; before any update, Open
        // window's or Repair's own status stays.
        Equal(("Grok Bot is updating...", "Opening Grok Bot...", "Connecting Grok Bot...", (string?)null, (string?)null, (string?)null),
            (GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.Updating, true), GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.Working, true),
             GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.Connecting, true), GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.Working, false),
             GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.Connecting, false), GrokBotReconnectPolicy.StatusWhileOwned(GrokBotPromptState.CouldNotStart, true)),
            "tray status while Open window or Repair waits");
        var trayHost = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal((true, false), (trayHost.Contains("afterUpdate |= prompt.State == GrokBotPromptState.Updating;", StringComparison.Ordinal)
            && trayHost.Contains("GrokBotReconnectPolicy.StatusWhileOwned(prompt.State, afterUpdate) is { } text", StringComparison.Ordinal),
            trayHost.Contains("tray.SetBusy(true, \"Grok Bot is updating...\")", StringComparison.Ordinal)), "the tray takes its status from the prompt's state");

        var exitsLate = Run(new FakeControl(new FakeProcess(target, [])) { InstallerAt = check => check >= 1 });
        Ends(exitsLate, GrokBotReconnectOutcome.ExitTooLong, GrokBotReconnectPolicy.ExitTooLongText, "an update started but Grok Bot didn't finish closing: the prompt says so");

        var reopened = new FakeControl(new FakeProcess(target, []) { Exited = true }, new FakeProcess(new GrokBotProcess(Third, started.AddMinutes(1)), [])) { InstallerAt = check => check < 3 };
        Ends(Run(reopened), GrokBotReconnectOutcome.StillRunning, GrokBotReconnectPolicy.StillRunningText, "Grok Bot still runs after the update: nothing started, the prompt says so");
        Equal(false, reopened.Log.Any(line => line.StartsWith("start", StringComparison.Ordinal)), "and nothing was started");

        using var reload = new CancellationTokenSource();
        reload.Cancel();
        Ends(Run(new FakeControl(new FakeProcess(target, [])), reload.Token), GrokBotReconnectOutcome.Cancelled, GrokBotReconnectPolicy.StoppedText,
            "Reload Helpers during the wait: the prompt says Wingman stopped, never a raw cancel");

        var throws = Run(new FakeControl(new FakeProcess(target, []) { Exited = true }) { StartThrows = new IOException("Access is denied.") });
        Ends(throws, GrokBotReconnectOutcome.Failed, GrokBotReconnectPolicy.FailedText, "an error after the close: a plain reason");
        Equal(true, throws.Prompt.Quit?.Process == target, "and the prompt has the quit's proof for Open Grok Bot");

        var unconnected = Run(new FakeControl(new FakeProcess(target, []) { Exited = true }) { Starts = reopenedProcess }, connects: false, seen: reopenedWithPort);
        Ends(unconnected, GrokBotReconnectOutcome.NotConnected, GrokBotReconnectPolicy.OpenedNotConnectedText, "Wingman reopened it, but the Helpers didn't connect: the prompt says so",
            "Try again");
        Equal((GrokBotPromptStep.None, GrokBotReconnectPolicy.OpenedNotConnectedText, reopenedWithPort.Process),
            (unconnected.Prompt.Observe(reopenedWithPort with { Connected = false }, busy: false), unconnected.Prompt.Problem, unconnected.Prompt.Offer.Target),
            "the Grok Bot Wingman opened is the prompt's own: never read as opened without Wingman");
        Ends(Run(new FakeControl(new FakeProcess(target, []) { Exited = true }), connects: false), GrokBotReconnectOutcome.NotConnected,
            GrokBotReconnectPolicy.ClosedBeforeConnectText, "reopened, but Grok Bot closed while Wingman connected: the prompt says it closed");

        // "Wingman opened Grok Bot" only for the exact process Wingman started: another one open now is just open.
        Ends(Run(new FakeControl(new FakeProcess(target, []) { Exited = true }) { Starts = new GrokBotProcess(Third, started) }, connects: false, seen: reopenedWithPort),
            GrokBotReconnectOutcome.NotConnected, GrokBotReconnectPolicy.ConnectFailedText, "Wingman started a different process than the one open now: Grok Bot is open", "Try again");
        var unseen = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        var blindHost = new FakeHost(new FakeControl(new FakeProcess(target, []) { Exited = true }) { Starts = reopenedProcess }, none) { Connects = false, ObserveFails = true };
        Ends((unseen, blindHost, Act(unseen, GrokBotPromptStep.ApprovedClose, blindHost)), GrokBotReconnectOutcome.NotConnected, GrokBotReconnectPolicy.NotConnectedUnseenText,
            "the connect failed and Wingman couldn't look at Grok Bot: neutral words, no guess either way");
        // The prompt holds the exact Grok Bot Wingman started, so the next check, which sees it, never calls it another Grok Bot.
        Equal((reopenedWithPort.Process, null), (unseen.Offer.Target, unseen.Quit), "the prompt holds the Grok Bot Wingman started; the quit's proof was spent on it");
        Equal((GrokBotPromptStep.None, GrokBotReconnectPolicy.OpenedNotConnectedText, ("Try again", true, "Not now"), GrokBotReconnectPolicy.ApprovedTitle, reopenedWithPort.Process),
            (unseen.Observe(reopenedWithPort with { Connected = false }, busy: false), unseen.Problem, unseen.Buttons, unseen.Title, unseen.Offer.Target),
            "after a blind last look, the next check sees Wingman's own Grok Bot: no hand-over, never \"opened again without Wingman\"");
        Equal(GrokBotPromptStep.None, unseen.Observe(reopenedWithPort, busy: false), "its port comes up: the connect that failed for it isn't retried on its own");
        Equal(false, GrokBotReconnectPolicy.NotConnectedUnseenText.Contains("is open", StringComparison.Ordinal) || GrokBotReconnectPolicy.NotConnectedUnseenText.Contains("opened", StringComparison.Ordinal)
            || GrokBotReconnectPolicy.NotConnectedUnseenText.Contains("closed", StringComparison.Ordinal), "the neutral words say nothing about Grok Bot's state");
        var connectThrows = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        var throwingHost = new FakeHost(new FakeControl(new FakeProcess(target, []) { Exited = true }) { Starts = reopenedProcess }, reopenedWithPort) { ConnectThrows = new InvalidOperationException("The operation was canceled.") };
        Ends((connectThrows, throwingHost, Act(connectThrows, GrokBotPromptStep.ApprovedClose, throwingHost)), GrokBotReconnectOutcome.NotConnected, GrokBotReconnectPolicy.OpenedNotConnectedText,
            "reopened, but connecting the Helpers threw: the prompt says so, never the raw text", "Try again");

        // Showing the prompt fails after the run: the connect still runs, and the prompt ends in its own state.
        var showFails = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        var showHost = new FakeHost(new FakeControl(new FakeProcess(target, []) { Exited = true }), reopenedWithPort) { ShowThrowsIn = GrokBotPromptState.Connecting };
        Equal((GrokBotReconnectOutcome.Connected, GrokBotPromptState.Closed, true), (Act(showFails, GrokBotPromptStep.ApprovedClose, showHost).Outcome, showFails.State,
            showHost.Log.Contains("connect True")), "showing Connecting throws: the Helpers are still connected and the prompt closes");
        var showFailsLast = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        var lastHost = new FakeHost(new FakeControl(new FakeProcess(target, []) { Exited = true }), reopenedWithPort) { Connects = false, ShowThrowsIn = GrokBotPromptState.CouldNotStart };
        Equal((GrokBotReconnectOutcome.NotConnected, GrokBotPromptState.CouldNotStart), (Act(showFailsLast, GrokBotPromptStep.ApprovedClose, lastHost).Outcome, showFailsLast.State),
            "showing the last word throws: the run still ends with it, not on Working");
        // The connect fails only because Wingman stayed busy: the prompt says that, with Try again while Grok Bot is open.
        var busyConnect = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        var busyHost = new FakeHost(new FakeControl(new FakeProcess(target, []) { Exited = true }), reopenedWithPort) { ConnectBusy = true };
        var busyResult = Act(busyConnect, GrokBotPromptStep.ApprovedClose, busyHost);
        Equal((GrokBotReconnectOutcome.NotConnected, true, GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ConnectBusyText, ("Try again", true, "Not now")),
            (busyResult.Outcome, busyResult.WingmanBusy, busyConnect.State, busyConnect.Message, busyConnect.Buttons), "connect busy, Grok Bot open: Wingman was busy, Try again");
        Equal((GrokBotReconnectPolicy.ClosedBeforeConnectText, GrokBotReconnectPolicy.ConnectBusyText),
            (GrokBotReconnectPrompt.Ending(GrokBotPromptReason.NotConnected(true, busy: true), GrokBotHeldState.Gone).Text,
             GrokBotReconnectPrompt.Ending(GrokBotPromptReason.NotConnected(false, busy: true), GrokBotHeldState.Unknown).Text),
            "connect busy: gone, it closed before the connect; not seen, Wingman was busy");
        var works = Run(new FakeControl(new FakeProcess(target, []) { Exited = true }));
        Equal((GrokBotReconnectOutcome.Connected, GrokBotPromptState.Closed, "connected", true), (works.Result.Outcome, works.Prompt.State, works.Prompt.ClosedBecause,
            works.Host.Log.Contains("changed Connecting") && works.Host.Log.Contains("connect True")), "reopened and connected: says so until then, then closes");

        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var ensure = tray[tray.IndexOf("private async Task EnsureGrokBotReadyAsync", StringComparison.Ordinal)..tray.IndexOf("private void GrokBotReconnectTick()", StringComparison.Ordinal)];
        var afterYes = ensure[ensure.IndexOf("if (open.Step == GrokBotOpenStep.CloseRequested)", StringComparison.Ordinal)..];
        afterYes = afterYes[..(afterYes.IndexOf("return;", StringComparison.Ordinal) + 7)];
        Equal(true, afterYes.Contains("var prompt = grokPrompts.AfterYes(open.Closed, open.Since, DateTimeOffset.UtcNow);", StringComparison.Ordinal)
            && afterYes.IndexOf("ShowGrokPrompt(prompt);", StringComparison.Ordinal) < afterYes.IndexOf("await ActOnGrokPromptAsync(prompt, GrokBotPromptStep.ApprovedClose, session, deadline.Token);", StringComparison.Ordinal)
            && !afterYes.Contains("MessageBox", StringComparison.Ordinal), "after the Yes the tray shows the prompt, then runs it; no message box");
        // A cancel that came in while the question was open is checked right after the Yes: nothing is closed, and the
        // prompt says Wingman stopped, with its button.
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var yes = launcher.IndexOf("if (!confirmRelaunch()) return new(GrokBotOpenStep.Declined);", StringComparison.Ordinal);
        var check = launcher.IndexOf("if (cancellationToken.IsCancellationRequested) return new(GrokBotOpenStep.StoppedAfterYes, closed, stamp);", StringComparison.Ordinal);
        Equal(true, yes > 0 && check > yes && check < launcher.IndexOf("process.CloseMainWindow();", yes, StringComparison.Ordinal), "the cancel is checked between the Yes and the close");
        Equal(true, ensure.Contains("if (open.Step == GrokBotOpenStep.StoppedAfterYes)", StringComparison.Ordinal)
            && ensure.Contains("prompt.Finished(new(GrokBotReconnectOutcome.Cancelled), seen);", StringComparison.Ordinal), "and the tray shows the stop in the prompt");
        var stoppedAtYes = GrokBotReconnectPrompt.AfterYes(target, null, T0);
        stoppedAtYes.Finished(new(GrokBotReconnectOutcome.Cancelled), new GrokBotObservation(Second, false, Present, started));
        Equal((GrokBotPromptState.CouldNotStart, "Wingman stopped, so Grok Bot is still open.", true, ("Try again", true, "Not now")),
            (stoppedAtYes.State, stoppedAtYes.Problem, stoppedAtYes.Pressed, stoppedAtYes.Buttons), "stopped at the Yes, nothing closed: says so, with Try again");
        // Before any Yes nothing changed: Open window and Repair handle a cancel as they always did, with no extra box.
        var repair = tray[tray.IndexOf("private async Task RepairGrokBotAsync", StringComparison.Ordinal)..tray.IndexOf("private async Task EnsureGrokBotReadyAsync", StringComparison.Ordinal)];
        Equal((false, false, false), (ensure.Contains("catch (OperationCanceledException)", StringComparison.Ordinal), repair.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            tray.Contains("StoppedText =", StringComparison.Ordinal)), "no new stop boxes for Open window or Repair");

        // The guide says the same: the prompt keeps saying it is opening or connecting until the Helpers are connected, and
        // every other result after the user acted stays in that prompt with a reason and Open Grok Bot.
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "grok-bot-after-update.md"));
        var every = guide[guide.IndexOf("## Every result after you act", StringComparison.Ordinal)..guide.IndexOf("## Connecting to a Grok Bot that already has a port", StringComparison.Ordinal)];
        Equal(true, every.Contains("the prompt closes only when Wingman has connected the Helpers, or when you press **Cancel** or **Not now**", StringComparison.Ordinal)
            && every.Contains("It is never a message box, never a raw error text, and never **Reconnect** again.", StringComparison.Ordinal)
            && every.Contains("said Yes to `Open window` or `Repair`", StringComparison.Ordinal)
            && every.Contains("The words and the button come from Wingman's last look at the Grok Bot the prompt is for, the same way for every result.", StringComparison.Ordinal)
            && every.Contains("\"Wingman was busy, so Grok Bot is still open.\"", StringComparison.Ordinal)
            && GrokBotPromptReason.For(GrokBotReconnectOutcome.BusyTooLong).WhileOpen == "Wingman was busy, so Grok Bot is still open."
            && every.Contains("didn't close within 20 seconds", StringComparison.Ordinal) && GrokBotReconnectPolicy.NotClosedText.Contains("within 20 seconds", StringComparison.Ordinal)
            && every.Contains("wasn't closed within 10 minutes", StringComparison.Ordinal) && GrokBotReconnectPolicy.QuitTimedOutText.Contains("within 10 minutes", StringComparison.Ordinal)
            && every.Contains("\"" + GrokBotReconnectPolicy.ConnectFailedText[..GrokBotReconnectPolicy.ConnectFailedText.IndexOf(". ", StringComparison.Ordinal)] + ".\"", StringComparison.Ordinal)
            && guide.Contains("\"" + GrokBotReconnectPolicy.ConnectingText + "\"", StringComparison.Ordinal)
            && every.Contains("\"" + GrokBotReconnectPolicy.ClosedBeforeConnectText + "\"", StringComparison.Ordinal)
            && every.Contains("\"" + GrokBotReconnectPolicy.NotConnectedUnseenText[..GrokBotReconnectPolicy.NotConnectedUnseenText.IndexOf(". ", StringComparison.Ordinal)] + ".\" and guesses neither way", StringComparison.Ordinal)
            && every.Contains("While that Grok Bot is still open, the button is **Try again**", StringComparison.Ordinal)
            && every.Contains("Once that Grok Bot has closed, also if it closed just as Wingman stopped, the button is **Open Grok Bot**", StringComparison.Ordinal)
            && every.Contains("\"" + GrokBotReconnectPolicy.BusyTooLongText + "\"", StringComparison.Ordinal)
            && every.Contains("they say \"" + GrokBotReconnectPolicy.GoneText + "\"", StringComparison.Ordinal)
            && every.Contains("\"" + GrokBotReconnectPolicy.OpenedNotConnectedText[..GrokBotReconnectPolicy.OpenedNotConnectedText.IndexOf(". ", StringComparison.Ordinal)] + ".\"", StringComparison.Ordinal)
            && guide.Contains("\"" + GrokBotReconnectPolicy.ReopenedText + "\"", StringComparison.Ordinal)
            && guide.Contains("\"Grok Bot is updating...\"", StringComparison.Ordinal) && GrokBotReconnectPolicy.UpdatingText.StartsWith("Grok Bot is updating...", StringComparison.Ordinal)
            && guide.Contains("the same prompt window now follows that Grok Bot", StringComparison.Ordinal) && !guide.Contains("takes its place", StringComparison.Ordinal)
            && !guide.Contains("offers **Reconnect** for that new Grok Bot", StringComparison.Ordinal)
            && !guide.Contains("it offers **Reconnect** again", StringComparison.Ordinal) && !guide.Contains("goes back to **Reconnect**", StringComparison.Ordinal),
            "the guide says exactly what the prompt does after the user acted");
    }

    /// <summary>The tray's side of a prompt run, over a fake Grok Bot.</summary>
    private sealed class FakeHost(FakeControl control, params GrokBotObservation[] observations) : IGrokBotPromptHost
    {
        public readonly List<string> Log = [];
        private int seen;
        public GrokBotReconnectPrompt? Prompt;
        public bool ObserveFails;
        public GrokBotObservation Observe() => ObserveFails ? throw new InvalidOperationException("no snapshot") : observations[Math.Min(seen++, observations.Length - 1)];
        public bool AdoptListeningPort(int process) => false;
        public bool Busy { get; set; }
        public bool Installed { get; set; } = true;
        public bool SessionEnding { get; set; }
        public IGrokBotProcessControl Control(Action<GrokBotProcess> started)
        {
            control.OnStart = started;
            return control;
        }
        /// <summary>Showing the prompt throws once it is in this state.</summary>
        public GrokBotPromptState? ShowThrowsIn;
        /// <summary>What the prompt showed at each change: its state, words and buttons.</summary>
        public readonly List<(GrokBotPromptState? State, string? Message, (string? Primary, bool PrimaryEnabled, string Secondary)? Buttons)> Shown = [];
        public void Changed()
        {
            Log.Add("changed " + Prompt?.State);
            Shown.Add((Prompt?.State, Prompt?.Message, Prompt?.Buttons));
            if (Prompt?.State is { } state && state == ShowThrowsIn) throw new InvalidOperationException("window gone");
        }
        public bool Connects = true;
        /// <summary>Wingman stays busy, so the connect isn't tried.</summary>
        public bool ConnectBusy;
        public Exception? ConnectThrows;
        public Task<GrokBotConnectResult> ConnectAsync(bool started, CancellationToken cancellationToken)
        {
            Log.Add("connect " + started);
            return ConnectThrows is { } error ? throw error
                : Task.FromResult(ConnectBusy ? GrokBotConnectResult.Busy : Connects ? GrokBotConnectResult.Connected : GrokBotConnectResult.Failed);
        }
    }

    private static GrokBotReconnectResult Act(GrokBotReconnectPrompt prompt, GrokBotPromptStep step, FakeHost host, CancellationToken cancellationToken = default)
    {
        host.Prompt = prompt;
        var clock = DateTime.UnixEpoch;
        var steps = new GrokBotPromptSteps(prompt, host, Limits, () => clock, (wait, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock += wait;
            return Task.CompletedTask;
        });
        return GrokBotPromptRun.ActAsync(prompt, step, steps, cancellationToken).GetAwaiter().GetResult();
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }

    /// <summary>
    /// Starting Grok Bot gives back the exact process it started, or throws; there is no "started, but which one is unknown",
    /// so a relaunch always carries the Grok Bot it started and the prompt can say Wingman opened it.
    /// </summary>
    private static void AStartReturnsTheGrokBotItStartedOrThrows()
    {
        var start = typeof(IGrokBotProcessControl).GetMethod(nameof(IGrokBotProcessControl.Start))!;
        Equal(System.Reflection.NullabilityState.NotNull, new System.Reflection.NullabilityInfoContext().Create(start.ReturnParameter).ReadState,
            "IGrokBotProcessControl.Start never returns null");
        var target = new GrokBotProcess(Second, DateTime.UnixEpoch);
        var control = new FakeControl(new FakeProcess(target, []) { QuitsAfter = 2 });
        var clock = DateTime.UnixEpoch;
        var result = GrokBotRelaunch.RunAsync(target, control, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.Relaunched, control.Starts), (result.Kind, result.Started), "a relaunch carries the exact Grok Bot it started");
    }

    private static GrokBotRelaunch.ClosedProcess QuitProof(GrokBotProcess target)
    {
        var control = new FakeControl(new FakeProcess(target, []) { QuitsAfter = 2 }) { Free = false };
        var clock = DateTime.UnixEpoch;
        var result = GrokBotRelaunch.RunAsync(target, control, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        return result.Quit ?? throw new InvalidOperationException("no proof");
    }

    private sealed class Steps(params GrokBotObservation[] observations) : IGrokBotReconnectSteps
    {
        public readonly List<string> Log = [];
        private int seen;
        public bool Adopts, IsBusy;
        /// <summary>The one process whose port is usable, if any.</summary>
        public int? AdoptsOnly;
        public GrokBotRelaunchKind Result = GrokBotRelaunchKind.Relaunched;
        /// <summary>The proof the quit wait returns (null: it ended before the quit).</summary>
        public GrokBotRelaunch.ClosedProcess? RelaunchQuit;
        public GrokBotObservation Observe() => LastSeen = observations[Math.Min(seen++, observations.Length - 1)];
        /// <summary>What the last look saw: the prompt's last word is about it.</summary>
        public GrokBotObservation? LastSeen { get; private set; }
        public GrokBotProcess? Started { get; set; }
        public bool AdoptListeningPort(int process) { Log.Add("adopt? " + process); return Adopts || process == AdoptsOnly; }
        public bool Busy => IsBusy;
        public void WaitingForQuit() => Log.Add("waiting");
        /// <summary>Thrown by the relaunch, the start or Open Grok Bot's wait, after <see cref="During"/>.</summary>
        public Exception? Throws;
        /// <summary>Runs inside the relaunch, the start or Open Grok Bot's wait (the user's Cancel, say).</summary>
        public Action? During;
        private Task<GrokBotRelaunchResult> Ends(GrokBotRelaunchKind kind, GrokBotRelaunch.ClosedProcess? quit)
        {
            During?.Invoke();
            return Throws is { } error ? Task.FromException<GrokBotRelaunchResult>(error) : Task.FromResult(new GrokBotRelaunchResult(kind, quit));
        }
        public Task<GrokBotRelaunchResult> RelaunchAfterQuitAsync(GrokBotProcess target, CancellationToken cancellationToken)
        {
            Log.Add("relaunch " + target.Id);
            return Ends(Result, RelaunchQuit);
        }
        public Task<GrokBotRelaunchResult> StartAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
        {
            Log.Add("start " + quit.Process.Id);
            return Ends(Result, quit);
        }
        /// <summary>What Open Grok Bot without the quit's proof ends with (its proof: the quit, once it has one).</summary>
        public GrokBotRelaunchKind OpenAfterPress = GrokBotRelaunchKind.Relaunched;
        public GrokBotRelaunch.ClosedProcess? OpenAfterPressQuit;
        public Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, CancellationToken cancellationToken)
        {
            Log.Add("open after press " + target.Id);
            return Ends(OpenAfterPress, OpenAfterPressQuit);
        }
        /// <summary>What the wait after the update ends with.</summary>
        public GrokBotRelaunchKind AfterUpdate = GrokBotRelaunchKind.Relaunched;
        public void Updating(GrokBotRelaunch.ClosedProcess quit) => Log.Add("updating " + quit.Process.Id);
        public Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
        {
            Log.Add("after update " + quit.Process.Id);
            return Task.FromResult(new GrokBotRelaunchResult(AfterUpdate, quit));
        }
        public bool Connects = true;
        public Exception? ConnectThrows;
        public Task<GrokBotConnectResult> ConnectAsync(bool started, CancellationToken cancellationToken)
        {
            Log.Add("connect " + started);
            return ConnectThrows is { } error ? Task.FromException<GrokBotConnectResult>(error)
                : Task.FromResult(Connects ? GrokBotConnectResult.Connected : GrokBotConnectResult.Failed);
        }
        public bool Ending;
        public bool SessionEnding => Ending;
        public void Changed() { }
        /// <summary>What Open window or Repair's wait after the close request ends with (an update first: then <see cref="AfterUpdate"/>).</summary>
        public GrokBotRelaunchKind AfterClose = GrokBotRelaunchKind.Relaunched;
        public GrokBotRelaunch.ClosedProcess? AfterCloseQuit;
        public Task<GrokBotRelaunchResult> AfterApprovedCloseAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since, CancellationToken cancellationToken)
        {
            Log.Add("after close " + closed?.Id);
            return Ends(AfterClose, AfterCloseQuit);
        }
    }

    /// <summary>Reconnect re-checks first: gone, connected or adoptable means nothing restarts; busy keeps it.</summary>
    private static void ReconnectRechecksBeforeActing()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started);
        GrokBotReconnectOutcome Run(Steps steps) => GrokBotReconnectRun.RunAsync(offer, steps, CancellationToken.None).GetAwaiter().GetResult().Outcome;
        var unconnected = new GrokBotObservation(Second, false, Present, started);

        var gone = new Steps(new GrokBotObservation(Second, false, Present, started.AddSeconds(3)));
        Equal(GrokBotReconnectOutcome.Replaced, Run(gone), "same id, other run: replaced");
        Equal(0, gone.Log.Count, "nothing adopted, nothing relaunched");

        var connected = new Steps(unconnected with { Connected = true });
        Equal(GrokBotReconnectOutcome.AlreadyConnected, Run(connected), "connected by now");
        Equal(0, connected.Log.Count, "nothing touched");

        var adopted = new Steps(unconnected) { Adopts = true };
        Equal(GrokBotReconnectOutcome.Adopted, Run(adopted), "a usable port is adopted");
        Equal(true, adopted.Log is ["adopt? 200"], "no restart");

        var busy = new Steps(unconnected) { IsBusy = true };
        Equal(GrokBotReconnectOutcome.Busy, Run(busy), "busy: kept for later");
        Equal(true, busy.Log is ["adopt? 200"], "not waiting, nothing relaunched");

        var ok = new Steps(unconnected);
        Equal(GrokBotReconnectOutcome.Relaunched, Run(ok), "relaunched");
        Equal(true, ok.Log is ["adopt? 200", "waiting", "relaunch 200"], "asks the user to quit, then relaunches that exact process");

        foreach (var kind in Enum.GetValues<GrokBotRelaunchKind>())
            Equal(kind.ToString(), Run(new Steps(unconnected) { Result = kind }).ToString(), kind + " is reported as such");

        var quit = QuitProof(offer.Target);
        var open = new Steps(new GrokBotObservation(null, false, Present));
        Equal(GrokBotReconnectOutcome.Relaunched, GrokBotReconnectRun.OpenAsync(quit.Process, quit, open, default).GetAwaiter().GetResult().Outcome, "Open Grok Bot starts it");
        Equal(true, open.Log is ["start 200"], "without adopting or waiting for a quit");
        var openBusy = new Steps(new GrokBotObservation(null, false, Present)) { IsBusy = true };
        Equal(GrokBotReconnectOutcome.Busy, GrokBotReconnectRun.OpenAsync(quit.Process, quit, openBusy, default).GetAwaiter().GetResult().Outcome, "Open while busy: kept");
        Equal(0, openBusy.Log.Count, "nothing started");

        // The tray acts only through the run, never through the ordinary open, and has no balloon-click reconnect at all.
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var act = tray[tray.IndexOf("private async Task ActOnGrokPromptAsync(GrokBotReconnectPrompt prompt, GrokBotPromptStep step, GrokBotSessionWatch.Run session", StringComparison.Ordinal)..];
        act = act[..act.IndexOf("\n    }\n", StringComparison.Ordinal)];
        var run = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotPromptRun.cs"));
        Equal(true, act.Contains("await GrokBotPromptRun.ActAsync(prompt, step, steps, wait.Token,", StringComparison.Ordinal)
            && run.Contains("GrokBotReconnectRun.RunAsync(prompt.Offer, steps, cancellationToken)", StringComparison.Ordinal)
            && run.Contains("GrokBotReconnectRun.OpenAsync(prompt.Offer.Target, prompt.Quit, steps, cancellationToken, prompt.Offer)", StringComparison.Ordinal)
            && !act.Contains("grokLauncher.OpenAsync", StringComparison.Ordinal) && !act.Contains("EnsureGrokBotReadyAsync", StringComparison.Ordinal)
            && !run.Contains("OpenAsync(confirm", StringComparison.Ordinal), "acts only through the checked run");
        Equal(false, tray.Contains("BalloonTipClicked", StringComparison.Ordinal) || tray.Contains("BalloonTipClosed", StringComparison.Ordinal),
            "no balloon click or close handler");
        Equal(true, tray.Contains("tray.ShowBalloonTip(2500);", StringComparison.Ordinal) && tray.Contains("tray.ShowBalloonTip(4000);", StringComparison.Ordinal)
            && tray.Split("ShowBalloonTip(").Length - 1 == 3 && !tray.Contains("BalloonTipIcon", StringComparison.Ordinal), "the other balloons are as before");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal((1, 1, 1), (launcher.Split("private GrokBotProcess StartWithPort(Action<GrokBotProcess>? started)").Length - 1,
                launcher.Split("StartWithPort(null)").Length - 1, launcher.Split("StartWithPort(started)").Length - 1),
            "StartWithPort: defined once, called by the ordinary open and by the relaunch's proof-gated Start only");
        Equal(3, launcher.Split("StartWithPort(").Length - 1, "and nowhere else");
        Equal(true, launcher.Contains("public GrokBotProcess Start(GrokBotRelaunch.ClosedProcess proof) => launcher.StartWithPort(started);", StringComparison.Ordinal),
            "the reconnect starts Grok Bot only with the relaunch's proof");
        var reconnect = launcher[launcher.IndexOf("public IGrokBotProcessControl Control(", StringComparison.Ordinal)..];
        reconnect = reconnect[..reconnect.IndexOf("    private static DateTime? StartedUtc", StringComparison.Ordinal)];
        Equal(false, reconnect.Contains("CloseMainWindow", StringComparison.Ordinal) || reconnect.Contains("Kill", StringComparison.Ordinal),
            "the reconnect never closes Grok Bot");
    }

    /// <summary>
    /// The prompt window: topmost and never activated through its window styles, not Form.TopMost; placed in the working
    /// area's corner by the notification area wherever the taskbar is.
    /// </summary>
    private static void PromptWindowStaysOutOfTheWay()
    {
        var window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotPromptWindow.cs"));
        Equal(true, window.Contains("protected override bool ShowWithoutActivation => true;", StringComparison.Ordinal)
            && window.Contains("WsExTopmost | WsExToolWindow | WsExNoActivate", StringComparison.Ordinal)
            && window.Contains("ShowWindow(handle, SwShowNoActivate);", StringComparison.Ordinal)
            && window.Contains("SwpNoActivate", StringComparison.Ordinal) && !window.Contains("TopMost =", StringComparison.Ordinal),
            "the prompt window never takes focus and stays on top without Form.TopMost");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("window.ShowWithoutFocus();", StringComparison.Ordinal) && !tray.Contains("window.Show();", StringComparison.Ordinal),
            "shown without activation");

        var size = new System.Drawing.Size(300, 100);
        var screen = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        System.Drawing.Point At(System.Drawing.Rectangle working) => GrokBotPromptPlacement.Corner(screen, working, size, 12);
        Equal(new System.Drawing.Point(1608, 928), At(new(0, 0, 1920, 1040)), "bottom taskbar: bottom right");
        Equal(new System.Drawing.Point(1608, 52), At(new(0, 40, 1920, 1040)), "top taskbar: top right");
        Equal(new System.Drawing.Point(72, 968), At(new(60, 0, 1860, 1080)), "left taskbar: bottom left");
        Equal(new System.Drawing.Point(1548, 968), At(new(0, 0, 1860, 1080)), "right taskbar: bottom right, left of it");
        Equal(new System.Drawing.Point(1608, 968), At(screen), "auto-hidden taskbar: bottom right");

        Equal((360, 450, 540, 720, 360), (GrokBotPromptPlacement.Scaled(360, 96), GrokBotPromptPlacement.Scaled(360, 120),
            GrokBotPromptPlacement.Scaled(360, 144), GrokBotPromptPlacement.Scaled(360, 192), GrokBotPromptPlacement.Scaled(360, 0)),
            "the width scales with DPI: 100%, 125%, 150%, 200%, unknown");
        Equal(true, window.Contains("message.MaximumSize = new Size(Px(MessageWidth), 0);", StringComparison.Ordinal)
            && window.Contains("int Px(int logical) => GrokBotPromptPlacement.Scaled(logical, DeviceDpi);", StringComparison.Ordinal)
            && window.Contains("GrokBotPromptPlacement.Scaled(ScreenGap, DeviceDpi)", StringComparison.Ordinal)
            && !window.Contains("new Size(360, 0)", StringComparison.Ordinal), "the window uses the scaled sizes");
        Equal(true, window.Contains("var corner = GrokBotPromptPlacement.Corner(screen.Bounds, screen.WorkingArea,", StringComparison.Ordinal)
            && window.Contains("if (TrayScreen() is not { } screen) return;", StringComparison.Ordinal)
            && window.Contains("var taskbar = FindWindow(\"Shell_TrayWnd\", null);\n        return taskbar != IntPtr.Zero ? Screen.FromHandle(taskbar) : Screen.PrimaryScreen;", StringComparison.Ordinal)
            && window.Split("Screen.PrimaryScreen").Length - 1 == 1, "placed on the screen with the notification area, the primary screen only as a fallback");
    }

    /// <summary>
    /// Grok Bot exiting with a failure code (a crash, or ended from Task Manager) isn't the user's quit: Wingman starts
    /// nothing and the prompt says so, with Open Grok Bot for the user to choose. Only exit code 0 (the user's quit) is
    /// followed by a start.
    /// </summary>
    private static void ACrashIsNotReopened()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        var log = new List<string>();
        foreach (var code in new int?[] { unchecked((int)0xC0000005), 1, null })
        {
            var crashed = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = code });
            var result = Run(crashed);
            Equal((GrokBotRelaunchKind.Crashed, true), (result.Kind, result.Quit?.Process == target), $"exit code {code?.ToString() ?? "unknown"}: not the user's quit");
            Equal(true, crashed.Log is ["forward 200"], $"exit code {code?.ToString() ?? "unknown"}: nothing started");
        }
        var quit = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = 0 });
        Equal(GrokBotRelaunchKind.Relaunched, Run(quit).Kind, "exit code 0, the user's quit: started");

        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started));
        prompt.Reconnect(busy: false);
        prompt.WaitingForQuit();
        var crash = Run(new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2, Code = 3 }));
        prompt.Finished(GrokBotReconnectRunResult(crash), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.CrashedText), (prompt.State, prompt.Problem), "the prompt says why");
        Equal(true, prompt.Open(busy: false), "Open Grok Bot stays the user's choice");
    }

    /// <summary>
    /// The update check comes before the exit code: Grok Bot's installer may end it with a failure code after the user's
    /// quit, and that is an update (after the quit Wingman waits for it to finish), not a crash.
    /// </summary>
    private static void AnInstallersKillIsAnUpdateNotACrash()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        var log = new List<string>();
        // QuitsAfter 3: one check while it runs, then the one right after it exited.
        var installer = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = 1 }) { InstallerAt = check => check >= 1 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(installer).Kind, "an installer runs as it ends with a failure code: an update");
        Equal(true, installer.Log is ["forward 200"], "nothing started");
        var changed = new GrokBotExecutableStamp(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 200);
        var stamp = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = unchecked((int)0xC000013A) }) { StampAt = check => check >= 2 ? changed : new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100) };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(stamp).Kind, "Grok Bot.exe changed as it ended: an update");
        var gone = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = 1 }) { Missing = check => check >= 2 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(gone).Kind, "Grok Bot.exe unreadable as it ended: an update");
        var crash = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3, Code = 1 });
        Equal(GrokBotRelaunchKind.Crashed, Run(crash).Kind, "no installer and the same Grok Bot.exe: still a crash");
    }

    /// <summary>
    /// Windows began shutting down and another app cancelled it: the reconnect isn't lost. The wait reports SessionEnding
    /// even when the tray cancelled it for that, and the prompt stays for that process.
    /// </summary>
    private static void ACancelledShutdownKeepsThePrompt()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var log = new List<string>();
        var waiting = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 100 }) { EndingAt = _ => true };
        var result = GrokBotRelaunch.RunAsync(target, waiting, Limits, () => clock, (wait, _) => Task.CompletedTask, cancelled.Token).GetAwaiter().GetResult();
        Equal(GrokBotRelaunchKind.SessionEnding, result.Kind, "the tray cancels the wait as Windows begins to shut down: SessionEnding, not Cancelled");
        // Windows begins to shut down while the wait sleeps, and the tray cancels that sleep.
        foreach (var quits in new[] { 100, 2 })
        {
            var asleep = new FakeControl(new FakeProcess(target, log) { QuitsAfter = quits }, new FakeProcess(new GrokBotProcess(Third, started), log) { QuitsAfter = 100 }) { EndingAt = check => check >= 1 };
            var woken = GrokBotRelaunch.RunAsync(target, asleep, Limits, () => clock, (wait, _) => Task.FromException(new OperationCanceledException()), default).GetAwaiter().GetResult();
            Equal(GrokBotRelaunchKind.SessionEnding, woken.Kind, quits == 2 ? "a sleep cancelled for shutdown after the quit: SessionEnding" : "a sleep cancelled for shutdown: SessionEnding");
        }
        var plainCancel = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 100 });
        Equal(GrokBotRelaunchKind.Cancelled, GrokBotRelaunch.RunAsync(target, plainCancel, Limits, () => clock, (wait, _) => Task.CompletedTask, cancelled.Token).GetAwaiter().GetResult().Kind,
            "Cancel pressed: Cancelled");

        var before = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started));
        before.Reconnect(busy: false);
        before.WaitingForQuit();
        before.Finished(GrokBotReconnectRunResult(result), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.SessionEndingText), (before.State, before.Problem), "before the quit: the prompt says why");
        Equal(true, before.Open(busy: false), "and Open Grok Bot works (it asks for the quit again while Grok Bot still runs)");

        var after = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started));
        after.Reconnect(busy: false);
        after.WaitingForQuit();
        var quitThenEnding = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { EndingAt = check => check >= 1 };
        var ended = GrokBotRelaunch.RunAsync(target, quitThenEnding, Limits, () => clock, (wait, _) => Task.CompletedTask, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.SessionEnding, true), (ended.Kind, ended.Quit is not null), "after the quit: SessionEnding with the quit's proof");
        after.Finished(GrokBotReconnectRunResult(ended), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.SessionEndingText), (after.State, after.Problem), "after the quit: it says why");
        Equal(true, after.Open(busy: false), "and Open Grok Bot works");
    }

    /// <summary>The installer check is awaited off the UI thread; Wingman turning busy meanwhile still stops the start.</summary>
    private static void NothingIsAwaitedBetweenTheBusyCheckAndTheStart()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        var log = new List<string>();
        FakeControl? control = null;
        control = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { DuringInstallerCheck = check => { if (check >= 1) control!.Free = false; } };
        var result = GrokBotRelaunch.RunAsync(target, control, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        Equal(GrokBotRelaunchKind.BusyTooLong, result.Kind, "Wingman turned busy during the last installer check: not started");
        Equal(true, !control.Log.Any(line => line.StartsWith("start", StringComparison.Ordinal)), "nothing started");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("public Task<bool> InstallerRunningAsync() => Task.Run(launcher.InstallerRunning);", StringComparison.Ordinal),
            "the process listing runs on the thread pool");
    }

    /// <summary>A --type= child or a Grok Bot.exe running a script as Node is not Grok Bot's main process; the single-instance owner is preferred.</summary>
    private static void TheMainProcessIsGrokBotItself()
    {
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" --type=utility --utility-sub-type=node.mojom.NodeService"), "a utility child (0.78.1's local-exec daemon)");
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" \"C:\\Grok Bot\\resources\\box-computer\\main.cjs\""), "a script run as Node");
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" C:\\tools\\helper.MJS"), "any script, any case");
        Equal(true, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" --updated"), "Grok Bot itself");
        Equal(true, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" grokbot://open"), "a link it was opened with");
        var early = new GrokBotProcess(10, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
        var owner = new GrokBotProcess(20, new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        Equal(owner, GrokBotLaunchPolicy.ChooseMainProcess([early, owner], new HashSet<int> { 20 }), "the single-instance window's owner, even if started later");
        Equal(early, GrokBotLaunchPolicy.ChooseMainProcess([owner, early], new HashSet<int>()), "no owner known: the earliest started");
        Equal(null, GrokBotLaunchPolicy.ChooseMainProcess([], new HashSet<int> { 20 }), "none");
        StillRunningMeansGrokBotItself();
    }

    /// <summary>
    /// "Still running" (the reconnect's settle check and Open window's close wait) is any Grok Bot.exe in the session. In
    /// 0.78.1 every Grok Bot.exe besides the main one is its own <c>--type=</c> child (the local-exec daemon is a
    /// <c>--type=utility</c> child), and those end with it, so nothing is left out.
    /// </summary>
    private static void StillRunningMeansGrokBotItself()
    {
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var isRunning = launcher[launcher.IndexOf("private bool IsRunning()", StringComparison.Ordinal)..launcher.IndexOf("private Process[] Running()", StringComparison.Ordinal)];
        Equal(true, launcher.Contains("public bool AnyRunning() => launcher.IsRunning();", StringComparison.Ordinal)
            && isRunning.Contains("return processes.Length > 0;", StringComparison.Ordinal) && !isRunning.Contains("CommandLine", StringComparison.Ordinal),
            "the reconnect and Open window count every Grok Bot.exe in the session");
        Equal(false, typeof(GrokBotLaunchPolicy).GetMethods().Any(method => method.Name is "KeepsGrokBotRunning" or "RunsScriptOnly"), "no script exception is left");

        // Open window and Repair use the reconnect's installer guard before any start.
        var stamp = new GrokBotExecutableStamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100);
        Equal(false, GrokBotRelaunch.UpdateUnderWay(false, stamp, stamp), "no installer, same Grok Bot.exe");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(true, stamp, stamp), "an installer runs");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(false, null, stamp), "Grok Bot.exe missing");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(false, stamp with { Length = 101 }, stamp), "Grok Bot.exe changed");
        var open = launcher[launcher.IndexOf("public async Task<GrokBotOpen> OpenAsync", StringComparison.Ordinal)..launcher.IndexOf("private async Task ThrowIfUpdateUnderWayAsync", StringComparison.Ordinal)];
        var direct = open[open.IndexOf("if (running.Length == 0)", StringComparison.Ordinal)..open.IndexOf("return new(GrokBotOpenStep.Started);", StringComparison.Ordinal)];
        Equal((2, true, 2, true), (open.Split("ThrowIfUpdateUnderWayAsync(stamp)").Length, direct.Contains("await ThrowIfUpdateUnderWayAsync(stamp);\n                StartWithPort(null);", StringComparison.Ordinal),
            open.Split("StartWithPort(null)").Length, open.IndexOf("StartWithPort(null)", StringComparison.Ordinal) < open.IndexOf("confirmRelaunch()", StringComparison.Ordinal)),
            "Open window checks for an installer only right before a direct start, when no Grok Bot runs; with Grok Bot running it asks as before");
        // After the Yes it only asks Grok Bot to close; the close wait and the start are the prompt run's
        // (GrokBotPromptSteps.AfterApprovedCloseAsync, behaviour in OpenWindowsCloseWaitIsUpdateAware).
        var promptSteps = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotPromptRun.cs"));
        Equal(true, !open.Contains("AfterCloseRequestAsync", StringComparison.Ordinal)
            && open.Contains("return new(GrokBotOpenStep.CloseRequested, closed, stamp);", StringComparison.Ordinal)
            && promptSteps.Contains("GrokBotRelaunch.AfterCloseRequestAsync(closed, since, Control(), limits.Close, limits.Poll,", StringComparison.Ordinal),
            "after the Yes, Open window hands the close wait and the start to the prompt's run");

        // Start times come straight from GetProcessTimes in UTC.
        Equal(true, launcher.Contains("DateTime.FromFileTimeUtc(created)", StringComparison.Ordinal) && !launcher.Contains(".StartTime", StringComparison.Ordinal)
            && !launcher.Contains("FromFileTime(", StringComparison.Ordinal), "start times via FromFileTimeUtc");
    }

    private static GrokBotReconnectResult GrokBotReconnectRunResult(GrokBotRelaunchResult result) => new(result.Kind switch
    {
        GrokBotRelaunchKind.Crashed => GrokBotReconnectOutcome.Crashed,
        GrokBotRelaunchKind.SessionEnding => GrokBotReconnectOutcome.SessionEnding,
        _ => throw new InvalidOperationException(result.Kind.ToString()),
    }, result.Quit);

    /// <summary>While Windows shuts down or signs out, Wingman never starts Grok Bot: not during the quit wait, nor after it.</summary>
    private static void NothingStartsWhileWindowsShutsDown()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        var log = new List<string>();
        var duringWait = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 100 }) { EndingAt = check => check >= 5 };
        Equal(GrokBotRelaunchKind.SessionEnding, Run(duringWait).Kind, "during the 10-minute wait");
        Equal(true, duringWait.Log is ["forward 200"], "nothing started");
        // Grok Bot quits cleanly (exit code 0) as Windows shuts down: still nothing starts.
        var helper = new FakeProcess(new GrokBotProcess(Third, started), log) { QuitsAfter = 6 };
        var afterQuit = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }, helper) { EndingAt = check => check >= 2 };
        Equal(GrokBotRelaunchKind.SessionEnding, Run(afterQuit).Kind, "after the quit, while its other processes exit");
        Equal(true, !afterQuit.Log.Any(line => line.StartsWith("start", StringComparison.Ordinal)), "nothing started");
        var busy = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Free = false, EndingAt = check => check >= 2 };
        Equal(GrokBotRelaunchKind.SessionEnding, Run(busy).Kind, "while Wingman is busy");
        // Quit at once, nothing else running, Wingman free: the only check is the last one before the start.
        var last = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { EndingAt = check => check == 0 };
        Equal(GrokBotRelaunchKind.SessionEnding, Run(last).Kind, "at the final check before the start");
        Equal(true, !last.Log.Any(line => line.StartsWith("start", StringComparison.Ordinal)), "nothing started");
        var open = GrokBotRelaunch.StartWhenClearAsync(Run(new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2, Code = 5 })).Quit!, null,
            new FakeControl() { EndingAt = _ => true }, Limits, () => clock, (wait, _) => Task.CompletedTask, default).GetAwaiter().GetResult();
        Equal(GrokBotRelaunchKind.SessionEnding, open.Kind, "Open Grok Bot too");

        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;", StringComparison.Ordinal)
            && tray.Contains("Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;", StringComparison.Ordinal)
            && tray.Contains("        sessionWatch.SessionEnding();\n", StringComparison.Ordinal),
            "the tray tells the runs under way when Windows is shutting down or signing out");
        TheShutdownBelongsToTheRunsItStopped(tray);
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("public bool SessionEnding() => sessionEnding();", StringComparison.Ordinal)
            && launcher.Contains("public int? ExitCode => HasExited && GetExitCodeProcess(handle, out var code) ? unchecked((int)code) : null;", StringComparison.Ordinal),
            "the launcher passes it on and reads the real exit code from its process handle");
    }

    /// <summary>
    /// Windows doesn't say when another app cancels a shutdown, so the news is scoped to the runs under way: a run begun
    /// after a cancelled shutdown (the next Reconnect, Open Grok Bot, Open window or Repair) starts unmarked.
    /// </summary>
    private static void TheShutdownBelongsToTheRunsItStopped(string tray)
    {
        var watch = new GrokBotSessionWatch();
        var during = watch.Begin();
        var other = watch.Begin();
        watch.SessionEnding();
        Equal((true, true), (during.Ending, other.Ending), "every run under way is told");
        during.Dispose();
        other.Dispose();
        using var next = watch.Begin();
        Equal(false, next.Ending, "a run begun after a shutdown another app cancelled starts unmarked");
        using var later = watch.Begin();
        watch.SessionEnding();
        Equal((true, true), (next.Ending, later.Ending), "and is told about the next one");
        Equal(true, !tray.Contains("bool sessionEnding", StringComparison.Ordinal) && !tray.Contains("tray.sessionEnding", StringComparison.Ordinal)
            && tray.Split("using var session = sessionWatch.Begin();").Length - 1 == 2 && !tray.Contains("() => sessionEnding", StringComparison.Ordinal)
            && tray.Split("session.Ending").Length - 1 == 2
            && tray.Contains("public bool SessionEnding => session.Ending;", StringComparison.Ordinal)
            && tray.Contains("public IGrokBotProcessControl Control(Action<GrokBotProcess> started) => tray.grokLauncher.Control(() => !Busy, () => session.Ending, started);", StringComparison.Ordinal)
            && tray.Contains("new GrokBotPromptSteps(prompt, new TrayPromptHost(this, prompt, session, owned),", StringComparison.Ordinal)
            && tray.Contains("await ActOnGrokPromptAsync(prompt, GrokBotPromptStep.ApprovedClose, session, deadline.Token);", StringComparison.Ordinal),
            "the tray keeps no shutdown flag of its own: the prompt's run and Open window/Repair each begin their own");
    }

    /// <summary>The prompt is closed from its own click handlers: hidden at once, disposed after the handler returns.</summary>
    private static void ThePromptClosesAfterItsOwnClick()
    {
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var close = tray[tray.IndexOf("private void CloseGrokPromptWindow(bool now = false)", StringComparison.Ordinal)..];
        close = close[..close.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, close.Contains("window.Hide();\n        window.BeginInvoke(window.Dispose);", StringComparison.Ordinal)
            && close.Contains("if (now || !window.IsHandleCreated) { window.Dispose(); return; }", StringComparison.Ordinal)
            && !tray.Contains("window?.Dispose();", StringComparison.Ordinal)
            && tray.Contains("CloseGrokPrompt(now: true);", StringComparison.Ordinal) && tray.Split("now: true").Length - 1 == 1,
            "disposed later (BeginInvoke), except in Wingman's own Dispose");
    }

    /// <summary>grok-runtime.json is read and written under one lock, by the shortcut thread and the UI thread alike.</summary>
    private static void TheRuntimeFileHasOneWriterAtATime()
    {
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var reserve = launcher[launcher.IndexOf("public int ReservePortForShortcut()", StringComparison.Ordinal)..];
        reserve = reserve[..reserve.IndexOf("\n    }\n", StringComparison.Ordinal)];
        var record = launcher[launcher.IndexOf("public bool RecordPort(int port)", StringComparison.Ordinal)..];
        record = record[..record.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, reserve.Contains("lock (runtimeFile)", StringComparison.Ordinal) && record.Contains("lock (runtimeFile)", StringComparison.Ordinal)
            && launcher.Contains("lock (runtimeFile) return StartWithPortLocked(started);", StringComparison.Ordinal)
            && launcher.Split("AppRuntimePortFile.Write(").Length - 1 == 3 && launcher.Split("lock (runtimeFile)").Length - 1 == 3,
            "every write is under the lock");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, !tray.Contains("AppRuntimePortFile.Write(grokLauncher.RuntimePath", StringComparison.Ordinal)
            && tray.Contains("if (grokLauncher.RecordPort(port)) WingmanLog.Write(\"grok-bot-port-adopted\"", StringComparison.Ordinal),
            "adoption records the port through the launcher's lock");
        Equal(true, !launcher.Contains("using System.Net.NetworkInformation;", StringComparison.Ordinal)
            && !launcher.Contains("how long its helper processes may take to exit.</summary>\n    private static readonly HttpClient", StringComparison.Ordinal),
            "no unused using or stray doc comment");
    }

    /// <summary>
    /// Codex bring-forward, Open window and Host Action opens keep the old behaviour: SW_RESTORE every time, no flash. Only
    /// the Grok Bot reconnect keeps the window's size (restores only a minimized one) and flashes when refused the foreground.
    /// </summary>
    private static void BringingForwardKeepsTheWindowAsItIs()
    {
        static int? Show(bool minimized, bool keepSize) => CodexWingman.Core.Helpers.WindowsForegroundActivator.ShowCommandBeforeActivating(minimized, keepSize);
        Equal((int?)9, Show(minimized: true, keepSize: false), "old path, minimized: restored");
        Equal((int?)9, Show(minimized: false, keepSize: false), "old path, maximized or normal: SW_RESTORE as before");
        Equal((int?)9, Show(minimized: true, keepSize: true), "reconnect, minimized: restored");
        Equal((int?)null, Show(minimized: false, keepSize: true), "reconnect, maximized or normal: left as it is");
        var activator = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "Helpers", "WindowsForegroundActivator.cs"));
        var activate = activator[activator.IndexOf("private static bool Activate(IntPtr handle, bool keepSize)", StringComparison.Ordinal)..];
        activate = activate[..activate.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, activate.Split("ShowWindowAsync(").Length - 1 == 1
            && activate.Contains("if (ShowCommandBeforeActivating(keepSize && IsIconic(handle), keepSize) is { } command) _ = ShowWindowAsync(handle, command);", StringComparison.Ordinal),
            "the only show call goes through ShowCommandBeforeActivating");
        Equal(true, activator.Contains("public static bool ActivateWindow(IntPtr handle) => Activate(handle, keepSize: false);", StringComparison.Ordinal)
            && activator.Contains("public static bool BringForwardKeepingSize(IntPtr handle) => Activate(handle, keepSize: true);", StringComparison.Ordinal)
            && activator.Contains("if (keepSize) Flash(handle);", StringComparison.Ordinal) && activator.Split("Flash(handle)").Length - 1 == 1
            && activator.Contains("FlashWindowEx(ref info)", StringComparison.Ordinal), "ActivateWindow is the old always-restore, never-flash path");
        var process = activator[activator.IndexOf("public static Task<bool> ActivateProcessAsync(", StringComparison.Ordinal)..];
        process = process[..process.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, process.Contains("            ActivateWindow,\n", StringComparison.Ordinal) && !process.Contains("KeepingSize", StringComparison.Ordinal),
            "Host Action opens (ActivateProcessAsync, 20 tries): old path, always restore, never flash");
        var hostActions = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "Helpers", "HostActions.cs"));
        Equal(true, hostActions.Contains("WindowsForegroundActivator.ActivateProcessAsync(process, cancellationToken)", StringComparison.Ordinal)
            && !hostActions.Contains("KeepingSize", StringComparison.Ordinal), "HostActions uses the old path");
        var codex = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "CodexLauncher.cs"));
        Equal(true, codex.Contains("WindowsForegroundActivator.ActivateWindow(windows[0])", StringComparison.Ordinal) && !codex.Contains("KeepingSize", StringComparison.Ordinal),
            "Codex bring-forward: old path, always restore, never flash");
        var source = Path.Combine(RepositoryRoot(), "src");
        var flashing = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(file => File.ReadAllText(file).Split("WindowsForegroundActivator.BringForwardKeepingSize(").Length - 1);
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var bring = launcher[launcher.IndexOf("private sealed class ProcessHandle : IGrokBotProcessHandle", StringComparison.Ordinal)..];
        bring = bring[..bring.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal((1, true), (flashing, bring.Contains("WindowsForegroundActivator.BringForwardKeepingSize(window)", StringComparison.Ordinal)), "the one keep-size, flashing caller is the reconnect's bring-forward");
        var open = launcher[launcher.IndexOf("public async Task<GrokBotOpen> OpenAsync(", StringComparison.Ordinal)..];
        open = open[..open.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, open.Contains("WindowsForegroundActivator.ActivateWindow(window)", StringComparison.Ordinal), "Open window: old path, as before");
        Equal(false, launcher.Contains("ShowWindowAsync", StringComparison.Ordinal) || launcher.Contains("SW_RESTORE", StringComparison.Ordinal),
            "Grok Bot's launcher brings windows forward only through the shared activator");
    }

    /// <summary>
    /// The quit wait holds its own process handle (SYNCHRONIZE) from the snapshot until the exit and waits on it, so Windows
    /// can't give the id to another process meanwhile, and the start time comes from that same handle.
    /// </summary>
    private static void TheQuitWaitHoldsTheProcessItself()
    {
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var handle = launcher[launcher.IndexOf("private sealed class ProcessHandle : IGrokBotProcessHandle", StringComparison.Ordinal)..];
        handle = handle[..handle.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, handle.Contains("private readonly SafeProcessHandle handle;", StringComparison.Ordinal)
            && handle.Contains("OpenProcessHandle(Synchronize | ProcessQueryLimitedInformation, false, processId)", StringComparison.Ordinal)
            && handle.Contains("public bool HasExited => WaitForSingleObject(handle, 0) == WaitObject0;", StringComparison.Ordinal)
            && handle.Contains("new GrokBotProcess(processId, StartedUtc(handle))", StringComparison.Ordinal)
            && launcher.Contains("GetProcessTimes(handle, out var created", StringComparison.Ordinal)
            && handle.Contains("public void Dispose() => handle.Dispose();", StringComparison.Ordinal),
            "a real process handle, opened at the snapshot, waited on, released at the end");
        Equal(false, handle.Contains("process.HasExited", StringComparison.Ordinal) || handle.Contains("Process process)", StringComparison.Ordinal),
            "never a Process object's id-based exit check");
        Equal(true, launcher.Contains("private const uint Synchronize = 0x00100000;", StringComparison.Ordinal)
            && launcher.Contains("private const uint ProcessQueryLimitedInformation = 0x1000;", StringComparison.Ordinal)
            && launcher.Contains("[DllImport(\"kernel32.dll\", EntryPoint = \"OpenProcess\", SetLastError = true)]\n    private static extern SafeProcessHandle OpenProcessHandle(", StringComparison.Ordinal),
            "SYNCHRONIZE | QUERY_LIMITED_INFORMATION, into a SafeHandle");
        Equal(true, launcher.Contains("running.Select(process => ProcessHandle.Open(process.Id))", StringComparison.Ordinal)
            && launcher.Contains("finally { foreach (var process in running) process.Dispose(); }", StringComparison.Ordinal), "the snapshot opens one handle per process");
        var policy = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotReconnectPolicy.cs"));
        Equal(true, policy.Contains("held by an open process handle (SYNCHRONIZE) from the snapshot until it exits", StringComparison.Ordinal),
            "the doc promises what the code does");
    }

    /// <summary>An open prompt keeps checking its process after Grok Bot is uninstalled, so it closes when that process exits.</summary>
    private static void ThePromptFollowsItsProcessAfterAnUninstall()
    {
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var tick = tray[tray.IndexOf("private void GrokBotReconnectTick()", StringComparison.Ordinal)..];
        tick = tick[..tick.IndexOf("\n    }\n", StringComparison.Ordinal)];
        var observe = tick.IndexOf("prompt.Observe(observation,", StringComparison.Ordinal);
        const string Gate = "if (!GrokBotReconnectPolicy.ShouldCheck(grokPrompt is not null, installed,";
        Equal(true, tick.Contains("var installed = grokLauncher.IsInstalled();\n        " + Gate, StringComparison.Ordinal)
            && tick.Split("IsInstalled()").Length - 1 == 1 && tick.IndexOf(Gate, StringComparison.Ordinal) < observe,
            "not installed: stops only when no prompt is open");
        Equal(true, observe > 0 && tick.IndexOf("if (!installed || busy || grokPromptWait is not null || grokPrompt is { InRun: true }) return;", StringComparison.Ordinal) > observe,
            "nothing new is offered or adopted while it isn't installed, after the prompt's check");
        // And the prompt then closes when its process exits.
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started));
        prompt.Observe(new GrokBotObservation(null, false, Present), busy: false);
        Equal((GrokBotPromptState.Closed, "gone"), (prompt.State, prompt.ClosedBecause), "its process exited: closed");
    }

    /// <summary>
    /// The user opens Grok Bot again while Wingman waits to start it: Wingman starts nothing and closes the prompt quietly
    /// (no "still closing"); the new run's own prompt takes over.
    /// </summary>
    private static void AReopenDuringTheWaitStartsNothing()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var reopened = new GrokBotProcess(Third, started.AddMinutes(2));
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        var log = new List<string>();

        var mine = new FakeProcess(reopened, log);
        var during = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }, mine) { Main = _ => reopened };
        var before = clock;
        Equal(GrokBotRelaunchKind.Reopened, Run(during).Kind, "reopened by the user right after the quit: not started");
        Equal(true, clock - before < TimeSpan.FromSeconds(5) && during.Log is ["forward 200"], "noticed at once, nothing started");

        var whileBusy = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Free = false, Main = check => check < 3 ? null : reopened };
        Equal(GrokBotRelaunchKind.Reopened, Run(whileBusy).Kind, "reopened while Wingman was busy: not started");
        Equal(true, whileBusy.Log is ["forward 200"], "never started");

        var lastMoment = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Main = _ => reopened };
        Equal(GrokBotRelaunchKind.Reopened, Run(lastMoment).Kind, "reopened just before the start: not started");
        Equal(true, lastMoment.Log is ["forward 200"], "never started");

        var ownQuitOnly = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Main = _ => null };
        Equal(GrokBotRelaunchKind.Relaunched, Run(ownQuitOnly).Kind, "nobody reopened it: started as before");

        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started));
        prompt.Reconnect(busy: false);
        prompt.WaitingForQuit();
        prompt.Finished(new(GrokBotReconnectOutcome.Reopened), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText), (prompt.State, prompt.Problem),
            "after Reconnect the prompt never closes silently: it says Grok Bot was opened again (the tray moves it to that Grok Bot)");
    }

    /// <summary>No prompt over a full-screen app, game or presentation: it waits until that's over, like the idle gate.</summary>
    private static void NoPromptOverAFullScreenApp()
    {
        Equal(true, GrokBotReconnectPolicy.FullScreen(2) && GrokBotReconnectPolicy.FullScreen(3) && GrokBotReconnectPolicy.FullScreen(4) && GrokBotReconnectPolicy.FullScreen(7),
            "busy, full-screen Direct3D, presentation mode, a full-screen Store app");
        Equal(false, GrokBotReconnectPolicy.FullScreen(5) || GrokBotReconnectPolicy.FullScreen(1) || GrokBotReconnectPolicy.FullScreen(6),
            "accepting notifications, not present, quiet time: shown as usual");
        var policy = new GrokBotReconnectPolicy();
        var facts = new Facts();
        GrokBotReconnectOffer? At(double seconds, int? pid, bool connected, bool fullScreen) =>
            policy.Observe(new(pid, connected, Present, FullScreen: fullScreen), T0.AddSeconds(seconds), facts);
        At(0, First, true, false);
        At(2, null, false, false);
        Equal(true, At(10, Second, false, true) is null && At(300, Second, false, true) is null, "full screen: waits");
        Equal(true, At(302, Second, false, false) is not null, "then shown once it's over");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("GrokBotLauncher.FullScreenInFront());", StringComparison.Ordinal), "the tray observes it");
    }

    private sealed class FakeProcess(GrokBotProcess identity, List<string> log) : IGrokBotProcessHandle
    {
        public GrokBotProcess Identity => identity;
        public bool Exited;
        /// <summary>How many checks until the user quits it; null: never.</summary>
        public int? QuitsAfter;
        public bool HasExited
        {
            get
            {
                if (QuitsAfter is { } left) QuitsAfter = left - 1;
                if (QuitsAfter <= 0) { Exited = true; QuitsAfter = null; }
                return Exited;
            }
        }
        /// <summary>Its exit code once exited: 0 is the user's quit (closing Grok Bot's last window); null: Windows can't say.</summary>
        public int? Code = 0;
        public int? ExitCode => Exited ? Code : null;
        public void Dispose() => log.Add("dispose " + identity.Id);
    }

    private sealed class FakeControl(params FakeProcess[] processes) : IGrokBotProcessControl
    {
        public readonly List<string> Log = [];
        public int Snapshots;
        public bool Free = true;
        /// <summary>Checks before Wingman is free again (0: free); counted only while <see cref="Free"/> is false.</summary>
        public int FreeAfter = -1;
        public Func<int, bool>? InstallerAt;
        /// <summary>Grok Bot's main process now (null: none), by check.</summary>
        public Func<int, GrokBotProcess?>? Main;
        private int mainChecks;
        public GrokBotExecutableStamp? Stamp = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100);
        public Func<int, GrokBotExecutableStamp?>? StampAt;
        public Func<int, bool>? Missing;
        private int installerChecks, stampChecks;
        public IReadOnlyList<IGrokBotProcessHandle> Snapshot() { Snapshots++; return processes; }
        public void BringForward(IGrokBotProcessHandle process) => Log.Add("forward " + process.Identity.Id);
        public bool AnyRunning() => processes.Any(process => !process.Exited);
        public bool MayStart()
        {
            if (!Free && FreeAfter >= 0 && FreeAfter-- == 0) Free = true;
            return Free;
        }
        public GrokBotExecutableStamp? ExecutableStamp()
        {
            var check = stampChecks++;
            if (Missing?.Invoke(check) == true) return null;
            return StampAt is null ? Stamp : StampAt(check);
        }
        /// <summary>Runs while the installer check is awaited, as other tray work could.</summary>
        public Action<int>? DuringInstallerCheck;
        public Task<bool> InstallerRunningAsync()
        {
            var check = installerChecks++;
            DuringInstallerCheck?.Invoke(check);
            return Task.FromResult(InstallerAt?.Invoke(check) ?? false);
        }
        /// <summary>Windows shutting down or signing out, by check.</summary>
        public Func<int, bool>? EndingAt;
        private int endingChecks;
        public bool SessionEnding() => EndingAt?.Invoke(endingChecks++) ?? false;
        public GrokBotProcess? MainProcess() => Main?.Invoke(mainChecks++);
        /// <summary>Starting Grok Bot throws this (an error after the quit, such as a missing Grok Bot.exe).</summary>
        public Exception? StartThrows;
        /// <summary>The process the fake "starts" (a start returns the process or throws, as the real one does).</summary>
        public GrokBotProcess Starts = new(FakeStarted, DateTime.UnixEpoch.AddHours(5));
        /// <summary>Hears of the start the moment it happens, as the real control does.</summary>
        public Action<GrokBotProcess>? OnStart;
        /// <summary>Thrown once Grok Bot has started (writing the port file failed, say).</summary>
        public Exception? ThrowsAfterStart;
        public GrokBotProcess Start(GrokBotRelaunch.ClosedProcess proof)
        {
            if (StartThrows is { } error) throw error;
            Log.Add("start after " + proof.Process.Id);
            OnStart?.Invoke(Starts);
            if (ThrowsAfterStart is { } after) throw after;
            return Starts;
        }
    }

    private static readonly GrokBotRelaunchLimits Limits = GrokBotRelaunchLimits.Default;

    /// <summary>
    /// Grok Bot didn't close in time and is still open: Try again asks the user to close it again, through the same run as
    /// Reconnect, and only the user's close leads to a start. It never opens a second Grok Bot beside the open one.
    /// </summary>
    private static void TryAgainAsksForTheCloseAgain()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started);
        var open = new GrokBotObservation(Second, false, Present, started);
        var prompt = new GrokBotReconnectPrompt(offer);
        prompt.Reconnect(busy: false);
        prompt.Finished(new(GrokBotReconnectOutcome.ExitTooLong), open);
        Equal((GrokBotPromptState.CouldNotStart, true, "Try again"), (prompt.State, prompt.Quit is null, prompt.Buttons.Primary),
            "Grok Bot didn't close and is still open: Try again, with no quit's proof");
        Equal(true, prompt.Open(busy: false), "Try again is pressed");

        var processLog = new List<string>();
        var control = new FakeControl(new FakeProcess(offer.Target, processLog) { QuitsAfter = 3 }) { Starts = new(Third, started.AddMinutes(2)) };
        var host = new FakeHost(control, open, new GrokBotObservation(Third, true, Present, started.AddMinutes(2)));
        var result = Act(prompt, GrokBotPromptStep.Open, host);
        Equal(true, host.Shown.Contains((GrokBotPromptState.WaitingForQuit, GrokBotReconnectPolicy.QuitText, (null, false, "Cancel"))),
            "Try again asks again for Grok Bot's close, with Cancel");
        var asked = host.Shown.FindIndex(shown => shown.State == GrokBotPromptState.WaitingForQuit);
        Equal(true, asked >= 0 && host.Shown.Take(asked).All(shown => shown.State != GrokBotPromptState.Working),
            "it asks for the close before anything opens");
        Equal((GrokBotReconnectOutcome.Connected, 1, true), (result.Outcome, control.Log.Count(line => line.StartsWith("start", StringComparison.Ordinal)), control.Log.Contains("start after 200")),
            "after the user's close Wingman opens Grok Bot once, with the close's proof");
        Equal(true, processLog.Contains("dispose 200"), "the run held the open Grok Bot itself while it waited for its close");

        // Still open when the close wait ends: nothing opens beside it.
        var again = new GrokBotReconnectPrompt(offer);
        again.Reconnect(busy: false);
        again.Finished(new(GrokBotReconnectOutcome.QuitTimedOut), open);
        again.Open(busy: false);
        var stays = new FakeControl(new FakeProcess(offer.Target, [])) { Starts = new(Third, started.AddMinutes(2)) };
        var stayed = Act(again, GrokBotPromptStep.Open, new FakeHost(stays, open));
        Equal((GrokBotReconnectOutcome.QuitTimedOut, true), (stayed.Outcome, stays.Log.All(line => !line.StartsWith("start", StringComparison.Ordinal))),
            "Grok Bot stays open through the wait: it says so and starts nothing");
    }

    /// <summary>
    /// The run throws after Grok Bot started (writing the port file failed, say): the Grok Bot Wingman started is the
    /// prompt's own and the words say Wingman opened it, never that it was opened again without Wingman.
    /// </summary>
    private static void AnErrorAfterWingmansStartKeepsThatWingmanOpenedIt()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started);
        var mine = new GrokBotProcess(Third, started.AddMinutes(2));
        var other = new GrokBotProcess(Third + 1, started.AddMinutes(3));
        GrokBotObservation Seen(GrokBotProcess? process, bool connected = false) => new(process?.Id, connected, Present, process?.Started);
        // Every way a run can end after Wingman's start without connecting: an error, Wingman's stop, Windows shutting down.
        var endings = new (string Name, Exception Error, bool SessionEnding, GrokBotReconnectOutcome Outcome, string Words)[]
        {
            ("an error", new IOException("port file"), false, GrokBotReconnectOutcome.Failed, GrokBotReconnectPolicy.StartedThenFailedText),
            ("Wingman's stop", new OperationCanceledException(), false, GrokBotReconnectOutcome.Cancelled, GrokBotReconnectPolicy.StartedThenStoppedText),
            ("a shutdown", new OperationCanceledException(), true, GrokBotReconnectOutcome.SessionEnding, GrokBotReconnectPolicy.StartedThenEndingText),
        };
        GrokBotReconnectPrompt Run((string Name, Exception Error, bool SessionEnding, GrokBotReconnectOutcome Outcome, string Words) ending, GrokBotProcess? atTheEnd, bool blind,
            out GrokBotReconnectResult result)
        {
            var prompt = new GrokBotReconnectPrompt(offer);
            prompt.Reconnect(busy: false);
            var control = new FakeControl(new FakeProcess(offer.Target, []) { QuitsAfter = 2 }) { Starts = mine, ThrowsAfterStart = ending.Error };
            var host = new FakeHost(control, Seen(new(Second, started)), Seen(atTheEnd)) { SessionEnding = ending.SessionEnding, Prompt = prompt };
            var steps = new GrokBotPromptSteps(prompt, host, Limits, () => DateTime.UnixEpoch, (_, _) => Task.CompletedTask);
            result = GrokBotPromptRun.ActAsync(prompt, GrokBotPromptStep.Reconnect, blind ? new BlindAtTheEnd(steps) : steps, CancellationToken.None).GetAwaiter().GetResult();
            return prompt;
        }
        foreach (var ending in endings)
        foreach (var (look, atTheEnd, blind) in new (string, GrokBotProcess?, bool)[] { ("open", mine, false), ("gone", null, false), ("not seen", mine, true) })
        {
            var name = ending.Name + ", " + look;
            var prompt = Run(ending, atTheEnd, blind, out var result);
            // Wingman opened the Grok Bot it now holds, so the title is the neutral one, never "reopened without Wingman".
            var title = GrokBotReconnectPolicy.ApprovedTitle;
            Equal((ending.Outcome, mine), (result.Outcome, result.Started), name + ": the run kept the start it made");
            Equal((GrokBotPromptState.CouldNotStart, mine, true, title), (prompt.State, prompt.Offer.Target, prompt.Quit is null, prompt.Title),
                name + ": the prompt holds Wingman's Grok Bot, the close's proof is spent, the title is the neutral one");
            Equal(look switch { "gone" => (GrokBotReconnectPolicy.ClosedBeforeConnectText, "Open Grok Bot"), "open" => (ending.Words, "Try again"), _ => (ending.Words, "Open Grok Bot") },
                (prompt.Problem, prompt.Buttons.Primary), name + ": the words say Wingman opened Grok Bot");
            if (look == "gone") continue;
            Equal(GrokBotPromptStep.None, prompt.Observe(Seen(mine), busy: false), name + ": the next check hands nothing over");
            Equal((mine, ending.Words, "Try again", title), (prompt.Offer.Target, prompt.Problem, prompt.Buttons.Primary, prompt.Title),
                name + ": seen open, it is the Grok Bot Wingman opened");
            // No connect was tried, so once Wingman's Grok Bot listens with its port the next check connects it and the prompt
            // closes, with no words left behind.
            Equal(GrokBotPromptStep.Connect, prompt.Observe(Seen(mine, connected: true), busy: false), name + ": connected later, the next check connects it");
            var connect = new FakeHost(new FakeControl(), Seen(mine, connected: true)) { Prompt = prompt };
            var connected = Act(prompt, GrokBotPromptStep.Connect, connect);
            Equal((GrokBotReconnectOutcome.Connected, GrokBotPromptState.Closed, true), (connected.Outcome, prompt.State, connect.Log.Contains("connect False")),
                name + ": the Helpers connect and the prompt closes");
        }
        // Another Grok Bot open at the end: the one hand-over rule, with true words; its port coming up is connected.
        foreach (var ending in endings)
        {
            var prompt = Run(ending, other, false, out _);
            Equal((other, GrokBotReconnectPolicy.ReopenedText, "Try again"), (prompt.Offer.Target, prompt.Problem, prompt.Buttons.Primary),
                ending.Name + ", another Grok Bot open: handed over as opened again without Wingman");
            Equal(GrokBotPromptStep.None, prompt.Observe(Seen(other), busy: false), ending.Name + ", another Grok Bot: the next check changes nothing");
            Equal(GrokBotPromptStep.Connect, prompt.Observe(Seen(other, connected: true), busy: false), ending.Name + ", another Grok Bot comes up connected: it is connected");
            // Wingman's own Grok Bot in front again: the words and the title it had, never opened without Wingman.
            var back = Run(ending, other, false, out _);
            Equal(GrokBotPromptStep.HandOver, back.Observe(Seen(mine), busy: false), ending.Name + ", Wingman's Grok Bot in front again: the prompt follows it");
            Equal((mine, ending.Words, "Try again", GrokBotReconnectPolicy.ApprovedTitle), (back.Offer.Target, back.Problem, back.Buttons.Primary, back.Title),
                ending.Name + ", Wingman's Grok Bot in front again: what was true of it");
        }
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var start = launcher.IndexOf("Process.Start(start)", StringComparison.Ordinal);
        var heard = launcher.IndexOf("}, started);", start, StringComparison.Ordinal);
        Equal(true, start > 0 && heard > start && heard < launcher.IndexOf("AppRuntimePortFile.Write(RuntimePath, port);", start, StringComparison.Ordinal)
            && launcher.Contains("public GrokBotProcess Start(GrokBotRelaunch.ClosedProcess proof) => launcher.StartWithPort(started);", StringComparison.Ordinal),
            "the real start records Grok Bot the moment it started, before writing the port file");
    }

    /// <summary>
    /// Open window started Grok Bot, but an error right after (writing its port down, say) left it unconnected: no prompt
    /// calls it reopened without Wingman. Another Grok Bot is prompted as before.
    /// </summary>
    private static void AGrokBotWingmanStartedIsNeverOfferedAsReopened()
    {
        var started = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var facts = new Facts();
        // Open window's start: no one else to tell, and still recorded; a prompt's run is told after the record.
        Equal(Second, facts.Starts.Start(() => new GrokBotProcess(Second, started), null).Id, "the start's Grok Bot is returned");
        var heard = new List<bool>();
        var told = new GrokBotProcess(Third + 5, started);
        facts.Starts.Start(() => told, process => heard.Add(facts.Starts.Contains(process)));
        Equal("True", string.Join(",", heard), "a prompt's run hears of its start once, after it is recorded");
        var threw = false;
        try { facts.Starts.Start(() => throw new InvalidOperationException("Grok Bot did not start."), _ => heard.Add(false)); }
        catch (InvalidOperationException) { threw = true; }
        Equal((true, "True"), (threw, string.Join(",", heard)), "a start that fails records and tells nothing");
        Equal(true, facts.Starts.Contains(new GrokBotProcess(Second, started)), "a later start keeps the earlier ones");
        var lost = 0;
        for (var id = 0; id < 200; id++)
        {
            var next = new GrokBotProcess(5000 + id, started);
            facts.Starts.Start(() => next, null);
            if (!facts.Starts.Contains(new GrokBotProcess(5000 + id, started))) lost++;
        }
        Equal(0, lost, "the latest start is always kept");
        facts.Starts.Start(() => new GrokBotProcess(Second, started), null);
        var policy = new GrokBotReconnectPolicy();
        policy.Observe(new GrokBotObservation(First, true, Present, started.AddHours(-1)), T0, facts);
        GrokBotReconnectOffer? offer = null;
        for (var second = 2; second <= 60; second += 2)
            offer ??= policy.Observe(new GrokBotObservation(Second, false, Present, started), T0.AddSeconds(second), facts);
        Equal(true, offer is null, "Wingman's own Grok Bot, unconnected after an error: no reopened-without-Wingman prompt");
        for (var second = 62; second <= 120; second += 2)
            offer ??= policy.Observe(new GrokBotObservation(Third, false, Present, started.AddMinutes(1)), T0.AddSeconds(second), facts);
        Equal(Third, offer?.ProcessId, "a Grok Bot Wingman didn't start is prompted as before");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        var start = launcher.IndexOf("Process.Start(start)", StringComparison.Ordinal);
        // The launcher's one Process.Start is the start Starts.Start runs, with nothing around it that could skip the record.
        const string OneStart = """
                    var identity = Starts.Start(() =>
                    {
                        using var process = Process.Start(start) ?? throw new InvalidOperationException("Grok Bot did not start.");
                        return new GrokBotProcess(process.Id, StartedUtc(process));
                    }, started);
                    AppRuntimePortFile.Write(RuntimePath, port);
            """;
        Equal(true, launcher.Contains(OneStart.Replace("\r\n", "\n"), StringComparison.Ordinal) && launcher.Split("Process.Start(").Length - 1 == 1
            && launcher.Split("Starts.Start(").Length - 1 == 1 && launcher.Contains("public GrokBotStarts Starts { get; } = new();", StringComparison.Ordinal),
            "the one start Wingman makes is Starts.Start, always, before writing the port file, in the Starts the policy reads");
    }

    /// <summary>
    /// At every run's end and at the checks after (every state before and during a run is in
    /// <see cref="TheTitleAndTheMessageAgreeInEveryStateThePromptCanShow"/>): for every result, from every kind of offer, the
    /// title never says reopened without Wingman while the body says Wingman opened or is opening it; an update proven for
    /// the prompt keeps its title.
    /// </summary>
    private static void TheTitleNeverSaysWithoutWingmanWhileTheBodySaysWingmanOpenedIt()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var mine = new GrokBotProcess(Third, started.AddMinutes(2));
        var other = new GrokBotProcess(Third + 1, started.AddMinutes(3));
        GrokBotObservation Seen(GrokBotProcess? process, bool connected = false) => new(process?.Id, connected, Present, process?.Started);
        var checkedWords = 0;
        foreach (var how in new[] { GrokBotOpenedBy.WithoutWingman, GrokBotOpenedBy.AfterUpdate, GrokBotOpenedBy.Wingman })
        foreach (var outcome in Enum.GetValues<GrokBotReconnectOutcome>())
        foreach (var (look, end) in new (string, GrokBotObservation?)[] { ("open", Seen(mine)), ("gone", Seen(null)), ("not seen", null), ("another", Seen(other)) })
        {
            var name = $"{how} offer, {outcome}, {look}";
            var prompt = how == GrokBotOpenedBy.Wingman
                ? GrokBotReconnectPrompt.AfterYes(new(Second, started), null, T0)
                : new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, how, T0, started));
            if (how != GrokBotOpenedBy.Wingman) prompt.Reconnect(busy: false);
            prompt.Finished(new(outcome, Started: mine), end);
            void Check(string when)
            {
                if (prompt.State == GrokBotPromptState.Closed) return;
                var wingmanWords = prompt.Message.StartsWith("Wingman opened", StringComparison.Ordinal) || prompt.Message.StartsWith("Wingman is opening", StringComparison.Ordinal);
                if (wingmanWords) checkedWords++;
                Equal(false, wingmanWords && prompt.Title == GrokBotReconnectPolicy.ReopenedTitle, name + when + ": the title agrees with the body");
                if (prompt.Offer.Target == mine)
                    Equal(false, prompt.Message == GrokBotReconnectPolicy.ReopenedText || prompt.Message == GrokBotReconnectPolicy.ReplacedText, name + when + ": Wingman's Grok Bot is never called opened without Wingman");
                if (prompt.Offer.Target == mine)
                    Equal(how == GrokBotOpenedBy.AfterUpdate ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ApprovedTitle, prompt.Title,
                        name + when + ": Wingman's Grok Bot gets the neutral title, or the update's");
            }
            Check(" at the run's end");
            foreach (var later in new[] { Seen(mine), Seen(other), Seen(other, connected: true) })
            {
                prompt.Observe(later, busy: false);
                Check(" at a later check");
            }
        }
        Equal(true, checkedWords > 50, "the body said Wingman opened or is opening Grok Bot in many of them");
    }

    /// <summary>
    /// Every state the prompt can show, driven the way the run and the tray drive it: Reconnect, Open Grok Bot and Try again
    /// (also kept while Wingman is busy), the wait for the quit, Exited, the update's wait, every result with and without
    /// Wingman's start, and the checks between runs with every hand-over, from every kind of offer. In every state: while the
    /// message speaks for Wingman (opening Grok Bot, having opened it, connecting its Helpers, or failing to), the title is
    /// Grok Bot, or Grok Bot updated for a proven update, never reopened without Wingman; otherwise it says how the held Grok
    /// Bot opened (or Grok Bot, once Wingman's words have shown for this offer), and before any press it is the offer's title as
    /// it always was. Within one offer (until the prompt follows another Grok Bot than Wingman's start) the title changes at
    /// most once, together with the words or as Wingman's start becomes the Grok Bot it holds, and never goes back to a title it had.
    /// </summary>
    private static void TheTitleAndTheMessageAgreeInEveryStateThePromptCanShow()
    {
        // Written out here, not taken from the code under test: the words that speak for Wingman.
        var wingmanVoice = new HashSet<string>
        {
            GrokBotReconnectPolicy.OpeningText, GrokBotReconnectPolicy.ConnectingText,
            GrokBotReconnectPolicy.OpenedNotConnectedText, GrokBotReconnectPolicy.ConnectFailedText, GrokBotReconnectPolicy.NotConnectedUnseenText,
            GrokBotReconnectPolicy.ConnectBusyText, GrokBotReconnectPolicy.ClosedBeforeConnectText,
            GrokBotReconnectPolicy.StartedThenFailedText, GrokBotReconnectPolicy.StartedThenStoppedText, GrokBotReconnectPolicy.StartedThenEndingText,
        };
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offered = new GrokBotProcess(Second, started);
        var processes = new[] { offered, new GrokBotProcess(Third, started.AddMinutes(2)), new GrokBotProcess(Third + 1, started.AddMinutes(3)), new GrokBotProcess(Third + 2, started.AddMinutes(4)) };
        var proven = processes[3];
        var proofs = processes.ToDictionary(process => process, QuitProof);
        var outcomes = Enum.GetValues<GrokBotReconnectOutcome>();
        var seen = new Dictionary<string, int>();
        void Count(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        var states = 0;
        foreach (var how in new[] { GrokBotOpenedBy.WithoutWingman, GrokBotOpenedBy.AfterUpdate, GrokBotOpenedBy.Wingman })
        for (var walk = 0; walk < 1500; walk++)
        {
            var random = new Random(walk * 3 + (int)how);
            // The policy's proof: the offer's own Grok Bot when the offer was for a proven update, and one other Grok Bot.
            bool Proven(GrokBotProcess process) => process == proven || how == GrokBotOpenedBy.AfterUpdate && process == offered;
            var prompt = how == GrokBotOpenedBy.Wingman
                ? GrokBotReconnectPrompt.AfterYes(offered, null, T0, Proven)
                : new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, how, T0, started)) { UpdateProven = Proven };
            var clock = T0;
            var steps = new List<string>();
            var last = (Title: prompt.Title, Message: prompt.Message, Target: prompt.Offer.Target);
            var leftTheOffer = false;
            // The test's own account of the offer: Wingman's words seen since the last hand-over, and the titles shown in it.
            var spoke = false;
            var titles = new List<string> { prompt.Title };
            GrokBotProcess? startedNow = null;
            for (var step = 0; step < 14 && prompt.State != GrokBotPromptState.Closed; step++)
            {
                GrokBotObservation? Look()
                {
                    var pick = random.Next(processes.Length + 2);
                    if (pick == processes.Length) return null;
                    if (pick == processes.Length + 1) return new(null, false, Present);
                    var process = pick == 0 ? prompt.Offer.Target : processes[pick];
                    return new(process.Id, random.Next(5) == 0, Present, process.Started);
                }
                var busy = random.Next(3) == 0;
                var held = prompt.Offer.Target;
                startedNow = null;
                // The run's calls come only while a run is under way (a run only follows a press); the buttons and checks any time.
                var action = random.Next(9);
                if (action is >= 2 and <= 6 && !prompt.InRun) action = random.Next(2) == 0 ? random.Next(2) : 7;
                switch (action)
                {
                    case 0:
                        if (prompt.Reconnect(busy) || prompt.State == GrokBotPromptState.Working) Count(busy ? "Reconnect kept" : "Reconnect");
                        steps.Add("Reconnect");
                        break;
                    case 1:
                        var tryAgain = prompt.Buttons.Primary == "Try again";
                        if (prompt.Open(busy)) Count(tryAgain ? "Try again" : "Open Grok Bot");
                        else if (prompt.State == GrokBotPromptState.Working && prompt.Pressed) Count("press kept while busy");
                        steps.Add(tryAgain ? "Try again" : "Open");
                        break;
                    case 2:
                        prompt.WaitingForQuit();
                        if (prompt.State == GrokBotPromptState.WaitingForQuit) Count("waiting for the quit");
                        steps.Add("WaitingForQuit");
                        break;
                    case 3:
                        prompt.Exited(proofs.GetValueOrDefault(held) ?? QuitProof(held));
                        if (prompt.Message == GrokBotReconnectPolicy.OpeningText) Count("Exited, opening");
                        steps.Add("Exited");
                        break;
                    case 4:
                        prompt.Updating(random.Next(2) == 0 ? null : proofs.GetValueOrDefault(held) ?? QuitProof(held));
                        Count("updating");
                        steps.Add("Updating");
                        break;
                    case 5:
                    case 6:
                        var outcome = outcomes[random.Next(outcomes.Length)];
                        // A start returns the process or throws, so a relaunch always carries the one it started.
                        var start = outcome == GrokBotReconnectOutcome.Relaunched || random.Next(2) == 0 ? processes[1 + random.Next(processes.Length - 1)] : (GrokBotProcess?)null;
                        var quit = random.Next(3) == 0 ? proofs.GetValueOrDefault(held) : null;
                        var end = Look();
                        startedNow = start;
                        prompt.Finished(new(outcome, quit, start) { WingmanBusy = random.Next(4) == 0 }, end);
                        Count(start is null ? "result" : "result after Wingman's start");
                        // The prompt holds Wingman's start from now on, whatever it shows next.
                        leftTheOffer |= start is not null;
                        steps.Add($"Finished({outcome}, start {start?.Id}, end {end?.ProcessId})");
                        break;
                    default:
                        var current = Look() ?? new(null, false, Present);
                        clock = clock.AddSeconds(random.Next(2) == 0 ? 5 : 70);
                        var did = prompt.Observe(current, busy, clock);
                        if (did != GrokBotPromptStep.None) Count("check: " + did);
                        if (prompt.InRun || prompt.State == GrokBotPromptState.Working) Count("check: waits");
                        // The window shows Connecting before the connect's result.
                        if (did == GrokBotPromptStep.Connect) spoke |= wingmanVoice.Contains(prompt.Message);
                        if (did == GrokBotPromptStep.Connect) prompt.Finished(new(random.Next(2) == 0 ? GrokBotReconnectOutcome.NotConnected : GrokBotReconnectOutcome.Connected) { WingmanBusy = busy }, Look());
                        steps.Add($"Observe({current.ProcessId}, connected {current.Connected}, busy {busy}) -> {did}");
                        break;
                }
                if (prompt.State == GrokBotPromptState.Closed) break;
                states++;
                var name = $"{how} offer, walk {walk}: {string.Join(" > ", steps)}";
                var shown = (Title: prompt.Title, Message: prompt.Message, Target: prompt.Offer.Target);
                Count("state: " + prompt.State);
                // A new offer: the prompt followed another Grok Bot than the one Wingman just started (also back to the one it
                // held before that start).
                var newOffer = shown.Target != startedNow && (shown.Target != last.Target || startedNow is not null);
                if (newOffer)
                {
                    spoke = false;
                    titles.Clear();
                    Count("new offer");
                }
                spoke |= wingmanVoice.Contains(shown.Message);
                string ByHow(GrokBotOpenedBy opened) => opened switch
                {
                    GrokBotOpenedBy.AfterUpdate => GrokBotReconnectPolicy.UpdatedTitle,
                    GrokBotOpenedBy.Wingman => GrokBotReconnectPolicy.ApprovedTitle,
                    _ when spoke => GrokBotReconnectPolicy.ApprovedTitle,
                    _ => GrokBotReconnectPolicy.ReopenedTitle,
                };
                if (wingmanVoice.Contains(shown.Message))
                {
                    Count("Wingman's words");
                    Equal(prompt.Offer.Updated ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ApprovedTitle, shown.Title,
                        name + $": over \"{shown.Message}\" the title is Wingman's");
                }
                else
                {
                    if (spoke) Count("other words after Wingman's");
                    Equal(ByHow(prompt.Offer.OpenedBy), shown.Title,
                        name + $": over \"{shown.Message}\" the title says how the held Grok Bot opened, or Grok Bot once Wingman's words showed");
                }
                leftTheOffer |= shown.Target != offered;
                if (!leftTheOffer && !wingmanVoice.Contains(shown.Message))
                    Equal(ByHow(how), shown.Title, name + $": over \"{shown.Message}\" until the prompt first leaves the offered Grok Bot, the title says how that one opened");
                if (!prompt.Pressed)
                    Equal(how == GrokBotOpenedBy.AfterUpdate ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle, shown.Title, name + ": before any press, the offer's title");
                if (!newOffer && shown.Title != last.Title)
                {
                    Count("title change");
                    Equal(false, titles.Contains(shown.Title), name + $": within one offer the title never goes back to \"{shown.Title}\"");
                    if (startedNow is not null && shown.Message == last.Message) Count("title change at Wingman's start");
                    Equal(true, shown.Message != last.Message || startedNow is not null, name + ": within one offer the title changes only with the words or Wingman's start");
                }
                if (titles.Count == 0 || titles[^1] != shown.Title) titles.Add(shown.Title);
                Equal(true, titles.Count <= 2, name + $": within one offer the title changes at most once ({string.Join(" > ", titles)})");
                if (shown.Target != last.Target) Count("hand-over");
                last = shown;
            }
        }
        foreach (var must in new[]
        {
            "Reconnect", "Reconnect kept", "Try again", "Open Grok Bot", "press kept while busy", "waiting for the quit", "Exited, opening", "updating",
            "result", "result after Wingman's start", "check: HandOver", "check: Connect", "check: waits", "hand-over", "Wingman's words",
            "new offer", "title change", "other words after Wingman's",
            "state: Offered", "state: Working", "state: WaitingForQuit", "state: Updating", "state: CouldNotStart", "state: Connecting",
        })
            Equal(true, seen.GetValueOrDefault(must) > 20, $"the walks reach \"{must}\" ({seen.GetValueOrDefault(must)} times)");
        Equal(true, states > 20000, $"many states checked ({states})");
        // The everyday Reconnect, step by step: the title turns to Wingman's at the moment the words do, and stays.
        var reconnect = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started));
        var seenSteps = new List<(string, string)> { (reconnect.Title, reconnect.Message) };
        reconnect.Reconnect(busy: false);
        seenSteps.Add((reconnect.Title, reconnect.Message));
        reconnect.WaitingForQuit();
        seenSteps.Add((reconnect.Title, reconnect.Message));
        reconnect.Exited(proofs[offered]);
        seenSteps.Add((reconnect.Title, reconnect.Message));
        reconnect.Finished(new(GrokBotReconnectOutcome.Relaunched, proofs[offered], processes[1]), new(Third, false, Present, processes[1].Started));
        seenSteps.Add((reconnect.Title, reconnect.Message));
        Equal(string.Join(" | ", new[]
            {
                (GrokBotReconnectPolicy.ReopenedTitle, GrokBotReconnectPolicy.PromptText), (GrokBotReconnectPolicy.ReopenedTitle, GrokBotReconnectPolicy.PromptText),
                (GrokBotReconnectPolicy.ReopenedTitle, GrokBotReconnectPolicy.QuitText), (GrokBotReconnectPolicy.ApprovedTitle, GrokBotReconnectPolicy.OpeningText),
                (GrokBotReconnectPolicy.ApprovedTitle, GrokBotReconnectPolicy.OpeningText),
            }), string.Join(" | ", seenSteps), "Reconnect: offered, working, waiting for the quit, Exited, the start");
        // The main update path: Grok Bot is closed and its installer starts at the same moment, so Wingman's opening words
        // show while it looks, then the update's. The title turns once, with Wingman's words, and stays.
        var update = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, GrokBotOpenedBy.WithoutWingman, T0, started));
        var updateSteps = new List<(string, string)> { (update.Title, update.Message) };
        update.Reconnect(busy: false);
        update.WaitingForQuit();
        updateSteps.Add((update.Title, update.Message));
        update.Exited(proofs[offered]);
        updateSteps.Add((update.Title, update.Message));
        update.Updating(proofs[offered]);
        updateSteps.Add((update.Title, update.Message));
        update.Finished(new(GrokBotReconnectOutcome.Relaunched, proofs[offered], processes[1]), new(Third, false, Present, processes[1].Started));
        updateSteps.Add((update.Title, update.Message));
        Equal(string.Join(" | ", new[]
            {
                (GrokBotReconnectPolicy.ReopenedTitle, GrokBotReconnectPolicy.PromptText), (GrokBotReconnectPolicy.ReopenedTitle, GrokBotReconnectPolicy.QuitText),
                (GrokBotReconnectPolicy.ApprovedTitle, GrokBotReconnectPolicy.OpeningText), (GrokBotReconnectPolicy.ApprovedTitle, GrokBotReconnectPolicy.UpdatingText),
                (GrokBotReconnectPolicy.ApprovedTitle, GrokBotReconnectPolicy.OpeningText),
            }), string.Join(" | ", updateSteps), "update at the close: the title turns once and doesn't flicker");
        var window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotPromptWindow.cs"));
        Equal(true, window.Contains("Text = prompt.Title;", StringComparison.Ordinal) && !window.Contains("Offer.Title", StringComparison.Ordinal),
            "the window shows only the prompt's one title");
    }

    /// <summary>The run's steps, but the look after the run can't see Grok Bot.</summary>
    private sealed class BlindAtTheEnd(GrokBotPromptSteps inner) : IGrokBotReconnectSteps
    {
        private bool ended;
        public GrokBotObservation Observe() => ended ? throw new InvalidOperationException("no snapshot") : inner.Observe();
        public bool AdoptListeningPort(int process) => inner.AdoptListeningPort(process);
        public bool Busy => inner.Busy;
        public void WaitingForQuit() => inner.WaitingForQuit();
        public async Task<GrokBotRelaunchResult> RelaunchAfterQuitAsync(GrokBotProcess target, CancellationToken cancellationToken)
        {
            try { return await inner.RelaunchAfterQuitAsync(target, cancellationToken); }
            finally { ended = true; }
        }
        public Task<GrokBotRelaunchResult> StartAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken) => inner.StartAsync(quit, cancellationToken);
        public Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, CancellationToken cancellationToken) => inner.OpenAfterPressAsync(target, cancellationToken);
        public void Updating(GrokBotRelaunch.ClosedProcess quit) => inner.Updating(quit);
        public Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken) => inner.AfterUpdateAsync(quit, cancellationToken);
        public Task<GrokBotConnectResult> ConnectAsync(bool started, CancellationToken cancellationToken) => inner.ConnectAsync(started, cancellationToken);
        public Task<GrokBotRelaunchResult> AfterApprovedCloseAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since, CancellationToken cancellationToken) =>
            inner.AfterApprovedCloseAsync(closed, since, cancellationToken);
        public bool SessionEnding => inner.SessionEnding;
        public void Changed() => inner.Changed();
        public GrokBotProcess? Started => inner.Started;
    }

    /// <summary>
    /// The last check before the start: a Grok Bot.exe that appeared while Wingman waited to be free (not Grok Bot's main
    /// window, so it isn't a reopen) means Grok Bot isn't closed, and nothing starts.
    /// </summary>
    private static void AGrokBotThatAppearsWhileWingmanIsBusyStartsNothing()
    {
        var target = new GrokBotProcess(Second, new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc));
        var quit = QuitProof(target);
        var late = new FakeProcess(new(Third, new DateTime(2026, 1, 1, 9, 0, 30, DateTimeKind.Utc)), []) { Exited = true };
        var control = new FakeControl(late) { Free = false, FreeAfter = 3, Starts = new(Third + 1, DateTime.UnixEpoch) };
        var appeared = false;
        control.DuringInstallerCheck = check => { if (check == 1) { late.Exited = false; appeared = true; } };
        var clock = DateTime.UnixEpoch;
        var result = GrokBotRelaunch.StartWhenClearAsync(quit, control.ExecutableStamp(), control, Limits, () => clock,
            (wait, _) => { clock += wait; return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, appeared && control.MainProcess() is null, "a non-main Grok Bot.exe appeared while Wingman was busy");
        Equal((GrokBotRelaunchKind.StillRunning, true, true), (result.Kind, ReferenceEquals(result.Quit, quit), result.Started is null),
            "Wingman is free, but a Grok Bot runs: still running, the proof kept");
        Equal(true, control.Log.All(line => !line.StartsWith("start", StringComparison.Ordinal)), "nothing started");

        // The same wait with no Grok Bot appearing starts Grok Bot once: the check is what stops it.
        var clear = new FakeControl(new FakeProcess(new(Third, DateTime.UnixEpoch), []) { Exited = true }) { Free = false, FreeAfter = 3, Starts = new(Third + 1, DateTime.UnixEpoch) };
        var started = GrokBotRelaunch.StartWhenClearAsync(quit, clear.ExecutableStamp(), clear, Limits, () => clock,
            (wait, _) => { clock += wait; return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.Relaunched, true), (started.Kind, clear.Log is ["start after 200"]), "with nothing open, the same wait starts Grok Bot once");
    }

    /// <summary>
    /// The user's quit started Grok Bot's update. Wingman waits up to 5 minutes until no installer runs and Grok Bot.exe
    /// stays unchanged, then starts it through the same guarded start; never while an installer runs, never twice, and
    /// nothing when Grok Bot was opened meanwhile or Windows is shutting down.
    /// </summary>
    private static void AnUpdateTheQuitStartedIsWaitedFor()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var quit = QuitProof(target);
        var clock = DateTime.UnixEpoch;
        var polls = 0;
        GrokBotRelaunchResult After(FakeControl control, CancellationToken token = default) => GrokBotRelaunch.AfterUpdateAsync(quit, control, Limits, () => clock,
            (wait, _) => { if (++polls > 10_000) throw new InvalidOperationException("the update wait never ends"); clock += wait; return Task.CompletedTask; },
            token).GetAwaiter().GetResult();
        Equal((TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5)), (Limits.Update, Limits.Stable), "the update limits");

        // The installer runs for its first 30 checks; Grok Bot.exe is missing, then rewritten, while it does.
        var newExe = new GrokBotExecutableStamp(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 120);
        var installs = new FakeControl { InstallerAt = check => check < 30, StampAt = check => check < 20 ? null : newExe };
        var installerEnded = DateTime.MinValue;
        installs.DuringInstallerCheck = check => { if (check == 30) installerEnded = clock; };
        var before = clock;
        var done = After(installs);
        Equal((GrokBotRelaunchKind.Relaunched, true), (done.Kind, ReferenceEquals(done.Quit, quit)), "the update finished: Grok Bot started, with the quit's proof");
        Equal(true, installs.Log is ["start after 200"], "started once");
        Equal(true, clock - installerEnded >= Limits.Stable && clock - before < TimeSpan.FromMinutes(1),
            "only after the installer ended and Grok Bot.exe stayed unchanged for 5 s");

        var forever = new FakeControl { InstallerAt = _ => true };
        before = clock;
        var tooLong = After(forever);
        Equal((GrokBotRelaunchKind.UpdateTooLong, true), (tooLong.Kind, tooLong.Quit is not null), "an installer still running at the limit: not started");
        Equal(true, clock - before >= Limits.Update && clock - before < Limits.Update + TimeSpan.FromSeconds(5), "after about 5 minutes");
        Equal(0, forever.Log.Count, "never started while an installer ran");

        var stamps = 0;
        var churning = new FakeControl { StampAt = _ => new(DateTime.UnixEpoch, stamps++) };
        Equal(GrokBotRelaunchKind.UpdateTooLong, After(churning).Kind, "Grok Bot.exe keeps changing: not started");
        Equal(0, churning.Log.Count, "never started");

        // The installer stops, then starts again just as Wingman is about to start Grok Bot: it goes back to waiting.
        var again = new FakeControl { InstallerAt = check => check is < 3 or (>= 9 and < 15) };
        var lastCheck = -1;
        again.DuringInstallerCheck = check => lastCheck = check;
        Equal(GrokBotRelaunchKind.Relaunched, After(again).Kind, "a second installer run: waited for too, then started");
        Equal((true, true), (again.Log is ["start after 200"], lastCheck >= 15), "started once, after both runs ended");

        var reopened = new FakeControl { InstallerAt = check => check < 5, Main = check => check >= 3 ? new GrokBotProcess(Third, started.AddMinutes(1)) : null };
        Equal((GrokBotRelaunchKind.Reopened, true), (After(reopened).Kind, reopened.Log.Count == 0), "Grok Bot opened during the update: nothing started");

        var ending = new FakeControl { InstallerAt = _ => true, EndingAt = check => check >= 4 };
        Equal((GrokBotRelaunchKind.SessionEnding, 0), (After(ending).Kind, ending.Log.Count), "Windows shutting down during the update: nothing started");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var cancelled = new FakeControl();
        Equal((GrokBotRelaunchKind.Cancelled, 0), (After(cancelled, cancel.Token).Kind, cancelled.Log.Count), "Cancel: nothing started");

        // The installer starts as Grok Bot exits, between the quit wait's checks: that is after the user's quit, so the
        // result carries the proof (the run then waits for the update instead of giving up).
        var exiting = new FakeProcess(target, []);
        var race = new FakeControl(exiting) { InstallerAt = _ => true };
        race.DuringInstallerCheck = _ => exiting.Exited = true;
        var raced = GrokBotRelaunch.RunAsync(target, race, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.UpdateRunning, true), (raced.Kind, raced.Quit is not null), "an installer seen as Grok Bot exits: an update after the quit");

        // Grok Bot 0.78.1 starts its installer while still shutting down (about 0.5 to 4 s): the run keeps waiting for that
        // exact process, then takes the update path with the proof, whatever its exit code (the installer may end it).
        var overlap = new FakeControl(new FakeProcess(target, []) { QuitsAfter = 4, Code = 1 }) { InstallerAt = _ => true };
        var overlapped = GrokBotRelaunch.RunAsync(target, overlap, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.UpdateRunning, (object?)target), (overlapped.Kind, (object?)overlapped.Quit?.Process),
            "an installer while Grok Bot is still closing: waited for its exit, then the update with the proof");
        Equal(true, overlap.Log.All(line => !line.StartsWith("start", StringComparison.Ordinal)), "nothing started by the quit wait itself");
        var notYet = new FakeControl(new FakeProcess(target, [])) { InstallerAt = _ => true };
        var early = GrokBotRelaunch.RunAsync(target, notYet, Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.ExitTooLong, false), (early.Kind, early.Quit is not null), "an installer while Grok Bot never closes: no proof, the prompt says so");
    }

    /// <summary>
    /// Every /// block in the Grok Bot sources has one summary and no loose text after it, so the comment reads on its own
    /// member (a summary moved off its method, or text left outside the tags, fails here).
    /// </summary>
    private static void DocCommentsStayOnTheirMember()
    {
        var root = RepositoryRoot();
        var files = new[] { Path.Combine("src", "CodexWingman", "GrokBotLauncher.cs"), Path.Combine("src", "CodexWingman.Core", "GrokBotReconnectPolicy.cs"),
            Path.Combine("src", "CodexWingman", "TrayApplicationContext.cs"), Path.Combine("src", "CodexWingman", "GrokBotPromptWindow.cs") };
        foreach (var file in files)
        {
            var block = new List<(int Line, string Text)>();
            var lines = File.ReadAllLines(Path.Combine(root, file));
            for (var index = 0; index <= lines.Length; index++)
            {
                var text = index < lines.Length ? lines[index].Trim() : "";
                if (text.StartsWith("///", StringComparison.Ordinal)) { block.Add((index + 1, text[3..].Trim())); continue; }
                if (block.Count == 0) continue;
                Equal(true, block.Count(entry => entry.Text.StartsWith("<summary>", StringComparison.Ordinal)) <= 1, $"{file}:{block[0].Line}: one summary per member");
                var afterSummary = false;
                var open = false;
                foreach (var (line, entry) in block)
                {
                    if (afterSummary && !open && entry.Length > 0)
                    {
                        Equal(true, entry.StartsWith('<'), $"{file}:{line}: no loose text after </summary>");
                        open = !entry.EndsWith("/>", StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(entry, "</(param|typeparam|returns|remarks|exception|example|value)>$");
                    }
                    else if (afterSummary && open && System.Text.RegularExpressions.Regex.IsMatch(entry, "</(param|typeparam|returns|remarks|exception|example|value)>$")) open = false;
                    if (entry.Contains("</summary>", StringComparison.Ordinal)) afterSummary = true;
                }
                block.Clear();
            }
        }
    }

    /// <summary>
    /// Open window and Repair: a slow Yes/No answer can't use up the deadline and cut off the update wait after it.
    /// </summary>
    private static void TheAnswerTimeDoesntCount()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var answer = GrokBotAnswerTime.AskOffTheClock(deadline, TimeSpan.FromSeconds(30), () => { Thread.Sleep(400); return true; });
        Thread.Sleep(100);
        Equal((true, false), (answer, deadline.IsCancellationRequested), "a slow answer: the deadline didn't run meanwhile");
        using var after = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        GrokBotAnswerTime.AskOffTheClock(after, TimeSpan.FromMilliseconds(50), () => false);
        Thread.Sleep(500);
        Equal(true, after.IsCancellationRequested, "after the answer the limit runs again, from the answer");
        Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5), GrokBotAnswerTime.AfterYes(Limits),
            "after the Yes: 2 minutes plus Grok Bot's exit and its update");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("Func<bool> confirm = () => GrokBotAnswerTime.AskOffTheClock(deadline, GrokBotAnswerTime.AfterYes(GrokBotRelaunchLimits.Default), ask);", StringComparison.Ordinal),
            "Open window asks with the clock paused");
        Equal(true, tray.Contains("using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));\n        activeInteractiveOperation = deadline;\n        SetBusy(true, \"Opening Grok Bot...\");", StringComparison.Ordinal),
            "and starts with its usual 2 minutes");
    }

    /// <summary>
    /// Open window or Repair after the user's Yes: up to 20 s for Grok Bot to close. An update that close starts, during the
    /// wait or right after it, is waited for and Grok Bot reopens after it, instead of "left it running". Otherwise the
    /// caller starts Grok Bot (null), or nothing starts because it didn't close or the user cancelled.
    /// </summary>
    private static void OpenWindowsCloseWaitIsUpdateAware()
    {
        var target = new GrokBotProcess(Second, new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc));
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Wait(FakeControl control, FakeProcess? closes = null, int closesAtPoll = 0, CancellationToken token = default)
        {
            var polls = 0;
            var updating = 0;
            var result = GrokBotRelaunch.AfterCloseRequestAsync(target, control.Stamp, control, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(250),
                () => { updating++; control.Log.Add("updating"); }, Limits, () => clock,
                (wait, _) =>
                {
                    if (++polls > 100_000) throw new InvalidOperationException("the close wait never ends");
                    if (closes is not null && polls >= closesAtPoll) closes.Exited = true;
                    clock += wait;
                    return Task.CompletedTask;
                }, token).GetAwaiter().GetResult();
            Equal(true, updating <= 1, "updating is said at most once");
            return result;
        }

        var closingProcess = new FakeProcess(target, []);
        var closing = new FakeControl(closingProcess);
        var before = clock;
        var closed = Wait(closing, closingProcess, 8);
        Equal((GrokBotRelaunchKind.Relaunched, (object?)target, true), (closed.Kind, (object?)closed.Quit?.Process, closing.Log is ["start after 200"]),
            "closed, no update: the quit's proof, and Grok Bot starts once, with the start's guards");
        Equal(true, clock - before < TimeSpan.FromSeconds(3), "as soon as it has closed");

        var stuck = new FakeControl(new FakeProcess(target, []));
        before = clock;
        Equal((GrokBotRelaunchKind.NotClosed, 0), (Wait(stuck).Kind, stuck.Log.Count), "never closes: left running, nothing started");
        Equal(true, clock - before >= TimeSpan.FromSeconds(20) && clock - before < TimeSpan.FromSeconds(21), "after 20 seconds");

        // The installer appears while Grok Bot is still closing (0.78.1 starts it then): no "left it running".
        var slowProcess = new FakeProcess(target, []);
        var slow = new FakeControl(slowProcess) { InstallerAt = check => check is >= 3 and < 40 };
        var during = Wait(slow, slowProcess, 10);
        Equal((GrokBotRelaunchKind.Relaunched, true), (during.Kind, slow.Log is ["updating", "start after 200"]),
            "an update during the close wait: waited for, then Grok Bot reopens once");

        var stampChanges = new FakeControl { StampAt = _ => new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 120) };
        Equal((GrokBotRelaunchKind.Relaunched, true), (Wait(stampChanges).Kind, stampChanges.Log is ["updating", "start after 200"]),
            "Grok Bot.exe changed right after the close: the update path, then it reopens");

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var cancelled = new FakeControl(new FakeProcess(target, []));
        Equal((GrokBotRelaunchKind.Cancelled, 0), (Wait(cancelled, token: cancel.Token).Kind, cancelled.Log.Count), "cancelled: nothing started");
    }

    /// <summary>
    /// Open Grok Bot after Reconnect without the quit's proof, and Open window or Repair after an approved close that started
    /// an update: never a start while that exact process runs (up to 30 s, then nothing), never during an update.
    /// </summary>
    private static void OpenWithoutTheQuitsProofIsStillGuarded()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        Task Delay(TimeSpan wait, CancellationToken _) { clock += wait; return Task.CompletedTask; }
        GrokBotRelaunchResult Open(FakeControl control) => GrokBotRelaunch.OpenAfterPressAsync(target, control, Limits, () => clock, Delay, default).GetAwaiter().GetResult();
        GrokBotRelaunchResult AfterClose(FakeControl control, GrokBotProcess? closed) =>
            GrokBotRelaunch.AfterApprovedCloseAsync(closed, control, Limits, () => clock, Delay, default).GetAwaiter().GetResult();

        var gone = new FakeControl();
        Equal((GrokBotRelaunchKind.Relaunched, true), (Open(gone).Kind, gone.Log is ["start after 200"]), "its process is gone: started");

        var closing = new FakeControl(new FakeProcess(target, []) { QuitsAfter = 3 });
        Equal((GrokBotRelaunchKind.Relaunched, true), (Open(closing).Kind, closing.Log is ["start after 200"]), "still closing: started once it exited");

        var stays = new FakeControl(new FakeProcess(target, []));
        var before = clock;
        Equal((GrokBotRelaunchKind.ExitTooLong, 0), (Open(stays).Kind, stays.Log.Count), "never closes: nothing started");
        Equal(true, clock - before >= Limits.Exit && clock - before < Limits.Exit + TimeSpan.FromSeconds(5), "after 30 seconds");

        var updating = new FakeControl { InstallerAt = _ => true };
        var update = Open(updating);
        Equal((GrokBotRelaunchKind.UpdateRunning, (object?)target, 0), (update.Kind, (object?)update.Quit?.Process, updating.Log.Count),
            "an update under way: handed to the update wait with the proof, nothing started");

        var other = new FakeControl(new FakeProcess(new GrokBotProcess(Third, started), []));
        Equal((GrokBotRelaunchKind.StillRunning, 0), (Open(other).Kind, other.Log.Count), "another Grok Bot still runs: nothing started");

        // Open window or Repair: the approved close started an update; Grok Bot is reopened once it has finished.
        var afterUpdate = new FakeControl { InstallerAt = check => check < 4 };
        Equal((GrokBotRelaunchKind.Relaunched, true), (AfterClose(afterUpdate, target).Kind, afterUpdate.Log is ["start after 200"]), "reopened after the update");
        var noMain = new FakeControl { InstallerAt = check => check < 2 };
        Equal(GrokBotRelaunchKind.Relaunched, AfterClose(noMain, null).Kind, "only leftover processes were closed: reopened after the update too");
        var notClosed = new FakeControl(new FakeProcess(target, [])) { InstallerAt = _ => false };
        Equal((GrokBotRelaunchKind.ExitTooLong, 0), (AfterClose(notClosed, target).Kind, notClosed.Log.Count), "the closed process still runs: nothing started");
        var tooLong = new FakeControl { InstallerAt = _ => true };
        Equal((GrokBotRelaunchKind.UpdateTooLong, 0), (AfterClose(tooLong, target).Kind, tooLong.Log.Count), "update too long: nothing started");
        Equal(GrokBotReconnectPolicy.UpdateTooLongText, GrokBotPromptReason.For(GrokBotReconnectRun.OutcomeOf(GrokBotRelaunchKind.UpdateTooLong)).WhenGone,
            "and Open window says why");
    }

    /// <summary>
    /// After Reconnect, the quit's update is waited for: the prompt says Grok Bot is updating, then Wingman opens it. Grok Bot
    /// opened during the update: its usable port is adopted, else the prompt moves to it without a second Reconnect.
    /// </summary>
    private static void AReconnectAfterAnUpdateFinishesTheJob()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, GrokBotOpenedBy.AfterUpdate, T0, started);
        var quit = QuitProof(offer.Target);
        var unconnected = new GrokBotObservation(Second, false, Present, started);
        var reopened = new GrokBotObservation(Third, false, Present, started.AddMinutes(2));
        GrokBotReconnectResult Run(Steps steps) => GrokBotReconnectRun.RunAsync(offer, steps, CancellationToken.None).GetAwaiter().GetResult();

        var updated = new Steps(unconnected) { Result = GrokBotRelaunchKind.UpdateRunning, RelaunchQuit = quit };
        Equal(GrokBotReconnectOutcome.Relaunched, Run(updated).Outcome, "the update finished: Grok Bot reopened");
        Equal(true, updated.Log is ["adopt? 200", "waiting", "relaunch 200", "updating 200", "after update 200"], "it said updating, then waited");

        var adopt = new Steps(unconnected, reopened) { Result = GrokBotRelaunchKind.UpdateRunning, RelaunchQuit = quit, AfterUpdate = GrokBotRelaunchKind.Reopened };
        var handOver = Run(adopt);
        Equal((GrokBotReconnectOutcome.UpdatedAndReopened, true), (handOver.Outcome, handOver.Quit is not null), "opened during the update without a port: handed over");
        Equal(true, adopt.Log is ["adopt? 200", "waiting", "relaunch 200", "updating 200", "after update 200", "adopt? 300"], "its port was tried first");

        var withPort = new Steps(unconnected, reopened) { Result = GrokBotRelaunchKind.UpdateRunning, RelaunchQuit = quit, AfterUpdate = GrokBotRelaunchKind.Reopened, AdoptsOnly = Third };
        Equal(GrokBotReconnectOutcome.Adopted, Run(withPort).Outcome, "opened during the update with a usable port: adopted");

        var connected = new Steps(unconnected, reopened with { Connected = true }) { Result = GrokBotRelaunchKind.UpdateRunning, RelaunchQuit = quit, AfterUpdate = GrokBotRelaunchKind.Reopened };
        Equal(GrokBotReconnectOutcome.AlreadyConnected, Run(connected).Outcome, "opened during the update and connected: done");

        var slow = new Steps(unconnected) { Result = GrokBotRelaunchKind.UpdateRunning, RelaunchQuit = quit, AfterUpdate = GrokBotRelaunchKind.UpdateTooLong };
        var prompt = new GrokBotReconnectPrompt(offer);
        prompt.Reconnect(busy: false);
        prompt.WaitingForQuit();
        prompt.Finished(Run(slow), null);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.UpdateTooLongText), (prompt.State, prompt.Problem), "too long: says so, with Open Grok Bot");

        var plainReopen = new Steps(unconnected, reopened) { Result = GrokBotRelaunchKind.Reopened, RelaunchQuit = quit };
        Equal(GrokBotReconnectOutcome.Reopened, Run(plainReopen).Outcome, "reopened by the user without an update: the prompt follows it, with Open Grok Bot");
        Equal(false, plainReopen.Log.Contains("updating 200"), "no update shown");

        var beforeQuit = new Steps(unconnected) { Result = GrokBotRelaunchKind.UpdateRunning };
        Equal(GrokBotReconnectOutcome.UpdateRunning, Run(beforeQuit).Outcome, "an update without the quit's proof: not waited for here");
        Equal(false, beforeQuit.Log.Contains("updating 200"), "and not shown as updating (the prompt says why and offers Open Grok Bot)");

        // Open Grok Bot without the proof goes through the guarded open, then the same update wait.
        var noProof = new Steps(unconnected) { OpenAfterPress = GrokBotRelaunchKind.UpdateRunning, OpenAfterPressQuit = quit };
        Equal(GrokBotReconnectOutcome.Relaunched, GrokBotReconnectRun.OpenAsync(quit.Process, null, noProof, CancellationToken.None).GetAwaiter().GetResult().Outcome,
            "Open Grok Bot without the proof: the update it finds is waited for, then started");
        Equal(true, noProof.Log is ["open after press 200", "updating 200", "after update 200"], "guarded open, updating, then the wait");

        var opened = new Steps(unconnected) { Result = GrokBotRelaunchKind.UpdateRunning };
        Equal(GrokBotReconnectOutcome.Relaunched, GrokBotReconnectRun.OpenAsync(quit.Process, quit, opened, CancellationToken.None).GetAwaiter().GetResult().Outcome,
            "Open Grok Bot during an update: waits for it too");
        Equal(true, opened.Log is ["start 200", "updating 200", "after update 200"], "Open waited for the update");

        // The prompt itself follows the reopened Grok Bot: after the update it goes straight on to asking for the quit,
        // after the user's own reopen it says so with Open Grok Bot. Pressed throughout; never Reconnect.
        foreach (var (outcome, goesOn) in new[] { (GrokBotReconnectOutcome.UpdatedAndReopened, true), (GrokBotReconnectOutcome.Reopened, false) })
        {
            var follows = new GrokBotReconnectPrompt(offer);
            follows.Reconnect(busy: false);
            follows.Finished(new(outcome, quit), reopened);
            Equal((Third, true, goesOn ? GrokBotPromptState.Working : GrokBotPromptState.CouldNotStart, goesOn ? ("Working...", false, "Not now") : ("Try again", true, "Not now")),
                (follows.Offer.ProcessId, follows.Pressed, follows.State, follows.Buttons), outcome + ": the same prompt follows the new Grok Bot");
            Equal(goesOn ? GrokBotPromptStep.Reconnect : GrokBotPromptStep.None, follows.Observe(reopened, busy: false), outcome + ": after the update it asks for the quit at once");
        }
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var tickHandOver = tray.IndexOf("case GrokBotPromptStep.HandOver:", StringComparison.Ordinal);
        Equal(true, tickHandOver > 0 && tray.IndexOf("grokReconnect.Following(prompt.Offer.Target);", tickHandOver, StringComparison.Ordinal) is var call
            && call > 0 && call < tray.IndexOf("break;", tickHandOver, StringComparison.Ordinal), "the tick tells the policy which Grok Bot the prompt follows");
        // Structural: the tray holds its prompt only through the slot, never makes one itself, and has no separate hand-over window.
        Equal((false, false, false, false, true),
            (tray.Contains("new GrokBotReconnectPrompt(", StringComparison.Ordinal), tray.Contains("grokPrompt = ", StringComparison.Ordinal),
                tray.Contains("HandOverGrokPrompt", StringComparison.Ordinal), tray.Contains("GrokBotReconnectPrompt.AfterYes(", StringComparison.Ordinal),
                tray.Contains("private GrokBotReconnectPrompt? grokPrompt => grokPrompts.Current;", StringComparison.Ordinal)),
            "the tray's prompt lives in the slot");
        var slot = new GrokBotPromptSlot(_ => false);
        var first = slot.Offer(offer)!;
        first.Reconnect(busy: false);
        Equal((true, true), (slot.Offer(offer with { ProcessId = Third }) is null, ReferenceEquals(slot.Current, first)), "a pressed prompt can't be replaced by an offer");
        slot.Release();
        Equal(true, ReferenceEquals(slot.Current, first), "nor let go while open");
        first.NotNow();
        slot.Release();
        Equal(true, slot.Current is null && slot.Offer(offer) is { Pressed: false }, "once closed, the next offer shows");
        var yes = slot.AfterYes(offer.Target, null, T0);
        Equal((true, true), (yes.Pressed, slot.Offer(offer) is null), "the Yes's prompt is pressed from the start");
    }

    /// <summary>
    /// The 2-second check runs only while a prompt is open or Grok Bot is set up in Wingman (a recorded port, or the Start
    /// menu option on), not merely because Grok Bot is installed. A port Wingman doesn't know yet is still adopted at
    /// startup and from Wingman's Grok Bot menu.
    /// </summary>
    private static void TheQuickCheckRunsOnlyForASetUpGrokBot()
    {
        var reads = 0;
        bool Link(bool on) { reads++; return on; }
        Equal(false, GrokBotReconnectPolicy.ShouldCheck(false, true, false, () => Link(false)), "installed but never set up: no check");
        Equal(true, GrokBotReconnectPolicy.ShouldCheck(false, true, true, () => Link(false)), "a recorded Grok Bot port: checked");
        Equal(true, GrokBotReconnectPolicy.ShouldCheck(false, true, false, () => Link(true)), "the Start menu option on: checked");
        Equal(true, GrokBotReconnectPolicy.ShouldCheck(true, false, false, () => Link(false)), "a prompt open: checked, even after an uninstall");
        Equal(false, GrokBotReconnectPolicy.ShouldCheck(false, false, true, () => Link(true)), "not installed and no prompt: no check");
        reads = 0;
        GrokBotReconnectPolicy.ShouldCheck(false, true, true, () => Link(true));
        Equal(0, reads, "with a recorded port the option file isn't read");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("if (!GrokBotReconnectPolicy.ShouldCheck(grokPrompt is not null, installed, grokLauncher.Port() is not null,", StringComparison.Ordinal),
            "the tick uses it");
        Equal(true, tray.Contains("if (grokLauncher.MainProcess() is { } running && AdoptGrokBotPort(running.Id, \"startup\"))", StringComparison.Ordinal)
            && tray.Contains("if (grokLauncher.MainProcess() is { } running) AdoptGrokBotPort(running.Id, \"open\");", StringComparison.Ordinal),
            "startup and the Grok Bot menu still adopt a running port");
    }

    /// <summary>The Grok Bot a pressed prompt follows has that one prompt: no second offer later, also after Not now.</summary>
    private static void AHandOverIsTheOnlyPromptForItsProcess()
    {
        var policy = new GrokBotReconnectPolicy();
        var facts = new Facts("1.0.0");
        policy.Observe(new(First, true, Present), T0, facts);
        facts.Version = "1.1.0";
        policy.Following(new GrokBotProcess(Third, null));
        var later = Enumerable.Range(1, 60).Select(second => policy.Observe(new(Third, false, Present), T0.AddSeconds(1 + second), facts)).ToArray();
        Equal(true, later.All(next => next is null), "no second prompt for it");
        Equal(true, policy.Observe(new(400, false, Present), T0.AddSeconds(70), facts) is null
            && policy.Observe(new(400, false, Present), T0.AddSeconds(80), facts) is { ProcessId: 400 }, "another Grok Bot later still gets its own offer");
    }

    /// <summary>
    /// The relaunch never closes Grok Bot: it brings the offer's exact process forward, waits up to 10 minutes for the user
    /// to quit it, then waits (bounded) for other Grok Bot processes and for Wingman to be free before starting it.
    /// </summary>
    private static void RelaunchWaitsForTheUsersQuit()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchKind Run(FakeControl control, CancellationToken token = default) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, token).GetAwaiter().GetResult().Kind;

        Equal((TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60)), (Limits.Quit, Limits.Settle, Limits.Busy), "the limits");
        var shared = new List<string>();
        var ok = new FakeControl(new FakeProcess(target, shared) { QuitsAfter = 590 }); // 9 min 50 s later
        Equal(GrokBotRelaunchKind.Relaunched, Run(ok), "quit by the user within the limit: started");
        Equal(true, ok.Log is ["forward 200", "start after 200"] && ok.Snapshots == 1, "brought forward, then started once, from one snapshot");

        var reused = new FakeControl(new FakeProcess(new GrokBotProcess(Second, started.AddMinutes(1)), shared) { QuitsAfter = 1 });
        Equal(GrokBotRelaunchKind.Gone, Run(reused), "same id, another start time: not the offer's process");
        Equal(0, reused.Log.Count, "nothing brought forward, nothing started");

        Equal(GrokBotRelaunchKind.Gone, Run(new FakeControl()), "not running: gone");
        var bystander = new FakeControl(new FakeProcess(new GrokBotProcess(Third, started), shared));
        Equal(GrokBotRelaunchKind.Gone, Run(bystander), "only another Grok Bot: gone");
        Equal(0, bystander.Log.Count, "the other one is never touched");

        var racing = new FakeControl(new FakeProcess(target, shared) { QuitsAfter = 1 });
        Equal(GrokBotRelaunchKind.Gone, Run(racing), "exited between the snapshot and the check: gone");
        Equal(0, racing.Log.Count, "nothing started");

        using var cancel = new CancellationTokenSource();
        var cancelled = new FakeControl(new FakeProcess(target, shared));
        var waiting = GrokBotRelaunch.RunAsync(target, cancelled, Limits, () => clock,
            async (wait, token) => { await Task.Yield(); token.ThrowIfCancellationRequested(); }, cancel.Token);
        cancel.Cancel();
        Equal(GrokBotRelaunchKind.Cancelled, waiting.GetAwaiter().GetResult().Kind, "Cancel while waiting");
        Equal(true, cancelled.Log is ["forward 200"], "nothing started");

        shared.Clear();
        Run(new FakeControl(new FakeProcess(target, shared) { QuitsAfter = 2 }, new FakeProcess(new GrokBotProcess(Third, started), shared)));
        Equal("dispose 200,dispose 300", string.Join(',', shared), "every handle from the snapshot is released");
    }

    /// <summary>After the quit Wingman waits, bounded, for helper processes and for itself to be free; it never gives up silently.</summary>
    private static void AfterTheQuitWingmanWaitsThenSaysSo()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult();
        var log = new List<string>();

        var helper = new FakeProcess(new GrokBotProcess(Third, started), log);
        var helpers = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }, helper);
        var exitsLater = GrokBotRelaunch.RunAsync(target, helpers, Limits, () => clock,
            (wait, _) => { clock += wait; if (clock - DateTime.UnixEpoch > TimeSpan.FromSeconds(10)) helper.Exited = true; return Task.CompletedTask; }, default);
        Equal(GrokBotRelaunchKind.Relaunched, exitsLater.GetAwaiter().GetResult().Kind, "a helper process exits a few seconds after the quit: waits, then starts");

        var lingering = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }, new FakeProcess(new GrokBotProcess(Third, started), log));
        var before = clock;
        var stuck = Run(lingering);
        Equal((GrokBotRelaunchKind.StillRunning, true), (stuck.Kind, stuck.Quit is not null), "a Grok Bot process still running at the limit: not started, with the proof for Open Grok Bot");
        Equal(true, clock - before >= Limits.Settle && clock - before < Limits.Settle + TimeSpan.FromSeconds(5), "waited for it, about 20 s");
        Equal(true, lingering.Log is ["forward 200"], "and never started");

        var busyThenFree = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Free = false, FreeAfter = 30 };
        Equal(GrokBotRelaunchKind.Relaunched, Run(busyThenFree).Kind, "Wingman busy for 30 s after the quit: waits, then starts");

        var busy = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Free = false };
        before = clock;
        var tooLong = Run(busy);
        Equal((GrokBotRelaunchKind.BusyTooLong, true), (tooLong.Kind, tooLong.Quit is not null), "busy past 60 s: not started, with the proof");
        Equal(true, clock - before >= Limits.Busy && clock - before < Limits.Busy + TimeSpan.FromSeconds(5), "waited about 60 s");
        Equal(true, busy.Log is ["forward 200"], "and never started");

        var opened = new FakeControl();
        Equal(GrokBotRelaunchKind.Relaunched, GrokBotRelaunch.StartWhenClearAsync(tooLong.Quit!, opened.ExecutableStamp(), opened, Limits, () => clock,
            (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult().Kind, "Open Grok Bot then starts it");
        Equal(true, opened.Log is ["start after 200"], "with the same proof");
    }

    /// <summary>
    /// Wingman never starts Grok Bot while its installer or uninstaller runs, or once Grok Bot.exe changed: the wait ends and
    /// after the quit Wingman waits for the update instead. No quit within 10 minutes: back to the offer.
    /// </summary>
    private static void NeverStartsDuringAnUpdate()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var target = new GrokBotProcess(Second, started);
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchKind Run(FakeControl control) => GrokBotRelaunch.RunAsync(target, control,
            Limits, () => clock, (wait, _) => { clock += wait; return Task.CompletedTask; }, default).GetAwaiter().GetResult().Kind;
        var log = new List<string>();

        var never = new FakeControl(new FakeProcess(target, log));
        var before = clock;
        Equal(GrokBotRelaunchKind.QuitTimedOut, Run(never), "no quit in 10 minutes: the wait ends");
        Equal(true, clock - before >= Limits.Quit && clock - before < Limits.Quit + TimeSpan.FromSeconds(5), "after 10 minutes");
        Equal(true, never.Log is ["forward 200"], "nothing started");

        var installerWhileWaiting = new FakeControl(new FakeProcess(target, log)) { InstallerAt = check => check >= 5 };
        before = clock;
        Equal(GrokBotRelaunchKind.ExitTooLong, Run(installerWhileWaiting), "an installer runs but Grok Bot never closes: the wait ends");
        Equal(true, clock - before >= Limits.Exit && clock - before < Limits.Exit + TimeSpan.FromSeconds(10), "30 seconds after the installer appeared");
        Equal(true, installerWhileWaiting.Log is ["forward 200"], "never started");

        // Checks: one while waiting for the quit, then after it.
        var installerAtQuit = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3 }) { InstallerAt = check => check >= 1 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(installerAtQuit), "Grok Bot quit into its staged installer: not started");
        Equal(true, installerAtQuit.Log is ["forward 200"], "never started");

        var installerAfter = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }, new FakeProcess(new GrokBotProcess(Third, started), log) { QuitsAfter = 5 })
            { InstallerAt = check => check >= 2 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(installerAfter), "an installer starts while helpers exit: not started");
        Equal(true, installerAfter.Log is ["forward 200"], "never started");

        var installerWhileBusy = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { Free = false, InstallerAt = check => check >= 3 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(installerWhileBusy), "an installer starts while Wingman is busy: not started");

        // Quit at once: the only check is the one just before the start.
        var lastMoment = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { InstallerAt = check => check >= 0 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(lastMoment), "an installer just before the start: not started");
        Equal(true, lastMoment.Log is ["forward 200"], "never started");

        var replaced = new GrokBotExecutableStamp(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 120);
        var exeChanged = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3 });
        exeChanged.StampAt = check => check >= 2 ? replaced : exeChanged.Stamp;
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(exeChanged), "Grok Bot.exe changed since the wait began: not started");
        Equal(true, exeChanged.Log is ["forward 200"], "never started");
        var exeMissing = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3 }) { Missing = check => check >= 2 };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(exeMissing), "Grok Bot.exe gone (being replaced): not started");
        var exeUnreadable = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 3 }) { Missing = _ => true };
        Equal(GrokBotRelaunchKind.UpdateRunning, Run(exeUnreadable), "Grok Bot.exe unreadable throughout: never started");

        const string Grok = @"C:\Users\a\AppData\Local\Programs\Grok Bot\Grok Bot.exe";
        Equal(true, GrokBotInstallers.IsInstaller(@"C:\Users\a\AppData\Local\Temp\sand-update-x1\sand-update-0.79.0-x64.exe", Grok), "the staged update installer");
        Equal(true, GrokBotInstallers.IsInstaller(@"C:\Users\a\AppData\Local\Programs\Grok Bot\Uninstall Grok Bot.exe", Grok), "the uninstaller");
        const string Copy = @"C:\Users\a\AppData\Local\Temp\~nsuA.tmp\Un_A.exe";
        Equal(true, GrokBotInstallers.IsInstaller(Copy, Grok, () => "\"" + Copy + "\" _?=C:\\Users\\a\\AppData\\Local\\Programs\\Grok Bot"),
            "Grok Bot's uninstaller copy: its _?= names Grok Bot's folder (with a space, unquoted)");
        Equal(true, GrokBotInstallers.IsInstaller(Copy.Replace("Un_A", "Au_"), Grok, () => "\"x\" /S _?=c:\\users\\a\\appdata\\local\\programs\\grok bot\\"),
            "any case, a trailing backslash");
        Equal(true, GrokBotInstallers.IsInstaller(Copy, Grok, () => null, () => @"C:\Users\a\AppData\Local\Programs\Grok Bot\Uninstall Grok Bot.exe"),
            "started by Grok Bot's Uninstall.exe");
        Equal(false, GrokBotInstallers.IsInstaller(Copy, Grok), "an uninstaller copy nothing ties to Grok Bot");
        Equal(false, GrokBotInstallers.IsInstaller(Copy, Grok, () => "\"" + Copy + "\" _?=C:\\Program Files\\Other"), "another app's uninstaller copy");
        Equal(false, GrokBotInstallers.IsInstaller(Copy, Grok, () => "\"" + Copy + "\" _?=C:\\Users\\a\\AppData\\Local\\Programs\\Grok Bot Beta"),
            "a folder that only starts like Grok Bot's");
        Equal(false, GrokBotInstallers.IsInstaller(Copy, Grok, () => null, () => @"C:\Program Files\Other\Uninstall.exe"), "started by another app's uninstaller");
        Equal(false, GrokBotInstallers.IsInstaller(Copy, Grok, () => "\"" + Copy + "\" /D=x?_?=C:\\Users\\a\\AppData\\Local\\Programs\\Grok Bot"),
            "_?= only as its own argument");
        Equal((string?)null, GrokBotInstallers.UninstallTarget("\"x\" /S"), "no _?=: no target");
        var reads = 0;
        GrokBotInstallers.IsInstaller(@"C:\Users\a\AppData\Local\Temp\sand-update-x1\sand-update-0.79.0-x64.exe", Grok, () => { reads++; return null; }, () => { reads++; return null; });
        Equal(0, reads, "the staged update needs no command line or parent");
        // The process scan: only these names are asked about, and only theirs are read.
        foreach (var name in new[] { "sand-update-0.79.0-x64", "SAND-UPDATE-x", "Uninstall Grok Bot", "uninstall", "Un_A", "au_" })
            Equal(true, GrokBotInstallers.MayBeInstaller(name), name + ": asked about");
        foreach (var name in new[] { "Grok Bot", "Un_A2", "Au_x", "sand-updater", "MyUninstall", "explorer", "" })
            Equal(false, GrokBotInstallers.MayBeInstaller(name), name + ": never asked about");
        var looked = new List<int>();
        string? Image(int id)
        {
            looked.Add(id);
            return id switch
            {
                5 => Copy,
                6 => @"C:\Program Files\Other\notepad.exe",
                7 => @"C:\Users\a\AppData\Local\Programs\Grok Bot\Uninstall Grok Bot.exe",
                9 => @"C:\Users\a\AppData\Local\Programs\Grok Bot\Uninstall Grok Bot.exe",
                _ => null,
            };
        }
        string? Line(int id) { looked.Add(-id); return id == 5 ? "\"" + Copy + "\" _?=C:\\Program Files\\Other" : null; }
        int? Parent(int id) => id == 8 ? 9 : null;
        Equal(false, GrokBotInstallers.AnyRunning([("notepad", 6), ("Grok Bot", 7)], Grok, Image, Line, Parent), "other names: not installers");
        Equal(0, looked.Count, "and nothing about them is read");
        Equal(false, GrokBotInstallers.AnyRunning([("Un_A", 5)], Grok, Image, Line, Parent), "another app's uninstaller copy");
        Equal(true, GrokBotInstallers.AnyRunning([("notepad", 6), ("Uninstall Grok Bot", 7)], Grok, Image, Line, Parent), "Grok Bot's uninstaller");
        Equal(true, GrokBotInstallers.AnyRunning([("Au_", 8)], Grok, id => id == 8 ? Copy : Image(id), Line, Parent), "a copy started by Grok Bot's uninstaller");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("GrokBotInstallers.AnyRunning(Named(processes), Executable,", StringComparison.Ordinal) && !launcher.Contains("MayBeInstaller", StringComparison.Ordinal),
            "the launcher's installer check is that scan");
        var installerDocs = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "grok-bot-after-update.md"));
        var policySource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman.Core", "GrokBotReconnectPolicy.cs"));
        Equal(true, installerDocs.Contains("Wingman looks only at programs named `sand-update-…`, `Uninstall…`, `Un_A` or `Au_`", StringComparison.Ordinal)
            && policySource.Contains("Wingman asks only about programs named <c>sand-update-…</c>, <c>Uninstall…</c>, <c>Un_A</c> or <c>Au_</c>", StringComparison.Ordinal)
            && !installerDocs.Contains("any other program in Grok Bot's install folder", StringComparison.Ordinal)
            && !policySource.Contains("Any program in the install folder", StringComparison.Ordinal), "docs and comment match the names checked");
        Equal(false, GrokBotInstallers.IsInstaller(Grok, Grok), "Grok Bot itself");
        Equal(false, GrokBotInstallers.IsInstaller(@"C:\Users\a\AppData\Local\Programs\Grok Bot\resources\elevate.exe", Grok), "a subfolder");
        Equal(false, GrokBotInstallers.IsInstaller(@"C:\Program Files\Other\Uninstall Other.exe", Grok), "another app's uninstaller");
        Equal(false, GrokBotInstallers.IsInstaller(null, Grok), "unreadable");
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "CodexWingman", "CodexWingman.csproj"))) return folder.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
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
