using System.Text;
using System.Text.Json;

namespace CodexWingman.Core;

public sealed class SessionQuotaSource(
    IEnumerable<string> roots,
    Func<DateTimeOffset>? clock = null) : IQuotaSource
{
    private const int FilesPerRoot = 16;
    private const int MaxTailBytes = 16 * 1024 * 1024;
    private readonly string[] roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);

    public static IReadOnlyList<string> DefaultRoots(string? userProfile = null)
    {
        userProfile ??= Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(userProfile, ".codex", "sessions"),
            Path.Combine(userProfile, ".codex", "archived_sessions"),
        ];
    }

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        SessionSnapshot? newest = null;
        foreach (var file in FindCandidateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await TryReadLatestAsync(file.FullName, cancellationToken);
            if (candidate is not null && (newest is null || candidate.Timestamp > newest.Timestamp))
                newest = candidate;
        }

        return newest is null ? QuotaSnapshot.Unavailable : RemoveExpiredWindows(newest.Snapshot, clock());
    }

    private IEnumerable<FileInfo> FindCandidateFiles()
    {
        foreach (var root in roots)
        {
            IEnumerable<FileInfo> files;
            try
            {
                if (!Directory.Exists(root)) continue;
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(FilesPerRoot)
                    .ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files) yield return file;
        }
    }

    private static async Task<SessionSnapshot?> TryReadLatestAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);

            var bytesToRead = (int)Math.Min(stream.Length, MaxTailBytes);
            if (bytesToRead == 0) return null;
            var buffer = new byte[bytesToRead];
            stream.Seek(-bytesToRead, SeekOrigin.End);
            var read = 0;
            while (read < bytesToRead)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read, bytesToRead - read), cancellationToken);
                if (count == 0) break;
                read += count;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (stream.Length > bytesToRead)
            {
                var firstNewline = text.IndexOf('\n');
                text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
            }

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var parsed = TryParse(lines[index]);
                if (parsed is not null) return parsed;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return null;
    }

    private static SessionSnapshot? TryParse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("type", out var eventType)
                || eventType.GetString() != "token_count")
                return null;

            var snapshot = QuotaPayloadNormalizer.Normalize(payload);
            if (snapshot == QuotaSnapshot.Unavailable) return null;

            if (!root.TryGetProperty("timestamp", out var timestampValue)
                || !DateTimeOffset.TryParse(timestampValue.GetString(), out var timestamp))
                return null;

            return new SessionSnapshot(timestamp, snapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static QuotaSnapshot RemoveExpiredWindows(QuotaSnapshot snapshot, DateTimeOffset now) => new(
        IsCurrent(snapshot.Primary, now) ? snapshot.Primary : null,
        IsCurrent(snapshot.Secondary, now) ? snapshot.Secondary : null);

    private static bool IsCurrent(QuotaWindow? window, DateTimeOffset now)
    {
        if (window is null) return false;
        if (window.ResetsAtUnixSeconds is null) return true;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(window.ResetsAtUnixSeconds.Value) > now;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record SessionSnapshot(DateTimeOffset Timestamp, QuotaSnapshot Snapshot);
}
