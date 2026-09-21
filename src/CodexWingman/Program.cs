using CodexWingman.Core;
using System.Runtime.InteropServices;

namespace CodexWingman;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WingmanIdentity.ActivationEventName);
        using var singleInstance = new Mutex(true, WingmanIdentity.SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            activationEvent.Set();
            return;
        }

        SetCurrentProcessExplicitAppUserModelID(WingmanIdentity.AppUserModelId);
        var portOverride = CodexLaunchPolicy.TryGetPortOverride(args);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(portOverride, activationEvent));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
