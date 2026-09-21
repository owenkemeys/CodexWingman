using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWingman.Core;

public sealed record CodexTarget(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);

public static class CodexTargetCatalog
{
    public static string? TryGetThreadId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("app", StringComparison.OrdinalIgnoreCase)) return null;
        var decoded = Uri.UnescapeDataString(uri.Query);
        var marker = "/local/";
        var index = decoded.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + marker.Length;
        var end = decoded.IndexOfAny(['/', '?', '#', '&'], start);
        var candidate = decoded[start..(end < 0 ? decoded.Length : end)];
        return Guid.TryParse(candidate, out _) ? candidate : null;
    }

    public static IReadOnlyList<CodexTarget> SelectPages(IEnumerable<CodexTarget> targets) => targets
        .Where((target) => target.Type == "page"
            && target.Url.StartsWith("app://-/index.html", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        .DistinctBy((target) => target.Id)
        .ToArray();

    public static bool PreservesPages(
        IEnumerable<CodexTarget> before,
        IEnumerable<CodexTarget> after)
    {
        var afterIds = SelectPages(after).Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        return SelectPages(before).All(target => afterIds.Contains(target.Id));
    }

    public static IReadOnlyList<CodexTarget> FindNewPages(
        IEnumerable<CodexTarget> before,
        IEnumerable<CodexTarget> after)
    {
        var beforeIds = SelectPages(before).Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        return SelectPages(after).Where(target => !beforeIds.Contains(target.Id)).ToArray();
    }
}

public interface ICodexTargetSource
{
    Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class HttpCodexTargetSource : ICodexTargetSource
{
    private static readonly TimeSpan BrowserCommandTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient httpClient;
    private readonly Func<int> portProvider;
    private readonly Func<ICdpWebSocket> socketFactory;

    public HttpCodexTargetSource(HttpClient httpClient, Func<int> portProvider)
        : this(httpClient, portProvider, () => new ClientWebSocketCdpSocket()) { }

    public HttpCodexTargetSource(HttpClient httpClient, int port = CodexLaunchPolicy.PreferredPort)
        : this(httpClient, () => port) { }

    public HttpCodexTargetSource(HttpClient httpClient, int port, Func<ICdpWebSocket> socketFactory)
        : this(httpClient, () => port, socketFactory) { }

    public HttpCodexTargetSource(HttpClient httpClient, Func<int> portProvider, Func<ICdpWebSocket> socketFactory)
    {
        this.httpClient = httpClient;
        this.portProvider = portProvider;
        this.socketFactory = socketFactory;
    }

    public async Task<IReadOnlyList<CodexTarget>> ListAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = $"http://127.0.0.1:{portProvider()}";
        await using var stream = await httpClient.GetStreamAsync($"{endpoint}/json/list", cancellationToken);
        var targets = await JsonSerializer.DeserializeAsync<CodexTarget[]>(stream, cancellationToken: cancellationToken) ?? [];
        var pages = CodexTargetCatalog.SelectPages(targets);
        return pages.Count > 0
            ? pages
            : await ListBrowserTargetsAsync(endpoint, cancellationToken);
    }

    private async Task<IReadOnlyList<CodexTarget>> ListBrowserTargetsAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        await using var versionStream = await httpClient.GetStreamAsync($"{endpoint}/json/version", cancellationToken);
        using var version = await JsonDocument.ParseAsync(versionStream, cancellationToken: cancellationToken);
        if (!version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserUrlValue)
            || browserUrlValue.GetString() is not { Length: > 0 } browserUrl)
            return [];

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(BrowserCommandTimeout);
        using var socket = socketFactory();
        try
        {
            await socket.ConnectAsync(new Uri(browserUrl), deadline.Token);
            var request = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = 1,
                method = "Target.getTargets",
                @params = new { filter = new object[] { new { } } },
            });
            await socket.SendAsync(request, WebSocketMessageType.Text, true, deadline.Token);

            var buffer = new byte[16 * 1024];
            using var accumulator = new CdpMessageAccumulator();
            while (true)
            {
                var receive = await socket.ReceiveAsync(buffer, deadline.Token);
                if (receive.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("The Codex browser target registry closed before replying");
                var payload = accumulator.Append(buffer.AsSpan(0, receive.Count), receive.EndOfMessage);
                if (!payload.HasValue) continue;
                var response = CdpTargetDiscoveryResponseParser.Parse(payload.Value.Span, requestId: 1, new Uri(browserUrl));
                if (response is null) continue;
                return CodexTargetCatalog.SelectPages(response);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Codex browser target discovery did not answer within {BrowserCommandTimeout.TotalSeconds:0.###} seconds", error);
        }
        finally
        {
            if (socket.State is not WebSocketState.Closed and not WebSocketState.Aborted)
            {
                try { socket.Abort(); } catch { }
            }
        }
    }
}

public static class CdpTargetDiscoveryResponseParser
{
    public static IReadOnlyList<CodexTarget>? Parse(ReadOnlySpan<byte> payload, int requestId, Uri browserWebSocketUrl)
    {
        using var message = JsonDocument.Parse(payload.ToArray());
        var root = message.RootElement;
        if (!root.TryGetProperty("id", out var id)
            || !id.TryGetInt32(out var actualId)
            || actualId != requestId)
            return null;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException(error.TryGetProperty("message", out var value)
                ? value.GetString() ?? "CDP target discovery failed"
                : error.ToString());
        if (!root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("targetInfos", out var targetInfos)
            || targetInfos.ValueKind != JsonValueKind.Array)
            return [];

        var authority = browserWebSocketUrl.GetLeftPart(UriPartial.Authority);
        var targets = new List<CodexTarget>();
        foreach (var targetInfo in targetInfos.EnumerateArray())
        {
            var targetId = targetInfo.TryGetProperty("targetId", out var idValue) ? idValue.GetString() : null;
            var type = targetInfo.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            var url = targetInfo.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(targetId) || type is null || url is null) continue;
            targets.Add(new(targetId, type, url, $"{authority}/devtools/page/{Uri.EscapeDataString(targetId)}"));
        }
        return targets;
    }
}

