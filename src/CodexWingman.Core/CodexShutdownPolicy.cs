namespace CodexWingman.Core;

public sealed record CodexProcessSnapshot(
    int Id,
    string Name,
    int SessionId,
    string? ExecutablePath,
    string? PackageFamilyName,
    bool HasMainWindow);

public static class CodexShutdownPolicy
{
    public static bool IsAppRunning(
        IEnumerable<CodexProcessSnapshot> processes,
        int currentSessionId) => processes.Any(process => process.SessionId == currentSessionId
            && string.Equals(process.PackageFamilyName, CodexLaunchPolicy.PackageFamilyName,
                StringComparison.OrdinalIgnoreCase));

    public static string SelectExecutablePath(
        IEnumerable<CodexProcessSnapshot> processes,
        int currentSessionId)
    {
        var windows = processes
            .Where(process => process.SessionId == currentSessionId
                && process.HasMainWindow
                && string.Equals(process.PackageFamilyName, CodexLaunchPolicy.PackageFamilyName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (windows.Length == 0)
            throw new InvalidOperationException(
                "No window from the registered Codex app could be identified in this Windows session. Wingman did not stop any processes.");
        if (windows.Any(process => string.IsNullOrWhiteSpace(process.ExecutablePath)))
            throw new InvalidOperationException(
                "Wingman could not verify the executable behind every Codex or ChatGPT window. It did not stop any processes.");

        var paths = windows
            .Select(process => process.ExecutablePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length != 1)
            throw new InvalidOperationException(
                "More than one Codex or ChatGPT application is open. Wingman could not choose one safely, so it did not stop any processes.");
        return paths[0];
    }

    public static IReadOnlySet<int> SelectProcessIds(
        IEnumerable<CodexProcessSnapshot> processes,
        int currentSessionId) => processes
            .Where(process => process.SessionId == currentSessionId
                && string.Equals(process.PackageFamilyName, CodexLaunchPolicy.PackageFamilyName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(process => process.Id)
            .ToHashSet();
}
