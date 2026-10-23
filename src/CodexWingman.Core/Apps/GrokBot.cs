using System.Text;
using System.Text.Json;

namespace CodexWingman.Core;

public static class GrokBotLaunchPolicy
{
    public const int PreferredPort = 9523;
    public const string RendererSuffix = "/resources/app.asar/dist/renderer/index.html";

    public static string ExecutablePath(string localAppData) =>
        Path.Combine(localAppData, "Programs", "Grok Bot", "Grok Bot.exe");

    public static string UserDataDirectory(string appData) => Path.Combine(appData, "Grok Bot");

    public static string RuntimePath(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "grok-runtime.json");

    /// <summary>Only the debugging flags are added: Grok Bot keeps its own user data, so its sign-in is untouched.</summary>
    public static IReadOnlyList<string> Arguments(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        return ["--remote-debugging-address=127.0.0.1", "--remote-debugging-port=" + port];
    }

    public static bool IsGrokBotExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.GetFileName(path.Replace('/', '\\').Split('\\')[^1]).Equals("Grok Bot.exe", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<int> CandidatePorts(int? remembered, IEnumerable<int> reserved)
    {
        var blocked = reserved.ToHashSet();
        blocked.Add(CodexLaunchPolicy.PreferredPort);
        blocked.Add(T3CodeLaunchPolicy.PreferredPort);
        blocked.Add(T3WebLaunchPolicy.PreferredPort);
        if (remembered is >= 1024 and <= 65535 && !blocked.Contains(remembered.Value)) yield return remembered.Value;
        for (var port = PreferredPort; port < PreferredPort + 50; port++)
            if (port != remembered && !blocked.Contains(port)) yield return port;
    }
}

public static class GrokBotTargetCatalog
{
    /// <summary>The Grok Bot main window is its packaged renderer page; webviews and other schemes are skipped.</summary>
    public static IReadOnlyList<CodexTarget> SelectPages(IEnumerable<CodexTarget> targets) => targets
        .Where(target => target.Type == "page"
            && Uri.TryCreate(target.Url, UriKind.Absolute, out var uri)
            && uri.IsFile
            && Uri.UnescapeDataString(uri.AbsolutePath).Replace('\\', '/')
                .EndsWith(GrokBotLaunchPolicy.RendererSuffix, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        .DistinctBy(target => target.Id)
        .ToArray();
}

public sealed class GrokBotTargetSource(HttpClient httpClient, Func<int?> portProvider) : ICodexTargetSource
{
    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var port = portProvider();
        if (port is null) return Task.FromResult<IReadOnlyList<CodexTarget>>([]);
        if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid Grok Bot debugging port.");
        return new HttpCodexTargetSource(
            httpClient, () => port.Value, () => new ClientWebSocketCdpSocket(),
            GrokBotTargetCatalog.SelectPages).ListAsync(cancellationToken);
    }
}

/// <summary>The only Grok Bot usage fields Wingman passes to a Helper. No account ids, plan names or credentials.</summary>
public sealed record GrokBotUsageReading(double PercentUsed, long? NextResetMs, long ReadAtMs, long? ExpiresAtMs);

/// <summary>
/// Reads Grok Bot's own weekly-usage cache from its client persistence folder (read-only).
/// File names are lowercase unpadded RFC 4648 base32 of the key plus <c>.blob</c>; long keys use a
/// digest name plus <c>.kblob</c> whose first line is the JSON-encoded key.
/// </summary>
public static class GrokBotUsageCacheFile
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
    private const string KeyPrefix = "sand.client.slice.account.";
    private const string KeySuffix = ".weekly-usage.cache";

    public static string PersistenceDirectory(string grokUserData) => Path.Combine(grokUserData, "sand-client-persistence");

    public static string EncodeName(string key)
    {
        var builder = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var value in Encoding.UTF8.GetBytes(key))
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    public static string? DecodeName(string encoded)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var character in encoded)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0) return null;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
            buffer &= (1 << bits) - 1;
        }
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (DecoderFallbackException) { return null; }
    }

    public static bool IsUsageKey(string? key) =>
        key is not null && key.StartsWith(KeyPrefix, StringComparison.Ordinal)
        && key.EndsWith(KeySuffix, StringComparison.Ordinal);

    public static GrokBotUsageReading? Read(string grokUserData, DateTimeOffset? now = null)
    {
        var directory = PersistenceDirectory(grokUserData);
        if (!Directory.Exists(directory)) return null;
        GrokBotUsageReading? newest = null;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(directory).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            string? value = null;
            try
            {
                if (name.EndsWith(".blob", StringComparison.Ordinal))
                {
                    if (!IsUsageKey(DecodeName(name[..^5]))) continue;
                    value = ReadCapped(file);
                }
                else if (name.EndsWith(".kblob", StringComparison.Ordinal))
                {
                    var text = ReadCapped(file);
                    var newline = text?.IndexOf('\n') ?? -1;
                    if (text is null || newline < 0) continue;
                    if (!IsUsageKey(JsonSerializer.Deserialize<string>(text[..newline]))) continue;
                    value = text[(newline + 1)..];
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { continue; }
            if (value is null) continue;
            var reading = Parse(value);
            if (reading is not null && (newest is null || reading.ReadAtMs > newest.ReadAtMs)) newest = reading;
        }
        if (newest is null) return null;
        var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        if (current - newest.ReadAtMs > (long)MaxAge.TotalMilliseconds) return null;
        if (newest.ExpiresAtMs is { } expires && current > expires) return null;
        return newest;
    }

    public static GrokBotUsageReading? Parse(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 2) return null;
            if (!root.TryGetProperty("value", out var body) || body.ValueKind != JsonValueKind.Object) return null;
            if (!body.TryGetProperty("kind", out var kind) || kind.GetString() != "present") return null;
            if (!body.TryGetProperty("reading", out var reading) || reading.ValueKind != JsonValueKind.Object) return null;
            if (!reading.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
            if (!usage.TryGetProperty("percentUsed", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used)
                || double.IsNaN(used) || double.IsInfinity(used)) return null;
            if (!reading.TryGetProperty("readAtMs", out var readAt) || readAt.ValueKind != JsonValueKind.Number || !readAt.TryGetInt64(out var readAtMs)) return null;
            long? reset = usage.TryGetProperty("nextResetMs", out var resetValue) && resetValue.ValueKind == JsonValueKind.Number && resetValue.TryGetInt64(out var resetMs) ? resetMs : null;
            long? expires = body.TryGetProperty("expiresAtMs", out var expiresValue) && expiresValue.ValueKind == JsonValueKind.Number && expiresValue.TryGetInt64(out var expiresMs) ? expiresMs : null;
            return new(Math.Clamp(used, 0, 100), reset, readAtMs, expires);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Typed projection handed to the Helper backend.</summary>
    public static object Snapshot(string grokUserData, DateTimeOffset? now = null)
    {
        var reading = Read(grokUserData, now);
        return reading is null
            ? new { available = false }
            : new { available = true, percentUsed = reading.PercentUsed, nextResetMs = reading.NextResetMs, readAtMs = reading.ReadAtMs };
    }

    private static string? ReadCapped(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
