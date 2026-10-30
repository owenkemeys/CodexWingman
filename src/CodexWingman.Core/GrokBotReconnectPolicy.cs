namespace CodexWingman.Core;

/// <summary>
/// One quick look at Grok Bot, taken every <see cref="GrokBotReconnectPolicy.CheckInterval"/>: its main (browser) process,
/// with or without a visible window (null when Grok Bot isn't running) and when that process started, whether Wingman's
/// debugging port is listening, how long the user has been away, and whether a full-screen app or presentation is in
/// front (Windows' notification state: busy, full-screen Direct3D or presentation mode).
/// </summary>
public sealed record GrokBotObservation(int? ProcessId, bool Connected, TimeSpan UserIdle, DateTime? ProcessStarted = null, bool FullScreen = false)
{
    /// <summary>The exact main process seen (id and start time), or null when Grok Bot isn't running.</summary>
    public GrokBotProcess? Process => ProcessId is { } id ? new GrokBotProcess(id, ProcessStarted) : null;
}

/// <summary>One exact Grok Bot process: its id and start time, so a reused id is never mistaken for it.</summary>
public sealed record GrokBotProcess(int Id, DateTime? Started);

/// <summary>Slow facts read only when needed: the Grok Bot process's command line and the Grok Bot.exe version.</summary>
public interface IGrokBotFacts
{
    string? CommandLine(int processId);
    string? ExecutableVersion();
}

/// <summary>
/// The one prompt Wingman showed for one unconnected Grok Bot process. <see cref="Updated"/> is true only when an update is
/// proven (the process was started with <c>--updated</c>, or Grok Bot.exe's version changed since Wingman last had it connected).
/// </summary>
public sealed record GrokBotReconnectOffer(int ProcessId, bool Updated, DateTimeOffset ShownAt, DateTime? ProcessStarted = null)
{
    public GrokBotProcess Target => new(ProcessId, ProcessStarted);

    /// <summary>Whether <paramref name="current"/> still shows this offer's exact process (id and start time).</summary>
    public bool Owns(GrokBotObservation current) => current.ProcessId == ProcessId && current.ProcessStarted == ProcessStarted;

    /// <summary>Open window or Repair, after the user's Yes to closing and reopening Grok Bot: no offer was shown.</summary>
    public bool Approved { get; init; }

    public string Title => Approved ? GrokBotReconnectPolicy.ApprovedTitle : Updated ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle;
    public string Text => GrokBotReconnectPolicy.PromptText;
}

/// <summary>
/// Grok Bot can update itself while the PC is idle, and its installer reopens it without Wingman's debugging port, so the
/// hook is lost. Wingman never closes or reopens Grok Bot on its own; it offers one prompt, once per Grok Bot process:
/// only after Wingman had Grok Bot connected earlier in this session, only once the unconnected main process (with or
/// without a visible window) has stayed so for <see cref="Settle"/> (a Grok Bot that relaunches itself with the port is listening by then), and only while the user is
/// at the PC (a prompt shown to an empty room is lost) and no full-screen app or presentation is in front; until then it
/// waits. The tray checks every <see cref="CheckInterval"/>, separately from
/// the helper refresh, so the prompt appears a few seconds after the port goes.
/// </summary>
public sealed class GrokBotReconnectPolicy
{
    /// <summary>
    /// SHQueryUserNotificationState: busy (QUNS_BUSY 2), full-screen Direct3D (3), presentation mode (4) or a full-screen
    /// Store app (QUNS_APP 7) means something full-screen is in front, so no prompt is shown over it.
    /// </summary>
    public static bool FullScreen(int notificationState) => notificationState is 2 or 3 or 4 or 7;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the every-<see cref="CheckInterval"/> check runs: while a prompt is open (it follows its process even after an
    /// uninstall), or when Grok Bot is installed and set up in Wingman: a Grok Bot port is recorded (Wingman opened Grok Bot
    /// or adopted its port before; the Start menu option records one too) or the Start menu option is on. A Grok Bot that
    /// Wingman has never had stays unwatched; one already running with a port is still adopted at Wingman's startup and
    /// from Wingman's Grok Bot menu, which records the port, so the check runs from then on.
    /// </summary>
    public static bool ShouldCheck(bool promptOpen, bool installed, bool portRecorded, Func<bool> startLinkOn) =>
        promptOpen || installed && (portRecorded || startLinkOn());
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromSeconds(60);

    public const string UpdatedTitle = "Grok Bot updated";
    public const string ReopenedTitle = "Grok Bot reopened without Wingman";
    public const string ApprovedTitle = "Grok Bot";
    /// <summary>The prompt window's one line; its Reconnect button is the consent.</summary>
    public const string PromptText = "Helpers need Grok Bot reopened by Wingman.";
    /// <summary>
    /// After Reconnect. On Windows Grok Bot 0.78.1 has no tray icon and closing its last window quits it; there is no
    /// outside quit Wingman could use gracefully, so the user closes the window. Wingman then opens it again with the port.
    /// </summary>
    public const string QuitText = "Close Grok Bot's window. Wingman reopens it.";
    public const string StillRunningText = "Grok Bot is still closing, so Wingman didn't reopen it.";
    public const string BusyTooLongText = "Wingman was busy, so it didn't reopen Grok Bot.";
    public const string CrashedText = "Grok Bot closed unexpectedly, so Wingman didn't reopen it.";
    public const string SessionEndingText = "Windows began to shut down or sign out, so Wingman didn't reopen Grok Bot.";
    /// <summary>The user quit Grok Bot and that started its update: Wingman reopens it once the update has finished.</summary>
    public const string UpdatingText = "Grok Bot is updating... Wingman reopens it when the update has finished.";
    public const string UpdateTooLongText = "Grok Bot's update was still running after 5 minutes, so Wingman didn't reopen it.";
    public const string ReopenedText = "Grok Bot was opened again without Wingman.";
    public const string CouldNotOpenText = "Wingman couldn't reopen Grok Bot.";
    public const string ExitTooLongText = "A Grok Bot update started, but Grok Bot didn't finish closing within 30 seconds, so Wingman didn't reopen it.";
    public const string GoneText = "Grok Bot closed before Wingman could reopen it.";
    public const string ReplacedText = "Another Grok Bot replaced the one you chose, so Wingman didn't reopen it.";
    public const string QuitTimedOutText = "Grok Bot wasn't closed within 10 minutes, so Wingman left it running.";
    public const string NotClosedText = "Grok Bot didn't close within 20 seconds, so Wingman left it running.";
    public const string OpeningText = "Wingman is opening Grok Bot...";
    public const string FailedText = "Something went wrong, so Wingman didn't reopen Grok Bot. Details are in Wingman's log.";
    /// <summary>Wingman's own run ended before it could reopen Grok Bot, and not because the user cancelled it.</summary>
    public const string StoppedText = "Wingman stopped before it could reopen Grok Bot.";
    /// <summary>Grok Bot runs with Wingman's port, but connecting the Helpers to it failed.</summary>
    public const string ConnectFailedText = "Grok Bot is open, but Wingman couldn't connect its Helpers. Details are in Wingman's log.";
    public const string ConnectingText = "Wingman is connecting Grok Bot's Helpers...";
    /// <summary>Grok Bot exited while Wingman was connecting its Helpers, or after that failed.</summary>
    public const string ClosedBeforeConnectText = "Grok Bot closed before Wingman could connect its Helpers.";

    private bool wasConnected;
    private GrokBotProcess? connectedProcess;
    private string? connectedVersion;
    // Tracked by id and start time, so a reused process id is a new Grok Bot run.
    private GrokBotProcess? unconnectedProcess;
    private DateTimeOffset unconnectedSince;
    private GrokBotProcess? promptedProcess;

    public GrokBotReconnectOffer? Observe(GrokBotObservation observation, DateTimeOffset now, IGrokBotFacts facts)
    {
        if (observation.Connected)
        {
            wasConnected = true;
            // Read the version once per connected run; it is the baseline that proves a later update.
            if (connectedProcess != observation.Process || connectedVersion is null)
            {
                connectedProcess = observation.Process;
                connectedVersion = facts.ExecutableVersion() ?? connectedVersion;
            }
            unconnectedProcess = null;
            promptedProcess = null;
            return null;
        }
        // The main process counts with or without a visible window (one may not be shown yet).
        if (observation.Process is not { } process)
        {
            unconnectedProcess = null;
            return null;
        }
        if (unconnectedProcess != process)
        {
            unconnectedProcess = process;
            unconnectedSince = now;
        }
        if (!wasConnected || promptedProcess == process) return null;
        if (now - unconnectedSince < Settle || observation.UserIdle > PresentWithin || observation.FullScreen) return null;
        promptedProcess = process;
        var version = facts.ExecutableVersion();
        var updated = HasUpdatedFlag(facts.CommandLine(process.Id))
            || connectedVersion is not null && version is not null && !string.Equals(connectedVersion, version, StringComparison.Ordinal);
        return new GrokBotReconnectOffer(process.Id, updated, now, process.Started);
    }

    /// <summary>
    /// The pressed prompt now follows <paramref name="process"/> (another Grok Bot opened while the reconnect the user had
    /// agreed to was under way): that prompt is this process's one prompt, so the policy never offers it a second one,
    /// also after the user closes it.
    /// </summary>
    public void Following(GrokBotProcess process)
    {
        if (process.Id != 0) promptedProcess = process;
    }

    /// <summary>True when a Windows command line carries the whole argument <c>--updated</c> (Grok Bot's installer adds it).</summary>
    public static bool HasUpdatedFlag(string? commandLine) =>
        commandLine is not null && SplitCommandLine(commandLine).Skip(1).Any(argument => argument == "--updated");

    /// <summary>Splits a command line the way Windows programs read it: spaces separate, double quotes group.</summary>
    public static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var character in commandLine)
        {
            if (character == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && char.IsWhiteSpace(character))
            {
                if (any) yield return current.ToString();
                current.Clear();
                any = false;
                continue;
            }
            current.Append(character);
            any = true;
        }
        if (any) yield return current.ToString();
    }
}

