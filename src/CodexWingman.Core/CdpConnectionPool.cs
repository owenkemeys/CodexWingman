using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace CodexWingman.Core;

/// <summary>
/// Keeps one persistent DevTools websocket per local endpoint URL and multiplexes
/// commands over it by id, instead of opening a new connection per command.
/// Connections close after an idle period, on any socket error, or after
/// consecutive timeouts, and are reopened on the next command.
/// </summary>
public sealed class CdpConnectionPool : IDisposable
{
    public static CdpConnectionPool Shared { get; } = new();

    private readonly Func<ICdpWebSocket> socketFactory;
    private readonly TimeSpan idleTimeout;
    private readonly ConcurrentDictionary<string, Connection> connections = new(StringComparer.Ordinal);
    private readonly Timer? sweeper;
    private int opened;
    private bool disposed;

    public CdpConnectionPool(Func<ICdpWebSocket>? socketFactory = null, TimeSpan? idleTimeout = null, bool sweep = true)
    {
        this.socketFactory = socketFactory ?? (() => new ClientWebSocketCdpSocket());
        this.idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        if (sweep) sweeper = new Timer(_ => CloseIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Connections opened over this pool's lifetime (diagnostics and tests).</summary>
    public int OpenedCount => Volatile.Read(ref opened);
    public int OpenCount => connections.Count;

    /// <summary>Sends one command and returns the response's <c>result</c>, or throws on a protocol error.</summary>
    public async Task<JsonElement?> SendAsync(
        Uri url, string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (url.Scheme is not ("ws" or "wss")) throw new ArgumentException("CDP endpoints are websockets.", nameof(url));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var key = url.AbsoluteUri;
        while (true)
        {
            var connection = connections.GetOrAdd(key, _ => new Connection(this, url));
            try
            {
                var root = await connection.SendAsync(method, parameters, timeout, cancellationToken);
                if (root.TryGetProperty("error", out var error))
                    throw new CdpProtocolException(error.TryGetProperty("message", out var message)
                        ? message.GetString() ?? "CDP protocol error" : error.GetRawText(), root);
                return root.TryGetProperty("result", out var result) ? result : null;
            }
            catch (ConnectionRetiredException)
            {
                // A connection retired before this command was sent; open a fresh one.
                continue;
            }
        }
    }

    /// <summary>Closes connections idle for longer than the idle timeout.</summary>
    public void CloseIdle(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        foreach (var (key, connection) in connections)
            if (connection.IsIdle(now, idleTimeout)) connection.Retire(null);
    }

    public void Dispose()
    {
        disposed = true;
        sweeper?.Dispose();
        foreach (var connection in connections.Values) connection.Retire(null);
    }

    private void Remove(string key, Connection connection) =>
        ((ICollection<KeyValuePair<string, Connection>>)connections).Remove(new(key, connection));

    private sealed class ConnectionRetiredException : Exception;

    private sealed class Connection
    {
        private const int MaxConsecutiveTimeouts = 2;
        private readonly CdpConnectionPool pool;
        private readonly Uri url;
        private readonly ICdpWebSocket socket;
        private readonly SemaphoreSlim sendGate = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
        private readonly object sync = new();
        private Task? connectTask;
        private int nextId;
        private int consecutiveTimeouts;
        private long lastUsedTicks = DateTime.UtcNow.Ticks;
        private bool retired;

        public Connection(CdpConnectionPool pool, Uri url)
        {
            this.pool = pool;
            this.url = url;
            socket = pool.socketFactory();
        }

        public bool IsIdle(DateTime now, TimeSpan idleTimeout) =>
            pending.IsEmpty && now - new DateTime(Interlocked.Read(ref lastUsedTicks), DateTimeKind.Utc) > idleTimeout;

        public async Task<JsonElement> SendAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref lastUsedTicks, DateTime.UtcNow.Ticks);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            Task connecting;
            lock (sync)
            {
                if (retired) throw new ConnectionRetiredException();
                connecting = connectTask ??= ConnectAsync();
            }
            try { await connecting.WaitAsync(deadline.Token); }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                Retire(null);
                throw new TimeoutException($"CDP endpoint did not connect within {timeout.TotalSeconds:0.###} seconds", error);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                Retire(null);
                throw;
            }

            var id = Interlocked.Increment(ref nextId);
            var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = reply;
            lock (sync)
            {
                if (retired) { pending.TryRemove(id, out _); throw new ConnectionRetiredException(); }
            }
            try
            {
                var request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } });
                await sendGate.WaitAsync(deadline.Token);
                try { await socket.SendAsync(request, WebSocketMessageType.Text, true, deadline.Token); }
                finally { sendGate.Release(); }
                var root = await reply.Task.WaitAsync(deadline.Token);
                Interlocked.Exchange(ref consecutiveTimeouts, 0);
                Interlocked.Exchange(ref lastUsedTicks, DateTime.UtcNow.Ticks);
                return root;
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref consecutiveTimeouts) >= MaxConsecutiveTimeouts) Retire(null);
                throw new TimeoutException($"CDP endpoint did not answer {method} within {timeout.TotalSeconds:0.###} seconds", error);
            }
            catch (OperationCanceledException)
            {
                // A cancelled send can leave a partial frame; never reuse that socket.
                Retire(null);
                throw;
            }
            catch (WebSocketException)
            {
                Retire(null);
                throw;
            }
            finally
            {
                pending.TryRemove(id, out _);
            }
        }

        private async Task ConnectAsync()
        {
            await socket.ConnectAsync(url, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Increment(ref pool.opened);
            _ = Task.Run(ReceiveLoopAsync);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            using var accumulator = new CdpMessageAccumulator();
            Exception? failure = null;
            try
            {
                while (true)
                {
                    var receive = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                    if (receive.MessageType == WebSocketMessageType.Close)
                    {
                        failure = new InvalidOperationException("The CDP endpoint closed the connection.");
                        break;
                    }
                    var payload = accumulator.Append(buffer.AsSpan(0, receive.Count), receive.EndOfMessage);
                    if (!payload.HasValue) continue;
                    JsonElement root;
                    try
                    {
                        using var document = JsonDocument.Parse(payload.Value);
                        root = document.RootElement.Clone();
                    }
                    catch (JsonException) { continue; }
                    // Events carry no id; this pool never enables domains, so ignore them.
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idValue)
                        || idValue.ValueKind != JsonValueKind.Number || !idValue.TryGetInt32(out var id)) continue;
                    if (pending.TryGetValue(id, out var reply)) reply.TrySetResult(root);
                }
            }
            catch (Exception error) { failure = error; }
            Retire(failure);
        }

        public void Retire(Exception? failure)
        {
            lock (sync)
            {
                if (retired) return;
                retired = true;
            }
            pool.Remove(url.AbsoluteUri, this);
            var reason = failure as Exception ?? new InvalidOperationException("The CDP connection was closed.");
            foreach (var reply in pending.Values)
                reply.TrySetException(reason is WebSocketException or InvalidOperationException
                    ? reason : new InvalidOperationException(reason.Message, reason));
            try { if (socket.State is not WebSocketState.Closed and not WebSocketState.Aborted) socket.Abort(); } catch { }
            try { socket.Dispose(); } catch { }
        }
    }
}

