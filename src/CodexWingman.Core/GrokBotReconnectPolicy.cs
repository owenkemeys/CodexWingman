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

    public string Title => Updated ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle;
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
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromSeconds(60);

    public const string UpdatedTitle = "Grok Bot updated";
    public const string ReopenedTitle = "Grok Bot reopened without Wingman";
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
    public const string UpdatingText = "Grok Bot is updating… Wingman reopens it when the update has finished.";
    public const string UpdateTooLongText = "Grok Bot's update was still running after 5 minutes, so Wingman didn't reopen it.";
    public const string ReopenedText = "Grok Bot was opened again without Wingman.";
    public const string CouldNotOpenText = "Wingman couldn't reopen Grok Bot.";

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
    /// Grok Bot was opened again (by the user, or by its installer) while a reconnect the user had agreed to was waiting
    /// to reopen it: the prompt moves to that new process at once, without the settle, presence and earlier-connected
    /// checks, and the policy won't offer it a second prompt. Null when no unconnected Grok Bot is running.
    /// </summary>
    public GrokBotReconnectOffer? HandOver(GrokBotObservation observation, DateTimeOffset now, IGrokBotFacts facts)
    {
        if (observation.Connected || observation.Process is not { } process) return null;
        unconnectedProcess = process;
        unconnectedSince = now;
        promptedProcess = process;
        var version = facts.ExecutableVersion();
        var updated = HasUpdatedFlag(facts.CommandLine(process.Id))
            || connectedVersion is not null && version is not null && !string.Equals(connectedVersion, version, StringComparison.Ordinal);
        return new GrokBotReconnectOffer(process.Id, updated, now, process.Started);
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
    /// <summary>A button was pressed: a disabled "Working…" button; acts once Wingman is free.</summary>
    Working,
    /// <summary>Waiting for the user to quit that Grok Bot process; [Cancel].</summary>
    WaitingForQuit,
    /// <summary>The user quit it and Grok Bot's update is running: "Grok Bot is updating…"; [Cancel]. Wingman reopens it after.</summary>
    Updating,
    /// <summary>It quit, but Wingman couldn't open it again: says why; [Open Grok Bot] [Not now].</summary>
    CouldNotStart,
    Closed,
}

/// <summary>What the tray should do now for the prompt.</summary>
public enum GrokBotPromptStep { None, Reconnect, Open }

/// <summary>
/// The reconnect prompt window's state, bound to one offer: its exact Grok Bot process (id and start time). It replaces
/// tray balloons, whose clicks Windows can't tie to one balloon. Until Reconnect it has no time limit, and closes when
/// that process exits or is replaced, when Grok Bot connects, or after Not now. Once the user has quit Grok Bot for it,
/// it never closes silently: it closes once Grok Bot is reconnected (or the user cancels), moves to a Grok Bot opened again
/// meanwhile, shows "Grok Bot is updating…" while an update runs, or stays and says why Wingman couldn't open it, with an
/// Open Grok Bot button. A button pressed while Wingman is busy shows "Working…" and
/// acts once Wingman is free.
/// </summary>
public sealed class GrokBotReconnectPrompt(GrokBotReconnectOffer offer)
{
    private GrokBotPromptStep pending;

    public GrokBotReconnectOffer Offer { get; } = offer;
    public GrokBotPromptState State { get; private set; } = GrokBotPromptState.Offered;
    public string? ClosedBecause { get; private set; }
    /// <summary>Set once that exact process has quit: what Open Grok Bot needs.</summary>
    public GrokBotRelaunch.ClosedProcess? Quit { get; private set; }
    /// <summary>Why Wingman couldn't open Grok Bot again (shown while <see cref="GrokBotPromptState.CouldNotStart"/>).</summary>
    public string? Problem { get; private set; }

    /// <summary>The Reconnect button. True: act now. Only from the offered state, so a second press does nothing.</summary>
    public bool Reconnect(bool busy) => Press(GrokBotPromptState.Offered, GrokBotPromptStep.Reconnect, busy);

    /// <summary>The Open Grok Bot button, offered only after Wingman couldn't open it. True: act now.</summary>
    public bool Open(bool busy) => Quit is not null && Press(GrokBotPromptState.CouldNotStart, GrokBotPromptStep.Open, busy);

    private bool Press(GrokBotPromptState from, GrokBotPromptStep step, bool busy)
    {
        if (State != from) return false;
        State = GrokBotPromptState.Working;
        pending = busy ? step : GrokBotPromptStep.None;
        return !busy;
    }

    /// <summary>Not now, or Cancel while waiting for the quit.</summary>
    public void NotNow() => Close("not-now");