/// <summary>What the prompt window shows.</summary>
public enum GrokBotPromptState
{
    /// <summary>[Reconnect] [Not now].</summary>
    Offered,
    /// <summary>A button was pressed: a disabled "Working..." button; acts once Wingman is free.</summary>
    Working,
    /// <summary>Waiting for the user to quit that Grok Bot process; [Cancel].</summary>
    WaitingForQuit,
    /// <summary>The user quit it and Grok Bot's update is running: "Grok Bot is updating..."; [Cancel]. Wingman reopens it after.</summary>
    Updating,
    /// <summary>It quit, but Wingman couldn't open it again: says why; [Open Grok Bot] [Not now].</summary>
    CouldNotStart,
    /// <summary>Grok Bot runs with the port: Wingman connects its Helpers; a disabled "Working...". Closes only once they are connected.</summary>
    Connecting,
    Closed,
}

/// <summary>
/// What the tray should do now for the prompt. HandOver: another Grok Bot started after the press, and the same prompt now
/// follows it (<see cref="GrokBotReconnectPrompt.Offer"/> changed); the tray tells the policy
/// (<see cref="GrokBotReconnectPolicy.Following"/>) and shows the prompt's new state.
/// </summary>
public enum GrokBotPromptStep { None, Reconnect, Open, HandOver, Connect, ApprovedClose }

/// <summary>
/// The reconnect prompt window's state, bound to one offer: its exact Grok Bot process (id and start time). It replaces
/// tray balloons, whose clicks Windows can't tie to one balloon. Until Reconnect it has no time limit, and closes when
/// that process exits or is replaced, when Grok Bot connects, or after Not now. Once the user has pressed Reconnect (or
/// Open Grok Bot, or said Yes to Open window or Repair), the prompt stays pressed for the rest of its life: nothing can
/// turn it back into an unpressed one. It closes once Grok Bot is reconnected (or the user cancels). When another Grok
/// Bot opens after the press, this same prompt follows it (<see cref="Follow"/>) and says "Grok Bot was opened again
/// without Wingman." with Open Grok Bot, never Reconnect; there is no second window. It shows "Grok Bot is updating..."
/// while an update runs, or stays and says why Wingman couldn't open it, with an Open Grok Bot button. A button pressed while Wingman is busy shows "Working..." and
/// acts once Wingman is free.
/// </summary>
public sealed class GrokBotReconnectPrompt(GrokBotReconnectOffer offer)
{
    private GrokBotPromptStep pending;
    /// <summary>The reason shown once the Grok Bot the prompt follows has closed between runs; null while that isn't news.</summary>
    private string? goneText;

    /// <summary>The Grok Bot process the prompt is for. After a press it can only move to another Grok Bot (<see cref="Follow"/>).</summary>
    public GrokBotReconnectOffer Offer { get; private set; } = offer;
    public GrokBotPromptState State { get; private set; } = GrokBotPromptState.Offered;
    public string? ClosedBecause { get; private set; }
    /// <summary>Set once that exact process has quit: what Open Grok Bot needs.</summary>
    public GrokBotRelaunch.ClosedProcess? Quit { get; private set; }
    /// <summary>Why Wingman couldn't open Grok Bot again (shown while <see cref="GrokBotPromptState.CouldNotStart"/>).</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The window's text for the current state. "Working..." never shows the reason from before the press: it says Wingman is
    /// opening Grok Bot after Open Grok Bot, and the prompt's own text after Reconnect.
    /// </summary>
    public string Message => State switch
    {
        GrokBotPromptState.WaitingForQuit => GrokBotReconnectPolicy.QuitText,
        GrokBotPromptState.Updating => GrokBotReconnectPolicy.UpdatingText,
        GrokBotPromptState.CouldNotStart => Problem ?? GrokBotReconnectPolicy.StillRunningText,
        GrokBotPromptState.Working => lastStep is GrokBotPromptStep.Open or GrokBotPromptStep.ApprovedClose || Quit is not null ? GrokBotReconnectPolicy.OpeningText : GrokBotReconnectPolicy.PromptText,
        GrokBotPromptState.Connecting => connectingText,
        _ => Problem ?? (Quit is not null ? GrokBotReconnectPolicy.QuitText : GrokBotReconnectPolicy.PromptText),
    };

    /// <summary>
    /// The window's buttons for the current state: the primary one (null: hidden) and whether it is enabled, and the other
    /// one. Every state the prompt ends a run in other than Closed has a reason and either [Reconnect] (only while the
    /// offer's Grok Bot still runs and Wingman never saw it quit) or [Open Grok Bot], with [Not now].
    /// </summary>
    public (string? Primary, bool PrimaryEnabled, string Secondary) Buttons => State switch
    {
        GrokBotPromptState.WaitingForQuit or GrokBotPromptState.Updating => (null, false, "Cancel"),
        GrokBotPromptState.CouldNotStart => ("Open Grok Bot", true, "Not now"),
        GrokBotPromptState.Offered => ("Reconnect", true, "Not now"),
        _ => ("Working...", false, "Not now"),
    };

    /// <summary>
    /// A run is under way for this prompt (waiting for the quit, opening Grok Bot, waiting for the update, or connecting the
    /// Helpers): the run says what became of Grok Bot, so the check every few seconds leaves the prompt to it.
    /// </summary>
    public bool InRun => State is GrokBotPromptState.WaitingForQuit or GrokBotPromptState.Updating or GrokBotPromptState.Connecting
        || State == GrokBotPromptState.Working && pending == GrokBotPromptStep.None;

    private string connectingText = GrokBotReconnectPolicy.ConnectingText;
    private GrokBotProcess? connectFailedFor;

    /// <summary>
    /// Open window or Repair after the user's Yes: the prompt for that run, already pressed and working, so every result
    /// of the close and reopen ends here like a Reconnect's. <paramref name="since"/> is Grok Bot.exe before the close.
    /// </summary>
    public static GrokBotReconnectPrompt AfterYes(GrokBotProcess? closed, GrokBotExecutableStamp? since, DateTimeOffset now)
    {
        var prompt = new GrokBotReconnectPrompt(new GrokBotReconnectOffer(closed?.Id ?? 0, false, now, closed?.Started) { Approved = true })
        {
            State = GrokBotPromptState.Working, Pressed = true, ApprovedSince = since,
        };
        prompt.lastStep = GrokBotPromptStep.ApprovedClose;
        return prompt;
    }

