using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWingman.Core;

/// <summary>What Wingman remembers locally about its T3 web pairing. Holds no credential.</summary>
public sealed record T3PairingState(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("pairedAtUtc")] DateTimeOffset PairedAtUtc,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

public static class T3PairingStateStore
{
    public static T3PairingState? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<T3PairingState>(File.ReadAllText(path));
            return state is { SchemaVersion: 1 } && T3WebUrlPolicy.TryNormalizeOrigin(state.Origin, out _) ? state : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void Save(string path, T3PairingState state)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Path needs a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}

public enum T3PairingAction { None, Pair, Renew }

/// <summary>
/// T3 browser sessions last a fixed 30 days from pairing. Wingman pairs when the page reports no session
/// and renews a few days before the expiry, so the window never reaches the pairing screen.
/// The source of truth for expiry is the page itself (<c>/api/auth/session</c> <c>expiresAt</c>, read inside the
/// T3 page); the locally remembered expiry is only a fallback when the page does not report one, and is
/// re-synced from the page whenever they differ.
/// </summary>
public static class T3PairingPolicy
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(3);

    public static T3PairingAction Evaluate(T3SessionStatus session, T3PairingState? state, DateTimeOffset now)
    {
        if (!session.Authenticated) return T3PairingAction.Pair;
        var expires = session.ExpiresAtUtc ?? state?.ExpiresAtUtc;
        if (expires is null) return T3PairingAction.None;
        return now >= expires.Value - RenewBefore ? T3PairingAction.Renew : T3PairingAction.None;
    }
}

public sealed record T3SessionStatus(bool Authenticated, DateTimeOffset? ExpiresAtUtc, string? Origin = null);

/// <summary>The page Wingman drives: the T3 web client's own session endpoints, called from inside its page.</summary>
public interface IT3WebPage
{
    Task<T3SessionStatus> GetSessionAsync(CancellationToken cancellationToken);
    /// <summary>Exchanges a one-time pairing credential for a browser session cookie, exactly like the T3 /pair page does.</summary>
    Task<T3SessionStatus> ExchangePairingCredentialAsync(string credential, CancellationToken cancellationToken);
    /// <summary>Leaves the pairing screen after a first pairing (renewals never navigate).</summary>
    Task ReloadAsync(CancellationToken cancellationToken);
}

public sealed record T3PairingLinkRequest(Uri? Origin, string Label, string Reason);

public sealed record T3PairingLink(string Url);

/// <summary>Pluggable source of fresh one-time T3 pairing links.</summary>
public interface IT3PairingLinkProvider
{
    string Name { get; }
    Task<T3PairingLink> CreateLinkAsync(T3PairingLinkRequest request, CancellationToken cancellationToken);
    /// <summary>Asks the T3 host to revoke older sessions carrying <paramref name="label"/>, keeping the newest one.</summary>
    Task RevokePreviousAsync(Uri origin, string label, CancellationToken cancellationToken);
}

public enum T3PairingResult { AlreadyPaired, Paired, Renewed, Failed }

public sealed record T3PairingOutcome(T3PairingResult Result, string Message, DateTimeOffset? ExpiresAtUtc = null);

