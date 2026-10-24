namespace CodexWingman.Core;

/// <summary>
/// Which processes a second Wingman launch lets come to the front. The launch came from the user's
/// click, so Windows gave it the foreground right; it passes that right only to the running Wingman
/// (another CodexWingman process in the same Windows session), never to any process (ASFW_ANY).
/// </summary>
public static class WingmanForegroundPolicy
{
    public static IReadOnlyList<int> Recipients(IEnumerable<(int ProcessId, int? SessionId)> wingmanProcesses, int selfProcessId, int selfSessionId) =>
        wingmanProcesses
            .Where(process => process.ProcessId > 0 && process.ProcessId != selfProcessId && process.SessionId == selfSessionId)
            .Select(process => process.ProcessId)
            .Distinct()
            .ToArray();
}
