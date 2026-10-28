using System.Text.Json;

namespace CodexWingman.Core;

public sealed record HelperSettings(
    bool UsageDialsEnabled,
    IReadOnlyList<string> AdditionalSessionRoots,
    IReadOnlyDictionary<string, bool>? HelperEnabled = null,
    bool ForceHighPerformanceGpu = false,
    IReadOnlyDictionary<string, JsonElement>? HelperConfig = null,
    bool OpenCodexOnLaunch = true,
    bool OpenT3CodeOnLaunch = false,
    T3WebSettings? T3Web = null,
    bool OpenGrokBotOnLaunch = false)
{
    public static HelperSettings Default { get; } = new(
        true,
        [],
        new Dictionary<string, bool>(StringComparer.Ordinal) { ["json-debug"] = false });

    /// <summary>
    /// Used when settings.json exists but can't be read and no earlier reading is known: the defaults, except that
    /// Wingman opens no app on its own. An unreadable file must never turn an app's Open when Wingman starts on.
    /// </summary>
    public static HelperSettings UnreadableFallback { get; } = Default with
    {
        OpenCodexOnLaunch = false,
        OpenT3CodeOnLaunch = false,
        OpenGrokBotOnLaunch = false,
    };

    /// <summary>The tray's "Open on launch" choice for one app.</summary>
    public bool OpensOnLaunch(WingmanLaunchTarget app) => app switch
    {
        WingmanLaunchTarget.Codex => OpenCodexOnLaunch,
        WingmanLaunchTarget.T3Code => OpenT3CodeOnLaunch,
        WingmanLaunchTarget.T3Web => T3Web?.OpenOnLaunch == true,
        WingmanLaunchTarget.GrokBot => OpenGrokBotOnLaunch,
        _ => false,
    };

    public HelperSettings WithOpenOnLaunch(WingmanLaunchTarget app, bool value) => app switch
    {
        WingmanLaunchTarget.Codex => this with { OpenCodexOnLaunch = value },
        WingmanLaunchTarget.T3Code => this with { OpenT3CodeOnLaunch = value },
        WingmanLaunchTarget.T3Web => this with { T3Web = (T3Web ?? new T3WebSettings()) with { OpenOnLaunch = value } },
        WingmanLaunchTarget.GrokBot => this with { OpenGrokBotOnLaunch = value },
        _ => throw new ArgumentOutOfRangeException(nameof(app), "Only an app has an Open on launch choice."),
    };

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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HelperSettings> LastGood =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Changes made while the file could not be read: kept in memory only, never written over the file.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HelperSettings> Held =
        new(StringComparer.OrdinalIgnoreCase);

    private enum FileState { Missing, Read, Unreadable }

    /// <summary>
    /// Reads settings. A missing file gives the defaults. A file that exists but can't be read or parsed gives a change
    /// held in memory since then, else the last settings this process read or saved for that path, else
    /// <see cref="HelperSettings.UnreadableFallback"/>.
    /// </summary>
    public static HelperSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        var (state, value, reason) = Read(path);
        return state switch
        {
            FileState.Missing => HelperSettings.Default,
            FileState.Read => value!,
            _ => Unreadable(path, reason),
        };
    }

    /// <summary>
    /// Changes settings. The file is read again first, so the change applies to what is on disk now. If the file
    /// exists but still can't be read, it is not overwritten: the change is kept in memory (and returned by
    /// <see cref="Load"/> while the file stays unreadable) and logged.
    /// </summary>
    public static SettingsUpdate Update(string path, Func<HelperSettings, HelperSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var (state, value, reason) = Read(path);
        if (state == FileState.Unreadable)
        {
            var held = change(Unreadable(path, reason));
            Held[Path.GetFullPath(path)] = held;
            WingmanLog.Write("settings-change-held", ("reason", reason));
            return new(held, Saved: false);
        }
        var next = change(state == FileState.Read ? value! : HelperSettings.Default);
        return new(next, Save(path, next));
    }

    private static (FileState State, HelperSettings? Value, string Reason) Read(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(path)) return (FileState.Missing, null, "missing");
            var value = Parse(File.ReadAllText(path));
            if (value is null) return (FileState.Unreadable, null, "empty");
            LastGood[full] = value;
            // The file is readable again, so it wins over a change that could only be kept in memory.
            if (Held.TryRemove(full, out _)) WingmanLog.Write("settings-held-dropped", ("reason", "file-readable"));
            return (FileState.Read, value, "read");
        }
        catch (JsonException) { return (FileState.Unreadable, null, nameof(JsonException)); }
        catch (IOException) { return (FileState.Unreadable, null, nameof(IOException)); }
        catch (UnauthorizedAccessException) { return (FileState.Unreadable, null, nameof(UnauthorizedAccessException)); }
    }

    private static HelperSettings Unreadable(string path, string reason)
    {
        var full = Path.GetFullPath(path);
        if (Held.TryGetValue(full, out var held))
        {
            WingmanLog.Write("settings-unreadable", ("reason", reason), ("using", "held-change"));
            return held;
        }
        var known = LastGood.TryGetValue(full, out var lastGood);
        WingmanLog.Write("settings-unreadable", ("reason", reason), ("using", known ? "last-good" : "safe-defaults"));
        return known ? lastGood! : HelperSettings.UnreadableFallback;
    }

    private static HelperSettings? Parse(string json)
    {
        var value = JsonSerializer.Deserialize<HelperSettings>(json, JsonOptions);
        return value is null
                ? null
                : new HelperSettings(
                    value.UsageDialsEnabled,
                    value.AdditionalSessionRoots ?? [],
                    new Dictionary<string, bool>(value.HelperEnabled ?? new Dictionary<string, bool>(), StringComparer.Ordinal),
                    value.ForceHighPerformanceGpu,
                    value.HelperConfig,
                    value.OpenCodexOnLaunch,
                    value.OpenT3CodeOnLaunch,
                    value.T3Web,
                    value.OpenGrokBotOnLaunch);
    }

    /// <summary>
    /// Writes settings, unless the file exists but can't be read: then nothing is written (the value is kept in memory
    /// and logged), so a value built from a fallback never replaces the user's file. Returns whether it was written.
    /// </summary>
    public static bool Save(string path, HelperSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Settings path must include a directory", nameof(path));
        var (state, _, reason) = Read(path);
        if (state == FileState.Unreadable)
        {
            Held[Path.GetFullPath(path)] = settings;
            WingmanLog.Write("settings-change-held", ("reason", reason));
            return false;
        }
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, path, overwrite: true);
            LastGood[Path.GetFullPath(path)] = settings;
            Held.TryRemove(Path.GetFullPath(path), out _);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>The settings after a change, and whether they were written (false: kept in memory, file unreadable).</summary>
public sealed record SettingsUpdate(HelperSettings Value, bool Saved);

/// <summary>
/// Local, per-PC T3 web settings. The URL and the pairing folder are private machine
/// configuration: they live only in settings.json, never in the public source.
/// </summary>
public sealed record T3WebSettings(
    string? Url = null,
    T3PairingSettings? Pairing = null,
    bool OpenOnLaunch = false,
    string? ChromePath = null)
{
    public bool IsConfigured => T3WebUrlPolicy.TryNormalizeOrigin(Url, out _) || Pairing?.IsConfigured == true;
}

/// <summary>Which pluggable provider mints a pairing link, and where it looks.</summary>
public sealed record T3PairingSettings(
    string? Provider = null,
    string? Folder = null,
    string? Label = null)
{
    public const string SharedFolderProvider = "shared-folder";
    public bool IsConfigured => string.Equals(Provider ?? SharedFolderProvider, SharedFolderProvider, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Folder);
}

public sealed record InitializedHelperSettings(string Path, HelperSettings Settings);
