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
            return new("T3 Code: Status unavailable", "Wingman could not count visible T3 Code windows. Choose View status details again or Repair T3 Code.");
        if (visibleWindows < 0) throw new ArgumentOutOfRangeException(nameof(visibleWindows));
        if (visibleWindows == 0)
            return new("T3 Code: Closed (0/0)", "No T3 Code windows are open.");
        if (helpersSuspended)
            return new($"T3 Code: Paused (0/{visibleWindows})", "Helpers are paused. Turn on Helpers enabled to reconnect the visible T3 Code windows.");
        if (!endpointConfigured)
            return new($"T3 Code: Problem (0/{visibleWindows})", "Wingman cannot connect to the visible T3 Code windows. Choose Repair T3 Code after saving work.");
        if (checkFailed || report is null || connections is null)
            return new($"T3 Code: Problem (0/{visibleWindows})", "Wingman could not check the visible T3 Code windows. Choose Repair T3 Code after saving work.");

        var hooked = Math.Min(Math.Min(Math.Max(connections.Connected, 0),
            Math.Max(report.TargetsDiscovered, 0)), visibleWindows.Value);
        if (hooked < visibleWindows)
            return new($"T3 Code: Problem ({hooked}/{visibleWindows})", $"Wingman has a Helper connection for {hooked} of {visibleWindows} visible T3 Code windows. Choose Repair T3 Code after saving work.");
        if (connections.Failed > 0 || report.Failed > 0)
            return new($"T3 Code: Problem ({hooked}/{visibleWindows})", "T3 Code windows are connected, but one or more Helpers failed. Choose Reload helpers, then Repair T3 Code if the problem remains.");
        if (report.Diagnostics.Count > 0)
            return new($"T3 Code: Problem ({hooked}/{visibleWindows})",
                "T3 Code windows are connected, but a Helper needs attention. " + string.Join(" ", report.Diagnostics.Select(item => item.Message)));
        if (!hasEnabledHelpers)
            return new($"T3 Code: No Helpers (0/{visibleWindows})", "Wingman can connect to T3 Code, but no T3 Code Helpers are enabled. Add or enable a T3 Code Helper to alter its windows.");
        return new($"T3 Code: OK ({hooked}/{visibleWindows})", "Wingman is connected to each visible T3 Code window, and enabled Helpers report no problems.");
    }
}
