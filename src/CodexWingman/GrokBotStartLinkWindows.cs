using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>Windows wiring for the opt-in debugging port on Grok Bot's Start menu shortcut (see <see cref="GrokBotStartLink"/>).</summary>
internal static class GrokBotStartLinkWindows
{
    /// <summary>Grok Bot's install key as its installer writes it (electron-builder: Software\{APP_GUID}).</summary>
    private const string KnownInstallGuid = "8db316bb-b3e2-5dd2-9554-ecccb62be362";
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static GrokBotStartLink For(string grokExecutable, string wingmanDataRoot) => new(
        GrokBotStartLink.DefaultLinkPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        GrokBotStartLink.BackupFolder(wingmanDataRoot), grokExecutable, new ShellLinkFiles(), () => DateTime.Now);

    /// <summary>
    /// Read-only: Grok Bot's KeepShortcuts value (its installer keeps the Start menu shortcut over updates only when it is
    /// "true"), and the key it came from. Uses the known install key, else finds Grok Bot in the per-user uninstall list.
    /// </summary>
    public static (string? Value, string? Key) ReadKeepShortcuts()
    {
        foreach (var guid in InstallGuids())
        {
            var path = @"Software\" + guid;
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null) continue;
            return (key.GetValue("KeepShortcuts")?.ToString(), @"HKCU\" + path);
        }
        return (null, null);
    }

    private static IEnumerable<string> InstallGuids()
    {
        yield return KnownInstallGuid;
        using var uninstall = Registry.CurrentUser.OpenSubKey(UninstallRoot);
        if (uninstall is null) yield break;
        foreach (var name in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(name);
            if (name != KnownInstallGuid && entry?.GetValue("DisplayName") is string display
                && display.StartsWith("Grok Bot", StringComparison.OrdinalIgnoreCase))
                yield return name;
        }
    }

    /// <summary><c>--grok-bot-start-link-status</c>: prints the report as JSON. Reads only.</summary>
    public static string StatusJson(string grokExecutable, string wingmanDataRoot)
    {
        var (value, key) = ReadKeepShortcuts();
        var report = For(grokExecutable, wingmanDataRoot).Report(
            GrokBotStartLinkOptIn.Load(GrokBotStartLinkOptIn.PathIn(wingmanDataRoot)),
            AppRuntimePortFile.Read(GrokBotLaunchPolicy.RuntimePath(wingmanDataRoot)), value, key);
        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });
    }

    /// <summary>
    /// <c>--restore-grok-bot-start-link</c>: removes exactly the pair Wingman recorded adding to Grok Bot's shortcut and
    /// switches the opt-in off. Runs on its own, without a running Wingman. The shortcut works either way; this tidies up.
    /// </summary>
    public static GrokBotStartLinkRestore RestoreForUninstall(string grokExecutable, string wingmanDataRoot)
    {
        var path = GrokBotStartLinkOptIn.PathIn(wingmanDataRoot);
        // Off and the record cleared only once the pair is verified gone; a failed restore keeps both.
        var result = GrokBotStartLinkOptIn.Locked(path, () =>
            For(grokExecutable, wingmanDataRoot).Restore(GrokBotStartLinkOptIn.Load(path), off => GrokBotStartLinkOptIn.Save(path, off)));
        WingmanLog.Write("grok-start-link-restored", ("reason", "command"), ("outcome", result.Kind), ("leftAlone", result.LeftAlone.Count));
        return result;
    }
}
