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
            try { EnsureShortcuts(options); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 1;
            }
            return;
        }
        if (options.Command != WingmanCommand.None)
        {
            // Commands only signal a running Wingman (development builds listen); they never start one.
            try
            {
                using var command = EventWaitHandle.OpenExisting(WingmanLaunchOptions.CommandEventName(options.Command));
                command.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Console.Error.WriteLine("No running development Wingman accepts that command.");
                Environment.ExitCode = 4;
            }
            return;
        }
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WingmanIdentity.ActivationEventName);
        using var codexActivation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.Codex).ActivationEventName, out var createdCodexEvent);
        using var t3Activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.T3Code).ActivationEventName, out var createdT3Event);
        using var t3WebActivation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.T3Web).ActivationEventName, out var createdT3WebEvent);
        using var grokActivation = new EventWaitHandle(false, EventResetMode.AutoReset,
            new WingmanLaunchOptions(WingmanLaunchTarget.GrokBot).ActivationEventName, out var createdGrokEvent);
        // A running Wingman always holds every activation event; creating one here means an older build is running.
        var olderInstanceRunning = createdCodexEvent || createdT3Event || createdT3WebEvent || createdGrokEvent;
        using var singleInstance = new Mutex(true, WingmanIdentity.SingleInstanceName, out var isFirstInstance);
        if (isFirstInstance || olderInstanceRunning)
        {
            try { EnsureShortcuts(options); }
            catch (Exception error)
            {
                MessageBox.Show("Wingman could not create its Start shortcuts.\n\n" + error.Message,
                    "Wingman launchers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        if (!isFirstInstance)
        {
            if (olderInstanceRunning)
            {
                MessageBox.Show("Close the older Wingman from its tray menu, then use this shortcut again.",
                    "Wingman update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.ExitCode = 3;
                return;
            }
            using var selected = EventWaitHandle.OpenExisting(options.ActivationEventName);
            // This launch came from the user's click; pass the right to come to the front only to the
            // running Wingman (which passes it on to the app window it raises), not to every process.
            AllowRunningWingmanToForeground();
            selected.Set();
            return;
        }

        SetCurrentProcessExplicitAppUserModelID(WingmanLaunchOptions.AppUserModelIdFor(options.Target));
        var portOverride = CodexLaunchPolicy.TryGetPortOverride(args);
        Application.Run(new TrayApplicationContext(portOverride, activationEvent, options, codexActivation, t3Activation,
            t3WebActivation, grokActivation));
    }

    private static void AllowRunningWingmanToForeground()
    {
        var self = Environment.ProcessId;
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        var candidates = new List<(int ProcessId, int? SessionId)>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
        {
            using (process) candidates.Add((process.Id, WindowProcessInfo.SessionOf((uint)process.Id)));
        }
        foreach (var processId in WingmanForegroundPolicy.Recipients(candidates, self, WindowProcessInfo.CurrentSession()))
            AllowSetForegroundWindow(processId);
    }

    private static void EnsureShortcuts(WingmanLaunchOptions options)
    {
        var profile = LauncherShortcuts.CurrentProfileOverride(options);
        LauncherShortcuts.Ensure(Application.ExecutablePath, profile, includeT3Web: LauncherShortcuts.T3WebAvailable(options.LocalAppData));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
