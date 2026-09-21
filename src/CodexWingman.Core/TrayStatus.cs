using CodexWingman.Core.Helpers;

namespace CodexWingman.Core;

public enum TrayLifecycleState
{
    Starting,
    Working,
    Active,
    Suspended,
    Removed,
    Error,
}

public enum TrayIconState
{
    Normal,
    Attention,
}

public sealed record TrayStatus(
    string Label,
    string DiagnosticText,
    string WindowSummary = "Checking windows...",
    TrayIconState IconState = TrayIconState.Normal);

public enum CodexHookState
{
    NotOpen,
    OpenButCannotHook,
    Hooked,
    UnknownProblem,
}

public static class TrayStatusPolicy
{
    public static TrayStatus ForCodexState(CodexHookState state, string diagnosticText) =>
        new(LabelFor(state), diagnosticText, IconState: state is CodexHookState.OpenButCannotHook or CodexHookState.UnknownProblem
            ? TrayIconState.Attention
            : TrayIconState.Normal);

    public static TrayStatus ForLifecycle(TrayLifecycleState state, string diagnosticText) =>
        new(LabelFor(state), diagnosticText, IconState: state == TrayLifecycleState.Error
            ? TrayIconState.Attention
            : TrayIconState.Normal);

    public static TrayStatus ForReport(TrayLifecycleState state, HelperHostReport report)
    {
        var label = report.Failed > 0 ? LabelFor(TrayLifecycleState.Error) : LabelFor(state);
        var diagnostic = $"{label}: {report.Succeeded} succeeded, {report.Failed} failed across {report.TargetsDiscovered} windows";
        if (report.Diagnostics.Count > 0)
            diagnostic += $"; {string.Join("; ", report.Diagnostics.Select(item => item.Message))}";
        return new(label, diagnostic, IconState: report.Failed > 0
            ? TrayIconState.Attention
            : TrayIconState.Normal);
    }

    public static TrayStatus ForError(string diagnosticText) =>
        new(LabelFor(TrayLifecycleState.Error), diagnosticText, IconState: TrayIconState.Attention);

    private static string LabelFor(TrayLifecycleState state) => state switch
    {
        TrayLifecycleState.Starting => "Starting",
        TrayLifecycleState.Working => "Working",
        TrayLifecycleState.Active => "Active",
        TrayLifecycleState.Suspended => "Suspended",
        TrayLifecycleState.Removed => "Removed",
        TrayLifecycleState.Error => "Needs attention",
        _ => "Needs attention",
    };

    private static string LabelFor(CodexHookState state) => state switch
    {
        CodexHookState.NotOpen => "Codex not running",
        CodexHookState.OpenButCannotHook => "Codex needs helper restart",
        CodexHookState.Hooked => "Codex connected",
        CodexHookState.UnknownProblem => "Codex status unknown",
        _ => "Codex status unknown",
    };
}
