using System.Text.Json;

namespace CodexWingman.Core;

/// <summary>
/// A development package carries <c>development.json</c> (channel "development") beside the executable and no
/// <c>release.json</c>. Test hooks such as forcing a T3 web renewal exist only there, never in stable releases.
/// </summary>
public static class DevelopmentChannel
{
    public static bool IsDevelopmentBuild(string applicationDirectory)
    {
        try
        {
            if (File.Exists(Path.Combine(applicationDirectory, "release.json"))) return false;
            var path = Path.Combine(applicationDirectory, "development.json");
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("schema", out var schema) && schema.GetString() == "wingman.development-package.v1"
                && root.TryGetProperty("channel", out var channel) && channel.GetString() == "development";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
}
