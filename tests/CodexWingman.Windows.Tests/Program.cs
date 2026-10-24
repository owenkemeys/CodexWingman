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
        if (args.FirstOrDefault() == "--verify-brand")
        {
            var identity = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.GetFullPath(args[1]));
            Equal("Wingman", identity.ProductName, "released Windows product name");
            Equal("Wingman", identity.FileDescription, "released Windows application description");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", identity.ProductName, identity.FileDescription, identity.ProductVersion }));
            return;
        }
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
                    var expectedArguments = target == WingmanLaunchTarget.Preferences
                        ? WingmanLaunchOptions.RelaunchArguments(profile)
                        : WingmanLaunchOptions.ShortcutArguments(target, profile);
                    Equal(expectedArguments, arguments, "app and profile arguments");
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
            // T3 web gets its own Start shortcut and taskbar identity only when it can work on this PC.
            var withWeb = LauncherShortcuts.Ensure(executable, profile, programs, includeT3Web: true);
            Equal(4, withWeb.Length, "T3 web shortcut added when available");
            Equal(LauncherShortcuts.T3WebShortcutName + ".lnk", Path.GetFileName(withWeb[3]), "T3 web shortcut name");
            object webShortcut = ((dynamic)shell).CreateShortcut(withWeb[3]);
            object webFolder = ((dynamic)explorer).NameSpace(programs);
            object webItem = ((dynamic)webFolder).ParseName(Path.GetFileName(withWeb[3]));
            try
            {
                Equal(WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.T3Web, profile), (string)((dynamic)webShortcut).Arguments, "T3 web shortcut arguments");
                Equal(WingmanIdentity.AppUserModelId + ".T3Web", (string)((dynamic)webItem).ExtendedProperty("System.AppUserModel.ID"), "T3 web taskbar identity");
                observed.Add(new { name = Path.GetFileName(withWeb[3]), arguments = (string)((dynamic)webShortcut).Arguments, appId = WingmanIdentity.AppUserModelId + ".T3Web" });
            }
            finally
            {
                Marshal.FinalReleaseComObject(webItem);
                Marshal.FinalReleaseComObject(webFolder);
                Marshal.FinalReleaseComObject(webShortcut);
            }
            // T3's own cached icon: the T3 web shortcut uses it (Wingman's until one is cached); the others keep Wingman's.
            object webIconBefore = ((dynamic)shell).CreateShortcut(withWeb[3]);
            try { Equal(executable + ",0", (string)((dynamic)webIconBefore).IconLocation, "Wingman icon until T3's is cached"); }
            finally { Marshal.FinalReleaseComObject(webIconBefore); }
            var t3Icon = Path.Combine(root, "t3-web", "app-icon.ico");
            Directory.CreateDirectory(Path.GetDirectoryName(t3Icon)!);
            File.WriteAllBytes(t3Icon, [0, 0, 1, 0, 0, 0]);
            var withIcon = LauncherShortcuts.Ensure(executable, profile, programs, includeT3Web: true, t3WebIcon: t3Icon);
            Equal(withWeb[3], withIcon[3], "T3 web shortcut path unchanged");
            object webIcon = ((dynamic)shell).CreateShortcut(withIcon[3]);
            object codexIcon = ((dynamic)shell).CreateShortcut(withIcon[0]);
            object iconFolder = ((dynamic)explorer).NameSpace(programs);
            object iconItem = ((dynamic)iconFolder).ParseName(Path.GetFileName(withIcon[3]));
            try
            {
                Equal(t3Icon + ",0", (string)((dynamic)webIcon).IconLocation, "T3 web shortcut shows T3's icon");
                Equal(executable, (string)((dynamic)webIcon).TargetPath, "T3 web shortcut target unchanged");
                Equal(WingmanIdentity.AppUserModelId + ".T3Web", (string)((dynamic)iconItem).ExtendedProperty("System.AppUserModel.ID"), "T3 web identity unchanged with T3's icon");
                Equal(executable + ",0", (string)((dynamic)codexIcon).IconLocation, "Codex shortcut keeps Wingman's icon");
            }
            finally
            {
                Marshal.FinalReleaseComObject(iconItem);
                Marshal.FinalReleaseComObject(iconFolder);
                Marshal.FinalReleaseComObject(codexIcon);
                Marshal.FinalReleaseComObject(webIcon);
            }
            Equal(WingmanIdentity.AppUserModelId + ".Codex", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.Codex), "Codex identity unchanged");
            Equal(WingmanIdentity.AppUserModelId + ".T3Code", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Code), "T3 desktop identity unchanged");
            Equal(WingmanIdentity.AppUserModelId, WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.Preferences), "preferences identity unchanged");

            // Window grouping: Wingman can give another process's top-level windows its T3 web identity.
            // Here the window is a test form in this process; on a real PC it is Wingman's own Chrome app window.
            using (var form = new System.Windows.Forms.Form { Text = "Wingman window identity test", ShowInTaskbar = true, Opacity = 0.01 })
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var webId = WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Web);
                var applied = WindowAppIdentity.Apply(Environment.ProcessId, webId,
                    "\"" + executable + "\" --launch=t3-web", LauncherShortcuts.T3WebShortcutName, executable + ",0");
                Equal(true, applied >= 1, "window identity applied to a visible top-level window");
                Equal(webId, WindowAppIdentity.Read(form.Handle), "window carries the T3 web identity");
                Equal(0, WindowAppIdentity.Apply(Environment.ProcessId, webId, "unused", "unused", "unused"), "re-applying is a no-op");
                form.Close();
            }
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
