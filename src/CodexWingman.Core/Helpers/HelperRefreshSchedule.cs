namespace CodexWingman.Core.Helpers;

public static class HelperRefreshSchedule
{
    public const int HostActionIntervalMilliseconds = 250;

    public static int IntervalMilliseconds(IEnumerable<HelperSummary> helpers)
    {
        var enabled = helpers.Where(helper => helper.Enabled).ToArray();
        var requestedSeconds = enabled
            .Where(helper => helper.RefreshSeconds > 0)
            .Select(helper => helper.RefreshSeconds)
            .DefaultIfEmpty(60)
            .Min();
        return Math.Clamp(requestedSeconds, 1, 60) * 1000;
    }

    public static bool RequiresHostActionPolling(IEnumerable<HelperSummary> helpers) => helpers.Any(
        helper => helper.Enabled && (helper.Capabilities ?? []).Any(HostActionDispatcher.IsKnownCapability));
}