public sealed class T3PairingCoordinator(
    IT3PairingLinkProvider provider,
    string statePath,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.UtcNow);

    public T3PairingState? State => T3PairingStateStore.Load(statePath);

    public async Task<T3PairingOutcome> EnsurePairedAsync(
        IT3WebPage page,
        Uri? configuredOrigin,
        string label,
        CancellationToken cancellationToken = default,
        T3PairingLink? prefetchedLink = null,
        bool forceRenew = false)
    {
        var session = await page.GetSessionAsync(cancellationToken);
        var state = T3PairingStateStore.Load(statePath);
        var now = clock();
        var action = T3PairingPolicy.Evaluate(session, state, now);
        // Development test hook: renew a signed-in page now, by the same path as renewal before expiry.
        if (forceRenew && session.Authenticated) action = T3PairingAction.Renew;
        var knownOrigin = configuredOrigin
            ?? (state is not null && T3WebUrlPolicy.TryNormalizeOrigin(state.Origin, out var remembered) ? remembered : null)
            ?? (T3WebUrlPolicy.TryNormalizeOrigin(session.Origin, out var pageOrigin) ? pageOrigin : null);
        if (action == T3PairingAction.None)
        {
            if (session.Authenticated && knownOrigin is not null && session.ExpiresAtUtc is { } expires
                && (state is null || state.ExpiresAtUtc != expires))
                Save(knownOrigin, label, state?.PairedAtUtc ?? expires - T3PairingPolicy.SessionLifetime, expires);
            return new(T3PairingResult.AlreadyPaired, "T3 Code (web) is signed in.", session.ExpiresAtUtc ?? state?.ExpiresAtUtc);
        }

        T3PairingLink link;
        try
        {
            link = prefetchedLink ?? await provider.CreateLinkAsync(
                new(knownOrigin, label, action == T3PairingAction.Pair ? "signed-out" : "renew-before-expiry"),
                cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new(T3PairingResult.Failed, $"Wingman could not get a T3 pairing link from {provider.Name}: {error.Message}");
        }
        if (!T3WebUrlPolicy.TryParsePairingLink(link.Url, knownOrigin, out var origin, out var credential))
            return new(T3PairingResult.Failed, "The pairing link did not match the T3 Code (web) address, so Wingman did not use it.");

        var paired = await page.ExchangePairingCredentialAsync(credential, cancellationToken);
        if (!paired.Authenticated)
            return new(T3PairingResult.Failed, "T3 did not accept the pairing link. Wingman will try again on the next check.");

        var pairedAt = clock();
        var expiresAt = paired.ExpiresAtUtc ?? pairedAt + T3PairingPolicy.SessionLifetime;
        Save(origin, label, pairedAt, expiresAt);
        var message = action == T3PairingAction.Pair ? "T3 Code (web) is paired." : "T3 Code (web) pairing was renewed.";
        if (action == T3PairingAction.Pair && !session.Authenticated)
            await page.ReloadAsync(cancellationToken);
        if (state is not null || action == T3PairingAction.Renew)
        {
            try { await provider.RevokePreviousAsync(origin, label, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message += " The previous session could not be revoked yet: " + error.Message;
            }
        }
        return new(action == T3PairingAction.Pair ? T3PairingResult.Paired : T3PairingResult.Renewed, message, expiresAt);
    }

    private void Save(Uri origin, string label, DateTimeOffset pairedAt, DateTimeOffset expiresAt) =>
        T3PairingStateStore.Save(statePath, new(1, origin.GetLeftPart(UriPartial.Authority), label, pairedAt, expiresAt));

    /// <summary>Session label sent to the T3 host. It names the PC only, so older Wingman sessions can be found and revoked.</summary>
    public static string DefaultLabel(string machineName) =>
        "Wingman T3 web (" + new string((machineName ?? "PC").Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').Take(40).ToArray()) + ")";
}

/// <summary>
/// Pairing provider that drops a request file into a folder the T3 host watches (answered by a host-side bridge
/// such as tools/t3_pairing_bridge.py; format in docs/T3_WEB_PAIRING.md) and waits for the host's answer file.
/// The folder holds a one-time, two-minute link only briefly;
/// Wingman deletes the answer as soon as it has read it.
/// </summary>
public sealed class SharedFolderPairingProvider : IT3PairingLinkProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    // Optional receipt integration (one sync tool's layout); unused unless that receipt exists on this PC.
    private static readonly string[] OptionalReceiptPath = ["Jarvis", "Sync", "receipts", "latest.json"];
    private static readonly string[] OptionalReceiptRequestFolder = ["ops", "jarvis-sync", "requests", "t3-pairing"];
    private readonly string folder;
    private readonly string instance;
    private readonly TimeSpan timeout;
    private readonly TimeSpan pollInterval;
    private readonly Func<DateTimeOffset> clock;

    public SharedFolderPairingProvider(
        string folder,
        string instance,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A pairing folder is required.", nameof(folder));
        this.folder = folder;
        this.instance = instance;
        this.timeout = timeout ?? TimeSpan.FromSeconds(150);
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Name => "the T3 host pairing folder";

    public async Task<T3PairingLink> CreateLinkAsync(T3PairingLinkRequest request, CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync("pair", request.Origin, request.Label, request.Reason, cancellationToken);
        if (!response.TryGetProperty("pairing_url", out var url) || url.GetString() is not { Length: > 0 } value)
            throw new InvalidDataException("The pairing answer had no link.");
        return new(value);
    }

    public async Task RevokePreviousAsync(Uri origin, string label, CancellationToken cancellationToken) =>
        _ = await ExchangeAsync("revoke-previous", origin, label, "renewed", cancellationToken);

    private async Task<JsonElement> ExchangeAsync(
        string kind, Uri? origin, string label, string reason, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The pairing folder is not reachable.");
        var id = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(folder, id + ".request.json");
        var responsePath = Path.Combine(folder, id + ".response.json");
        var temporary = Path.Combine(folder, "." + id + ".request.tmp");
        var created = clock();
        // Request and answer files use snake_case JSON (docs/T3_WEB_PAIRING.md).
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema_version"] = 1,
            ["kind"] = kind,
            ["id"] = id,
            ["instance"] = instance,
            ["label"] = label,
            ["reason"] = reason,
            ["origin"] = origin?.GetLeftPart(UriPartial.Authority),
            ["created_at_utc"] = created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        }, JsonOptions), cancellationToken);
        File.Move(temporary, requestPath, overwrite: false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(responsePath))
                {
                    JsonElement root;
                    try
                    {
                        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, cancellationToken));
                        root = document.RootElement.Clone();
                    }
                    catch (IOException) { await Task.Delay(pollInterval, cancellationToken); continue; }
                    // Request first, so the host never sees an unanswered request again.
                    TryDelete(requestPath);
                    TryDelete(responsePath);
                    if (!root.TryGetProperty("id", out var answered) || answered.GetString() != id)
                        throw new InvalidDataException("The pairing answer did not match the request.");
                    if (root.TryGetProperty("status", out var status) && status.GetString() == "ok") return root;
                    var message = root.TryGetProperty("message", out var text) ? text.GetString() : null;
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "The T3 host refused the request." : message);
                }
                if (clock() - created > timeout)
                    throw new TimeoutException("The T3 host did not answer the pairing request in time.");
                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Folder discovery: an explicit <c>t3Web.pairing.folder</c> setting wins. Otherwise, optionally, a sync tool's
    /// receipt on this PC names a share root whose fixed request folder is used (docs/T3_WEB_PAIRING.md).
    /// </summary>
    public static string? ResolveFolder(T3PairingSettings? settings, string localAppData)
    {
        if (!string.IsNullOrWhiteSpace(settings?.Folder)) return Environment.ExpandEnvironmentVariables(settings.Folder.Trim());
        var receipt = Path.Combine(localAppData, Path.Combine(OptionalReceiptPath));
        try
        {
            if (!File.Exists(receipt)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(receipt));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("share_root", StringComparison.OrdinalIgnoreCase)
                    && !property.Name.Equals("shareRoot", StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.GetString() is { Length: > 0 } root)
                    return Path.Combine(root, Path.Combine(OptionalReceiptRequestFolder));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        return null;
    }
}

