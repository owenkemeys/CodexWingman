namespace CodexWingman.Core;

public static class TrayVisibilityRecovery
{
    public static void EnsureVisible(bool exiting, Action<bool> setVisible)
    {
        ArgumentNullException.ThrowIfNull(setVisible);
        if (exiting) return;

        setVisible(false);
        setVisible(true);
    }
}
