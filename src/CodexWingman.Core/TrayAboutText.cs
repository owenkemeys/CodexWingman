namespace CodexWingman.Core;

public static class TrayAboutText
{
    public static string For(Version version) => $"About Wingman v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