    /// <summary>Grok Bot.exe as it was before Open window or Repair's close (see <see cref="AfterYes"/>).</summary>
    public GrokBotExecutableStamp? ApprovedSince { get; private init; }

    /// <summary>The Reconnect button. True: act now. Only from the offered state, so a second press does nothing.</summary>
    public bool Reconnect(bool busy) => Press(GrokBotPromptState.Offered, GrokBotPromptStep.Reconnect, busy);

    /// <summary>The Open Grok Bot button, offered only after Wingman couldn't open it. True: act now.</summary>
    public bool Open(bool busy) => Press(GrokBotPromptState.CouldNotStart, GrokBotPromptStep.Open, busy);

    /// <summary>True once the user pressed Reconnect (or Open Grok Bot): from then on only a reconnect or their Cancel closes it.</summary>
    public bool Pressed { get; private set; }
    private GrokBotPromptStep lastStep;

    private bool Press(GrokBotPromptState from, GrokBotPromptStep step, bool busy)
    {
        if (State != from) return false;
        State = GrokBotPromptState.Working;
        Pressed = true;
        lastStep = step;
        pending = busy ? step : GrokBotPromptStep.None;
        return !busy;
    }

    /// <summary>Not now, or Cancel while waiting for the quit.</summary>
    public void NotNow() => Close("not-now");

    /// <summary>
    /// Every check. Before a press the prompt belongs to its process: it closes when that process exits or is replaced, or
    /// when Grok Bot connects. After a press it never closes here: while a run is under way the run decides; between runs a
    /// Grok Bot that came up with Wingman's port gets its Helpers connected (<see cref="GrokBotPromptStep.Connect"/>), and
    /// this same prompt follows another Grok Bot (<see cref="GrokBotPromptStep.HandOver"/>), with [Open Grok Bot]. Returns a step kept while Wingman was busy, once it is free.
    /// </summary>
    public GrokBotPromptStep Observe(GrokBotObservation current, bool busy)
    {
        if (State == GrokBotPromptState.Closed) return GrokBotPromptStep.None;
        if (!Pressed)
        {
            if (current.Connected) Close("connected");
            else if (!Offer.Owns(current)) Close("gone");
            return GrokBotPromptStep.None;
        }
        if (InRun) return GrokBotPromptStep.None;
        if (current.Connected)
        {
            // Connected, but the Helpers only count once Wingman has connected them; once that failed for this Grok Bot,
            // only the user's Open Grok Bot tries again.
            if (busy || current.Process == connectFailedFor) return GrokBotPromptStep.None;
            pending = GrokBotPromptStep.None;
            Problem = null;
            connectingText = GrokBotReconnectPolicy.ConnectingText;
            State = GrokBotPromptState.Connecting;
            return GrokBotPromptStep.Connect;
        }
        if ((State == GrokBotPromptState.CouldNotStart || Quit is not null) && current.Process is { } other && other != Offer.Target)
        {
            // Another Grok Bot runs now (opened by the user, or an installer) after the quit, or while the prompt says why it
            // couldn't reopen Grok Bot (a Reconnect kept while busy runs, and re-checks, first): this same prompt follows it.
            Follow(other, GrokBotReconnectPolicy.ReopenedText, GrokBotReconnectPolicy.GoneText);
            return GrokBotPromptStep.HandOver;
        }
        if (State == GrokBotPromptState.CouldNotStart && current.Process is null && goneText is not null)
        {
            // The Grok Bot the reason was about has closed: say so; Open Grok Bot now starts one.
            Problem = goneText;
            goneText = null;
            return GrokBotPromptStep.None;
        }
        if (State != GrokBotPromptState.Working || pending == GrokBotPromptStep.None || busy) return GrokBotPromptStep.None;
        var step = pending;
        pending = GrokBotPromptStep.None;
        return step;
    }

    /// <summary>
    /// The one way the prompt moves to another Grok Bot: the same prompt, still pressed, now for <paramref name="process"/>,
    /// with <paramref name="problem"/> and [Open Grok Bot] [Not now]. The quit's proof was for the old process, so it goes;
    /// Open Grok Bot then asks the user to close this one (or connects a port it has). <paramref name="whenGone"/> is said
    /// if this Grok Bot closes before the user acts.
    /// </summary>
    private void Follow(GrokBotProcess process, string problem, string whenGone)
    {
        Offer = Offer with { ProcessId = process.Id, ProcessStarted = process.Started };
        Quit = null;
        pending = GrokBotPromptStep.None;
        Problem = problem;
        goneText = whenGone;
        State = GrokBotPromptState.CouldNotStart;
    }

    /// <summary>The run reached the point where the user quits Grok Bot.</summary>
    public void WaitingForQuit()
    {
        if (State == GrokBotPromptState.Working) State = GrokBotPromptState.WaitingForQuit;
    }

    /// <summary>
    /// The offer's process has exited (the relaunch calls this the moment it does, before it goes on): the prompt keeps the
    /// proof whatever happens next, and stops asking for the quit; it says Wingman is opening Grok Bot.
    /// </summary>
    public void Exited(GrokBotRelaunch.ClosedProcess quit)
    {
        if (State == GrokBotPromptState.Closed) return;
        Quit ??= quit;
        if (State == GrokBotPromptState.WaitingForQuit) State = GrokBotPromptState.Working;
    }

    /// <summary>The user quit Grok Bot and its update is running: the prompt says so and keeps [Cancel].</summary>
    public void Updating(GrokBotRelaunch.ClosedProcess? quit)
    {
        if (State == GrokBotPromptState.Closed) return;
        if (quit is not null) Quit = quit;
        Problem = null;
        State = GrokBotPromptState.Updating;
    }

    /// <summary>
    /// The run's result (a run only follows a press). The structural guarantee: only <see cref="GrokBotReconnectOutcome.Connected"/>
    /// (the Helpers are connected) closes the prompt here, and the user's Cancel or Not now has closed it already. Busy keeps
    /// the press pending. Grok Bot running with the port (Relaunched, Adopted, AlreadyConnected) goes on to Connecting.
    /// Every other outcome, including a Cancelled that didn't come from the user, keeps the prompt with a plain reason and
    /// [Open Grok Bot] [Not now]; never [Reconnect] again. <paramref name="now"/> is Grok Bot as the run ended (null when
    /// Wingman couldn't look); a failed connect says whether Grok Bot is still open or closed meanwhile.
    /// </summary>
    public void Finished(GrokBotReconnectResult result, GrokBotObservation? now = null)
    {
        if (State == GrokBotPromptState.Closed) return;
        if (result.Quit is not null) Quit ??= result.Quit;
        switch (result.Outcome)
        {
            case GrokBotReconnectOutcome.Busy:
                pending = Quit is null ? lastStep : GrokBotPromptStep.Open;
                State = GrokBotPromptState.Working;
                return;
            case GrokBotReconnectOutcome.Connected:
                Close("connected");
                return;
            case GrokBotReconnectOutcome.Relaunched or GrokBotReconnectOutcome.Adopted or GrokBotReconnectOutcome.AlreadyConnected:
                Problem = null;
                connectingText = result.Outcome == GrokBotReconnectOutcome.Relaunched ? GrokBotReconnectPolicy.OpeningText : GrokBotReconnectPolicy.ConnectingText;
                State = GrokBotPromptState.Connecting;
                return;
        }
        // The user's Cancel closed the prompt before the run ended, so a Cancelled reaching it here is Wingman's own stop.
        var outcome = result.Outcome == GrokBotReconnectOutcome.Cancelled ? GrokBotReconnectOutcome.Stopped : result.Outcome;
        if (outcome == GrokBotReconnectOutcome.NotConnected)
        {
            // The reason comes from what Grok Bot is doing now: still open, or closed while Wingman was connecting.
            connectFailedFor = now?.Process;
            Problem = now is { Process: null } ? GrokBotReconnectPolicy.ClosedBeforeConnectText : GrokBotReconnectPolicy.ConnectFailedText;
            goneText = now is { Process: null } ? null : GrokBotReconnectPolicy.ClosedBeforeConnectText;
            State = GrokBotPromptState.CouldNotStart;
            return;
        }
        if (outcome is GrokBotReconnectOutcome.Reopened or GrokBotReconnectOutcome.UpdatedAndReopened && now?.Process is { } reopened && reopened != Offer.Target)
        {
            // Opened again without Wingman: the same prompt follows that Grok Bot. After an update the user's quit started,
            // it goes straight on to asking for the quit, since the user already chose to reconnect.
            Follow(reopened, GrokBotReconnectPolicy.ReopenedText, GrokBotReconnectPolicy.GoneText);
            if (outcome == GrokBotReconnectOutcome.UpdatedAndReopened)
            {
                lastStep = GrokBotPromptStep.Reconnect;
                pending = GrokBotPromptStep.Reconnect;
                State = GrokBotPromptState.Working;
            }
            return;
        }
        Problem = outcome is GrokBotReconnectOutcome.Reopened or GrokBotReconnectOutcome.UpdatedAndReopened && now is { Process: null }
            ? GrokBotReconnectPolicy.GoneText : ProblemAfterQuit(outcome);
        goneText = null;
        State = GrokBotPromptState.CouldNotStart;
    }

