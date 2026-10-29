namespace CodexWingman.Core;

/// <summary>
/// One quick look at Grok Bot, taken every <see cref="GrokBotReconnectPolicy.CheckInterval"/>: the process that owns its
/// main window (null when it has none), whether Wingman's debugging port is listening, and how long the user has been away.
/// </summary>
public sealed record GrokBotObservation(int? WindowProcessId, bool Connected, TimeSpan UserIdle);

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
public sealed record GrokBotReconnectOffer(int ProcessId, bool Updated, DateTimeOffset ShownAt)
{
    public string Title => Updated ? GrokBotReconnectPolicy.UpdatedTitle : GrokBotReconnectPolicy.ReopenedTitle;
    public string Text => GrokBotReconnectPolicy.PromptText;
}

public enum GrokBotReconnectAction
{
    /// <summary>The click is consent: reconnect now with no second question.</summary>
    ReconnectNow,
    /// <summary>The click can't be tied to the prompt's Grok Bot: ask with the usual Yes/No relaunch question.</summary>
    AskFirst,
    /// <summary>Grok Bot is already connected again: nothing to do.</summary>
    AlreadyConnected,
    /// <summary>Wingman is busy: the click is kept and acted on once Wingman is free (<see cref="GrokBotReconnectClicks.Drain"/>).</summary>
    WaitUntilFree,
}

/// <summary>
/// Grok Bot can update itself while the PC is idle, and its installer reopens it without Wingman's debugging port, so the
/// hook is lost. Wingman never closes or reopens Grok Bot on its own; it offers one prompt, once per Grok Bot process:
/// only after Wingman had Grok Bot connected earlier in this session, only once the unconnected window has stayed so for
/// <see cref="Settle"/> (a Grok Bot that relaunches itself with the port is listening by then), and only while the user is
/// at the PC (a prompt shown to an empty room is lost). The tray checks every <see cref="CheckInterval"/>, separately from
/// the helper refresh, so the prompt appears a few seconds after the port goes.
/// </summary>
public sealed class GrokBotReconnectPolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromSeconds(60);
    /// <summary>A click counts only this long after the prompt appeared (and only before Windows reports it closed).</summary>
    public static readonly TimeSpan PromptLifetime = TimeSpan.FromSeconds(30);

    public const string UpdatedTitle = "Grok Bot updated";
    public const string ReopenedTitle = "Grok Bot reopened without Wingman";
    /// <summary>The prompt's own words. Its click is the consent, so no second question follows it.</summary>
    public const string PromptText = "Grok Bot is running without Wingman's connection. Click to reconnect: Wingman closes Grok Bot and opens it again. Grok Bot keeps its sign-in and data.";

    private bool wasConnected;
    private int? connectedProcess;
    private string? connectedVersion;
    private int? unconnectedProcess;
    private DateTimeOffset unconnectedSince;
    private int? promptedProcess;

    public GrokBotReconnectOffer? Observe(GrokBotObservation observation, DateTimeOffset now, IGrokBotFacts facts)
    {
        if (observation.Connected)
        {
            wasConnected = true;
            // Read the version once per connected run; it is the baseline that proves a later update.
            if (connectedProcess != observation.WindowProcessId || connectedVersion is null)
            {
                connectedProcess = observation.WindowProcessId;
                connectedVersion = facts.ExecutableVersion() ?? connectedVersion;
            }
            unconnectedProcess = null;
            promptedProcess = null;
            return null;
        }
        if (observation.WindowProcessId is not { } process)
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
        if (now - unconnectedSince < Settle || observation.UserIdle > PresentWithin) return null;
        promptedProcess = process;
        var version = facts.ExecutableVersion();
        var updated = HasUpdatedFlag(facts.CommandLine(process))
            || connectedVersion is not null && version is not null && !string.Equals(connectedVersion, version, StringComparison.Ordinal);
        return new GrokBotReconnectOffer(process, updated, now);
    }

    /// <summary>
    /// Whether a click belongs to the prompt: it lands within the prompt's lifetime, Windows hasn't reported the prompt
    /// closed or replaced, and the same unconnected Grok Bot process is still the one running.
    /// </summary>
    public static GrokBotReconnectAction Validate(GrokBotReconnectOffer? offer, bool offerClosed, DateTimeOffset clickedAt,
        GrokBotObservation current)
    {
        if (current.Connected) return GrokBotReconnectAction.AlreadyConnected;
        if (offer is null || offerClosed) return GrokBotReconnectAction.AskFirst;
        var age = clickedAt - offer.ShownAt;
        if (age < TimeSpan.Zero || age > PromptLifetime) return GrokBotReconnectAction.AskFirst;
        return current.WindowProcessId == offer.ProcessId ? GrokBotReconnectAction.ReconnectNow : GrokBotReconnectAction.AskFirst;
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

/// <summary>
/// Turns prompt clicks into actions without ever dropping one. A click that arrives while Wingman is busy is kept, and
/// <see cref="Drain"/> acts on it once Wingman is free, after checking again that the same unconnected Grok Bot is running.
/// </summary>
public sealed class GrokBotReconnectClicks
{
    private int? consentedProcess;
    private bool askPending;

    public bool Pending => consentedProcess is not null || askPending;

    public GrokBotReconnectAction Click(GrokBotReconnectOffer? offer, bool offerClosed, DateTimeOffset clickedAt,
        GrokBotObservation current, bool busy)
    {
        var action = GrokBotReconnectPolicy.Validate(offer, offerClosed, clickedAt, current);
        if (!busy || action == GrokBotReconnectAction.AlreadyConnected) return action;
        // A later click replaces an earlier kept one; the newest is what the user meant.
        consentedProcess = action == GrokBotReconnectAction.ReconnectNow ? offer!.ProcessId : null;
        askPending = action == GrokBotReconnectAction.AskFirst;
        return GrokBotReconnectAction.WaitUntilFree;
    }

    /// <summary>The kept click's action once Wingman is free (null when nothing is kept). Consent holds only for the same process.</summary>
    public GrokBotReconnectAction? Drain(GrokBotObservation current)
    {
        if (!Pending) return null;
        var process = consentedProcess;
        consentedProcess = null;
        askPending = false;
        if (current.Connected) return GrokBotReconnectAction.AlreadyConnected;
        return process is not null && current.WindowProcessId == process
            ? GrokBotReconnectAction.ReconnectNow
            : GrokBotReconnectAction.AskFirst;
    }
}
