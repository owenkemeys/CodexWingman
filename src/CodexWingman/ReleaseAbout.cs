using System.Reflection;
using System.Text.Json;

namespace CodexWingman;

internal static class ReleaseAbout
{
    public static string TextFor(Assembly assembly)
    {
        var version = assembly.GetName().Version ?? throw new InvalidOperationException("Wingman has no assembly version.");
        var installedVersion = $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        using var stream = assembly.GetManifestResourceStream("Wingman.ReleaseContent.json")
            ?? throw new InvalidOperationException("Wingman release content is missing.");
        using var document = JsonDocument.Parse(stream);
        var releaseVersion = document.RootElement.GetProperty("version").GetString();
        if (!string.Equals(releaseVersion, installedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Wingman release content does not match the installed version.");
        var highlights = document.RootElement.GetProperty("highlights").EnumerateArray()
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(5)
            .Select(item => $"• {item}");
        return $"Wingman v{installedVersion}\n\n" + string.Join("\n", highlights);
    }
}