    /// <summary>What the prompt says when Wingman couldn't reopen Grok Bot after Reconnect.</summary>
    public static string ProblemAfterQuit(GrokBotReconnectOutcome outcome) => outcome switch
    {
        GrokBotReconnectOutcome.StillRunning => GrokBotReconnectPolicy.StillRunningText,
        GrokBotReconnectOutcome.Crashed => GrokBotReconnectPolicy.CrashedText,
        GrokBotReconnectOutcome.BusyTooLong => GrokBotReconnectPolicy.BusyTooLongText,
        GrokBotReconnectOutcome.SessionEnding => GrokBotReconnectPolicy.SessionEndingText,
        GrokBotReconnectOutcome.UpdateTooLong => GrokBotReconnectPolicy.UpdateTooLongText,
        GrokBotReconnectOutcome.UpdateRunning => GrokBotRelaunch.UpdateRunningText,
        GrokBotReconnectOutcome.ExitTooLong => GrokBotReconnectPolicy.ExitTooLongText,
        GrokBotReconnectOutcome.Gone => GrokBotReconnectPolicy.GoneText,
        GrokBotReconnectOutcome.Replaced => GrokBotReconnectPolicy.ReplacedText,
        GrokBotReconnectOutcome.Failed => GrokBotReconnectPolicy.FailedText,
        GrokBotReconnectOutcome.Reopened or GrokBotReconnectOutcome.UpdatedAndReopened => GrokBotReconnectPolicy.ReopenedText,
        GrokBotReconnectOutcome.Stopped or GrokBotReconnectOutcome.Cancelled => GrokBotReconnectPolicy.StoppedText,
        GrokBotReconnectOutcome.NotConnected => GrokBotReconnectPolicy.ConnectFailedText,
        GrokBotReconnectOutcome.QuitTimedOut => GrokBotReconnectPolicy.QuitTimedOutText,
        GrokBotReconnectOutcome.NotClosed => GrokBotReconnectPolicy.NotClosedText,
        _ => GrokBotReconnectPolicy.CouldNotOpenText,
    };

    private void Close(string reason)
    {
        if (State == GrokBotPromptState.Closed) return;
        State = GrokBotPromptState.Closed;
        ClosedBecause = reason;
    }
}

/// <summary>
/// The tray's one reconnect prompt. A new, unpressed prompt from the policy's offer only takes the place of nothing, a
/// closed prompt or another unpressed one, so a prompt the user pressed (or said Yes for) can't be replaced by an
/// unpressed one. Only the Yes to Open window or Repair replaces it, with another pressed prompt.
/// </summary>
public sealed class GrokBotPromptSlot
{
    public GrokBotReconnectPrompt? Current { get; private set; }

    /// <summary>The policy's offer: the new prompt to show, or null while a pressed prompt is open (it stays).</summary>
    public GrokBotReconnectPrompt? Offer(GrokBotReconnectOffer offer)
    {
        if (Current is { Pressed: true, State: not GrokBotPromptState.Closed }) return null;
        return Current = new GrokBotReconnectPrompt(offer);
    }

    /// <summary>Open window or Repair after the Yes (see <see cref="GrokBotReconnectPrompt.AfterYes"/>): already pressed.</summary>
    public GrokBotReconnectPrompt AfterYes(GrokBotProcess? closed, GrokBotExecutableStamp? since, DateTimeOffset now) =>
        Current = GrokBotReconnectPrompt.AfterYes(closed, since, now);

    /// <summary>Lets go of a prompt once it is closed; Wingman's exit (<paramref name="exiting"/>) lets go of any prompt.</summary>
    public void Release(bool exiting = false)
    {
        if (exiting || Current is null or { State: GrokBotPromptState.Closed } or { Pressed: false }) Current = null;
    }
}

/// <summary>What a press on the prompt led to. Only <see cref="Relaunched"/> means Grok Bot was opened again.</summary>
public enum GrokBotReconnectOutcome
{
    /// <summary>The offer's Grok Bot process was gone at the press: nothing started; the prompt says so.</summary>
    Gone,
    /// <summary>Another Grok Bot process runs instead of the offer's at the press: nothing started; the prompt says so.</summary>
    Replaced,
    AlreadyConnected,
    /// <summary>Grok Bot already had a usable port: adopted, nothing restarted.</summary>
    Adopted,
    /// <summary>Wingman was busy: the prompt shows "Working..." and acts once free.</summary>
    Busy,
    /// <summary>Not now or Cancel: nothing started.</summary>
    Cancelled,
    /// <summary>Not quit within the limit: Grok Bot is left running; the prompt says so, with Open Grok Bot.</summary>
    QuitTimedOut,
    /// <summary>
    /// A Grok Bot installer or update is running. Before the quit: nothing started, and the post-update prompt handles what
    /// it opens. After the quit the run waits for it instead (<see cref="IGrokBotReconnectSteps.AfterUpdateAsync"/>).
    /// </summary>
    UpdateRunning,
    /// <summary>After the quit, Grok Bot's update was still running at the limit: nothing started; the prompt says so.</summary>
    UpdateTooLong,
    /// <summary>An installer started but Grok Bot hadn't finished closing within 30 s: nothing started; the prompt says so.</summary>
    ExitTooLong,
    /// <summary>It quit, but a Grok Bot process was still running at the limit: nothing started; the prompt says so.</summary>
    StillRunning,
    /// <summary>It quit, but Wingman stayed busy past the limit: nothing started; the prompt says so.</summary>
    BusyTooLong,
    /// <summary>The user opened Grok Bot again before Wingman did, without a usable port: nothing started; the prompt moves to it.</summary>
    Reopened,
    /// <summary>
    /// Grok Bot opened again during the update the user's quit started, without a usable port: nothing started; the prompt
    /// moves to it and goes straight on to the quit step (the user already chose Reconnect).
    /// </summary>
    UpdatedAndReopened,
    /// <summary>It exited with a failure code (a crash, or ended from Task Manager), not the user's quit: nothing started; the prompt says so.</summary>
    Crashed,
    /// <summary>Windows is shutting down or signing out: nothing started.</summary>
    SessionEnding,
    /// <summary>The run itself threw: nothing more started; the prompt stays and says so, and the log has the error.</summary>
    Failed,
    Relaunched,
    /// <summary>Wingman's own run stopped (cancelled, not by the user): nothing more started; the prompt says so.</summary>
    Stopped,
    /// <summary>Grok Bot ran with the port and Wingman connected its Helpers: the only outcome that closes the prompt by itself.</summary>
    Connected,
    /// <summary>Grok Bot ran with the port but connecting its Helpers failed: the prompt says so, with Open Grok Bot.</summary>
    NotConnected,
    /// <summary>Open window or Repair asked Grok Bot to close after the Yes, and it didn't in time: it is left running; the prompt says so.</summary>
    NotClosed,
}