/// <summary>Drives the T3 web client's own session endpoints from inside its page over local CDP.</summary>
public sealed class CdpT3WebPage(ICdpEvaluator evaluator, CodexTarget target) : IT3WebPage
{
    private const string SessionExpression = """
        (async () => {
          const response = await fetch(new URL('/api/auth/session', location.origin), { credentials: 'include', cache: 'no-store' })
          let body = null
          try { body = response.ok ? await response.json() : null } catch {}
          return { authenticated: body?.authenticated === true, expiresAt: typeof body?.expiresAt === 'string' ? body.expiresAt : null, origin: location.origin }
        })()
        """;

    public async Task<T3SessionStatus> GetSessionAsync(CancellationToken cancellationToken) =>
        Parse(await evaluator.EvaluateValueAsync(target, SessionExpression, cancellationToken));

    public async Task<T3SessionStatus> ExchangePairingCredentialAsync(string credential, CancellationToken cancellationToken)
    {
        var expression = "(async (credential) => {\n"
            + "  const response = await fetch(new URL('/api/auth/browser-session', location.origin), { method: 'POST', credentials: 'include', cache: 'no-store', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ credential }) })\n"
            + "  let body = null\n"
            + "  try { body = response.ok ? await response.json() : null } catch {}\n"
            + "  return { authenticated: body?.authenticated === true, expiresAt: typeof body?.expiresAt === 'string' ? body.expiresAt : null, origin: location.origin }\n"
            + "})(" + JsonSerializer.Serialize(credential) + ")";
        return Parse(await evaluator.EvaluateValueAsync(target, expression, cancellationToken));
    }

    public Task ReloadAsync(CancellationToken cancellationToken) => evaluator.EvaluateAsync(target,
        "(() => { if (location.pathname === '/pair') location.replace(location.origin + '/'); else location.reload(); return true })()",
        cancellationToken);

    public static T3SessionStatus Parse(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } root) return new(false, null);
        var authenticated = root.TryGetProperty("authenticated", out var flag) && flag.ValueKind == JsonValueKind.True;
        DateTimeOffset? expires = root.TryGetProperty("expiresAt", out var text) && text.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(text.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime() : null;
        var origin = root.TryGetProperty("origin", out var originValue) && originValue.ValueKind == JsonValueKind.String
            ? originValue.GetString() : null;
        return new(authenticated, expires, origin);
    }
}