public interface ICdpEvaluator
{
    Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default);
    async Task<JsonElement?> EvaluateValueAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        await EvaluateAsync(target, expression, cancellationToken);
        return null;
    }
}

public interface ICdpWebSocket : IDisposable
{
    WebSocketState State { get; }
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    void Abort();
}

public sealed class ClientWebSocketCdpSocket : ICdpWebSocket
{
    private readonly ClientWebSocket socket = new();

    public WebSocketState State => socket.State;
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) => socket.ConnectAsync(uri, cancellationToken);
    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) => socket.SendAsync(payload, messageType, endOfMessage, cancellationToken);
    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken) => socket.ReceiveAsync(buffer, cancellationToken);
    public void Abort() => socket.Abort();
    public void Dispose() => socket.Dispose();
}

public sealed class WebSocketCdpEvaluator : ICdpEvaluator
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(3);
    private readonly TimeSpan commandTimeout;
    private readonly Func<ICdpWebSocket> socketFactory;

    public WebSocketCdpEvaluator(
        TimeSpan? commandTimeout = null,
        Func<ICdpWebSocket>? socketFactory = null)
    {
        this.commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        if (this.commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        this.socketFactory = socketFactory ?? (() => new ClientWebSocketCdpSocket());
    }

    public async Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default) =>
        _ = await EvaluateCoreAsync(target, expression, cancellationToken);

    public Task<JsonElement?> EvaluateValueAsync(
        CodexTarget target,
        string expression,
        CancellationToken cancellationToken = default) => EvaluateCoreAsync(target, expression, cancellationToken);

    private async Task<JsonElement?> EvaluateCoreAsync(
        CodexTarget target,
        string expression,
        CancellationToken cancellationToken)
    {
        using var socket = socketFactory();
        var completed = false;
        try
        {
            using (var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectDeadline.CancelAfter(commandTimeout);
                try
                {
                    await socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), connectDeadline.Token);
                }
                catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"CDP target {target.Id} did not connect within {commandTimeout.TotalSeconds:0.###} seconds", error);
                }
            }

            using var commandDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            commandDeadline.CancelAfter(commandTimeout);
            try
            {
                var request = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    id = 1,
                    method = "Runtime.evaluate",
                    @params = new { expression, returnByValue = true, awaitPromise = true },
                });
                await socket.SendAsync(request, WebSocketMessageType.Text, true, commandDeadline.Token);

                var buffer = new byte[16 * 1024];
                using var accumulator = new CdpMessageAccumulator();
                while (true)
                {
                    var receive = await socket.ReceiveAsync(buffer, commandDeadline.Token);
                    if (receive.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException($"CDP target {target.Id} closed before replying");
                    var payload = accumulator.Append(buffer.AsSpan(0, receive.Count), receive.EndOfMessage);
                    if (!payload.HasValue) continue;
                    var response = CdpEvaluationResponseParser.Parse(payload.Value.Span, requestId: 1);
                    if (!response.IsResponse) continue;
                    completed = true;
                    return response.Value;
                }
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"CDP target {target.Id} did not answer within {commandTimeout.TotalSeconds:0.###} seconds", error);
            }
        }
        finally
        {
            if (!completed && socket.State is not WebSocketState.Closed and not WebSocketState.Aborted)
            {
                try { socket.Abort(); } catch { }
            }
        }
    }
}

public sealed record CdpEvaluationResponse(bool IsResponse, JsonElement? Value)
{
    public static CdpEvaluationResponse NotResponse { get; } = new(false, null);
}

public static class CdpEvaluationResponseParser
{
    public static CdpEvaluationResponse Parse(ReadOnlySpan<byte> payload, int requestId)
    {
        using var message = JsonDocument.Parse(payload.ToArray());
        var root = message.RootElement;
        if (!root.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out var actualId)
            || actualId != requestId)
            return CdpEvaluationResponse.NotResponse;
        if (root.TryGetProperty("error", out var error))
        {
            var messageText = error.TryGetProperty("message", out var value) ? value.GetString() : error.ToString();
            throw new InvalidOperationException(messageText ?? "CDP protocol error");
        }
        if (!root.TryGetProperty("result", out var result))
            return new(true, null);
        if (result.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException(exception.ToString());
        if (result.TryGetProperty("result", out var remoteResult)
            && remoteResult.TryGetProperty("value", out var returnedValue))
            return new(true, returnedValue.Clone());
        return new(true, null);
    }
}

public sealed class CdpMessageAccumulator : IDisposable
{
    private readonly MemoryStream payload = new();

    public ReadOnlyMemory<byte>? Append(ReadOnlySpan<byte> fragment, bool endOfMessage)
    {
        payload.Write(fragment);
        if (!endOfMessage) return null;
        var completed = payload.ToArray();
        payload.SetLength(0);
        return completed;
    }

    public void Dispose() => payload.Dispose();
}