    /// <summary>Every check. Returns a step kept while Wingman was busy, once it is free.</summary>
    public GrokBotPromptStep Observe(GrokBotObservation current, bool busy)
    {
        if (State == GrokBotPromptState.Closed) return GrokBotPromptStep.None;
        if (current.Connected) { Close("connected"); return GrokBotPromptStep.None; }
        if (Quit is null)
        {
            // Before the quit the prompt belongs to its process; while waiting for the quit its exit is expected.
            if (State != GrokBotPromptState.WaitingForQuit && !Offer.Owns(current)) { Close("gone"); return GrokBotPromptStep.None; }
        }
        else if (State != GrokBotPromptState.Updating && current.Process is { } other && other != Offer.Target)
        {
            // After the quit: another Grok Bot started (by the user, or an installer); it gets its own prompt. While
            // updating, the run itself sees it and moves the prompt to it.
            Close("replaced");
            return GrokBotPromptStep.None;
        }
        if (State != GrokBotPromptState.Working || pending == GrokBotPromptStep.None || busy) return GrokBotPromptStep.None;
        var step = pending;
        pending = GrokBotPromptStep.None;
        return step;
    }

    /// <summary>The run reached the point where the user quits Grok Bot.</summary>
    public void WaitingForQuit()
    {
        if (State == GrokBotPromptState.Working) State = GrokBotPromptState.WaitingForQuit;
    }

    /// <summary>The user quit Grok Bot and its update is running: the prompt says so and keeps [Cancel].</summary>
    public void Updating(GrokBotRelaunch.ClosedProcess quit)
    {
        if (State == GrokBotPromptState.Closed) return;
        Quit = quit;
        Problem = null;
        State = GrokBotPromptState.Updating;
    }

    /// <summary>
    /// The run's result. Busy keeps the press pending; no quit within the limit goes back to the offer, silently. Before
    /// the quit anything else closes it. After the quit only a reconnect (or the user's Cancel) closes it; every other
    /// outcome keeps it with the reason and Open Grok Bot (a Grok Bot opened meanwhile gets the prompt moved to it).
    /// </summary>
    public void Finished(GrokBotReconnectResult result)
    {
        if (State == GrokBotPromptState.Closed) return;
        if (result.Quit is not null) Quit = result.Quit;
        switch (result.Outcome)
        {
            case GrokBotReconnectOutcome.Busy:
                pending = Quit is null ? GrokBotPromptStep.Reconnect : GrokBotPromptStep.Open;
                State = GrokBotPromptState.Working;
                return;
            case GrokBotReconnectOutcome.QuitTimedOut when Quit is null:
                State = GrokBotPromptState.Offered;
                return;
            // Windows began shutting down or signing out. If that goes ahead Wingman exits with it; if another app cancels
            // it, the prompt is still here: Reconnect again before the quit, Open Grok Bot after it.
            case GrokBotReconnectOutcome.SessionEnding when Quit is null:
                State = GrokBotPromptState.Offered;
                return;
            case GrokBotReconnectOutcome.SessionEnding:
                Problem = GrokBotReconnectPolicy.SessionEndingText;
                State = GrokBotPromptState.CouldNotStart;
                return;
            case GrokBotReconnectOutcome.Relaunched or GrokBotReconnectOutcome.Adopted or GrokBotReconnectOutcome.AlreadyConnected or GrokBotReconnectOutcome.Cancelled:
            case var _ when Quit is null:
                Close(result.Outcome.ToString());
                return;
            default:
                Problem = ProblemAfterQuit(result.Outcome);
                State = GrokBotPromptState.CouldNotStart;
                return;
        }
    }

    /// <summary>What the prompt says when Wingman couldn't reopen Grok Bot after the user's quit.</summary>
    public static string ProblemAfterQuit(GrokBotReconnectOutcome outcome) => outcome switch
    {
        GrokBotReconnectOutcome.StillRunning => GrokBotReconnectPolicy.StillRunningText,
        GrokBotReconnectOutcome.Crashed => GrokBotReconnectPolicy.CrashedText,
        GrokBotReconnectOutcome.BusyTooLong => GrokBotReconnectPolicy.BusyTooLongText,
        GrokBotReconnectOutcome.SessionEnding => GrokBotReconnectPolicy.SessionEndingText,
        GrokBotReconnectOutcome.UpdateTooLong => GrokBotReconnectPolicy.UpdateTooLongText,
        GrokBotReconnectOutcome.UpdateRunning => GrokBotRelaunch.UpdateRunningText,
        GrokBotReconnectOutcome.Reopened or GrokBotReconnectOutcome.UpdatedAndReopened => GrokBotReconnectPolicy.ReopenedText,
        _ => GrokBotReconnectPolicy.CouldNotOpenText,
    };

    private void Close(string reason)
    {
        if (State == GrokBotPromptState.Closed) return;
        State = GrokBotPromptState.Closed;
        ClosedBecause = reason;
    }
}

