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

    /// <summary>
    /// The debugging port a Grok Bot command line asks for, when it listens where Wingman connects: 127.0.0.1, either named
    /// or as Chromium's default with no address switch. Null for no port, an invalid one, or any other address (including
    /// <c>localhost</c> and <c>::1</c>, which Wingman's 127.0.0.1 connection may not reach). Switches are read the way
    /// Chromium reads them on Windows: "--name", "-name" and "/name" alike, names in any case.
    /// </summary>
    public static int? DebugPortFrom(string? commandLine)
    {
        if (commandLine is null) return null;
        int? port = null;
        foreach (var argument in GrokBotReconnectPolicy.SplitCommandLine(commandLine).Skip(1))
        {
            if (argument == "--") break; // Chromium reads no switches after a bare "--"
            switch (ChromiumSwitch.Parse(argument))
            {
                case ("remote-debugging-address", var address) when !WingmanConnects(address):
                    return null;
                case ("remote-debugging-port", var value):
                    port = int.TryParse(value, out var number) && number is >= 1024 and <= 65535 ? number : null;
                    break;
            }
        }
        return port;
    }

    /// <summary>
    /// Wingman connects to Grok Bot only at <c>http://127.0.0.1:&lt;port&gt;</c>: the one address switch value it adopts
    /// (with no address switch at all, Chromium listens there by default).
    /// </summary>
    public static bool WingmanConnects(string? address) => address is "127.0.0.1";

    /// <summary>
    /// Whether a listening socket is where Wingman connects: IPv4 127.0.0.1 only, the same loopback-only rule as adoption
    /// (<see cref="WingmanConnects"/>). A socket on all addresses (<c>0.0.0.0</c>) or IPv6 (<c>::1</c>, <c>::</c>) never
    /// counts as connected or adoptable.
    /// </summary>
    public static bool ReachableByWingman(System.Net.IPEndPoint listener, int port) =>
        listener.Port == port && listener.Address.Equals(System.Net.IPAddress.Loopback);

    /// <summary>
    /// Whether Grok Bot is connected for Wingman: something listens on 127.0.0.1 (see <see cref="ReachableByWingman"/>)
    /// at its recorded <paramref name="port"/> and the listening socket belongs to that Grok Bot process, the same terms as
    /// adoption. Another program on the port is not Grok Bot.
    /// </summary>
    public static bool Connected(int? port, int? listenerProcess, int? grokProcess) =>
        port is not null && grokProcess is { } grok && listenerProcess == grok;

    /// <summary>
    /// Whether Wingman may take <paramref name="port"/> (from Grok Bot's own command line) as Grok Bot's port: never one of
    /// the ports Wingman keeps for Codex or T3 (their preferred ports and <paramref name="otherAppPorts"/> in use now), and
    /// only once the port is verified listening and owned by that Grok Bot process (<paramref name="listenerProcess"/>
    /// is the listening socket's owner, null when nothing listens there or Windows can't tell).
    /// </summary>
    public static bool Adoptable(int? port, IEnumerable<int> otherAppPorts, int? listenerProcess, int grokProcess) =>
        port is { } value
        && value != CodexLaunchPolicy.PreferredPort && value != T3CodeLaunchPolicy.PreferredPort && value != T3WebLaunchPolicy.PreferredPort
        && !otherAppPorts.Contains(value)
        && listenerProcess == grokProcess;

    /// <summary>
    /// Whether a Grok Bot.exe command line is its main (browser) process: Chromium's helper processes (renderer, GPU and
    /// the like, and in 0.78.1 its local-exec daemon, a <c>--type=utility</c> child) carry a <c>--type=</c> switch before any
    /// bare <c>--</c>. Grok Bot.exe running a script as Node (<c>ELECTRON_RUN_AS_NODE</c>, as an unpackaged Grok Bot starts
    /// its box-computer server; a packaged 0.78.1 doesn't) names a script file there. Unreadable (null): not known to be the main one.
    /// </summary>
    public static bool IsMainProcess(string? commandLine)
    {
        if (commandLine is null) return false;
        foreach (var argument in GrokBotReconnectPolicy.SplitCommandLine(commandLine).Skip(1))
        {
            if (argument == "--") break;
            if (ChromiumSwitch.Parse(argument) is ("type", _)) return false;
            if (ChromiumSwitch.Parse(argument) is null && IsScript(argument)) return false;
        }
        return true;
    }

    private static readonly string[] ScriptExtensions = [".js", ".cjs", ".mjs", ".ts", ".cts", ".mts"];

    private static bool IsScript(string argument) =>
        ScriptExtensions.Any(extension => argument.TrimEnd().EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Grok Bot's main process among <paramref name="candidates"/> (each already <see cref="IsMainProcess"/>): the one that
    /// owns Grok Bot's single-instance window (<paramref name="singleInstanceOwners"/>) if any does, else the earliest started.
    /// </summary>
    public static GrokBotProcess? ChooseMainProcess(IEnumerable<GrokBotProcess> candidates, IReadOnlySet<int> singleInstanceOwners) =>
        candidates
            .OrderBy(candidate => singleInstanceOwners.Contains(candidate.Id) ? 0 : 1)
            .ThenBy(candidate => candidate.Started ?? DateTime.MaxValue)
            .FirstOrDefault();

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

/// <summary>A Chromium switch as Chromium reads it on Windows: "--name", "-name" or "/name", the name in any case.</summary>
public static class ChromiumSwitch
{
    /// <summary>The lower-case name and the value after '=' (null when there is none), or null for a plain argument.</summary>
    public static (string Name, string? Value)? Parse(string argument)
    {
        var text = argument.Replace("\"", string.Empty);
        var body = text.StartsWith("--", StringComparison.Ordinal) ? text[2..]
            : text.StartsWith('-') || text.StartsWith('/') ? text[1..]
            : null;
        if (string.IsNullOrEmpty(body)) return null;
        var equals = body.IndexOf('=');
        return equals < 0 ? (body.ToLowerInvariant(), null) : (body[..equals].ToLowerInvariant(), body[(equals + 1)..]);
    }

    /// <summary>Any remote debugging switch (port, address, pipe and the like).</summary>
    public static bool IsRemoteDebugging(string argument) =>
        Parse(argument) is { Name: var name } && name.StartsWith("remote-debugging-", StringComparison.Ordinal);

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

public sealed class GrokBotTargetSource(HttpClient httpClient, Func<int?> portProvider, CdpConnectionPool? pool = null) : ICodexTargetSource
{
    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var port = portProvider();
        if (port is null) return Task.FromResult<IReadOnlyList<CodexTarget>>([]);
        if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid Grok Bot debugging port.");
        return new HttpCodexTargetSource(
            httpClient, () => port.Value, () => new ClientWebSocketCdpSocket(),
            GrokBotTargetCatalog.SelectPages, pool).ListAsync(cancellationToken);
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
                    // Only the key line is read; another slice's body is never loaded.
                    if (ReadKblobUsageBody(file) is not { } body) continue;
                    value = body;
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

    /// <summary>Longest key line accepted in a <c>.kblob</c>.</summary>
    public const int MaxKeyLineBytes = 1024;
    private static int bodyReads;

    /// <summary>How many cache bodies this process has read (diagnostics and tests).</summary>
    public static int BodyReads => Volatile.Read(ref bodyReads);

    /// <summary>
    /// Reads the first line (the JSON key, at most <see cref="MaxKeyLineBytes"/>) of a <c>.kblob</c>.
    /// Null when the line is missing, too long or not a JSON string.
    /// </summary>
    public static string? ReadKblobKey(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadKeyLine(stream);
    }

    private static string? ReadKeyLine(Stream stream)
    {
        var line = new List<byte>(128);
        for (var count = 0; count <= MaxKeyLineBytes; count++)
        {
            var next = stream.ReadByte();
            if (next < 0) return null;
            if (next == '\n')
            {
                try { return JsonSerializer.Deserialize<string>(new UTF8Encoding(false, true).GetString(line.ToArray())); }
                catch (Exception error) when (error is JsonException or DecoderFallbackException or ArgumentException) { return null; }
            }
            line.Add((byte)next);
        }
        return null;
    }

    /// <summary>The body of a <c>.kblob</c> whose key line is the weekly-usage key; otherwise null without reading the body.</summary>
    public static string? ReadKblobUsageBody(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes + MaxKeyLineBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!IsUsageKey(ReadKeyLine(stream))) return null;
        Interlocked.Increment(ref bodyReads);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var buffer = new char[MaxBytes + 1];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return read > MaxBytes ? null : new string(buffer, 0, read);
    }

    private static string? ReadCapped(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Interlocked.Increment(ref bodyReads);
        return reader.ReadToEnd();
    }
}