public sealed class CdpProtocolException(string message, JsonElement response) : InvalidOperationException(message)
{
    public JsonElement Response { get; } = response;
}

/// <summary>Runtime.evaluate over the shared persistent connection pool.</summary>
public sealed class PooledCdpEvaluator(CdpConnectionPool pool, TimeSpan? commandTimeout = null) : ICdpEvaluator
{
    private readonly TimeSpan timeout = commandTimeout ?? TimeSpan.FromSeconds(3);

    public async Task EvaluateAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default) =>
        _ = await EvaluateValueAsync(target, expression, cancellationToken);

    public async Task<JsonElement?> EvaluateValueAsync(CodexTarget target, string expression, CancellationToken cancellationToken = default)
    {
        var result = await pool.SendAsync(new Uri(target.WebSocketDebuggerUrl), "Runtime.evaluate",
            new { expression, returnByValue = true, awaitPromise = true }, timeout, cancellationToken);
        if (result is not { } value) return null;
        if (value.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException(exception.ToString());
        return value.TryGetProperty("result", out var remote) && remote.TryGetProperty("value", out var returned)
            ? returned.Clone() : null;
    }
}

/// <summary>
/// Remembers each local port's browser websocket URL (from <c>/json/version</c>, loopback and same port only),
/// so browser-level commands and target listing reuse one pooled connection instead of re-describing the endpoint.
/// </summary>
public static class CdpBrowserEndpoints
{
    private static readonly ConcurrentDictionary<int, Uri> Known = new();

    public static async Task<Uri> ResolveAsync(HttpClient httpClient, int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (Known.TryGetValue(port, out var known)) return known;
        using var versionDocument = JsonDocument.Parse(
            await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json/version", cancellationToken));
        if (!versionDocument.RootElement.TryGetProperty("webSocketDebuggerUrl", out var urlValue)
            || !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url)
            || url.Scheme != "ws" || !url.IsLoopback || url.Port != port)
            throw new InvalidOperationException("The local browser endpoint did not describe itself as expected.");
        Known[port] = url;
        return url;
    }

    public static void Forget(int port) => Known.TryRemove(port, out _);
}