/// <summary>What a press on the prompt led to. Only <see cref="Relaunched"/> means Grok Bot was opened again.</summary>
public enum GrokBotReconnectOutcome
{
    /// <summary>The offer's Grok Bot process is gone or was replaced before the quit: nothing started.</summary>
    Gone,
    AlreadyConnected,
    /// <summary>Grok Bot already had a usable port: adopted, nothing restarted.</summary>
    Adopted,
    /// <summary>Wingman was busy: the prompt shows "Working…" and acts once free.</summary>
    Busy,
    /// <summary>Not now or Cancel: nothing started.</summary>
    Cancelled,
    /// <summary>Not quit within the limit: back to the offer.</summary>
    QuitTimedOut,
    /// <summary>
    /// A Grok Bot installer or update is running. Before the quit: nothing started, and the post-update prompt handles what
    /// it opens. After the quit the run waits for it instead (<see cref="IGrokBotReconnectSteps.AfterUpdateAsync"/>).
    /// </summary>
    UpdateRunning,
    /// <summary>After the quit, Grok Bot's update was still running at the limit: nothing started; the prompt says so.</summary>
    UpdateTooLong,
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
    Relaunched,
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
    /// <summary>The prompt now says Grok Bot is updating.</summary>
    void Updating(GrokBotRelaunch.ClosedProcess quit);
    /// <summary>Waits (bounded) for the update to finish, then opens Grok Bot with the port (<see cref="GrokBotRelaunch.AfterUpdateAsync"/>).</summary>
    Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken);
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
    public static async Task<GrokBotReconnectResult> RunAsync(GrokBotReconnectOffer offer, IGrokBotReconnectSteps steps, CancellationToken cancellationToken)
    {
        var current = steps.Observe();
        if (current.Connected) return new(GrokBotReconnectOutcome.AlreadyConnected);
        if (!offer.Owns(current)) return new(GrokBotReconnectOutcome.Gone);
        if (steps.AdoptListeningPort(offer.ProcessId)) return new(GrokBotReconnectOutcome.Adopted);
        if (steps.Busy) return new(GrokBotReconnectOutcome.Busy);
        steps.WaitingForQuit();
        return await AfterQuitAsync(await steps.RelaunchAfterQuitAsync(offer.Target, cancellationToken), steps, cancellationToken);
    }

    public static async Task<GrokBotReconnectResult> OpenAsync(GrokBotRelaunch.ClosedProcess quit, IGrokBotReconnectSteps steps, CancellationToken cancellationToken)
    {
        if (steps.Observe().Connected) return new(GrokBotReconnectOutcome.AlreadyConnected, quit);
        if (steps.Busy) return new(GrokBotReconnectOutcome.Busy, quit);
        return await AfterQuitAsync(await steps.StartAsync(quit, cancellationToken), steps, cancellationToken);
    }

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

    private static GrokBotReconnectResult Result(GrokBotRelaunchResult result) => new(result.Kind switch
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
        _ => GrokBotReconnectOutcome.BusyTooLong,
    }, result.Quit);
}

public enum GrokBotRelaunchKind { Relaunched, Gone, Cancelled, QuitTimedOut, UpdateRunning, StillRunning, BusyTooLong, Reopened, Crashed, SessionEnding, UpdateTooLong }

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

/// <summary>The relaunch's limits.</summary>
public sealed record GrokBotRelaunchLimits(TimeSpan Quit, TimeSpan Settle, TimeSpan Busy, TimeSpan Poll)
{
    /// <summary>An update the user's quit started: up to 5 minutes for it to finish.</summary>
    public TimeSpan Update { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Finished means no Grok Bot installer runs and Grok Bot.exe stays unchanged for this long.</summary>
    public TimeSpan Stable { get; init; } = TimeSpan.FromSeconds(5);

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

    public static async Task<GrokBotRelaunchResult> RunAsync(GrokBotProcess target, IGrokBotProcessControl control, GrokBotRelaunchLimits limits,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        var snapshot = control.Snapshot();
        GrokBotExecutableStamp? stamp;
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
                // The installer starts as Grok Bot exits, so an update seen here may already follow the user's quit.
                if (await UpdateUnderWayAsync(control, stamp))
                    return handle.HasExited ? new(GrokBotRelaunchKind.UpdateRunning, new ClosedProcess(target)) : new(GrokBotRelaunchKind.UpdateRunning);
                if (now() >= quitBy) return new(GrokBotRelaunchKind.QuitTimedOut);
                if (!await Wait(delay, limits.Poll, cancellationToken)) return Stopped(control, cancellationToken) ?? new(GrokBotRelaunchKind.Cancelled);
            }
            // An update first: Grok Bot's installer may end it with a failure code after the user's quit, and that is an
            // update, not a crash. Then only the user's quit (exit code 0) is followed by a start; a crash or a kill is not
            // reopened behind their back.
            if (await UpdateUnderWayAsync(control, stamp)) return new(GrokBotRelaunchKind.UpdateRunning, new ClosedProcess(target));
            if (handle.ExitCode is not 0) return new(GrokBotRelaunchKind.Crashed, new ClosedProcess(target));
        }
        finally { foreach (var process in snapshot) process.Dispose(); }
        return await StartWhenClearAsync(new ClosedProcess(target), stamp, control, limits, now, delay, cancellationToken);
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
/// Any program in the install folder other than Grok Bot.exe counts, such as its uninstaller. An NSIS uninstaller runs
/// from a <c>%TEMP%\~nsu….tmp</c> copy (<c>Un_A.exe</c> or <c>Au_.exe</c>), and every NSIS app's does, so such a copy counts
/// only when it is Grok Bot's: its <c>_?=</c> argument names Grok Bot's install folder, or its parent runs from there.
/// </summary>
public static class GrokBotInstallers
{
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
