using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CodexWingman.Core;

/// <summary>Origin rules for the T3 web client. Only exact https origins from local settings or a validated pairing link count.</summary>
public static class T3WebUrlPolicy
{
    public static bool TryNormalizeOrigin(string? value, [NotNullWhen(true)] out Uri? origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        origin = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        return true;
    }

    public static bool SameOrigin(Uri candidate, Uri origin) =>
        candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && candidate.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == origin.Port;

    /// <summary>
    /// Validates a T3 pairing link (<c>https://host/pair#token=…</c>) and extracts its one-time credential.
    /// When <paramref name="expectedOrigin"/> is null the link's own origin is accepted.
    /// </summary>
    public static bool TryParsePairingLink(
        string? link,
        Uri? expectedOrigin,
        [NotNullWhen(true)] out Uri? origin,
        [NotNullWhen(true)] out string? credential)
    {
        origin = null;
        credential = null;
        if (string.IsNullOrWhiteSpace(link) || link.Length > 4096
            || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)
            || !TryNormalizeOrigin(uri.GetLeftPart(UriPartial.Authority), out var linkOrigin)
            || (expectedOrigin is not null && !SameOrigin(uri, expectedOrigin))
            || !uri.AbsolutePath.Equals("/pair", StringComparison.Ordinal)
            || !uri.Fragment.StartsWith("#token=", StringComparison.Ordinal))
            return false;
        var token = Uri.UnescapeDataString(uri.Fragment["#token=".Length..]);
        var ampersand = token.IndexOf('&');
        if (ampersand >= 0) token = token[..ampersand];
        if (token.Length is < 8 or > 1024 || token.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return false;
        origin = linkOrigin;
        credential = token;
        return true;
    }
}

public static class T3WebTargetCatalog
{
    public static IReadOnlyList<CodexTarget> SelectPages(IEnumerable<CodexTarget> targets, Uri origin) => targets
        .Where(target => target.Type == "page"
            && Uri.TryCreate(target.Url, UriKind.Absolute, out var uri)
            && T3WebUrlPolicy.SameOrigin(uri, origin)
            && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        .DistinctBy(target => target.Id)
        .ToArray();
}

/// <summary>Reads only T3 web pages (exact configured origin) from Wingman's own Chrome debugging endpoint.</summary>
public sealed class T3WebTargetSource(HttpClient httpClient, Func<int?> portProvider, Func<Uri?> originProvider) : ICodexTargetSource
{
    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var port = portProvider();
        var origin = originProvider();
        if (port is null || origin is null) return Task.FromResult<IReadOnlyList<CodexTarget>>([]);
        if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid T3 web debugging port.");
        return new HttpCodexTargetSource(
            httpClient, () => port.Value, () => new ClientWebSocketCdpSocket(),
            targets => T3WebTargetCatalog.SelectPages(targets, origin)).ListAsync(cancellationToken);
    }
}

public static class T3WebLaunchPolicy
{
    public const int PreferredPort = 9423;

    public static IReadOnlyList<string> CandidateChromeExecutables(string localAppData, string programFiles, string programFilesX86) =>
    [
        Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
    ];

    /// <summary>Wingman's own Chrome data folder. The user's normal Chrome profile is never used or read.</summary>
    public static string DataDirectory(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "t3-web", "chrome-data");

    public static string RuntimePath(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "t3-web-runtime.json");

    public static string PairingStatePath(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "t3-web", "pairing.json");

    public static IReadOnlyList<string> ChromeArguments(string dataDirectory, int port, Uri startUrl)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (!Path.IsPathFullyQualified(dataDirectory)) throw new ArgumentException("Chrome data folder must be absolute.", nameof(dataDirectory));
        if (!startUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("T3 web must open an https URL.", nameof(startUrl));
        return
        [
            "--user-data-dir=" + dataDirectory,
            "--remote-debugging-address=127.0.0.1",
            "--remote-debugging-port=" + port,
            "--no-first-run",
            "--no-default-browser-check",
            "--app=" + startUrl.AbsoluteUri,
        ];
    }

    /// <summary>True when a Chrome command line belongs to Wingman's T3 web data folder.</summary>
    public static bool IsWingmanChromeCommandLine(string? commandLine, string dataDirectory) =>
        !string.IsNullOrEmpty(commandLine)
        && commandLine.Contains("--user-data-dir=" + dataDirectory, StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<int> CandidatePorts(int? remembered, IEnumerable<int> reserved)
    {
        var blocked = reserved.ToHashSet();
        blocked.Add(CodexLaunchPolicy.PreferredPort);
        blocked.Add(T3CodeLaunchPolicy.PreferredPort);
        if (remembered is >= 1024 and <= 65535 && !blocked.Contains(remembered.Value)) yield return remembered.Value;
        for (var port = PreferredPort; port < PreferredPort + 50; port++)
            if (port != remembered && !blocked.Contains(port)) yield return port;
    }
}

/// <summary>Small JSON file that remembers one debugging port, used by T3 web and Grok Bot.</summary>
public static class AppRuntimePortFile
{
    public static int? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("port", out var value) && value.TryGetInt32(out var port)
                && port is >= 1024 and <= 65535 ? port : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void Write(string path, int port)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Path needs a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { schemaVersion = 1, port }));
        File.Move(temporary, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>One CDP command on the browser endpoint of Wingman's own local debugging port (never a remote host).</summary>
public static class CdpBrowserCommand
{
    public static async Task<JsonElement?> SendAsync(
        HttpClient httpClient, int port, string method, object? parameters, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        using var versionDocument = JsonDocument.Parse(
            await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json/version", cancellationToken));
        if (!versionDocument.RootElement.TryGetProperty("webSocketDebuggerUrl", out var urlValue)
            || !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url)
            || url.Scheme != "ws" || !url.IsLoopback || url.Port != port)
            throw new InvalidOperationException("The local browser endpoint did not describe itself as expected.");
        using var socket = new ClientWebSocketCdpSocket();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(url, deadline.Token);
        var request = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method, @params = parameters ?? new { } });
        await socket.SendAsync(request, System.Net.WebSockets.WebSocketMessageType.Text, true, deadline.Token);
        var buffer = new byte[16 * 1024];
        using var accumulator = new CdpMessageAccumulator();
        while (true)
        {
            var receive = await socket.ReceiveAsync(buffer, deadline.Token);
            if (receive.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                throw new InvalidOperationException("The local browser endpoint closed before replying.");
            var payload = accumulator.Append(buffer.AsSpan(0, receive.Count), receive.EndOfMessage);
            if (!payload.HasValue) continue;
            using var document = JsonDocument.Parse(payload.Value);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number != 1) continue;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException("The local browser refused " + method + ": " + error.GetRawText());
            return root.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
    }

    /// <summary>Reads the browser process id from <c>SystemInfo.getProcessInfo</c>.</summary>
    public static int? BrowserProcessId(JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("processInfo", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in list.EnumerateArray())
            if (item.TryGetProperty("type", out var type) && type.GetString() == "browser"
                && item.TryGetProperty("id", out var id) && id.TryGetInt32(out var pid) && pid > 0)
                return pid;
        return null;
    }
}