/// <summary>An outcome, and the proof that the offer's process quit once it has.</summary>
public sealed record GrokBotReconnectResult(GrokBotReconnectOutcome Outcome, GrokBotRelaunch.ClosedProcess? Quit = null);

/// <summary>What the reconnect needs from the tray, so its order of checks can be tested.</summary>
public interface IGrokBotReconnectSteps
{
    GrokBotObservation Observe();
    /// <summary>True when the process already has a usable port (127.0.0.1, listening, owned by it) and it was recorded.</summary>
    bool AdoptListeningPort(int process);
    bool Busy { get; }
    /// <summary>The prompt now asks the user to quit Grok Bot.</summary>
    void WaitingForQuit();
    /// <summary>Waits for the user to quit exactly the offer's process, then opens Grok Bot with the port (<see cref="GrokBotRelaunch.RunAsync"/>).</summary>
    Task<GrokBotRelaunchResult> RelaunchAfterQuitAsync(GrokBotProcess target, CancellationToken cancellationToken);
    /// <summary>Opens Grok Bot with the port after it quit (<see cref="GrokBotRelaunch.StartWhenClearAsync"/>).</summary>
    Task<GrokBotRelaunchResult> StartAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken);
    /// <summary>
    /// Open Grok Bot without the quit's proof (the process ended or was replaced at the press, or hadn't finished closing):
    /// <see cref="GrokBotRelaunch.OpenAfterPressAsync"/>.
    /// </summary>
    Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, CancellationToken cancellationToken);
    /// <summary>The prompt now says Grok Bot is updating.</summary>
    void Updating(GrokBotRelaunch.ClosedProcess quit);
    /// <summary>Waits (bounded) for the update to finish, then opens Grok Bot with the port (<see cref="GrokBotRelaunch.AfterUpdateAsync"/>).</summary>
    Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken);
    /// <summary>Grok Bot runs with Wingman's port (<paramref name="started"/>: Wingman just started it): connect its Helpers. True once they are.</summary>
    Task<bool> ConnectAsync(bool started, CancellationToken cancellationToken);
    /// <summary>Open window or Repair after the Yes and the close request: <see cref="GrokBotRelaunch.AfterCloseRequestAsync"/>.</summary>
    Task<GrokBotRelaunchResult> AfterApprovedCloseAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since, CancellationToken cancellationToken);
    /// <summary>True once Windows began to shut down or sign out during this run.</summary>
    bool SessionEnding { get; }
    /// <summary>The prompt's state changed: show it.</summary>
    void Changed();
}

/// <summary>
/// Pressing Reconnect: Wingman checks that the offer's exact Grok Bot process is still the one running unconnected (else
/// nothing happens, and Grok Bot is never started) and whether it already has a usable port (then that port is adopted and
/// nothing restarts). Busy: kept for later. Otherwise the user quits Grok Bot and Wingman opens it again with the port.
/// Pressing Open Grok Bot (after Wingman couldn't): the same checks for opening it. After the quit, an update it started is
/// waited for, and a Grok Bot opened meanwhile is adopted or gets the prompt.
/// </summary>
public static class GrokBotReconnectRun
{
    public static Task<GrokBotReconnectResult> RunAsync(GrokBotReconnectOffer offer, IGrokBotReconnectSteps steps, CancellationToken cancellationToken) =>
        RunAsync(offer, steps.Observe(), steps, cancellationToken);

    private static async Task<GrokBotReconnectResult> RunAsync(GrokBotReconnectOffer offer, GrokBotObservation current, IGrokBotReconnectSteps steps, CancellationToken cancellationToken)
    {
        if (current.Connected) return new(GrokBotReconnectOutcome.AlreadyConnected);
        if (!offer.Owns(current)) return new(current.Process is null ? GrokBotReconnectOutcome.Gone : GrokBotReconnectOutcome.Replaced);
        if (steps.AdoptListeningPort(offer.ProcessId)) return new(GrokBotReconnectOutcome.Adopted);
        if (steps.Busy) return new(GrokBotReconnectOutcome.Busy);
        steps.WaitingForQuit();
        return await AfterQuitAsync(await steps.RelaunchAfterQuitAsync(offer.Target, cancellationToken), steps, cancellationToken);
    }

    /// <summary>
    /// Open Grok Bot: with the quit's proof when there is one; else, while <paramref name="offer"/>'s own Grok Bot still
    /// runs, the same as Reconnect (the user is asked to close it); else <see cref="IGrokBotReconnectSteps.OpenAfterPressAsync"/>.
    /// </summary>
    public static async Task<GrokBotReconnectResult> OpenAsync(GrokBotProcess target, GrokBotRelaunch.ClosedProcess? quit, IGrokBotReconnectSteps steps, CancellationToken cancellationToken,
        GrokBotReconnectOffer? offer = null)
    {
        var current = steps.Observe();
        if (current.Connected) return new(GrokBotReconnectOutcome.AlreadyConnected, quit);
        if (quit is null && offer is not null && offer.Owns(current)) return await RunAsync(offer, current, steps, cancellationToken);
        if (steps.Busy) return new(GrokBotReconnectOutcome.Busy, quit);
        var result = quit is not null ? await steps.StartAsync(quit, cancellationToken) : await steps.OpenAfterPressAsync(target, cancellationToken);
        return await AfterQuitAsync(result, steps, cancellationToken);
    }

    /// <summary>Open window or Repair after the Yes and the close request: the same as after the user's quit.</summary>
    public static async Task<GrokBotReconnectResult> ApprovedCloseAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since, IGrokBotReconnectSteps steps, CancellationToken cancellationToken) =>
        await AfterQuitAsync(await steps.AfterApprovedCloseAsync(closed, since, cancellationToken), steps, cancellationToken);

    /// <summary>
    /// After the user's quit. An update it started: the prompt says Grok Bot is updating, and the run waits for it to
    /// finish, then opens Grok Bot. A Grok Bot opened again meanwhile: its usable port is adopted, else the prompt moves to it.
    /// </summary>
    private static async Task<GrokBotReconnectResult> AfterQuitAsync(GrokBotRelaunchResult result, IGrokBotReconnectSteps steps, CancellationToken cancellationToken)
    {
        var updated = false;
        if (result is { Kind: GrokBotRelaunchKind.UpdateRunning, Quit: { } updating })
        {
            steps.Updating(updating);
            updated = true;
            result = await steps.AfterUpdateAsync(updating, cancellationToken);
        }
        if (result is { Kind: GrokBotRelaunchKind.Reopened, Quit: { } quit })
        {
            var current = steps.Observe();
            if (current.Connected) return new(GrokBotReconnectOutcome.AlreadyConnected, quit);
            if (current.ProcessId is { } reopened && steps.AdoptListeningPort(reopened)) return new(GrokBotReconnectOutcome.Adopted, quit);
            return new(updated ? GrokBotReconnectOutcome.UpdatedAndReopened : GrokBotReconnectOutcome.Reopened, quit);
        }
        return Result(result);
    }

    private static GrokBotReconnectResult Result(GrokBotRelaunchResult result) => new(OutcomeOf(result.Kind), result.Quit);

    public static GrokBotReconnectOutcome OutcomeOf(GrokBotRelaunchKind kind) => kind switch
    {
        GrokBotRelaunchKind.Relaunched => GrokBotReconnectOutcome.Relaunched,
        GrokBotRelaunchKind.Gone => GrokBotReconnectOutcome.Gone,
        GrokBotRelaunchKind.Cancelled => GrokBotReconnectOutcome.Cancelled,
        GrokBotRelaunchKind.QuitTimedOut => GrokBotReconnectOutcome.QuitTimedOut,
        GrokBotRelaunchKind.UpdateRunning => GrokBotReconnectOutcome.UpdateRunning,
        GrokBotRelaunchKind.StillRunning => GrokBotReconnectOutcome.StillRunning,
        GrokBotRelaunchKind.Reopened => GrokBotReconnectOutcome.Reopened,
        GrokBotRelaunchKind.Crashed => GrokBotReconnectOutcome.Crashed,
        GrokBotRelaunchKind.SessionEnding => GrokBotReconnectOutcome.SessionEnding,
        GrokBotRelaunchKind.UpdateTooLong => GrokBotReconnectOutcome.UpdateTooLong,
        GrokBotRelaunchKind.ExitTooLong => GrokBotReconnectOutcome.ExitTooLong,
        GrokBotRelaunchKind.NotClosed => GrokBotReconnectOutcome.NotClosed,
        _ => GrokBotReconnectOutcome.BusyTooLong,
    };
}

