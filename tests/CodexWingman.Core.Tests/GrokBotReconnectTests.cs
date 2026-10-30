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
        AHandOverIsTheOnlyPromptForItsProcess();
        PromptWindowStaysOutOfTheWay();
        BringingForwardKeepsTheWindowAsItIs();
        TheQuitWaitHoldsTheProcessItself();
        ThePromptFollowsItsProcessAfterAnUninstall();
        AReopenDuringTheWaitStartsNothing();
        NoPromptOverAFullScreenApp();
        ACrashIsNotReopened();
        AnInstallersKillIsAnUpdateNotACrash();
        ACancelledShutdownKeepsTheReconnect();
        NothingIsAwaitedBetweenTheBusyCheckAndTheStart();
        TheMainProcessIsGrokBotItself();
        NothingStartsWhileWindowsShutsDown();
        ThePromptClosesAfterItsOwnClick();
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
    /// Bot connects, and after Not now; Reconnect acts once, and while Wingman is busy it waits ("Working…") and acts when free.
    /// </summary>
    private static void PromptIsBoundToItsProcess()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, true, T0, started);
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
        busy.Finished(new(GrokBotReconnectOutcome.Busy));
        Equal(true, busy.State == GrokBotPromptState.Working && busy.Observe(Seen(Second), busy: false) == GrokBotPromptStep.Reconnect,
            "busy again by then: kept, acts when free");

        var gone = new GrokBotReconnectPrompt(offer);
        gone.Reconnect(busy: true);
        // Pressed: the run (which re-checks the process and starts nothing) says what became of it; never a silent close.
        Equal(GrokBotPromptStep.Reconnect, gone.Observe(Seen(Third), busy: false), "replaced while waiting to act: the run re-checks");
        gone.Finished(new(GrokBotReconnectOutcome.Gone));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.GoneText), (gone.State, gone.Problem), "and the prompt says so, with Open Grok Bot");
        Equal(true, gone.Open(busy: false), "Open Grok Bot without the quit's proof");
        gone.Finished(new(GrokBotReconnectOutcome.Busy));
        Equal(GrokBotPromptStep.Open, gone.Observe(Seen(null), busy: false), "busy then: Open Grok Bot is kept, not turned into Reconnect");

        var quitting = new GrokBotReconnectPrompt(offer);
        quitting.Reconnect(busy: false);
        quitting.WaitingForQuit();
        quitting.Observe(Seen(null), busy: false);
        Equal(GrokBotPromptState.WaitingForQuit, quitting.State, "waiting for the quit: its exit is expected, the run decides");
        quitting.Finished(new(GrokBotReconnectOutcome.Relaunched));
        Equal((GrokBotPromptState.Closed, "Relaunched"), (quitting.State, quitting.ClosedBecause), "closed when the reconnect finishes");
    }

    /// <summary>
    /// Once the user has quit Grok Bot for the prompt, it never closes silently: it stays and says why Wingman couldn't open
    /// it, with Open Grok Bot. No quit within the limit: back to the offer, silently. An update running: closed.
    /// </summary>
    private static void AfterTheQuitThePromptNeverGivesUpSilently()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, true, T0, started);
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
            prompt.Finished(new(outcome, quit));
            Equal((GrokBotPromptState.CouldNotStart, text), (prompt.State, prompt.Problem), outcome + ": the prompt stays and says why");
            for (var tick = 0; tick < 100; tick++) prompt.Observe(Seen(null), busy: false);
            Equal(GrokBotPromptState.CouldNotStart, prompt.State, outcome + ": Grok Bot not running is expected now; it stays");
            Equal(false, prompt.Reconnect(busy: false), outcome + ": Reconnect isn't offered again");
            Equal(true, prompt.Open(busy: false), outcome + ": Open Grok Bot acts");
            Equal(GrokBotPromptState.Working, prompt.State, outcome + ": Working…");
            Equal(false, prompt.Open(busy: false), outcome + ": a second press does nothing");
        }

        var openBusy = Waiting();
        openBusy.Finished(new(GrokBotReconnectOutcome.StillRunning, quit));
        Equal(false, openBusy.Open(busy: true), "Open pressed while busy: not yet");
        Equal(GrokBotPromptStep.Open, openBusy.Observe(Seen(null), busy: false), "free: opens, without the offer's process");
        openBusy.Finished(new(GrokBotReconnectOutcome.Relaunched, quit));
        Equal(GrokBotPromptState.Closed, openBusy.State, "opened: closed");

        // After the quit another Grok Bot started: never a silent close. The prompt asks the tray to hand over once, and
        // if nothing takes over it stays, says what happened and offers Open Grok Bot.
        var replaced = Waiting();
        replaced.Finished(new(GrokBotReconnectOutcome.BusyTooLong, quit));
        Equal(GrokBotPromptStep.HandOver, replaced.Observe(Seen(Third), busy: false), "another Grok Bot started: hand over");
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText), (replaced.State, replaced.Problem), "and it says what happened");
        for (var tick = 0; tick < 10; tick++) Equal(GrokBotPromptStep.None, replaced.Observe(Seen(Third), busy: false), "asked once for that Grok Bot");
        Equal((GrokBotPromptState.CouldNotStart, true), (replaced.State, replaced.Open(busy: false)), "still there, with Open Grok Bot");
        Equal(GrokBotPromptStep.None, replaced.Observe(Seen(400), busy: false), "while Open Grok Bot runs, the run sees a new Grok Bot itself");
        Equal(GrokBotPromptState.Working, replaced.State, "not closed or handed over mid-run");
        var pendingOpen = Waiting();
        pendingOpen.Finished(new(GrokBotReconnectOutcome.StillRunning, quit));
        pendingOpen.Open(busy: true);
        Equal(GrokBotPromptStep.HandOver, pendingOpen.Observe(Seen(Third), busy: true), "a kept Open Grok Bot press: handed over instead");
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ReopenedText), (pendingOpen.State, pendingOpen.Problem), "and it says what happened");

        var timedOut = Waiting();
        timedOut.Finished(new(GrokBotReconnectOutcome.QuitTimedOut));
        Equal((GrokBotPromptState.Offered, (string?)null), (timedOut.State, timedOut.ClosedBecause), "no quit within the limit: back to the offer, silently");
        Equal(true, timedOut.Reconnect(busy: false), "and Reconnect works again");

        var reconnects = new[] { GrokBotReconnectOutcome.Relaunched, GrokBotReconnectOutcome.Adopted, GrokBotReconnectOutcome.AlreadyConnected,
            GrokBotReconnectOutcome.Cancelled };
        // Once Reconnect is pressed, also without the quit's proof (before the quit, or Grok Bot hadn't finished closing as
        // its installer started), only a reconnect or the user's Cancel closes the prompt. No quit in time, or a shutdown
        // another app cancelled, offers Reconnect again; every other outcome stays, says why and offers Open Grok Bot.
        foreach (var waited in new[] { false, true })
            foreach (var outcome in Enum.GetValues<GrokBotReconnectOutcome>())
            {
                var prompt = new GrokBotReconnectPrompt(offer);
                prompt.Reconnect(busy: false);
                if (waited) prompt.WaitingForQuit();
                prompt.Finished(new(outcome));
                var name = outcome + (waited ? " while waiting for the quit" : " right after the press");
                if (reconnects.Contains(outcome)) Equal(GrokBotPromptState.Closed, prompt.State, name + ": closed");
                else if (outcome == GrokBotReconnectOutcome.Busy) Equal(GrokBotPromptState.Working, prompt.State, name + ": Working…");
                else if (outcome is GrokBotReconnectOutcome.QuitTimedOut or GrokBotReconnectOutcome.SessionEnding)
                    Equal((GrokBotPromptState.Offered, true), (prompt.State, prompt.Reconnect(busy: false)), name + ": Reconnect again");
                else if (outcome == GrokBotReconnectOutcome.Failed)
                    Equal((GrokBotPromptState.Offered, GrokBotReconnectPolicy.FailedText, true), (prompt.State, prompt.Problem, prompt.Reconnect(busy: false)),
                        name + ": an error before the quit says so, and Reconnect works again");
                else
                {
                    Equal((GrokBotPromptState.CouldNotStart, true), (prompt.State, !string.IsNullOrEmpty(prompt.Problem)), name + ": stays and says why");
                    for (var tick = 0; tick < 10; tick++) prompt.Observe(Seen(null), busy: false);
                    Equal(GrokBotPromptState.CouldNotStart, prompt.State, name + ": no Grok Bot doesn't close it either");
                    Equal(true, prompt.Open(busy: false), name + ": Open Grok Bot acts");
                }
            }
        // An error after the quit: the prompt stays, says so in plain words and offers Open Grok Bot, even with no Grok Bot.
        var failed = Waiting();
        failed.Finished(new(GrokBotReconnectOutcome.Failed, quit));
        for (var tick = 0; tick < 10; tick++) failed.Observe(Seen(null), busy: false);
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.FailedText, true), (failed.State, failed.Problem, failed.Open(busy: false)),
            "an error after the quit: stays with a plain reason and Open Grok Bot");
        var trayText = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var act = trayText[trayText.IndexOf("private async Task ActOnGrokPromptAsync", StringComparison.Ordinal)..trayText.IndexOf("private void HandOverGrokPrompt", StringComparison.Ordinal)];
        Equal(true, !act.Contains("MessageBox", StringComparison.Ordinal) && act.Contains("result = new(GrokBotReconnectOutcome.Failed, prompt.Quit);", StringComparison.Ordinal),
            "the tray turns an error into Failed, with no message box");
        var exitTooLong = Waiting();
        exitTooLong.Finished(new(GrokBotReconnectOutcome.ExitTooLong));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.ExitTooLongText), (exitTooLong.State, exitTooLong.Problem),
            "an installer started but Grok Bot didn't finish closing: says so");

        // After the user's quit, every outcome either reconnected Grok Bot (or the user cancelled), or the prompt stays and
        // says something; none closes it silently.
        foreach (var outcome in Enum.GetValues<GrokBotReconnectOutcome>())
        {
            var prompt = Waiting();
            prompt.Finished(new(outcome, quit));
            if (reconnects.Contains(outcome)) Equal(GrokBotPromptState.Closed, prompt.State, outcome + " after the quit: closed");
            else if (outcome == GrokBotReconnectOutcome.Busy) Equal(GrokBotPromptState.Working, prompt.State, "busy after the quit: Working…");
            else Equal((GrokBotPromptState.CouldNotStart, true), (prompt.State, !string.IsNullOrEmpty(prompt.Problem)), outcome + " after the quit: stays and says why");
        }
        Equal(GrokBotReconnectPolicy.UpdateTooLongText, GrokBotReconnectPrompt.ProblemAfterQuit(GrokBotReconnectOutcome.UpdateTooLong), "the update limit is named");
        Equal(GrokBotReconnectPolicy.ReopenedText, GrokBotReconnectPrompt.ProblemAfterQuit(GrokBotReconnectOutcome.UpdatedAndReopened), "reopened during the update");

        // The quit started an update: "Grok Bot is updating…" with Cancel, kept while another Grok Bot appears (the run
        // moves the prompt to it) and while none runs; Grok Bot connected closes it; Cancel closes it.
        var updating = Waiting();
        updating.Updating(quit);
        Equal((GrokBotPromptState.Updating, (object?)quit), (updating.State, (object?)updating.Quit), "updating, with the quit's proof");
        Equal("Grok Bot is updating… Wingman reopens it when the update has finished.", GrokBotReconnectPolicy.UpdatingText, "updating wording");
        for (var tick = 0; tick < 10; tick++) updating.Observe(Seen(null), busy: false);
        updating.Observe(Seen(Third), busy: false);
        Equal(GrokBotPromptState.Updating, updating.State, "updating: neither no Grok Bot nor a new one closes it");
        Equal(false, updating.Reconnect(busy: false) || updating.Open(busy: false), "no button acts while updating");
        updating.Finished(new(GrokBotReconnectOutcome.UpdateTooLong, quit));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.UpdateTooLongText), (updating.State, updating.Problem), "too long: says so");
        Equal(true, updating.Open(busy: false), "and offers Open Grok Bot");
        var connectedWhileUpdating = Waiting();
        connectedWhileUpdating.Updating(quit);
        connectedWhileUpdating.Observe(Seen(Third, connected: true), busy: false);
        Equal((GrokBotPromptState.Closed, "connected"), (connectedWhileUpdating.State, connectedWhileUpdating.ClosedBecause), "connected while updating: closed");
        var cancelled = Waiting();
        cancelled.Updating(quit);
        cancelled.NotNow();
        Equal(GrokBotPromptState.Closed, cancelled.State, "Cancel while updating: closed");

        Equal("Helpers need Grok Bot reopened by Wingman.", GrokBotReconnectPolicy.PromptText, "prompt wording");
        Equal("Close Grok Bot's window. Wingman reopens it.", GrokBotReconnectPolicy.QuitText, "quit wording: on Windows closing the window quits Grok Bot");
        var window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotPromptWindow.cs"));
        Equal(true, window.Contains("\"Open Grok Bot\"", StringComparison.Ordinal) && window.Contains("OpenClicked", StringComparison.Ordinal),
            "the window has the Open Grok Bot button");
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
        Equal(true, tray.Contains("WingmanLog.Write(\"grok-bot-reconnect-outcome\", (\"process\", prompt.Offer.ProcessId), (\"step\", step), (\"outcome\", result.Outcome));", StringComparison.Ordinal),
            "the real outcome is logged");
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
        public GrokBotObservation Observe() => observations[Math.Min(seen++, observations.Length - 1)];
        public bool AdoptListeningPort(int process) { Log.Add("adopt? " + process); return Adopts || process == AdoptsOnly; }
        public bool Busy => IsBusy;
        public void WaitingForQuit() => Log.Add("waiting");
        public Task<GrokBotRelaunchResult> RelaunchAfterQuitAsync(GrokBotProcess target, CancellationToken cancellationToken)
        {
            Log.Add("relaunch " + target.Id);
            return Task.FromResult(new GrokBotRelaunchResult(Result, RelaunchQuit));
        }
        public Task<GrokBotRelaunchResult> StartAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
        {
            Log.Add("start " + quit.Process.Id);
            return Task.FromResult(new GrokBotRelaunchResult(Result, quit));
        }
        /// <summary>What Open Grok Bot without the quit's proof ends with (its proof: the quit, once it has one).</summary>
        public GrokBotRelaunchKind OpenAfterPress = GrokBotRelaunchKind.Relaunched;
        public GrokBotRelaunch.ClosedProcess? OpenAfterPressQuit;
        public Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, CancellationToken cancellationToken)
        {
            Log.Add("open after press " + target.Id);
            return Task.FromResult(new GrokBotRelaunchResult(OpenAfterPress, OpenAfterPressQuit));
        }
        /// <summary>What the wait after the update ends with.</summary>
        public GrokBotRelaunchKind AfterUpdate = GrokBotRelaunchKind.Relaunched;
        public void Updating(GrokBotRelaunch.ClosedProcess quit) => Log.Add("updating " + quit.Process.Id);
        public Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
        {
            Log.Add("after update " + quit.Process.Id);
            return Task.FromResult(new GrokBotRelaunchResult(AfterUpdate, quit));
        }
    }

    /// <summary>Reconnect re-checks first: gone, connected or adoptable means nothing restarts; busy keeps it.</summary>
    private static void ReconnectRechecksBeforeActing()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, false, T0, started);
        GrokBotReconnectOutcome Run(Steps steps) => GrokBotReconnectRun.RunAsync(offer, steps, CancellationToken.None).GetAwaiter().GetResult().Outcome;
        var unconnected = new GrokBotObservation(Second, false, Present, started);

        var gone = new Steps(new GrokBotObservation(Second, false, Present, started.AddSeconds(3)));
        Equal(GrokBotReconnectOutcome.Gone, Run(gone), "same id, other run: gone");
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
        var act = tray[tray.IndexOf("private async Task ActOnGrokPromptAsync(", StringComparison.Ordinal)..];
        act = act[..act.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, act.Contains("GrokBotReconnectRun.RunAsync(prompt.Offer,", StringComparison.Ordinal) && act.Contains("GrokBotReconnectRun.OpenAsync(prompt.Offer.Target, prompt.Quit,", StringComparison.Ordinal)
            && !act.Contains("grokLauncher.OpenAsync", StringComparison.Ordinal) && !act.Contains("EnsureGrokBotReadyAsync", StringComparison.Ordinal), "acts only through the checked run");
        Equal(false, tray.Contains("BalloonTipClicked", StringComparison.Ordinal) || tray.Contains("BalloonTipClosed", StringComparison.Ordinal),
            "no balloon click or close handler");
        Equal(true, tray.Contains("tray.ShowBalloonTip(2500);", StringComparison.Ordinal) && tray.Contains("tray.ShowBalloonTip(4000);", StringComparison.Ordinal)
            && tray.Split("ShowBalloonTip(").Length - 1 == 3 && !tray.Contains("BalloonTipIcon", StringComparison.Ordinal), "the other balloons are as before");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(3, launcher.Split("StartWithPort()").Length - 1, "StartWithPort: defined once, called by the ordinary open and by the relaunch's proof-gated Start only");
        Equal(true, launcher.Contains("public void Start(GrokBotRelaunch.ClosedProcess proof) => launcher.StartWithPort();", StringComparison.Ordinal),
            "the reconnect starts Grok Bot only with the relaunch's proof");
        var reconnect = launcher[launcher.IndexOf("public async Task<GrokBotRelaunchResult> ReconnectAsync(", StringComparison.Ordinal)..];
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

        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, true, T0, started));
        prompt.Reconnect(busy: false);
        prompt.WaitingForQuit();
        var crash = Run(new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2, Code = 3 }));
        prompt.Finished(GrokBotReconnectRunResult(crash));
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
    private static void ACancelledShutdownKeepsTheReconnect()
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

        var before = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, true, T0, started));
        before.Reconnect(busy: false);
        before.WaitingForQuit();
        before.Finished(GrokBotReconnectRunResult(result));
        Equal(GrokBotPromptState.Offered, before.State, "before the quit: the prompt offers Reconnect again");
        Equal(true, before.Reconnect(busy: false), "and Reconnect works");

        var after = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, true, T0, started));
        after.Reconnect(busy: false);
        after.WaitingForQuit();
        var quitThenEnding = new FakeControl(new FakeProcess(target, log) { QuitsAfter = 2 }) { EndingAt = check => check >= 1 };
        var ended = GrokBotRelaunch.RunAsync(target, quitThenEnding, Limits, () => clock, (wait, _) => Task.CompletedTask, default).GetAwaiter().GetResult();
        Equal((GrokBotRelaunchKind.SessionEnding, true), (ended.Kind, ended.Quit is not null), "after the quit: SessionEnding with the quit's proof");
        after.Finished(GrokBotReconnectRunResult(ended));
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

    /// <summary>Grok Bot.exe running its local-exec daemon script as Node is not Grok Bot's main process; the single-instance owner is preferred.</summary>
    private static void TheMainProcessIsGrokBotItself()
    {
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" \"C:\\Grok Bot\\resources\\local-exec-daemon\\main.cjs\" --port 4100"), "the local-exec daemon");
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
    /// "Still running" (the reconnect's settle check and Open window's close wait) counts Grok Bot and its own children only:
    /// a leftover Grok Bot.exe running a script never blocks a start, unless it holds the single-instance window.
    /// </summary>
    private static void StillRunningMeansGrokBotItself()
    {
        const string daemon = "\"C:\\Grok Bot\\Grok Bot.exe\" \"C:\\Grok Bot\\resources\\local-exec-daemon\\main.cjs\"";
        Equal(false, GrokBotLaunchPolicy.KeepsGrokBotRunning(daemon, ownsSingleInstanceWindow: false), "a detached local-exec daemon");
        Equal(false, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" C:\\x\\tool.TS --flag", false), "any script");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning(daemon, ownsSingleInstanceWindow: true), "a script holding the single-instance window: a launch would go to it");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" --updated", false), "Grok Bot itself");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" --type=renderer --lang=en", false), "its renderer");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" --type=utility --utility-sub-type=node.mojom.NodeService", false),
            "its utility process (how 0.78.1 runs the daemon): ends with Grok Bot");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" --type=utility x.cjs", false), "a --type= child with a script still counts");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning("\"C:\\Grok Bot\\Grok Bot.exe\" -- x.cjs", false), "after -- it's a plain argument");
        Equal(true, GrokBotLaunchPolicy.KeepsGrokBotRunning(null, false), "unreadable: counts, so nothing starts on a guess");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("public bool AnyRunning() => launcher.StillRunning();", StringComparison.Ordinal)
            && !launcher.Contains("IsRunning()", StringComparison.Ordinal),
            "the reconnect and Open window both use StillRunning");

        // Open window and Repair use the reconnect's installer guard before any start.
        var stamp = new GrokBotExecutableStamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100);
        Equal(false, GrokBotRelaunch.UpdateUnderWay(false, stamp, stamp), "no installer, same Grok Bot.exe");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(true, stamp, stamp), "an installer runs");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(false, null, stamp), "Grok Bot.exe missing");
        Equal(true, GrokBotRelaunch.UpdateUnderWay(false, stamp with { Length = 101 }, stamp), "Grok Bot.exe changed");
        var open = launcher[launcher.IndexOf("public async Task<bool> OpenAsync", StringComparison.Ordinal)..launcher.IndexOf("private async Task ThrowIfUpdateUnderWayAsync", StringComparison.Ordinal)];
        Equal(true, open.Split("ThrowIfUpdateUnderWayAsync(stamp)").Length == 2
            && open.IndexOf("ThrowIfUpdateUnderWayAsync(stamp)", StringComparison.Ordinal) < open.IndexOf("confirmRelaunch()", StringComparison.Ordinal)
            && open.Split("StartWithPort()").Length == 2, "Open window checks before asking, and starts in one place");
        // Its close wait is the Core one (behaviour in OpenWindowsCloseWaitIsUpdateAware); Grok Bot starts only when it says so.
        Equal(true, open.Split("GrokBotRelaunch.AfterCloseRequestAsync(closed, stamp,").Length == 2
            && open.IndexOf("if (result is null)", StringComparison.Ordinal) < open.IndexOf("StartWithPort()", StringComparison.Ordinal)
            && open.IndexOf("AfterCloseRequestAsync", StringComparison.Ordinal) < open.IndexOf("if (result is null)", StringComparison.Ordinal),
            "Open window starts Grok Bot only after the close wait found no update");

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
            && tray.Contains("        sessionEnding = true;\n", StringComparison.Ordinal)
            && tray.Contains("tray.grokLauncher.ReconnectAsync(target, () => !Busy, () => tray.sessionEnding, cancellationToken)", StringComparison.Ordinal)
            && tray.Contains("tray.grokLauncher.StartAfterQuitAsync(quit, () => !Busy, () => tray.sessionEnding, cancellationToken)", StringComparison.Ordinal),
            "the tray tells the relaunch when Windows is shutting down or signing out");
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("public bool SessionEnding() => sessionEnding();", StringComparison.Ordinal)
            && launcher.Contains("public int? ExitCode => HasExited && GetExitCodeProcess(handle, out var code) ? unchecked((int)code) : null;", StringComparison.Ordinal),
            "the launcher passes it on and reads the real exit code from its process handle");
    }

    /// <summary>The prompt is closed from its own click handlers: hidden at once, disposed after the handler returns.</summary>
    private static void ThePromptClosesAfterItsOwnClick()
    {
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var close = tray[tray.IndexOf("private void CloseGrokPrompt(bool now = false)", StringComparison.Ordinal)..];
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
            && launcher.Contains("lock (runtimeFile) StartWithPortLocked();", StringComparison.Ordinal)
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
        var open = launcher[launcher.IndexOf("public async Task<bool> OpenAsync(", StringComparison.Ordinal)..];
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
        Equal(true, observe > 0 && tick.IndexOf("if (!installed || busy || grokPromptWait is not null) return;", StringComparison.Ordinal) > observe,
            "nothing new is offered or adopted while it isn't installed, after the prompt's check");
        // And the prompt then closes when its process exits.
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, true, T0, started));
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

        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(Second, true, T0, started));
        prompt.Reconnect(busy: false);
        prompt.WaitingForQuit();
        prompt.Finished(new(GrokBotReconnectOutcome.Reopened));
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
        public void Start(GrokBotRelaunch.ClosedProcess proof) => Log.Add("start after " + proof.Process.Id);
    }

    private static readonly GrokBotRelaunchLimits Limits = GrokBotRelaunchLimits.Default;

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
    /// Open window or Repair after the user's Yes: up to 20 s for Grok Bot to close. An update that close starts, during the
    /// wait or right after it, is waited for and Grok Bot reopens after it, instead of "left it running". Otherwise the
    /// caller starts Grok Bot (null), or nothing starts because it didn't close or the user cancelled.
    /// </summary>
    private static void OpenWindowsCloseWaitIsUpdateAware()
    {
        var target = new GrokBotProcess(Second, new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc));
        var clock = DateTime.UnixEpoch;
        GrokBotRelaunchResult? Wait(FakeControl control, FakeProcess? closes = null, int closesAtPoll = 0, CancellationToken token = default)
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
        Equal(((GrokBotRelaunchResult?)null, 0), (Wait(closing, closingProcess, 8), closing.Log.Count), "closed, no update: the caller starts Grok Bot");
        Equal(true, clock - before < TimeSpan.FromSeconds(3), "as soon as it has closed");

        var stuck = new FakeControl(new FakeProcess(target, []));
        before = clock;
        Equal((GrokBotRelaunchKind.StillRunning, 0), (Wait(stuck)?.Kind, stuck.Log.Count), "never closes: nothing started");
        Equal(true, clock - before >= TimeSpan.FromSeconds(20) && clock - before < TimeSpan.FromSeconds(21), "after 20 seconds");

        // The installer appears while Grok Bot is still closing (0.78.1 starts it then): no "left it running".
        var slowProcess = new FakeProcess(target, []);
        var slow = new FakeControl(slowProcess) { InstallerAt = check => check is >= 3 and < 40 };
        var during = Wait(slow, slowProcess, 10);
        Equal((GrokBotRelaunchKind.Relaunched, true), (during?.Kind, slow.Log is ["updating", "start after 200"]),
            "an update during the close wait: waited for, then Grok Bot reopens once");

        var stampChanges = new FakeControl { StampAt = _ => new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 120) };
        Equal((GrokBotRelaunchKind.Relaunched, true), (Wait(stampChanges)?.Kind, stampChanges.Log is ["updating", "start after 200"]),
            "Grok Bot.exe changed right after the close: the update path, then it reopens");

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var cancelled = new FakeControl(new FakeProcess(target, []));
        Equal((GrokBotRelaunchKind.Cancelled, 0), (Wait(cancelled, token: cancel.Token)?.Kind, cancelled.Log.Count), "cancelled: nothing started");
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
        Equal(GrokBotReconnectPolicy.UpdateTooLongText, GrokBotReconnectPrompt.ProblemAfterQuit(GrokBotReconnectRun.OutcomeOf(GrokBotRelaunchKind.UpdateTooLong)),
            "and Open window says why");
    }

    /// <summary>
    /// After Reconnect, the quit's update is waited for: the prompt says Grok Bot is updating, then Wingman opens it. Grok Bot
    /// opened during the update: its usable port is adopted, else the prompt moves to it without a second Reconnect.
    /// </summary>
    private static void AReconnectAfterAnUpdateFinishesTheJob()
    {
        var started = new DateTime(2026, 1, 1, 8, 59, 0, DateTimeKind.Utc);
        var offer = new GrokBotReconnectOffer(Second, true, T0, started);
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
        prompt.Finished(Run(slow));
        Equal((GrokBotPromptState.CouldNotStart, GrokBotReconnectPolicy.UpdateTooLongText), (prompt.State, prompt.Problem), "too long: says so, with Open Grok Bot");

        var plainReopen = new Steps(unconnected, reopened) { Result = GrokBotRelaunchKind.Reopened, RelaunchQuit = quit };
        Equal(GrokBotReconnectOutcome.Reopened, Run(plainReopen).Outcome, "reopened by the user without an update: handed over, waiting for Reconnect");
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

        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("HandOverGrokPrompt(prompt, goOn: result.Outcome == GrokBotReconnectOutcome.UpdatedAndReopened)", StringComparison.Ordinal)
            && tray.Contains("grokReconnect.HandOver(", StringComparison.Ordinal), "the tray moves the prompt to the reopened Grok Bot");
        var tickHandOver = tray.IndexOf("case GrokBotPromptStep.HandOver:", StringComparison.Ordinal);
        Equal(true, tickHandOver > 0 && tray.IndexOf("HandOverGrokPrompt(prompt, goOn: false);", tickHandOver, StringComparison.Ordinal) is var call
            && call > 0 && call < tray.IndexOf("break;", tickHandOver, StringComparison.Ordinal), "the tick hands over to a Grok Bot opened after the quit");
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

    /// <summary>The handed-over prompt is its process's one prompt: no settle, and no second offer later.</summary>
    private static void AHandOverIsTheOnlyPromptForItsProcess()
    {
        var policy = new GrokBotReconnectPolicy();
        var facts = new Facts("1.0.0");
        policy.Observe(new(First, true, Present), T0, facts);
        facts.Version = "1.1.0";
        var offer = policy.HandOver(new(Third, false, Present), T0.AddSeconds(1), facts);
        Equal((Third, true), (offer?.ProcessId, offer?.Updated ?? false), "handed over at once, and the new version proves the update");
        var later = Enumerable.Range(1, 60).Select(second => policy.Observe(new(Third, false, Present), T0.AddSeconds(1 + second), facts)).ToArray();
        Equal(true, later.All(next => next is null), "no second prompt for it");
        Equal(true, policy.HandOver(new(Third, true, Present), T0, facts) is null && policy.HandOver(new(null, false, Present), T0, facts) is null,
            "nothing to hand over to when connected or not running");
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
        var launcher = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("Executable, () => CommandLine(id),", StringComparison.Ordinal)
            && launcher.Contains("() => ParentProcessId(id) is { } parent ? WindowProcessInfo.ImagePathOf((uint)parent) : null", StringComparison.Ordinal),
            "the installer check gives an uninstaller copy's command line and parent");
        // Only these names are asked about; the docs and the class comment say exactly that.
        var mayBe = launcher[launcher.IndexOf("private static bool MayBeInstaller(", StringComparison.Ordinal)..];
        mayBe = mayBe[..mayBe.IndexOf(";\n", StringComparison.Ordinal)];
        Equal(4, new[] { "StartsWith(\"sand-update-\"", "StartsWith(\"Uninstall\"", "Equals(\"Un_A\"", "Equals(\"Au_\"" }.Count(name => mayBe.Contains(name, StringComparison.Ordinal)),
            "the launcher asks about sand-update-…, Uninstall…, Un_A and Au_");
        Equal(4, mayBe.Split("StringComparison.OrdinalIgnoreCase").Length - 1, "and nothing else");
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
