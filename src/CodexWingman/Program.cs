using CodexWingman.Core;
using System.Runtime.InteropServices;

namespace CodexWingman;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        WingmanLaunchOptions options;
        try { options = WingmanLaunchOptions.Parse(args); }
        catch (ArgumentException error)
        {
            MessageBox.Show(error.Message, "Wingman launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.ExitCode = 2;
            return;
        }
        if (options.RegisterOnly)
        {
            try { LauncherShortcuts.Ensure(Application.ExecutablePath, LauncherShortcuts.CurrentProfileOverride(options)); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 1;
            }
            return;
        }
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WingmanIdentity.ActivationEventName);
        using var codexActivation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.Codex).ActivationEventName, out var createdCodexEvent);
        using var t3Activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.T3Code).ActivationEventName, out var createdT3Event);
        using var singleInstance = new Mutex(true, WingmanIdentity.SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if ((options.Target == WingmanLaunchTarget.Codex && createdCodexEvent)
                || (options.Target == WingmanLaunchTarget.T3Code && createdT3Event))
            {
                MessageBox.Show("Close the older Wingman from its tray menu, then use this shortcut again.",
                    "Wingman update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.ExitCode = 3;
                return;
            }
            using var selected = EventWaitHandle.OpenExisting(options.ActivationEventName);
            selected.Set();
            return;
        }

        SetCurrentProcessExplicitAppUserModelID(WingmanIdentity.AppUserModelId + (options.Target switch
        {
            WingmanLaunchTarget.Codex => ".Codex",
            WingmanLaunchTarget.T3Code => ".T3Code",
            _ => string.Empty,
        }));
        var portOverride = CodexLaunchPolicy.TryGetPortOverride(args);
        Application.Run(new TrayApplicationContext(portOverride, activationEvent, options, codexActivation, t3Activation));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
