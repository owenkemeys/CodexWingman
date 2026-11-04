using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>Windows facts for <see cref="GrokBotInstall.Resolve"/>: read only, each looked up only when the one before found nothing.</summary>
internal static class GrokBotInstallWindows
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static GrokBotInstallResult Resolve() => GrokBotInstall.Resolve(new(
        RunningImagePaths(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProgramFilesFolders(),
        UninstallEntries(),
        StartMenuTargets(),
        File.Exists));

    /// <summary>Image paths of Grok Bot.exe processes in this Windows session.</summary>
    public static IEnumerable<string?> RunningImagePaths()
    {
        var session = WindowProcessInfo.CurrentSession();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GrokBotInstall.ExecutableName)))
        {
            string? image = null;
            using (process)
            {
                try { if (process.SessionId == session) image = WindowProcessInfo.ImagePathOf((uint)process.Id); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (image is not null) yield return image;
        }
    }

    private static IEnumerable<string?> ProgramFilesFolders()
    {
        yield return Environment.GetEnvironmentVariable("ProgramW6432");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static IEnumerable<GrokBotUninstallEntry> UninstallEntries()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var entries = new List<GrokBotUninstallEntry>();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallRoot);
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string display
                        && display.StartsWith(GrokBotInstall.FolderName, StringComparison.OrdinalIgnoreCase))
                        entries.Add(new(entry.GetValue("DisplayIcon") as string, entry.GetValue("InstallLocation") as string));
                }
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            foreach (var entry in entries) yield return entry;
        }
    }

    private static IEnumerable<string?> StartMenuTargets()
    {
        var files = new ShellLinkFiles();
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var link = Path.Combine(Environment.GetFolderPath(folder), GrokBotStartLink.FileName);
            yield return files.Read(link)?.Target;
        }
    }

    /// <summary><c>--grok-bot-install-status</c>: where Grok Bot was found and which running Grok Bot.exe processes match it. Reads only.</summary>
    public static string StatusJson()
    {
        var result = Resolve();
        var session = WindowProcessInfo.CurrentSession();
        var running = new List<object>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GrokBotInstall.ExecutableName)))
        {
            using (process)
            {
                string? image = null;
                int? processSession = null;
                try { processSession = process.SessionId; image = WindowProcessInfo.ImagePathOf((uint)process.Id); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                running.Add(new
                {
                    pid = process.Id,
                    thisSession = processSession == session,
                    recognised = processSession == session && GrokBotInstall.SameExecutable(image, result.Executable),
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            executable = result.Executable,
            source = result.Source.ToString(),
            found = result.Found,
            exists = File.Exists(result.Executable),
            running,
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
