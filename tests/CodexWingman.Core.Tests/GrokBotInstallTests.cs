using CodexWingman.Core;

internal static class GrokBotInstallTests
{
    private const string Local = @"C:\Users\u\AppData\Local";
    private const string PerUser = @"C:\Users\u\AppData\Local\Programs\Grok Bot\Grok Bot.exe";
    private const string PerMachine = @"C:\Program Files\Grok Bot\Grok Bot.exe";

    private static GrokBotInstallFacts Facts(string[] files, string?[]? running = null, GrokBotUninstallEntry[]? uninstall = null,
        string?[]? links = null) => new(
        running ?? [], Local, [@"C:\Program Files", @"C:\Program Files (x86)"], uninstall ?? [], links ?? [],
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase));

    public static void Run()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("GrokBotInstallTests: skipped (Windows paths)"); RunPortable(); return; }
        var perUser = GrokBotInstall.Resolve(Facts([PerUser]));
        Equal(PerUser, perUser.Executable, "per-user install found");
        Equal(GrokBotInstallSource.PerUser, perUser.Source, "per-user source");

        var perMachine = GrokBotInstall.Resolve(Facts([PerMachine]));
        Equal(PerMachine, perMachine.Executable, "per-machine install found (Program Files)");
        Equal(GrokBotInstallSource.PerMachine, perMachine.Source, "per-machine source");

        var running = GrokBotInstall.Resolve(Facts([PerUser, @"D:\Apps\Grok Bot\Grok Bot.exe"], running: [@"D:\Apps\Grok Bot\Grok Bot.exe"]));
        Equal(@"D:\Apps\Grok Bot\Grok Bot.exe", running.Executable, "running Grok Bot's own image wins");
        Equal(GrokBotInstallSource.Running, running.Source, "running source");

        var otherRunning = GrokBotInstall.Resolve(Facts([PerUser, @"C:\x\chrome.exe"], running: [@"C:\x\chrome.exe", null]));
        Equal(PerUser, otherRunning.Executable, "a non-Grok Bot image or unreadable process is skipped");

        var both = GrokBotInstall.Resolve(Facts([PerUser, PerMachine]));
        Equal(PerUser, both.Executable, "per-user before per-machine when both exist and none runs");

        var registry = GrokBotInstall.Resolve(Facts([@"E:\GB\Grok Bot.exe"],
            uninstall: [new(null, null), new("\"E:\\GB\\Grok Bot.exe\",0", null)]));
        Equal(@"E:\GB\Grok Bot.exe", registry.Executable, "uninstall DisplayIcon, quoted with icon index");
        Equal(GrokBotInstallSource.Uninstall, registry.Source, "uninstall source");

        var location = GrokBotInstall.Resolve(Facts([@"F:\Grok\Grok Bot.exe"], uninstall: [new("", "\"F:\\Grok\"")]));
        Equal(@"F:\Grok\Grok Bot.exe", location.Executable, "uninstall InstallLocation");

        var link = GrokBotInstall.Resolve(Facts([@"G:\G\Grok Bot.exe"], links: [null, @"G:\G\Grok Bot.exe"]));
        Equal(GrokBotInstallSource.StartMenu, link.Source, "Start menu shortcut target last");

        var none = GrokBotInstall.Resolve(Facts([]));
        Equal(GrokBotInstallSource.NotFound, none.Source, "nothing found");
        False(none.Found, "not found flagged");
        Equal(PerUser, none.Executable, "not found falls back to the usual per-user path");

        var stale = GrokBotInstall.Resolve(Facts([], uninstall: [new(PerMachine, null)]));
        False(stale.Found, "uninstall entry whose file is gone is not used");

        True(GrokBotInstall.SameExecutable(@"c:\program files\grok bot\GROK BOT.EXE", PerMachine), "running process matched by full path, any case");
        False(GrokBotInstall.SameExecutable(PerUser, PerMachine), "a different install is not the resolved one");
        False(GrokBotInstall.SameExecutable(null, PerMachine), "unreadable image never matches");
        RunPortable();
    }

    private static void RunPortable()
    {
        Equal(@"C:\Program Files\Grok Bot\Grok Bot.exe", GrokBotInstall.IconPath("\"C:\\Program Files\\Grok Bot\\Grok Bot.exe\",0"), "quoted DisplayIcon");
        Equal(@"C:\Program Files\Grok Bot\Grok Bot.exe", GrokBotInstall.IconPath(@"C:\Program Files\Grok Bot\Grok Bot.exe,0"), "DisplayIcon with index");
        Equal(@"C:\Program Files\Grok Bot\Grok Bot.exe", GrokBotInstall.IconPath(@"C:\Program Files\Grok Bot\Grok Bot.exe"), "plain DisplayIcon");
        Equal<string?>(null, GrokBotInstall.IconPath("  "), "empty DisplayIcon");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        Console.WriteLine("PASS " + name);
    }

    private static void True(bool value, string name) => Equal(true, value, name);
    private static void False(bool value, string name) => Equal(false, value, name);
}
