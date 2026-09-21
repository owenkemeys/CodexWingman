using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Jint;

namespace CodexWingman.Core.Helpers;

public sealed class HelperBackend
{
    private static readonly TimeSpan DefaultDurationLimit = TimeSpan.FromSeconds(5);
    private const int StatementLimit = 100_000;
    private readonly TimeSpan durationLimit;
    private readonly IHelperSessionFiles sessionFiles;
    private readonly Func<DateTimeOffset> currentTime;
    private readonly Action<string> log;

    public HelperBackend(
        IEnumerable<string> sessionRoots,
        Func<DateTimeOffset>? currentTime = null,
        Action<string>? log = null,
        TimeSpan? durationLimit = null,
        IHelperSessionFiles? sessionFiles = null)
    {
        this.durationLimit = durationLimit ?? DefaultDurationLimit;
        if (this.durationLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(durationLimit));
        this.sessionFiles = sessionFiles ?? new FileSystemHelperSessionFiles(sessionRoots);
        this.currentTime = currentTime ?? (() => DateTimeOffset.UtcNow);
        this.log = log ?? (_ => { });
    }

    public async Task<JsonDocument> RefreshAsync(
        HelperPackage package,
        CancellationToken cancellationToken = default,
        string? targetThreadId = null)
    {
        var source = package.BackendSource;
        if (source is null)
            return JsonDocument.Parse("{}");

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(durationLimit);
        var work = Task.Run(() => Refresh(package, source, deadline.Token, targetThreadId), CancellationToken.None);
        try
        {
            var result = await work.WaitAsync(durationLimit, cancellationToken).ConfigureAwait(false);
            deadline.Dispose();
            return result;
        }
        catch (TimeoutException error)
        {
            deadline.Cancel();
            ObserveAbandoned(work, deadline);
            throw new TimeoutException($"Helper '{package.Manifest.Id}' backend exceeded {durationLimit.TotalSeconds:0.###} seconds", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            deadline.Cancel();
            ObserveAbandoned(work, deadline);
            throw new TimeoutException($"Helper '{package.Manifest.Id}' backend exceeded {durationLimit.TotalSeconds:0.###} seconds", error);
        }
        catch
        {
            deadline.Cancel();
            if (work.IsCompleted) deadline.Dispose();
            else ObserveAbandoned(work, deadline);
            throw;
        }
    }

    private JsonDocument Refresh(HelperPackage package, string source, CancellationToken cancellationToken, string? targetThreadId)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = new Engine(options => options
            .TimeoutInterval(durationLimit)
            .MaxStatements(StatementLimit)
            .CancellationToken(cancellationToken));
        engine.SetValue("__wingmanLog", new Action<object?>(value => log(value?.ToString() ?? string.Empty)));
        engine.SetValue("__wingmanCurrentTime", new Func<string>(() => currentTime().ToUniversalTime().ToString("O")));
        engine.SetValue("__wingmanExpandEnvironmentVariables", new Func<string, string>(Environment.ExpandEnvironmentVariables));

        var hasSessionCapability = package.Manifest.Capabilities.Any(
            capability => string.Equals(capability, "files.codexSessions", StringComparison.Ordinal));
        var hasTargetSessionCapability = package.Manifest.Capabilities.Any(
            capability => string.Equals(capability, "files.codexTargetSession", StringComparison.Ordinal));
        if (hasSessionCapability)
        {
            engine.SetValue("__wingmanSessionRoots", new Func<string[]>(sessionFiles.Roots));
            engine.SetValue("__wingmanListSessionFiles", new Func<string, string[]>(root => sessionFiles.ListFiles(root, cancellationToken)));
            engine.SetValue("__wingmanReadSessionText", new Func<string, string>(path => sessionFiles.ReadText(path, cancellationToken)));
        }
        if (hasTargetSessionCapability)
        {
            engine.SetValue("__wingmanTargetSessionSnapshot", new Func<string?>(() => sessionFiles.ReadSessionSnapshotJson(targetThreadId, cancellationToken)));
            engine.SetValue("__wingmanTargetTurnMetadata", new Func<string?>(() => sessionFiles.ReadTurnMetadataJson(targetThreadId, cancellationToken)));
            engine.SetValue("__wingmanTargetHookTrace", new Func<string?>(() => sessionFiles.ReadHookTraceJson(targetThreadId, cancellationToken)));
        }

        engine.Execute(BuildPrelude(hasSessionCapability, hasTargetSessionCapability));
        engine.Execute(source);
        var result = engine.Evaluate("refresh(wingman)");
        engine.SetValue("__wingmanResult", result);
        var compatible = engine.Evaluate("""
((value) => {
  const seen = [];
  const visit = candidate => {
    if (candidate === null) return true;
    const type = typeof candidate;
    if (type === 'string' || type === 'boolean') return true;
    if (type === 'number') return Number.isFinite(candidate);
    if (type !== 'object' || seen.includes(candidate)) return false;
    seen.push(candidate);
    const valid = Array.isArray(candidate)
      ? candidate.every(visit)
      : (Object.getPrototypeOf(candidate) === Object.prototype || Object.getPrototypeOf(candidate) === null)
        && Object.keys(candidate).every(key => visit(candidate[key]));
    seen.pop();
    return valid;
  };
  return visit(value);
})(__wingmanResult)
""").AsBoolean();
        if (!compatible)
            throw new InvalidOperationException($"Helper '{package.Manifest.Id}' backend returned a non-JSON-compatible value");
        var serialized = engine.Evaluate("JSON.stringify(__wingmanResult)");
        if (serialized.IsUndefined())
            throw new InvalidOperationException($"Helper '{package.Manifest.Id}' backend returned an undefined or non-JSON value");
        return JsonDocument.Parse(serialized.AsString());
    }

