using CodexWingman.Core.Helpers;

namespace CodexWingman.Core;

public sealed record T3CodeMenuStatus(string Label, string Details);

public static class T3CodeMenuPolicy
{
    public static T3CodeMenuStatus Evaluate(
        int? visibleWindows,
        bool endpointConfigured,
        HelperHostReport? report,
        T3CodeConnectionReport? connections,
        bool checkFailed,
        bool helpersSuspended = false,
        bool hasEnabledHelpers = true)
    {
        if (visibleWindows is null)
            return new("T3 Code: Status unavailable", "Wingman could not count visible T3 Code windows. Choose Status details... again, or Repair.");
        if (visibleWindows < 0) throw new ArgumentOutOfRangeException(nameof(visibleWindows));
        if (visibleWindows == 0)
            return new("T3 Code: Closed", "No T3 Code windows are open.");
        if (helpersSuspended)
            return new("T3 Code: Paused", Counted(0, visibleWindows.Value) + "Helpers are paused. Turn on Helpers enabled to reconnect the visible T3 Code windows.");
        if (!endpointConfigured)
            return new("T3 Code: Needs repair", Counted(0, visibleWindows.Value) + "Wingman cannot connect to the visible T3 Code windows. Choose Repair after saving work.");
        if (checkFailed || report is null || connections is null)
            return new("T3 Code: Needs repair", Counted(0, visibleWindows.Value) + "Wingman could not check the visible T3 Code windows. Choose Repair after saving work.");

        var hooked = Math.Min(Math.Min(Math.Max(connections.Connected, 0),
            Math.Max(report.TargetsDiscovered, 0)), visibleWindows.Value);
        if (hooked < visibleWindows)
            return new("T3 Code: Needs repair", Counted(hooked, visibleWindows.Value) + "Choose Repair after saving work.");
        if (connections.Failed > 0 || report.Failed > 0)
            return new("T3 Code: Needs repair", Counted(hooked, visibleWindows.Value) + "T3 Code windows are connected, but one or more Helpers failed. Choose Reload helpers, then Repair if the problem remains.");
        if (report.Diagnostics.Count > 0)
            return new("T3 Code: Needs repair",
                Counted(hooked, visibleWindows.Value) + "T3 Code windows are connected, but a Helper needs attention. " + string.Join(" ", report.Diagnostics.Select(item => item.Message)));
        if (!hasEnabledHelpers)
            return new("T3 Code: No Helpers", Counted(0, visibleWindows.Value) + "Wingman can connect to T3 Code, but no T3 Code Helpers are enabled. Add or enable a T3 Code Helper to alter its windows.");
        return new("T3 Code: Running", Counted(hooked, visibleWindows.Value) + "Wingman is connected to each visible T3 Code window, and enabled Helpers report no problems.");
    }

    /// <summary>The window counts, for Status details only (the menu line shows the state alone).</summary>
    private static string Counted(int hooked, int visible) =>
        $"Wingman's connection reaches {hooked} of {visible} visible T3 Code window{(visible == 1 ? "" : "s")}. ";
}

/// <summary>Same menu rules as T3 Code, worded for another Wingman-launched app (T3 web, Grok Bot).</summary>
public static class AppTargetMenuPolicy
{
    public static T3CodeMenuStatus Evaluate(
        string appName,
        int? visibleWindows,
        bool endpointConfigured,
        HelperHostReport? report,
        T3CodeConnectionReport? connections,
        bool checkFailed,
        bool helpersSuspended = false,
        bool hasEnabledHelpers = true)
    {
        var status = T3CodeMenuPolicy.Evaluate(visibleWindows, endpointConfigured, report, connections, checkFailed,
            helpersSuspended, hasEnabledHelpers);
        return new(status.Label.Replace("T3 Code", appName, StringComparison.Ordinal),
            status.Details.Replace("T3 Code", appName, StringComparison.Ordinal));
    }
}

public sealed class AppTargetConnectionProbe(ICodexTargetSource targetSource, ICdpEvaluator evaluator)
{
    public async Task<T3CodeConnectionReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var targets = await targetSource.ListAsync(cancellationToken);
        var connected = 0;
        var failed = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await evaluator.EvaluateAsync(target, "void 0", cancellationToken);
                connected++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }
        return new(targets.Count, connected, failed);
    }
}

/// <summary>
/// Whether an app reached through Wingman's own debugging port still has a window. The menu line must
/// drop to Closed as soon as the app closes, even while a full Helper check is not running (for example
/// while Wingman is busy or a dialog is open), so this is a live check, not the last check's page count.
/// </summary>
public static class AppPresence
{
    /// <summary>Visible windows: none once nothing listens on the app's port; otherwise the last check's pages.</summary>
    public static int EndpointWindows(bool endpointListening, T3CodeConnectionReport? lastConnections) =>
        endpointListening ? Math.Max(lastConnections?.Discovered ?? 0, 0) : 0;

    /// <summary>True when a local process listens on the loopback TCP port (a cheap table read, no connection).</summary>
    public static bool IsListening(int? port)
    {
        if (port is not { } value) return false;
        try
        {
            return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == value
                    && (System.Net.IPAddress.IsLoopback(endpoint.Address) || endpoint.Address.Equals(System.Net.IPAddress.Any)
                        || endpoint.Address.Equals(System.Net.IPAddress.IPv6Any)));
        }
        catch (System.Net.NetworkInformation.NetworkInformationException) { return true; }
    }
}

public enum T3CodeRepairStep
{
    /// <summary>Wingman reaches every open window: Repair re-attached the Helpers quietly, nothing else.</summary>
    Reattached,
    /// <summary>Some open windows have no Wingman connection: only a restart fixes that, so ask first.</summary>
    RestartForUnreachedWindows,
    /// <summary>No window open or the check failed: take the Open window route (asks only if a restart is needed).</summary>
    OpenRoute,
}

/// <summary>What Repair does for T3 Code once Wingman has its debugging endpoint. Same rule as every app:
/// fix quietly, and ask before closing anything only when a restart is truly needed.</summary>
public static class T3CodeRepairPolicy
{
    public static T3CodeRepairStep Decide(int visibleWindows, int reachedWindows, bool checkFailed) =>
        checkFailed || visibleWindows <= 0 ? T3CodeRepairStep.OpenRoute
        : reachedWindows >= visibleWindows ? T3CodeRepairStep.Reattached
        : T3CodeRepairStep.RestartForUnreachedWindows;
}
