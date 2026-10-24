using System.Runtime.InteropServices;

namespace CodexWingman;

/// <summary>
/// Opt-in, off by default: points the user's existing Grok Bot taskbar pin at Wingman, so clicking the pin
/// opens Grok Bot through Wingman (with the dial). The pin keeps Grok Bot's icon and AppUserModelID,
/// so Grok Bot's window still groups with it. The original pin is backed up first and can be restored.
/// Wingman never adds a pin that was not already there.
/// </summary>
internal static class GrokBotPinRetarget
{
    public static string PinnedFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    public static string BackupFolder(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "backups", "taskbar-pins");

    /// <summary>The pin that currently opens Grok Bot directly or through Wingman, if any.</summary>
    public static (string Path, bool ThroughWingman)? FindPin(string grokExecutable, string wingmanExecutable)
    {
        var folder = PinnedFolder();
        if (!Directory.Exists(folder)) return null;
        foreach (var path in Directory.EnumerateFiles(folder, "*.lnk"))
        {
            var (target, arguments) = Read(path);
            if (SamePath(target, grokExecutable)) return (path, false);
            if (SamePath(target, wingmanExecutable) && arguments.Contains("--launch=grok-bot", StringComparison.Ordinal))
                return (path, true);
        }
        return null;
    }

    public static string Apply(string grokExecutable, string wingmanExecutable, string wingmanDataRoot, string arguments)
    {
        var pin = FindPin(grokExecutable, wingmanExecutable)
            ?? throw new FileNotFoundException("No Grok Bot taskbar pin was found. Wingman does not add pins; pin Grok Bot first if you want this.");
        if (pin.ThroughWingman) return pin.Path;
        var backups = BackupFolder(wingmanDataRoot);
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, DateTime.Now.ToString("yyyyMMdd-HHmmss") + " " + Path.GetFileName(pin.Path));
        File.Copy(pin.Path, backup, overwrite: false);
        Edit(pin.Path, wingmanExecutable, arguments, Path.GetDirectoryName(wingmanExecutable)!, grokExecutable + ",0");
        return backup;
    }

    public static bool Restore(string grokExecutable, string wingmanExecutable, string wingmanDataRoot)
    {
        var pin = FindPin(grokExecutable, wingmanExecutable);
        if (pin is not { ThroughWingman: true }) return false;
        var name = Path.GetFileName(pin.Value.Path);
        var backup = Directory.Exists(BackupFolder(wingmanDataRoot))
            ? Directory.EnumerateFiles(BackupFolder(wingmanDataRoot), "* " + name).OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (backup is not null)
        {
            File.Copy(backup, pin.Value.Path, overwrite: true);
            return true;
        }
        // No backup left: point the pin straight back at Grok Bot.
        Edit(pin.Value.Path, grokExecutable, string.Empty, Path.GetDirectoryName(grokExecutable)!, grokExecutable + ",0");
        return true;
    }

    private static bool SamePath(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static (string? Target, string Arguments) Read(string path)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new NotSupportedException("Windows shortcut services are unavailable."))!;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            dynamic readable = shortcut;
            return ((string?)readable.TargetPath, (string?)readable.Arguments ?? string.Empty);
        }
        catch (COMException) { return (null, string.Empty); }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    // Changes target, arguments, folder and icon only; the pin's AppUserModelID property is left as it is.
    private static void Edit(string path, string target, string arguments, string workingDirectory, string icon)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new NotSupportedException("Windows shortcut services are unavailable."))!;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            dynamic writable = shortcut;
            writable.TargetPath = target;
            writable.Arguments = arguments;
            writable.WorkingDirectory = workingDirectory;
            writable.IconLocation = icon;
            writable.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
