namespace CodexWingman.Core;

public enum HookReadyWindowPlan
{
    OpenNativeChild,
    LaunchNormalProfile,
    ConfirmRestart,
}

public static class HookReadyWindowPolicy
{
    public static HookReadyWindowPlan Decide(bool endpointReady, bool codexRunning) =>
        endpointReady
            ? HookReadyWindowPlan.OpenNativeChild
            : codexRunning
                ? HookReadyWindowPlan.ConfirmRestart
                : HookReadyWindowPlan.LaunchNormalProfile;
}
