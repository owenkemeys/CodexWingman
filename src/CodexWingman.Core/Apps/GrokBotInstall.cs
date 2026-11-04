namespace CodexWingman.Core;

/// <summary>Where Grok Bot's program was found, in the order <see cref="GrokBotInstall.Resolve"/> looks.</summary>
public enum GrokBotInstallSource { Running, PerUser, PerMachine, Uninstall, StartMenu, NotFound }

/// <summary>A Grok Bot uninstall entry's two path values (either may be missing).</summary>
public sealed record GrokBotUninstallEntry(string? DisplayIcon, string? InstallLocation);

/// <summary>What the PC says about Grok Bot, gathered by the Windows side; every list is in its look-up order.</summary>
public sealed record GrokBotInstallFacts(
    IEnumerable<string?> RunningImagePaths,
    string LocalAppData,
    IEnumerable<string?> ProgramFilesFolders,
    IEnumerable<GrokBotUninstallEntry> UninstallEntries,
    IEnumerable<string?> StartMenuTargets,
    Func<string, bool> FileExists);

public sealed record GrokBotInstallResult(string Executable, GrokBotInstallSource Source)
{
    public bool Found => Source != GrokBotInstallSource.NotFound;
}

/// <summary>
/// Finds Grok Bot.exe wherever it is installed: per user (%LOCALAPPDATA%\Programs) or per machine (Program Files), so
/// nothing about one PC's install is assumed. Order: the image of a running Grok Bot, the per-user folder, the
/// per-machine folder, the uninstall entries (per user first), then the Start menu shortcut. Nothing found: the usual
/// per-user path, so "not installed" reads as before.
/// </summary>
public static class GrokBotInstall
{
    public const string FolderName = "Grok Bot";
    public const string ExecutableName = "Grok Bot.exe";

    public static GrokBotInstallResult Resolve(GrokBotInstallFacts facts)
    {
        foreach (var path in facts.RunningImagePaths)
            if (Usable(path, facts.FileExists) is { } running) return new(running, GrokBotInstallSource.Running);
        if (Usable(GrokBotLaunchPolicy.ExecutablePath(facts.LocalAppData), facts.FileExists) is { } perUser)
            return new(perUser, GrokBotInstallSource.PerUser);
        foreach (var folder in facts.ProgramFilesFolders)
            if (!string.IsNullOrWhiteSpace(folder)
                && Usable(Path.Combine(folder, FolderName, ExecutableName), facts.FileExists) is { } perMachine)
                return new(perMachine, GrokBotInstallSource.PerMachine);
        foreach (var entry in facts.UninstallEntries)
        {
            if (Usable(IconPath(entry.DisplayIcon), facts.FileExists) is { } icon) return new(icon, GrokBotInstallSource.Uninstall);
            if (!string.IsNullOrWhiteSpace(entry.InstallLocation)
                && Usable(Path.Combine(entry.InstallLocation.Trim().Trim('"'), ExecutableName), facts.FileExists) is { } located)
                return new(located, GrokBotInstallSource.Uninstall);
        }
        foreach (var target in facts.StartMenuTargets)
            if (Usable(target, facts.FileExists) is { } linked) return new(linked, GrokBotInstallSource.StartMenu);
        return new(GrokBotLaunchPolicy.ExecutablePath(facts.LocalAppData), GrokBotInstallSource.NotFound);
    }

    /// <summary>DisplayIcon as installers write it: a path, maybe quoted, maybe followed by ",&lt;icon index&gt;".</summary>
    public static string? IconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var text = displayIcon.Trim();
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : null;
        }
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _)) text = text[..comma].TrimEnd();
        return text;
    }

    /// <summary>Whether <paramref name="processImage"/> is the resolved Grok Bot program (full path, any case).</summary>
    public static bool SameExecutable(string? processImage, string executable)
    {
        if (string.IsNullOrWhiteSpace(processImage) || string.IsNullOrWhiteSpace(executable)) return false;
        try { return string.Equals(Path.GetFullPath(processImage), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string? Usable(string? path, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Trim('"');
        if (!GrokBotLaunchPolicy.IsGrokBotExecutable(text)) return null;
        try { text = Path.GetFullPath(text); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        return exists(text) ? text : null;
    }
}
