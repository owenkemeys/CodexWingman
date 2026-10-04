namespace CodexWingman.Core;

public static class T3CodeLaunchPolicy
{
    public const int PreferredPort = 9323;

    public static IReadOnlyList<string> CandidateExecutables(string localAppData, string programFiles)
    {
        return [
            Path.Combine(localAppData, "Programs", "T3 Code", "T3 Code.exe"),
            Path.Combine(localAppData, "Programs", "T3 Code Nightly", "T3 Code Nightly.exe"),
            Path.Combine(programFiles, "T3 Code", "T3 Code.exe"),
            Path.Combine(programFiles, "T3 Code Nightly", "T3 Code Nightly.exe"),
            Path.Combine(localAppData, "Programs", "t3code", "T3 Code.exe"),
            Path.Combine(localAppData, "Programs", "t3code", "T3 Code (Nightly).exe"),
        ];
    }

    public static string? SelectExecutable(
        IEnumerable<string> candidates,
        IEnumerable<string> runningPaths,
        Func<string, bool> exists)
    {
        var installed = candidates.Where(IsT3Executable).Where(exists).ToArray();
        var running = runningPaths.Where(IsT3Executable).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return installed.FirstOrDefault(running.Contains) ?? installed.FirstOrDefault();
    }

    public static bool IsT3Executable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash < 0) return false;
        var name = normalized[(slash + 1)..];
        var parent = normalized[..slash];
        var directory = parent[(parent.LastIndexOf('/') + 1)..];
        return (name.Equals("T3 Code.exe", StringComparison.OrdinalIgnoreCase)
                && (directory.Equals("T3 Code", StringComparison.OrdinalIgnoreCase)
                    || directory.Equals("t3code", StringComparison.OrdinalIgnoreCase)))
            || (name.Equals("T3 Code Nightly.exe", StringComparison.OrdinalIgnoreCase)
                && directory.Equals("T3 Code Nightly", StringComparison.OrdinalIgnoreCase))
            || (name.Equals("T3 Code (Nightly).exe", StringComparison.OrdinalIgnoreCase)
                && directory.Equals("t3code", StringComparison.OrdinalIgnoreCase));
    }

    public static bool MatchesRunningProcess(string selectedExecutable, int selectedSession,
        string? processExecutable, int processSession) =>
        IsT3Executable(selectedExecutable)
        && selectedSession == processSession
        && string.Equals(selectedExecutable, processExecutable, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesVisibleWindow(
        IEnumerable<string> installedCandidates,
        int currentSession,
        string? processExecutable,
        int processSession,
        bool visible,
        bool owned,
        bool titled) =>
        visible && !owned && titled && processSession == currentSession
        && processExecutable is not null
        && installedCandidates.Any(path => IsT3Executable(path)
            && string.Equals(path, processExecutable, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<int> CandidatePorts(int? rememberedPort, int? codexPort = null)
    {
        var preferred = codexPort == PreferredPort ? Array.Empty<int>() : [PreferredPort];
        return rememberedPort is > 0 and <= 65535
            && rememberedPort != PreferredPort
            && rememberedPort != CodexLaunchPolicy.PreferredPort
            && rememberedPort != codexPort
            ? [rememberedPort.Value, .. preferred]
            : preferred;
    }

    public static string DebugArguments(int port, bool newWindow = false)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        return $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}"
            + (newWindow ? " --new-window" : string.Empty);
    }
}