    private static void ObserveAbandoned(Task<JsonDocument> work, CancellationTokenSource deadline)
    {
        _ = work.ContinueWith(
            completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
                else _ = completed.Exception;
                deadline.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string BuildPrelude(bool hasSessionCapability, bool hasTargetSessionCapability)
    {
        var sessionApi = hasSessionCapability
            ? "codexSessions:Object.freeze({roots:()=>sessionRoots(),listFiles:root=>listSessionFiles(String(root)),readText:path=>readSessionText(String(path)),snapshots:()=>JSON.parse(sessionSnapshots())})"
            : string.Empty;
        var targetSessionApi = hasTargetSessionCapability
            ? "codexTargetSession:Object.freeze({snapshot:()=>{const value=targetSessionSnapshot();return value===null?null:JSON.parse(value)},turnMetadata:()=>{const value=targetTurnMetadata();return value===null?null:JSON.parse(value)},hookTrace:()=>{const value=targetHookTrace();return value===null?null:JSON.parse(value)}})"
            : string.Empty;
        var apis = string.Join(",", new[] { sessionApi, targetSessionApi }.Where(value => !string.IsNullOrEmpty(value)));
        return """
const wingman=(()=>{
const writeLog=__wingmanLog;
const currentTime=__wingmanCurrentTime;
const expandEnvironmentVariables=__wingmanExpandEnvironmentVariables;
const sessionRoots=globalThis.__wingmanSessionRoots;
const listSessionFiles=globalThis.__wingmanListSessionFiles;
const readSessionText=globalThis.__wingmanReadSessionText;
const sessionSnapshots=globalThis.__wingmanSessionSnapshots;
const targetSessionSnapshot=globalThis.__wingmanTargetSessionSnapshot;
const targetTurnMetadata=globalThis.__wingmanTargetTurnMetadata;
const targetHookTrace=globalThis.__wingmanTargetHookTrace;
delete globalThis.__wingmanLog;
delete globalThis.__wingmanCurrentTime;
delete globalThis.__wingmanExpandEnvironmentVariables;
delete globalThis.__wingmanSessionRoots;
delete globalThis.__wingmanListSessionFiles;
delete globalThis.__wingmanReadSessionText;
delete globalThis.__wingmanSessionSnapshots;
delete globalThis.__wingmanTargetSessionSnapshot;
delete globalThis.__wingmanTargetTurnMetadata;
delete globalThis.__wingmanTargetHookTrace;
return Object.freeze({
  log:message=>writeLog(message),
  currentTime:()=>currentTime(),
  expandEnvironmentVariables:value=>expandEnvironmentVariables(String(value)),
  files:Object.freeze({__SESSION_APIS__})
});
})();
""".Replace("__SESSION_APIS__", apis, StringComparison.Ordinal);
    }

}

public interface IHelperSessionFiles
{
    string[] Roots();
    string[] ListFiles(string root, CancellationToken cancellationToken);
    string ReadText(string path, CancellationToken cancellationToken);
    string? ReadSessionSnapshotJson(string? threadId, CancellationToken cancellationToken) => null;
    string? ReadTurnMetadataJson(string? threadId, CancellationToken cancellationToken) => null;
    string? ReadHookTraceJson(string? threadId, CancellationToken cancellationToken) => null;
}

public sealed class FileSystemHelperSessionFiles : IHelperSessionFiles
{
    private const int MaximumReadBytes = 16 * 1024 * 1024;
    private const int MaximumSnapshotScanBytes = 8 * 1024 * 1024;
    private const int MaximumFullSnapshotScanBytes = 128 * 1024 * 1024;
    private readonly string[] sessionRoots;
    private readonly Func<string, string, EnumerationOptions, IEnumerable<string>> enumerateFiles;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedSnapshot> snapshotCache = new(StringComparer.OrdinalIgnoreCase);

    public FileSystemHelperSessionFiles(IEnumerable<string> sessionRoots,
        Func<string, string, EnumerationOptions, IEnumerable<string>>? enumerateFiles = null)
    {
        this.enumerateFiles = enumerateFiles ?? Directory.EnumerateFiles;
        this.sessionRoots = sessionRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string[] Roots() => [.. sessionRoots];

    public string[] ListFiles(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = RequireConfiguredRoot(root);
        return Directory.Exists(configured)
            ? Directory.EnumerateFiles(configured, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true,
                })
                .OrderByDescending(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return File.GetLastWriteTimeUtc(path);
                })
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    public string ReadText(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (!sessionRoots.Any(root => IsWithin(root, fullPath)))
            throw new InvalidOperationException("Session path is outside configured Codex session roots");
        RejectReparsePoints(fullPath);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var truncated = stream.Length > MaximumReadBytes;
        if (truncated) stream.Seek(-MaximumReadBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: !truncated);
        if (truncated) _ = reader.ReadLine();
        var text = new StringBuilder((int)Math.Min(stream.Length, MaximumReadBytes));
        var buffer = new char[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    public string? ReadSessionSnapshotJson(string? threadId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(threadId, out var parsedThreadId)) return null;
        var requestedId = parsedThreadId.ToString();
        SessionSnapshotEntry? newest = null;
        foreach (var root in sessionRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                var candidates = EnumerateTargetFiles(root, requestedId, cancellationToken).ToArray();
                var canonical = candidates.Where(path => !Path.GetFileName(path).StartsWith("BACKUP_", StringComparison.OrdinalIgnoreCase)).ToArray();
                files = canonical.Length > 0 ? canonical : candidates;
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SessionSnapshotEntry? entry;
                try
                {
                    var info = new FileInfo(path);
                    var cacheKey = Path.GetFullPath(path);
                    if (snapshotCache.TryGetValue(cacheKey, out var cached)
                        && cached.LastWriteUtc == info.LastWriteTimeUtc
                        && cached.Length == info.Length)
                    {
                        entry = cached.Entry;
                    }
                    else
                    {
                        entry = ReadSessionSnapshot(path, cancellationToken);
                        snapshotCache[cacheKey] = new(info.LastWriteTimeUtc, info.Length, entry);
                    }
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (entry is not null && (newest is null || entry.Timestamp > newest.Timestamp)) newest = entry;
            }
        }
        return newest is null
            ? null
            : JsonSerializer.Serialize(new { threadId = requestedId, @event = newest.Event, compactions = newest.Compactions });
    }

    public string? ReadTurnMetadataJson(string? threadId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(threadId, out var parsedThreadId)) return null;
        var requestedId = parsedThreadId.ToString();
        foreach (var root in sessionRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            string[] candidates;
            try
            {
                candidates = EnumerateTargetFiles(root, requestedId, cancellationToken).ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            var canonical = candidates.Where(path => !Path.GetFileName(path).StartsWith("BACKUP_", StringComparison.OrdinalIgnoreCase)).ToArray();
            var selected = (canonical.Length > 0 ? canonical : candidates)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (selected is null) continue;
            try
            {
                RejectReparsePointsForSnapshot(selected);
                return TurnMetadataSessionReader.ReadJson(selected, requestedId, cancellationToken);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    public string? ReadHookTraceJson(string? threadId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(threadId, out var parsedThreadId)) return null;
        var requestedId = parsedThreadId.ToString();
        foreach (var root in sessionRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            string[] candidates;
            try
            {
                candidates = EnumerateTargetFiles(root, requestedId, cancellationToken)
                    .Where(path => DeclaresRolloutId(path, requestedId, cancellationToken))
                    .ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            var canonical = candidates.Where(path => !Path.GetFileName(path).StartsWith("BACKUP_", StringComparison.OrdinalIgnoreCase)).ToArray();
            var selected = (canonical.Length > 0 ? canonical : candidates)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selected.Length == 0) continue;
            try
            {
                foreach (var path in selected) RejectReparsePointsForSnapshot(path);
                return HookTraceSessionReader.ReadJson(selected, requestedId, cancellationToken);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private IEnumerable<string> EnumerateTargetFiles(string root, string requestedId, CancellationToken cancellationToken)
    {
        // Native UUIDv7 rollouts live under sessions/yyyy/MM/dd. Include adjacent
        // UTC days for timezone boundaries, plus flat imported rollouts. Custom
        // roots, archive roots, and older IDs retain the general discovery path.
        DateTimeOffset? created = null;
        if (Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Equals("sessions", StringComparison.OrdinalIgnoreCase)
            && requestedId[14] == '7')
        {
            var milliseconds = Convert.ToInt64(requestedId.Replace("-", string.Empty)[..12], 16);
            try { created = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        var locations = new List<(string Path, bool Recursive)> { (root, created is null) };
        if (created is { } time)
        {
            foreach (var offset in new[] { 0, -1, 1 })
            {
                var day = time.AddDays(offset);
                locations.Add((Path.Combine(root, day.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    day.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), day.ToString("dd", System.Globalization.CultureInfo.InvariantCulture)), true));
            }
        }
        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] paths;
            try
            {
                paths = enumerateFiles(location.Path, $"*{requestedId}*.jsonl", new EnumerationOptions
                {
                    RecurseSubdirectories = location.Recursive,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true,
                }).ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }
        }
    }

    private bool DeclaresRolloutId(string path, string requestedId, CancellationToken cancellationToken)
    {
        try
        {
            RejectReparsePointsForSnapshot(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            for (var lineNumber = 0; lineNumber < 64 && reader.ReadLine() is { } line; lineNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var row = document.RootElement;
                    if (!row.TryGetProperty("type", out var type) || type.GetString() != "session_meta") continue;
                    if (!row.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return false;
                    return payload.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && string.Equals(id.GetString(), requestedId, StringComparison.OrdinalIgnoreCase);
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    private SessionSnapshotEntry? ReadSessionSnapshot(string path, CancellationToken cancellationToken)
    {
        RejectReparsePointsForSnapshot(path);
        JsonElement? latestEvent = null;
        DateTimeOffset latestTimestamp = DateTimeOffset.MinValue;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var truncated = stream.Length > MaximumFullSnapshotScanBytes;
        var compactions = truncated ? CountCompactionMarkers(path, cancellationToken) : 0;
        if (truncated) stream.Seek(-MaximumSnapshotScanBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: !truncated);
        if (truncated) _ = reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.IndexOf("token_count", StringComparison.OrdinalIgnoreCase) < 0
                && line.IndexOf("compact", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var payload = root.TryGetProperty("payload", out var payloadValue) ? payloadValue : default;
                var type = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("type", out var payloadType)
                    ? payloadType.GetString() : root.TryGetProperty("type", out var rootType) ? rootType.GetString() : null;
                if (!truncated && (string.Equals(type, "compacted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "context_compacted", StringComparison.OrdinalIgnoreCase))) compactions++;
                if (!string.Equals(type, "token_count", StringComparison.Ordinal)) continue;
                if (!root.TryGetProperty("timestamp", out var timestampValue)
                    || !DateTimeOffset.TryParse(timestampValue.GetString(), out var timestamp)) continue;
                if (timestamp >= latestTimestamp)
                {
                    latestTimestamp = timestamp;
                    latestEvent = root.Clone();
                }
            }
            catch (JsonException) { }
        }
        return latestEvent is { } value
            ? new(latestTimestamp, value, compactions)
            : null;
    }

    private static int CountCompactionMarkers(string path, CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;
        var markers = new[] { "\"type\":\"compacted\"", "\"type\":\"context_compacted\"" };
        var carry = string.Empty;
        var count = 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[bufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = carry + Encoding.UTF8.GetString(buffer, 0, read);
            foreach (var marker in markers)
            {
                var offset = 0;
                while ((offset = text.IndexOf(marker, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    count++;
                    offset += marker.Length;
                }
            }
            var carryLength = markers.Max(marker => marker.Length) - 1;
            carry = text.Length > carryLength ? text[^carryLength..] : text;
        }
        return count;
    }

    private sealed record CachedSnapshot(DateTime LastWriteUtc, long Length, SessionSnapshotEntry? Entry);
    private sealed record SessionSnapshotEntry(DateTimeOffset Timestamp, JsonElement Event, int Compactions);

    private void RejectReparsePointsForSnapshot(string path)
    {
        var root = sessionRoots.First(configured => IsWithin(configured, path));
        var current = Path.GetFullPath(root);
        foreach (var component in Path.GetRelativePath(current, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Session paths may not use symbolic links or junctions");
        }
    }

    private string RequireConfiguredRoot(string root)
    {
        var fullPath = Path.GetFullPath(root);
        return sessionRoots.FirstOrDefault(configured => configured.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Session root is not configured");
    }

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void RejectReparsePoints(string path)
    {
        var root = sessionRoots.First(configured => IsWithin(configured, path));
        var current = Path.GetFullPath(root);
        foreach (var component in Path.GetRelativePath(current, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Session paths may not use symbolic links or junctions");
        }
    }
}
