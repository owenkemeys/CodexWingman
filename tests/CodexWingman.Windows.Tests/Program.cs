using CodexWingman;
using CodexWingman.Core;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (NativeUpdaterTests.TryChild(args)) return;
        var root = Path.Combine(Path.GetTempPath(), "Wingman launcher test " + Guid.NewGuid().ToString("N"));
        object? shell = null;
        object? explorer = null;
        try
        {
            var package = Path.Combine(root, "Package with spaces");
            Directory.CreateDirectory(Path.Combine(package, "Helpers"));
            // The links are inspected, never executed. Registration doesn't need a running host.
            var executable = Path.Combine(package, "CodexWingman.exe");
            File.WriteAllBytes(executable, [0]);
            var programs = Path.Combine(root, "Programs");
            var profile = Path.Combine(root, "Isolated profile");
            var paths = LauncherShortcuts.Ensure(executable, profile, programs);
            Equal(3, paths.Length, "app launchers plus the renamed preferences launcher");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            explorer = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            var observed = new List<object>();
            foreach (var (path, target, suffix) in new[]
            {
                (paths[0], WingmanLaunchTarget.Codex, ".Codex"),
                (paths[1], WingmanLaunchTarget.T3Code, ".T3Code"),
                (paths[2], WingmanLaunchTarget.Preferences, ""),
            })
            {
                object shortcut = ((dynamic)shell).CreateShortcut(path);
                object folder = ((dynamic)explorer).NameSpace(programs);
                object item = ((dynamic)folder).ParseName(Path.GetFileName(path));
                try
                {
                    dynamic link = shortcut;
                    Equal(executable, (string)link.TargetPath, "direct executable target");
                    Equal(package, (string)link.WorkingDirectory, "package working directory");
                    var arguments = (string)link.Arguments;
                    Equal(WingmanLaunchOptions.ShortcutArguments(target, profile), arguments, "app and profile arguments");
                    var parsed = WingmanLaunchOptions.Parse(SplitCommandLine("Wingman.exe " + arguments).Skip(1));
                    Equal(target, parsed.Target, "Windows argument round trip selects app");
                    Equal(profile, parsed.LocalAppData, "Windows argument round trip preserves profile");
                    var appId = (string)((dynamic)item).ExtendedProperty("System.AppUserModel.ID");
                    Equal(WingmanIdentity.AppUserModelId + suffix, appId, "separate taskbar identity survives save");
                    observed.Add(new { name = Path.GetFileName(path), arguments, appId });
                }
                finally
                {
                    Marshal.FinalReleaseComObject(item);
                    Marshal.FinalReleaseComObject(folder);
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            // Updating a moved package changes both links; the user's other shortcuts survive.
            File.WriteAllText(Path.Combine(programs, "Unrelated.lnk"), "preserve");
            LauncherShortcuts.Ensure(executable, null, programs);
            Equal("preserve", File.ReadAllText(Path.Combine(programs, "Unrelated.lnk")), "unrelated shortcut preserved");
            object refreshed = ((dynamic)shell).CreateShortcut(paths[0]);
            try { Equal("--launch=codex", (string)((dynamic)refreshed).Arguments, "normal profile clears trial argument"); }
            finally { Marshal.FinalReleaseComObject(refreshed); }
            foreach (var trailingProfile in new[] { Path.GetPathRoot(root)!, profile + "\\" })
            {
                var parsed = WingmanLaunchOptions.Parse(SplitCommandLine("Wingman.exe " +
                    WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.T3Code, trailingProfile)).Skip(1));
                Equal(Path.GetFullPath(trailingProfile), parsed.LocalAppData, "trailing slash survives Windows parsing");
                var relaunch = WingmanLaunchOptions.Parse(SplitCommandLine("Wingman.exe " +
                    WingmanLaunchOptions.RelaunchArguments(trailingProfile)).Skip(1));
                Equal(Path.GetFullPath(trailingProfile), relaunch.LocalAppData, "update profile survives native parsing");
                Equal(WingmanLaunchTarget.Preferences, relaunch.Target, "update relaunch keeps startup preferences");
            }
            Equal("Wingman", TrayIconPresentation.TextFor(TrayIconState.Normal), "tray tooltip uses the renamed app");
            Equal("Wingman", TrayIconPresentation.TextFor(TrayIconState.Attention), "tray identity text stays stable during attention states");
            NativeUpdaterTests.Run(root);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", shortcuts = observed }));
        }
        finally
        {
            if (explorer is not null) Marshal.FinalReleaseComObject(explorer);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Windows argument parsing failed");
        try
        {
            return Enumerable.Range(0, count).Select(index =>
                Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!).ToArray();
        }
        finally { LocalFree(pointer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
}
