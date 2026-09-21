using CodexWingman.Core.Helpers;

namespace CodexWingman.Core;

public enum ConnectionHealthState
{
    CodexClosed,
    NeedsRepair,
    Degraded,
    Working,
    HelpersPaused,
}

public sealed record ConnectionHealthSnapshot(
    ConnectionHealthState State,
    string Label,
    string DiagnosticText,
    int Windows,
    string WindowSummary,
    bool CanRepair,
    bool CanOpenWindow)
{
    public TrayIconState IconState => State is ConnectionHealthState.NeedsRepair or ConnectionHealthState.Degraded
        ? TrayIconState.Attention
        : TrayIconState.Normal;
}

public static class ConnectionHealthPolicy
{
    public static ConnectionHealthSnapshot Evaluate(
        CodexHookState hookState,
        HelperHostReport? report,
        bool helpersSuspended,
        int? totalWindows = null)
    {
        if (hookState == CodexHookState.NotOpen)
            return new(
                ConnectionHealthState.CodexClosed,
                "Status: Codex closed",
                "Codex is not running. Launch or repair to start Codex with Helpers available.",
                0,
                "No Codex windows open",
                CanRepair: true,
                CanOpenWindow: false);

        if (hookState is CodexHookState.OpenButCannotHook or CodexHookState.UnknownProblem)
            return new(
                ConnectionHealthState.NeedsRepair,
                "Status: Needs repair",
                hookState == CodexHookState.OpenButCannotHook
                    ? "Codex is open, but Wingman cannot connect to its windows.\n\nWhat to do: Choose Repair Codex and Helpers."
                    : "Wingman could not check the Codex windows.\n\nWhat to do: Choose Repair Codex and Helpers.",
                0,
                "No windows hooked",
                CanRepair: true,
                CanOpenWindow: false);

        if (helpersSuspended)
        {
            var pausedWindows = report?.TargetsDiscovered ?? 0;
            return new(
                ConnectionHealthState.HelpersPaused,
                "Status: Helpers paused",
                "Helpers are paused. Turn on Helpers enabled when you want them active.",
                pausedWindows,
                FormatWindowSummary(pausedWindows, totalWindows),
                CanRepair: true,
                CanOpenWindow: hookState == CodexHookState.Hooked);
        }

        if (report is null || report.TargetsDiscovered <= 0)
            return new(
                ConnectionHealthState.NeedsRepair,
                "Status: Needs repair",
                "Wingman is connected to Codex, but no usable windows were found.\n\nWhat to do: Choose Repair Codex and Helpers.",
                0,
                FormatWindowSummary(0, totalWindows),
                CanRepair: true,
                CanOpenWindow: false);

        var windows = report.TargetsDiscovered;
        if (report.Failed > 0)
        {
            return new(
                ConnectionHealthState.Degraded,
                "Status: Needs attention",
                "Some Helpers are not working in every hooked window.\n\nWhat to do: Reload Helpers. If the problem remains, choose Repair Codex and Helpers.",
                windows,
                FormatWindowSummary(windows, totalWindows),
                CanRepair: true,
                CanOpenWindow: true);
        }

        return new(
            ConnectionHealthState.Working,
            "Status: OK",
            "Wingman is working normally. No action is needed.",
            windows,
            FormatWindowSummary(windows, totalWindows),
            CanRepair: true,
            CanOpenWindow: true);
    }

    public static string FormatWindowSummary(int alteredWindows, int? totalWindows = null)
    {
        var total = Math.Max(alteredWindows, totalWindows ?? alteredWindows);
        return $"Altering {alteredWindows} of {total} windows";
    }
}