public enum GrokBotRelaunchKind { Relaunched, Gone, Cancelled, QuitTimedOut, UpdateRunning, StillRunning, BusyTooLong, Reopened, Crashed, SessionEnding, UpdateTooLong, ExitTooLong, NotClosed }

/// <summary>What the relaunch step did, and the proof that the process quit once it has.</summary>
public sealed record GrokBotRelaunchResult(GrokBotRelaunchKind Kind, GrokBotRelaunch.ClosedProcess? Quit = null);

/// <summary>Grok Bot.exe as last seen: a changed file means an update or reinstall wrote it.</summary>
public sealed record GrokBotExecutableStamp(DateTime WrittenUtc, long Length);

/// <summary>
/// One Grok Bot process held by an open process handle (SYNCHRONIZE) from the snapshot until it exits. Windows doesn't
/// reuse a process id while a handle to that process is open, and <see cref="HasExited"/> waits on the handle itself, so
/// another process taking the id can't end the wait.
/// </summary>
public interface IGrokBotProcessHandle : IDisposable
{
    GrokBotProcess Identity { get; }
    bool HasExited { get; }
    /// <summary>Its exit code once it has exited; null while running or when Windows can't say. Closing Grok Bot's last window (its quit on Windows) exits with 0.</summary>
    int? ExitCode { get; }
}

public interface IGrokBotProcessControl
{
    /// <summary>One snapshot of the running Grok Bot processes.</summary>
    IReadOnlyList<IGrokBotProcessHandle> Snapshot();
    /// <summary>Brings the process's window forward if it has a visible one (best effort; never closes anything).</summary>
    void BringForward(IGrokBotProcessHandle process);
    bool AnyRunning();
    /// <summary>Grok Bot's main process now, or null; another one than the quit process means the user reopened it.</summary>
    GrokBotProcess? MainProcess();
    /// <summary>False while Wingman is doing something else (it may be opening Grok Bot itself).</summary>
    bool MayStart();
    /// <summary>Grok Bot.exe's current stamp; null when it can't be read (missing, or being replaced).</summary>
    GrokBotExecutableStamp? ExecutableStamp();
    /// <summary>
    /// True while a Grok Bot installer, update or uninstaller process runs. It lists every process, so it runs off the UI
    /// thread; the relaunch resumes on its caller's thread afterwards.
    /// </summary>
    Task<bool> InstallerRunningAsync();
    /// <summary>True once Windows has said it is shutting down or signing out: Grok Bot is never started then.</summary>
    bool SessionEnding();
    /// <summary>Starts Grok Bot with Wingman's port. Only <see cref="GrokBotRelaunch"/> can make the proof it needs.</summary>
    void Start(GrokBotRelaunch.ClosedProcess proof);
}

/// <summary>
/// Open window and Repair have one deadline for Wingman's own work. The time the user spends on the Yes/No question isn't
/// part of it: the clock stops while they answer, and restarts with <c>afterAnswer</c> once they have.
/// </summary>
public static class GrokBotAnswerTime
{
    /// <summary>Asks with <paramref name="deadline"/> paused; afterwards it fires <paramref name="afterAnswer"/> from now.</summary>
    public static bool AskOffTheClock(CancellationTokenSource deadline, TimeSpan afterAnswer, Func<bool> ask)
    {
        deadline.CancelAfter(Timeout.InfiniteTimeSpan);
        try { return ask(); }
        finally { deadline.CancelAfter(afterAnswer); }
    }

    /// <summary>After the Yes: 2 minutes for the close and start, plus room for Grok Bot to finish closing and for its update.</summary>
    public static TimeSpan AfterYes(GrokBotRelaunchLimits limits) => TimeSpan.FromMinutes(2) + limits.Exit + limits.Update;
}

