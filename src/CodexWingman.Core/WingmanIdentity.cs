namespace CodexWingman.Core;

public static class WingmanIdentity
{
    public const string PackageDirectoryName = "CodexWingman-verified";
    public const string HelpersDirectoryName = "Helpers";
    public const string SingleInstanceName = "CodexWingman.Desktop.Singleton.v1";
    public const string ActivationEventName = "CodexWingman.Desktop.Activate.v1";
    public const string AppUserModelId = "CodexWingman.Desktop.v1";
    public const string RunningMenuText = "Codex Wingman";
    public const string CloseMenuText = "Close Wingman (leave Codex open)";

    public static string ResolveHelpersRoot(string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new ArgumentException("Executable path has no directory.", nameof(executablePath));
        return Path.GetFullPath(Path.Combine(executableDirectory, HelpersDirectoryName));
    }

    public static bool IsCanonicalPackageDirectory(string packageDirectory) =>
        string.Equals(
            Path.GetFileName(Path.GetFullPath(packageDirectory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)),
            PackageDirectoryName,
            StringComparison.OrdinalIgnoreCase);
}
