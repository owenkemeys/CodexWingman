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
public sealed class T3WebTargetSource(HttpClient httpClient, Func<int?> portProvider, Func<Uri?> originProvider, CdpConnectionPool? pool = null) : ICodexTargetSource
{
    public Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var port = portProvider();
        var origin = originProvider();
        if (port is null || origin is null) return Task.FromResult<IReadOnlyList<CodexTarget>>([]);
        if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid T3 Code (web) debugging port.");
        return new HttpCodexTargetSource(
            httpClient, () => port.Value, () => new ClientWebSocketCdpSocket(),
            targets => T3WebTargetCatalog.SelectPages(targets, origin), pool).ListAsync(cancellationToken);
    }
}

public enum T3WebOpenAction
{
    /// <summary>Nothing of Wingman's T3 web Chrome is running: start it.</summary>
    StartNew,
    /// <summary>Wingman's Chrome runs but shows no T3 web window: ask it for one.</summary>
    OpenWindowInRunningProfile,
    /// <summary>A T3 web window exists: bring it to the front instead of opening another.</summary>
    ActivateExisting,
}

public static class T3WebLaunchPolicy
{
    public const int PreferredPort = 9423;

    /// <summary>
    /// What one launch of the T3 web shortcut does. Like one click on a pinned app, it never opens a
    /// second T3 web window while one exists.
    /// </summary>
    public static T3WebOpenAction OpenAction(bool endpointAlive, int t3WebPageCount) =>
        !endpointAlive ? T3WebOpenAction.StartNew
        : t3WebPageCount > 0 ? T3WebOpenAction.ActivateExisting
        : T3WebOpenAction.OpenWindowInRunningProfile;

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
            throw new ArgumentException("T3 Code (web) must open an https URL.", nameof(startUrl));
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
        => await SendAsync(httpClient, port, method, parameters, CdpConnectionPool.Shared, cancellationToken);

    /// <summary>Sends over the pooled browser connection; the endpoint description is cached per port and re-read after a failure.</summary>
    public static async Task<JsonElement?> SendAsync(
        HttpClient httpClient, int port, string method, object? parameters, CdpConnectionPool pool, CancellationToken cancellationToken)
    {
        var url = await CdpBrowserEndpoints.ResolveAsync(httpClient, port, cancellationToken);
        try
        {
            var result = await pool.SendAsync(url, method, parameters, TimeSpan.FromSeconds(5), cancellationToken);
            return result?.Clone();
        }
        catch (CdpProtocolException error)
        {
            throw new InvalidOperationException("The local browser refused " + method + ": " + error.Message, error);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            CdpBrowserEndpoints.Forget(port);
            throw;
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
