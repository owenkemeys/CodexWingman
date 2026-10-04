using CodexWingman.Core.Helpers;

namespace CodexWingman.Core;

public sealed record T3CodeMenuStatus(string Label, string Details);

public static class T3CodeMenuPolicy
{
    public static T3CodeMenuStatus Evaluate(bool endpointConfigured, HelperHostReport? report, bool checkFailed)
    {
        const string label = "T3 Code: Not ready";
        if (!endpointConfigured)
            return new(label, "Wingman has not connected to a hookable T3 Code window yet. Existing T3 windows were left alone.");
        if (checkFailed)
            return new(label, "Wingman could not check the configured T3 Code window. Existing T3 windows were left alone.");
        if (report is null || report.TargetsDiscovered == 0)
            return new(label, "Wingman has no verified T3 Code renderer window yet. Existing T3 windows were left alone.");
        return new(label,
            $"Wingman found {report.TargetsDiscovered} T3 Code renderer page(s). Hooking and repair still need a dedicated window trial.");
    }
}
