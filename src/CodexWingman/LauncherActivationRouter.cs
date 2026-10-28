using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Turns launcher activation events into <see cref="LauncherActivationQueue"/> entries on Wingman's UI thread.
/// Each event carries exactly one target: the general Wingman event is <see cref="WingmanLaunchTarget.Preferences"/>
/// (the apps ticked Open when Wingman starts) and each app's own event is that app.
/// </summary>
internal sealed class LauncherActivationRouter(Action<Action> post, Action<WingmanLaunchTarget> activate) : IDisposable
{
    private readonly List<RegisteredWaitHandle> registrations = [];

    public void Register(EventWaitHandle? handle, WingmanLaunchTarget target)
    {
        if (handle is null) return;
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown launch target.");
        registrations.Add(ThreadPool.RegisterWaitForSingleObject(handle,
            (_, _) => post(() => activate(target)), null, Timeout.Infinite, false));
    }

    public void Dispose()
    {
        foreach (var registration in registrations) registration.Unregister(null);
        registrations.Clear();
    }
}