/// <summary>The relaunch's limits.</summary>
public sealed record GrokBotRelaunchLimits(TimeSpan Quit, TimeSpan Settle, TimeSpan Busy, TimeSpan Poll)
{
    /// <summary>An update the user's quit started: up to 5 minutes for it to finish.</summary>
    public TimeSpan Update { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Finished means no Grok Bot installer runs and Grok Bot.exe stays unchanged for this long.</summary>
    public TimeSpan Stable { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>An installer appeared while Grok Bot was still closing (0.78.1 starts it then): up to 30 s for it to exit.</summary>
    public TimeSpan Exit { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Open window or Repair after the Yes: up to 20 s for Grok Bot to close once asked to.</summary>
    public TimeSpan Close { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>10 minutes to quit, then 20 s for its helper processes to exit and 60 s for Wingman to be free.</summary>
    public static readonly GrokBotRelaunchLimits Default = new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1));
}

/// <summary>
/// The reconnect relaunch. Wingman never closes Grok Bot: one snapshot must hold the offer's exact process (id and start
/// time), held by an open process handle until it exits (<see cref="IGrokBotProcessHandle"/>); Wingman brings its window
/// forward and waits for the user to quit it. If the user opens Grok Bot again meanwhile, Wingman starts nothing. After that process exits,
/// Wingman waits (bounded) for every Grok Bot process to exit and for itself to be free, then starts Grok Bot with the
/// port. It never starts Grok Bot while a Grok Bot installer runs or Grok Bot.exe has changed (an update is under way;
/// after the user's quit <see cref="AfterUpdateAsync"/> waits for it to finish), while Windows is shutting down or signing out, or after the process
/// exited with a failure code (a crash, not the user's quit). <see cref="IGrokBotProcessControl.Start"/> takes a
/// <see cref="ClosedProcess"/>, which only this class creates, once the offer's process has exited.
/// </summary>
public static class GrokBotRelaunch
{
    public sealed class ClosedProcess
    {
        internal ClosedProcess(GrokBotProcess process) => Process = process;
        public GrokBotProcess Process { get; }
    }

    /// <summary>
    /// Waits for the user to quit exactly <paramref name="target"/>, then starts Grok Bot with the port. The moment that
    /// process has exited, <paramref name="exited"/> gets the quit's proof, before anything else can go wrong, so a later
    /// error still leaves the caller holding it.
    /// </summary>
    public static async Task<GrokBotRelaunchResult> RunAsync(GrokBotProcess target, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken, Action<ClosedProcess>? exited = null)
    {
        var snapshot = control.Snapshot();
        GrokBotExecutableStamp? stamp;
        ClosedProcess quit;
        try
        {
            var handle = snapshot.FirstOrDefault(process => process.Identity == target);
            if (handle is null || handle.HasExited) return new(GrokBotRelaunchKind.Gone);
            stamp = control.ExecutableStamp();
            control.BringForward(handle);
            var quitBy = now() + limits.Quit;
            while (!handle.HasExited)
            {
                if (Stopped(control, cancellationToken) is { } stopped) return stopped;
                // Grok Bot 0.78.1 starts its staged installer while it is still shutting down (about 0.5 to 4 s), so an
                // update seen here follows the user's quit: wait (bounded) for that exact process to exit, then wait for
                // the update with the quit's proof.
                if (await UpdateUnderWayAsync(control, stamp))
                    return await ExitAsync(handle, control, limits, now, delay, cancellationToken)
                        ?? new(GrokBotRelaunchKind.UpdateRunning, Exited(target, exited));
                if (now() >= quitBy) return new(GrokBotRelaunchKind.QuitTimedOut);
                if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken) ?? new(GrokBotRelaunchKind.Cancelled);
            }
            // An update first: Grok Bot's installer may end it with a failure code after the user's quit, and that is an
            // update, not a crash. Then only the user's quit (exit code 0) is followed by a start; a crash or a kill is not
            // reopened behind their back.
            quit = Exited(target, exited);
            if (await UpdateUnderWayAsync(control, stamp)) return new(GrokBotRelaunchKind.UpdateRunning, quit);
            if (handle.ExitCode is not 0) return new(GrokBotRelaunchKind.Crashed, quit);
        }
        finally { foreach (var process in snapshot) process.Dispose(); }
        return await StartWhenClearAsync(quit, stamp, control, limits, now, delay, cancellationToken);
    }

    /// <summary>The proof that <paramref name="target"/> has exited, made once at the exit and handed to <paramref name="exited"/> at once.</summary>
    private static ClosedProcess Exited(GrokBotProcess target, Action<ClosedProcess>? exited)
    {
        var proof = new ClosedProcess(target);
        exited?.Invoke(proof);
        return proof;
    }

    /// <summary>
    /// After the offer's process quit: waits up to <c>Settle</c> for no Grok Bot to run and up to <c>Busy</c> for Wingman to
    /// be free, then starts Grok Bot with the port. Never while an update is under way (an installer runs, or Grok Bot.exe
    /// differs from <paramref name="since"/>).
    /// </summary>
    public static async Task<GrokBotRelaunchResult> StartWhenClearAsync(ClosedProcess quit, GrokBotExecutableStamp? since, IGrokBotProcessControl control,
        GrokBotRelaunchLimits limits, Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        var settled = now() + limits.Settle;
        while (control.AnyRunning())
        {
            if (Stopped(control, cancellationToken, quit) is { } stopped) return stopped;
            if (Reopened(control, quit)) return new(GrokBotRelaunchKind.Reopened, quit);
            if (await UpdateUnderWayAsync(control, since)) return new(GrokBotRelaunchKind.UpdateRunning, quit);
            if (now() >= settled) return new(GrokBotRelaunchKind.StillRunning, quit);
            if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken, quit) ?? new(GrokBotRelaunchKind.Cancelled, quit);
        }
        var free = now() + limits.Busy;
        while (true)
        {
            if (Stopped(control, cancellationToken, quit) is { } stopped) return stopped;
            if (Reopened(control, quit)) return new(GrokBotRelaunchKind.Reopened, quit);
            if (await UpdateUnderWayAsync(control, since)) return new(GrokBotRelaunchKind.UpdateRunning, quit);
            // Nothing is awaited from here to the start, so Wingman can't turn busy in between.
            if (control.MayStart())
            {
                if (Stopped(control, cancellationToken, quit) is { } last) return last;
                if (Reopened(control, quit)) return new(GrokBotRelaunchKind.Reopened, quit);
                if (control.AnyRunning()) return new(GrokBotRelaunchKind.StillRunning, quit);
                control.Start(quit);
                return new(GrokBotRelaunchKind.Relaunched, quit);
            }
            if (now() >= free) return new(GrokBotRelaunchKind.BusyTooLong, quit);
            if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken, quit) ?? new(GrokBotRelaunchKind.Cancelled, quit);
        }
    }

    /// <summary>
    /// Waits up to <c>Exit</c> for <paramref name="handle"/>'s process to exit. Null once it has; otherwise
    /// <see cref="GrokBotRelaunchKind.ExitTooLong"/>, or why the wait stopped. Never closes it.
    /// </summary>
    private static async Task<GrokBotRelaunchResult?> ExitAsync(IGrokBotProcessHandle handle, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        var until = now() + limits.Exit;
        while (!handle.HasExited)
        {
            if (Stopped(control, cancellationToken) is { } stopped) return stopped;
            if (now() >= until) return new(GrokBotRelaunchKind.ExitTooLong);
            if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken) ?? new(GrokBotRelaunchKind.Cancelled);
        }
        return null;
    }

    /// <summary>
    /// Open Grok Bot after Reconnect when there is no proof of the quit yet: the offer's process was gone or replaced at
    /// the press, or hadn't finished closing. The user pressed Open Grok Bot, so starting is their choice. One snapshot:
    /// if it still holds that exact process (id and start time), waits up to <c>Exit</c> for it to exit; a process the
    /// snapshot doesn't hold has exited. Then <see cref="StartWhenClearAsync"/>, as after the quit: an update under way comes
    /// back as <see cref="GrokBotRelaunchKind.UpdateRunning"/> with the proof, and the caller waits for it.
    /// </summary>
    public static async Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken, Action<ClosedProcess>? exited = null)
    {
        var stamp = control.ExecutableStamp();
        var snapshot = control.Snapshot();
        try
        {
            if (snapshot.FirstOrDefault(process => process.Identity == target) is { } handle
                && await ExitAsync(handle, control, limits, now, delay, cancellationToken) is { } notClosed) return notClosed;
        }
        finally { foreach (var process in snapshot) process.Dispose(); }
        return await StartWhenClearAsync(Exited(target, exited), stamp, control, limits, now, delay, cancellationToken);
    }

    /// <summary>
    /// Open window or Repair closed Grok Bot after the user's Yes, and that close started its update: the same wait as for
    /// Reconnect (<see cref="AfterUpdateAsync"/>), so Grok Bot is reopened after the update. <paramref name="closed"/> is
    /// the main process that was asked to close (null: only leftover processes ran); nothing starts while the snapshot
    /// still holds it.
    /// </summary>
    public static async Task<GrokBotRelaunchResult> AfterApprovedCloseAsync(GrokBotProcess? closed, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken, Action<ClosedProcess>? exited = null)
    {
        var target = closed ?? new GrokBotProcess(0, null);
        var snapshot = control.Snapshot();
        try
        {
            if (snapshot.FirstOrDefault(process => process.Identity == target) is { } handle
                && await ExitAsync(handle, control, limits, now, delay, cancellationToken) is { } notClosed) return notClosed;
        }
        finally { foreach (var process in snapshot) process.Dispose(); }
        return await AfterUpdateAsync(Exited(target, exited), control, limits, now, delay, cancellationToken);
    }

    /// <summary>
    /// Open window or Repair after the user's Yes and the close request: waits up to <paramref name="closeLimit"/> for
    /// every Grok Bot process to exit. If that close starts Grok Bot's update, at any point in the wait, it calls
    /// <paramref name="updating"/> and goes on to <see cref="AfterApprovedCloseAsync"/> instead of timing out. Closed with
    /// no update under way: the quit's proof, then <see cref="StartWhenClearAsync"/> with its guards. Not closed in time:
    /// <see cref="GrokBotRelaunchKind.NotClosed"/>, and Grok Bot is left running. Every result is the run's to show.
    /// </summary>
    public static async Task<GrokBotRelaunchResult> AfterCloseRequestAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since,
        IGrokBotProcessControl control, TimeSpan closeLimit, TimeSpan poll, Action? updating, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken, Action<ClosedProcess>? exited = null)
    {
        var until = now() + closeLimit;
        while (true)
        {
            var running = control.AnyRunning();
            if (await UpdateUnderWayAsync(control, since))
            {
                updating?.Invoke();
                return await AfterApprovedCloseAsync(closed, control, limits, now, delay, cancellationToken, exited);
            }
            if (!running) return await StartWhenClearAsync(Exited(closed ?? new GrokBotProcess(0, null), exited), since, control, limits, now, delay, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return new(GrokBotRelaunchKind.Cancelled);
            if (now() > until) return new(GrokBotRelaunchKind.NotClosed);
            if (!await Wait(delay, poll, cancellationToken)) return new(GrokBotRelaunchKind.Cancelled);
        }
    }

    /// <summary>
    /// The user's quit started a Grok Bot update. Waits up to <c>Update</c> for it to finish: no Grok Bot installer runs and
    /// Grok Bot.exe is present and unchanged for <c>Stable</c>. Then opens Grok Bot with the port through
    /// <see cref="StartWhenClearAsync"/>, with the same guards; an installer that starts again meanwhile goes back to waiting.
    /// A Grok Bot opened meanwhile (by the user, or the installer) is <see cref="GrokBotRelaunchKind.Reopened"/>: nothing is
    /// started. Nothing is ever started while an installer runs, and nothing is closed.
    /// </summary>
    public static async Task<GrokBotRelaunchResult> AfterUpdateAsync(ClosedProcess quit, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        var until = now() + limits.Update;
        GrokBotExecutableStamp? last = null;
        var stableSince = now();
        while (true)
        {
            if (Stopped(control, cancellationToken, quit) is { } stopped) return stopped;
            if (Reopened(control, quit)) return new(GrokBotRelaunchKind.Reopened, quit);
            var installer = await control.InstallerRunningAsync();
            var stamp = control.ExecutableStamp();
            if (installer || stamp is null || stamp != last)
            {
                last = installer ? null : stamp;
                stableSince = now();
            }
            else if (now() - stableSince >= limits.Stable)
            {
                var started = await StartWhenClearAsync(quit, stamp, control, limits, now, delay, cancellationToken);
                if (started.Kind != GrokBotRelaunchKind.UpdateRunning) return started;
                last = null;
                stableSince = now();
            }
            if (now() >= until) return new(GrokBotRelaunchKind.UpdateTooLong, quit);
            if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken, quit) ?? new(GrokBotRelaunchKind.Cancelled, quit);
        }
    }

    /// <summary>
    /// Windows shutting down or signing out comes first, so a wait cancelled for it reports <see cref="GrokBotRelaunchKind.SessionEnding"/>
    /// and the prompt survives a shutdown that another app cancels; otherwise a cancelled wait is <see cref="GrokBotRelaunchKind.Cancelled"/>.
    /// </summary>
    private static GrokBotRelaunchResult? Stopped(IGrokBotProcessControl control, CancellationToken cancellationToken, ClosedProcess? quit = null) =>
        control.SessionEnding() ? new(GrokBotRelaunchKind.SessionEnding, quit)
        : cancellationToken.IsCancellationRequested ? new(GrokBotRelaunchKind.Cancelled, quit)
        : null;

    private static bool Reopened(IGrokBotProcessControl control, ClosedProcess quit) =>
        control.MainProcess() is { } main && main != quit.Process;

    private static async Task<bool> UpdateUnderWayAsync(IGrokBotProcessControl control, GrokBotExecutableStamp? since) =>
        UpdateUnderWay(await control.InstallerRunningAsync(), control.ExecutableStamp(), since);

    /// <summary>
    /// The installer guard before any start of Grok Bot: an installer or uninstaller runs, Grok Bot.exe is missing, or it
    /// changed since <paramref name="since"/>.
    /// </summary>
    public static bool UpdateUnderWay(bool installerRunning, GrokBotExecutableStamp? current, GrokBotExecutableStamp? since) =>
        installerRunning || current is null || since is not null && current != since;

    public const string UpdateRunningText = "A Grok Bot update or uninstall is running, so Wingman didn't start Grok Bot. Try again when it has finished.";

    private static async Task<bool> Wait(Func<TimeSpan, CancellationToken, Task> delay, TimeSpan poll, CancellationToken cancellationToken)
    {
        try { await delay(poll, cancellationToken); return true; }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>
/// Grok Bot's installers, recognised by their program file. On Windows Grok Bot stages its update as
/// <c>%TEMP%\sand-update-…\sand-update-&lt;version&gt;-….exe</c> and runs it when Grok Bot quits (<c>--updated /S</c>).
/// Wingman asks only about programs named <c>sand-update-…</c>, <c>Uninstall…</c>, <c>Un_A</c> or <c>Au_</c>
/// (<see cref="MayBeInstaller"/>); no other program is checked, also not in the install folder. Of those: a
/// <c>sand-update-…</c> program counts wherever it runs; one running from Grok Bot's install folder counts (such as
/// <c>Uninstall Grok Bot.exe</c>). An NSIS uninstaller runs from a <c>%TEMP%\~nsu….tmp</c> copy (<c>Un_A.exe</c> or
/// <c>Au_.exe</c>), and every NSIS app's does, so such a copy counts only when it is Grok Bot's: its <c>_?=</c> argument
/// names Grok Bot's install folder, or its parent runs from there. Anywhere else nothing counts.
/// </summary>
public static class GrokBotInstallers
{
    /// <summary>The only process names asked about; any other process is skipped without reading anything about it.</summary>
    public static bool MayBeInstaller(string name) =>
        name.StartsWith("sand-update-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Un_A", StringComparison.OrdinalIgnoreCase) || name.Equals("Au_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when one of <paramref name="processes"/> (name and id) is a Grok Bot installer (<see cref="IsInstaller"/>).
    /// Only names <see cref="MayBeInstaller"/> allows are looked at, and a command line or parent only for those.
    /// </summary>
    public static bool AnyRunning(IEnumerable<(string Name, int Id)> processes, string grokExecutable, Func<int, string?> imagePath,
        Func<int, string?> commandLine, Func<int, int?> parentId)
    {
        foreach (var (name, id) in processes)
            if (MayBeInstaller(name) && IsInstaller(imagePath(id), grokExecutable, () => commandLine(id),
                    () => parentId(id) is { } parent ? imagePath(parent) : null)) return true;
        return false;
    }

    /// <param name="commandLine">Read only for an NSIS uninstaller copy; null when Windows won't say.</param>
    /// <param name="parentImagePath">The parent process's program path, read only for an NSIS uninstaller copy.</param>
    public static bool IsInstaller(string? imagePath, string grokExecutable, Func<string?>? commandLine = null, Func<string?>? parentImagePath = null)
    {
        if (string.IsNullOrEmpty(imagePath) || !imagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var (directory, name) = Split(imagePath);
        if (name.StartsWith("sand-update-", StringComparison.OrdinalIgnoreCase)) return true;
        var (installFolder, grokName) = Split(grokExecutable);
        if (installFolder.Length == 0) return false;
        var folder = Split(directory).Name;
        if (folder.StartsWith("~nsu", StringComparison.OrdinalIgnoreCase) && folder.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return SameFolder(UninstallTarget(commandLine?.Invoke()), installFolder)
                || parentImagePath?.Invoke() is { Length: > 0 } parent && SameFolder(Split(parent).Directory, installFolder);
        return SameFolder(directory, installFolder) && !string.Equals(name, grokName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The folder an NSIS uninstaller copy works on: its last argument <c>_?=&lt;folder&gt;</c>, which NSIS reads to the end of
    /// the command line unquoted (so it may contain spaces). Null without one.
    /// </summary>
    public static string? UninstallTarget(string? commandLine)
    {
        if (commandLine is null) return null;
        var at = commandLine.LastIndexOf("_?=", StringComparison.Ordinal);
        if (at < 0 || at > 0 && !char.IsWhiteSpace(commandLine[at - 1])) return null;
        var folder = commandLine[(at + 3)..].Trim().Trim('"').TrimEnd('\\', '/');
        return folder.Length == 0 ? null : folder;
    }

    private static bool SameFolder(string? folder, string installFolder) =>
        folder is not null && string.Equals(folder.Replace('/', '\\').TrimEnd('\\'), installFolder.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static (string Directory, string Name) Split(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? (string.Empty, trimmed) : (trimmed[..cut].TrimEnd('\\', '/'), trimmed[(cut + 1)..]);
    }
}

/// <summary>
/// Where the prompt window goes: inside the screen's working area, in the corner by the notification area, wherever the
/// taskbar is. Top taskbar: top right. Left: bottom left. Right, bottom or hidden: bottom right.
/// </summary>
public static class GrokBotPromptPlacement
{
    public static System.Drawing.Point Corner(System.Drawing.Rectangle bounds, System.Drawing.Rectangle working, System.Drawing.Size size, int gap)
    {
        var left = working.Left > bounds.Left;
        var top = working.Top > bounds.Top;
        var x = left ? working.Left + gap : working.Right - size.Width - gap;
        var y = top ? working.Top + gap : working.Bottom - size.Height - gap;
        return new(Math.Max(working.Left, x), Math.Max(working.Top, y));
    }

    /// <summary>A size given at 96 DPI, scaled for a display at <paramref name="dpi"/> (96 when unknown).</summary>
    public static int Scaled(int logical, int dpi) => (int)Math.Round(logical * (dpi > 0 ? dpi : 96) / 96.0, MidpointRounding.AwayFromZero);
}
