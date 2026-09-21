using System.Text.Json;

namespace CodexWingman.Core;

public sealed record HelperSettings(
    bool UsageDialsEnabled,
    IReadOnlyList<string> AdditionalSessionRoots,
    IReadOnlyDictionary<string, bool>? HelperEnabled = null,
    bool ForceHighPerformanceGpu = false,
    IReadOnlyDictionary<string, JsonElement>? HelperConfig = null)
{
    public static HelperSettings Default { get; } = new(
        true,
        [],
        new Dictionary<string, bool>(StringComparer.Ordinal) { ["json-debug"] = false });

    public bool IsHelperEnabled(string helperId, bool defaultEnabled = true)
    {
        if (HelperEnabled is not null && HelperEnabled.TryGetValue(helperId, out var enabled))
            return enabled;
        return helperId.Equals("usage-dials", StringComparison.Ordinal)
            ? UsageDialsEnabled
            : defaultEnabled;
    }

    public JsonElement? ConfigFor(string helperId, JsonElement? fallback) =>
        HelperConfig is not null && HelperConfig.TryGetValue(helperId, out var config)
            ? config.ValueKind == JsonValueKind.Object ? config.Clone() : JsonSerializer.SerializeToElement(new { })
            : fallback;

    public HelperSettings WithHelperEnabled(string helperId, bool enabled)
    {
        var values = new Dictionary<string, bool>(HelperEnabled ?? new Dictionary<string, bool>(), StringComparer.Ordinal)
        {
            [helperId] = enabled,
        };
        return this with
        {
            UsageDialsEnabled = helperId.Equals("usage-dials", StringComparison.Ordinal) ? enabled : UsageDialsEnabled,
            HelperEnabled = values,
        };
    }

    public IReadOnlyList<string> ComposeSessionRoots(string? userProfile = null) =>
        SessionQuotaSource.DefaultRoots(userProfile)
            .Concat(AdditionalSessionRoots ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public static class HelperSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string DefaultPath(string? localAppData = null)
    {
        localAppData ??= Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CodexWingman", "settings.json");
    }

    public static InitializedHelperSettings InitializeDefault(string? localAppData = null)
    {
        var path = DefaultPath(localAppData);
        if (File.Exists(path)) return new InitializedHelperSettings(path, Load(path));

        var root = Path.GetDirectoryName(Path.GetDirectoryName(path));
        if (!string.IsNullOrWhiteSpace(root))
        {
            var legacyPath = Path.Combine(root, "CodexHelper", "settings.json");
            if (File.Exists(legacyPath))
            {
                var migrated = Load(legacyPath);
                Save(path, migrated);
                return new InitializedHelperSettings(path, migrated);
            }
        }

        return new InitializedHelperSettings(path, HelperSettings.Default);
    }

    public static HelperSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path)) return HelperSettings.Default;
            var value = JsonSerializer.Deserialize<HelperSettings>(File.ReadAllText(path), JsonOptions);
            return value is null
                ? HelperSettings.Default
                : new HelperSettings(
                    value.UsageDialsEnabled,
                    value.AdditionalSessionRoots ?? [],
                    new Dictionary<string, bool>(value.HelperEnabled ?? new Dictionary<string, bool>(), StringComparer.Ordinal),
                    value.ForceHighPerformanceGpu,
                    value.HelperConfig);
        }
        catch (JsonException) { return HelperSettings.Default; }
        catch (IOException) { return HelperSettings.Default; }
        catch (UnauthorizedAccessException) { return HelperSettings.Default; }
    }

    public static void Save(string path, HelperSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Settings path must include a directory", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed record InitializedHelperSettings(string Path, HelperSettings Settings);
