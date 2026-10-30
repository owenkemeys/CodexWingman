namespace CodexWingman.Core;

/// <summary>What a prompt's run needs from the tray. Everything else (the order of checks, the relaunch, the logs) is here in Core.</summary>
public interface IGrokBotPromptHost
{
    GrokBotObservation Observe();
    /// <summary>True when the process already has a usable port (127.0.0.1, listening, owned by it) and it was recorded.</summary>
    bool AdoptListeningPort(int process);
    bool Busy { get; }
    bool Installed { get; }
    bool SessionEnding { get; }
    /// <summary>Grok Bot's processes, as the relaunch sees and starts them for this run.</summary>
    IGrokBotProcessControl Control();
    /// <summary>The prompt's state changed: show it.</summary>
    void Changed();
    /// <summary>Connects the Helpers to Grok Bot running with Wingman's port. True once they are connected; false (or a throw) otherwise.</summary>
    Task<bool> ConnectAsync(bool started, CancellationToken cancellationToken);
}

/// <summary>
/// The prompt's steps over the real relaunch: each wait goes straight to <see cref="GrokBotRelaunch"/>, which hands the
/// prompt the quit's proof (<see cref="GrokBotReconnectPrompt.Exited"/>) the moment the offer's process exits.
/// </summary>
public sealed class GrokBotPromptSteps(GrokBotReconnectPrompt prompt, IGrokBotPromptHost host, GrokBotRelaunchLimits limits,
    Func<DateTime>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null) : IGrokBotReconnectSteps
{
    private readonly Func<DateTime> now = now ?? (() => DateTime.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? ((wait, token) => Task.Delay(wait, token));

    public GrokBotObservation Observe() => host.Observe();
    public bool AdoptListeningPort(int process) => host.AdoptListeningPort(process);
    public bool Busy => host.Busy;
    public bool SessionEnding => host.SessionEnding;
    public void Changed() => host.Changed();

    public void WaitingForQuit()
    {
        prompt.WaitingForQuit();
        host.Changed();
    }

    public async Task<GrokBotRelaunchResult> RelaunchAfterQuitAsync(GrokBotProcess target, CancellationToken cancellationToken)
    {
        RequireInstalled();
        var result = await GrokBotRelaunch.RunAsync(target, host.Control(), limits, now, delay, cancellationToken, Exited);
        WingmanLog.Write("grok-bot-reconnect-relaunch", ("process", target.Id), ("result", result.Kind));
        return result;
    }

    /// <summary>The Open Grok Bot button after Wingman couldn't reopen it: the same bounded, installer-safe start.</summary>
    public async Task<GrokBotRelaunchResult> StartAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
    {
        RequireInstalled();
        var control = host.Control();
        var result = await GrokBotRelaunch.StartWhenClearAsync(quit, control.ExecutableStamp(), control, limits, now, delay, cancellationToken);
        WingmanLog.Write("grok-bot-reconnect-open", ("process", quit.Process.Id), ("result", result.Kind));
        return result;
    }

    public async Task<GrokBotRelaunchResult> OpenAfterPressAsync(GrokBotProcess target, CancellationToken cancellationToken)
    {
        RequireInstalled();
        var result = await GrokBotRelaunch.OpenAfterPressAsync(target, host.Control(), limits, now, delay, cancellationToken, Exited);
        WingmanLog.Write("grok-bot-reconnect-open-after-press", ("process", target.Id), ("result", result.Kind));
        return result;
    }

    public void Updating(GrokBotRelaunch.ClosedProcess quit)
    {
        WingmanLog.Write("grok-bot-reconnect-updating", ("process", quit.Process.Id));
        prompt.Updating(quit);
        host.Changed();
    }

    /// <summary>
    /// Grok Bot.exe may be missing while the installer replaces it, so this doesn't require it to be installed up front;
    /// the start's own guards do.
    /// </summary>
    public async Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, CancellationToken cancellationToken)
    {
        var result = await GrokBotRelaunch.AfterUpdateAsync(quit, host.Control(), limits, now, delay, cancellationToken);
        WingmanLog.Write("grok-bot-reconnect-after-update", ("process", quit.Process.Id), ("result", result.Kind));
        return result;
    }

    public Task<bool> ConnectAsync(bool started, CancellationToken cancellationToken) => host.ConnectAsync(started, cancellationToken);

    /// <summary>
    /// Open window or Repair after the Yes and the close request: up to <c>Close</c> for Grok Bot to close; an update that
    /// close starts shows in the prompt, and Grok Bot is reopened after it.
    /// </summary>
    public async Task<GrokBotRelaunchResult> AfterApprovedCloseAsync(GrokBotProcess? closed, GrokBotExecutableStamp? since, CancellationToken cancellationToken)
    {
        var result = await GrokBotRelaunch.AfterCloseRequestAsync(closed, since, host.Control(), limits.Close, limits.Poll, () =>
        {
            WingmanLog.Write("grok-bot-open-update-after-close");
            prompt.Updating(null);
            host.Changed();
        }, limits, now, delay, cancellationToken, Exited);
        WingmanLog.Write("grok-bot-open-close-wait", ("result", result.Kind));
        return result;
    }

    private void Exited(GrokBotRelaunch.ClosedProcess quit)
    {
        prompt.Exited(quit);
        host.Changed();
    }

    private void RequireInstalled()
    {
        if (!host.Installed) throw new FileNotFoundException("Grok Bot is not installed in its usual folder.");
    }
}

/// <summary>
/// One press on the prompt, from the step to where the prompt ends up. Every way a run can end goes through
/// <see cref="GrokBotReconnectPrompt.Finished"/>: an error is <see cref="GrokBotReconnectOutcome.Failed"/>, a cancel is
/// <see cref="GrokBotReconnectOutcome.Cancelled"/> (or SessionEnding), and Grok Bot running with the port is followed by
/// connecting its Helpers, which ends in Connected or NotConnected. Nothing here shows a message box.
/// </summary>
public static class GrokBotPromptRun
{
    /// <param name="ended">Called once the relaunch has ended, before the prompt is told, also after an error.</param>
    public static async Task<GrokBotReconnectResult> ActAsync(GrokBotReconnectPrompt prompt, GrokBotPromptStep step, IGrokBotReconnectSteps steps,
        CancellationToken cancellationToken, Action? ended = null)
    {
        GrokBotReconnectResult result;
        try
        {
            result = step switch
            {
                GrokBotPromptStep.Open => await GrokBotReconnectRun.OpenAsync(prompt.Offer.Target, prompt.Quit, steps, cancellationToken, prompt.Offer),
                GrokBotPromptStep.Connect => new(GrokBotReconnectOutcome.AlreadyConnected, prompt.Quit),
                GrokBotPromptStep.ApprovedClose => await GrokBotReconnectRun.ApprovedCloseAsync(prompt.Offer.ProcessId == 0 ? null : prompt.Offer.Target,
                    prompt.ApprovedSince, steps, cancellationToken),
                _ => await GrokBotReconnectRun.RunAsync(prompt.Offer, steps, cancellationToken),
            };
        }
        catch (OperationCanceledException)
        {
            result = new(GrokBotReconnectOutcome.Cancelled, prompt.Quit);
        }
        catch (Exception error)
        {
            // An error is its own outcome: the prompt stays with a plain reason and its buttons, and the log says so.
            WingmanLog.Write("grok-bot-reconnect-failed", ("process", prompt.Offer.ProcessId), ("step", step), ("error", error.GetType().Name));
            result = new(GrokBotReconnectOutcome.Failed, prompt.Quit);
        }
        finally { ended?.Invoke(); }
        if (result.Outcome == GrokBotReconnectOutcome.Cancelled && steps.SessionEnding) result = result with { Outcome = GrokBotReconnectOutcome.SessionEnding };
        WingmanLog.Write("grok-bot-reconnect-outcome", ("process", prompt.Offer.ProcessId), ("step", step), ("outcome", result.Outcome));
        prompt.Finished(result, ObserveSafely(steps));
        steps.Changed();
        // Grok Bot runs with the port: connect its Helpers, also when the user closed the prompt meanwhile.
        if (result.Outcome is not (GrokBotReconnectOutcome.Relaunched or GrokBotReconnectOutcome.Adopted or GrokBotReconnectOutcome.AlreadyConnected)) return result;
        bool connected;
        try { connected = await steps.ConnectAsync(result.Outcome == GrokBotReconnectOutcome.Relaunched, CancellationToken.None); }
        catch (Exception error)
        {
            WingmanLog.Write("grok-bot-reconnect-connect-failed", ("process", prompt.Offer.ProcessId), ("error", error.GetType().Name));
            connected = false;
        }
        result = result with { Outcome = connected ? GrokBotReconnectOutcome.Connected : GrokBotReconnectOutcome.NotConnected };
        WingmanLog.Write("grok-bot-reconnect-outcome", ("process", prompt.Offer.ProcessId), ("step", step), ("outcome", result.Outcome));
        prompt.Finished(result, ObserveSafely(steps));
        steps.Changed();
        return result;
    }

    /// <summary>The observation for the prompt's last word; if even that throws, no Grok Bot (so never [Reconnect] on a guess).</summary>
    private static GrokBotObservation ObserveSafely(IGrokBotReconnectSteps steps)
    {
        try { return steps.Observe(); }
        catch (Exception) { return new(null, false, TimeSpan.Zero); }
    }
}
