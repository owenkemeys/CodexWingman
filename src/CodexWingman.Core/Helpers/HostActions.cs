using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace CodexWingman.Core.Helpers;

public sealed record HostActionRequest(
    string HelperId,
    string Action,
    JsonElement? Payload,
    CodexTarget? SourceTarget = null);

public interface IHostActionDispatcher
{
    Task DispatchAsync(HostActionRequest request, CancellationToken cancellationToken = default);
}

public sealed class HostActionDispatcher : IHostActionDispatcher
{
    public const string OpenNewChatWindow = "codex.openNewChatWindow";
    public const string OpenObsidianUri = "system.openObsidianUri";
    public const string OpenJarvisPath = "system.openJarvisPath";
    public const string OpenChild = "wingman.openChild";
    public const string SystemHelperId = "__codex-wingman-system";
    private readonly ICdpEvaluator evaluator;
    private readonly Func<string, CancellationToken, Task> openObsidianUri;
    private readonly Func<string, CancellationToken, Task> openJarvisPath;

    public HostActionDispatcher(
        ICdpEvaluator evaluator,
        Func<string, CancellationToken, Task>? openObsidianUri = null,
        Func<string, CancellationToken, Task>? openJarvisPath = null)
    {
        this.evaluator = evaluator;
        this.openObsidianUri = openObsidianUri ?? LaunchObsidianUriAsync;
        this.openJarvisPath = openJarvisPath ?? LaunchJarvisPathAsync;
    }

    public Task DispatchAsync(HostActionRequest request, CancellationToken cancellationToken = default) =>
        request.Action switch
        {
            OpenNewChatWindow => CodexOpenNewChatWindowAdapter.DispatchAsync(evaluator, request, cancellationToken),
            OpenObsidianUri => ObsidianUriOpenAdapter.DispatchAsync(request, openObsidianUri, cancellationToken),
            OpenJarvisPath => JarvisPathOpenAdapter.DispatchAsync(request, openJarvisPath, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown Host Action '{request.Action}'"),
        };

    public static string RequiredCapability(string action) => action switch
    {
        OpenNewChatWindow => OpenNewChatWindow,
        OpenChild => OpenNewChatWindow,
        OpenObsidianUri => OpenObsidianUri,
        OpenJarvisPath => OpenJarvisPath,
        _ => throw new InvalidOperationException($"Unknown Host Action '{action}'"),
    };

    public static bool IsKnownCapability(string? capability) =>
        string.Equals(capability, OpenNewChatWindow, StringComparison.Ordinal)
        || string.Equals(capability, OpenObsidianUri, StringComparison.Ordinal)
        || string.Equals(capability, OpenJarvisPath, StringComparison.Ordinal);

    private static Task LaunchObsidianUriAsync(string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Windows did not start the Obsidian URI handler");
        return Task.CompletedTask;
    }

    private static async Task LaunchJarvisPathAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existingPath = JarvisPathOpenAdapter.RequireExistingFileOrDirectory(path);
        var isDirectory = Directory.Exists(existingPath);
        var process = Process.Start(new ProcessStartInfo(existingPath) { UseShellExecute = true });
        if (process is null)
        {
            if (isDirectory) return;
            throw new InvalidOperationException("Windows did not start the registered application for the Jarvis file");
        }
        _ = await WindowsForegroundActivator.ActivateProcessAsync(process, cancellationToken);
    }

}

public static class JarvisPathOpenAdapter
{
    public static string RequireExistingFileOrDirectory(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;
        var repaired = ResolveMarkdownEscapedSeparators(path);
        if (repaired is not null) return repaired;
        throw new FileNotFoundException("The file or folder is not available to Windows", path);
    }

    private static string? ResolveMarkdownEscapedSeparators(string path)
    {
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '/') return null;
        var segments = path[3..].Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0)) return null;

        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = 256;
        ResolveFrom(Path.GetPathRoot(path)!, segments, 0, matches, ref budget);
        return matches.Count == 1 ? matches.Single() : null;
    }

    private static void ResolveFrom(
        string current,
        string[] segments,
        int index,
        HashSet<string> matches,
        ref int budget)
    {
        if (budget <= 0 || matches.Count > 1) return;
        var isLast = index == segments.Length - 1;
        var exact = Path.Combine(current, segments[index]);
        var exactExists = isLast ? File.Exists(exact) || Directory.Exists(exact) : Directory.Exists(exact);
        if (exactExists)
        {
            if (isLast) matches.Add(Path.GetFullPath(exact));
            else ResolveFrom(exact, segments, index + 1, matches, ref budget);
            return;
        }

        foreach (var parts in MarkdownSeparatorPartitions(segments[index]))
        {
            if (budget-- <= 0 || matches.Count > 1) return;
            var candidate = current;
            var valid = true;
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                candidate = Path.Combine(candidate, parts[partIndex]);
                var finalPart = partIndex == parts.Length - 1;
                var needsDirectory = !isLast || !finalPart;
                if (needsDirectory ? !Directory.Exists(candidate) : !File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid) continue;
            if (isLast) matches.Add(Path.GetFullPath(candidate));
            else ResolveFrom(candidate, segments, index + 1, matches, ref budget);
        }
    }

    private static IEnumerable<string[]> MarkdownSeparatorPartitions(string segment)
    {
        var splitPositions = Enumerable.Range(1, Math.Max(0, segment.Length - 1))
            .Where(index => IsMarkdownEscapablePunctuation(segment[index]))
            .Take(8)
            .ToArray();
        if (splitPositions.Length == 0) yield break;

        var combinations = 1 << splitPositions.Length;
        for (var mask = 1; mask < combinations; mask++)
        {
            var parts = new List<string>();
            var start = 0;
            for (var index = 0; index < splitPositions.Length; index++)
            {
                if ((mask & (1 << index)) == 0) continue;
                var split = splitPositions[index];
                parts.Add(segment[start..split]);
                start = split;
            }
            parts.Add(segment[start..]);
            if (parts.Any(part => part is "" or "." or "..")) continue;
            yield return parts.ToArray();
        }
    }

    private static bool IsMarkdownEscapablePunctuation(char character) =>
        character is >= '!' and <= '/'
        || character is >= ':' and <= '@'
        || character is >= '[' and <= '`'
        || character is >= '{' and <= '~';

    public static string Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || payload.EnumerateObject().Count() != 1
            || !payload.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("system.openJarvisPath requires only path");

        var path = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)
            || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '/'
            || path.Contains('\\'))
            throw new InvalidOperationException("system.openJarvisPath requires a normalized absolute Windows drive file or folder path");

        var suffix = path[3..];
        if (suffix.Length > 0 && (suffix.EndsWith('/')
            || suffix.Split('/').Any(segment => segment is "" or "." or ".."
                || segment.IndexOfAny("<>:\"|?*".ToCharArray()) >= 0)))
            throw new InvalidOperationException("system.openJarvisPath requires a normalized absolute Windows drive file or folder path");
        return path;
    }

    public static Task DispatchAsync(
        HostActionRequest request,
        Func<string, CancellationToken, Task> launcher,
        CancellationToken cancellationToken = default) =>
        launcher(Parse(request.Payload ?? throw new InvalidOperationException("system.openJarvisPath requires a payload")), cancellationToken);
}

public static class ObsidianUriOpenAdapter
{
    private static readonly TimeSpan PluginStartupDelay = TimeSpan.FromSeconds(4);

    public static string Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || payload.EnumerateObject().Count() != 1
            || !payload.TryGetProperty("uri", out var uriElement)
            || uriElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("system.openObsidianUri requires only uri");

        var text = uriElement.GetString();
        if (string.IsNullOrEmpty(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("obsidian", StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("wait-for-note", StringComparison.OrdinalIgnoreCase))
            || uri.UserInfo.Length != 0 || uri.Port != -1 || uri.Fragment.Length != 0
            || (uri.AbsolutePath.Length != 0 && uri.AbsolutePath != "/"))
            throw new InvalidOperationException("system.openObsidianUri requires an obsidian://open or obsidian://wait-for-note URI");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = component.Split('=', 2);
            if (pair.Length != 2 || !values.TryAdd(pair[0], Uri.UnescapeDataString(pair[1])))
                throw new InvalidOperationException("system.openObsidianUri requires unique vault and file parameters");
        }
        if (values.Count != 2 || !values.TryGetValue("vault", out var vault) || !values.TryGetValue("file", out var file)
            || string.IsNullOrWhiteSpace(vault) || string.IsNullOrWhiteSpace(file)
            || vault.Any(character => character is '/' or '\\' || char.IsControl(character))
            || file.Any(char.IsControl))
            throw new InvalidOperationException("system.openObsidianUri requires nonblank vault and file parameters");

        var note = file.Split('#', 2)[0];
        if (!note.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || note.StartsWith('/')
            || note.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException("system.openObsidianUri requires a vault-relative Markdown note");
        return text;
    }

    public static async Task DispatchAsync(
        HostActionRequest request,
        Func<string, CancellationToken, Task> launcher,
        CancellationToken cancellationToken = default)
    {
        var text = Parse(request.Payload ?? throw new InvalidOperationException("system.openObsidianUri requires a payload"));
        var uri = new Uri(text);
        if (!uri.Host.Equals("wait-for-note", StringComparison.OrdinalIgnoreCase))
        {
            await launcher(text, cancellationToken);
            return;
        }

        var vaultComponent = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(component => component.StartsWith("vault=", StringComparison.Ordinal));
        await launcher($"obsidian://open?{vaultComponent}", cancellationToken);
        await Task.Delay(PluginStartupDelay, cancellationToken);
        await launcher(text, cancellationToken);
    }
}

public sealed record ExactChildBootstrapRequest(string HelperId, string Path, JsonElement Bootstrap)
{
    private const int MaximumBootstrapBytes = 16 * 1024;
    public bool RequiresCompletionAcknowledgement => !Bootstrap.TryGetProperty("controlKind", out _);

    public static ExactChildBootstrapRequest Parse(string helperId, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(helperId)
            || payload.ValueKind != JsonValueKind.Object
            || payload.EnumerateObject().Count() != 2
            || !payload.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String
            || !payload.TryGetProperty("bootstrap", out var bootstrap))
            throw new InvalidOperationException("wingman.openChild requires only path and bootstrap");

        var path = pathElement.GetString();
        if (path == "/")
        {
            if (!IsValidRootBootstrap(bootstrap))
                throw new InvalidOperationException("wingman.openChild root requires an exact native New Chat bootstrap");
        }
        else if (!CodexOpenNewChatWindowAdapter.IsSafeThreadRoute(path))
        {
            throw new InvalidOperationException("wingman.openChild requires a safe route");
        }
        if (Encoding.UTF8.GetByteCount(bootstrap.GetRawText()) > MaximumBootstrapBytes)
            throw new InvalidOperationException("wingman.openChild bootstrap exceeds 16 KiB");

        return new(helperId, path!, bootstrap.Clone());
    }

    private static bool IsValidRootBootstrap(JsonElement bootstrap)
    {
        if (bootstrap.ValueKind != JsonValueKind.Object
            || bootstrap.EnumerateObject().Count() is < 2 or > 3
            || !bootstrap.TryGetProperty("mode", out var mode)
            || mode.ValueKind != JsonValueKind.String
            || mode.GetString() != "native-new-chat"
            || !bootstrap.TryGetProperty("controlLabel", out var labelElement)
            || labelElement.ValueKind != JsonValueKind.String)
            return false;

        var label = labelElement.GetString();
        var generic = label is not null && Regex.IsMatch(
            label,
            "^(?:new (?:chat|task|thread|conversation)|(?:start|create)(?: a)? new (?:chat|task|thread|conversation))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var validKind = !bootstrap.TryGetProperty("controlKind", out var kindElement)
            ? !generic
            : kindElement.ValueKind == JsonValueKind.String
                && kindElement.GetString() is "semantic" or "sidebar-row" or "collapsed-header";
        return validKind
            && !string.IsNullOrEmpty(label)
            && label.Length <= 512
            && label == Regex.Replace(label, "\\s+", " ").Trim()
            && !label.Any(char.IsControl)
            && (Regex.IsMatch(
                    label,
                    "^(?:new (?:chat|task|thread|conversation)|(?:start|create)(?: a)? new (?:chat|task|thread|conversation))$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    label,
                    "^(?:start|create)(?: a)? new (?:chat|task|thread|conversation) in [\\p{L}\\p{N}][\\p{L}\\p{N} .()_&'\\u2019+\\-]*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}

public static class CodexOpenNewChatWindowAdapter
{
    public static string BuildExpression(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || payload.EnumerateObject().Count() != 1
            || !payload.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("codex.openNewChatWindow requires only a renderer-derived path");
        }

        var path = pathElement.GetString();
        if (!IsSafeThreadRoute(path))
        {
            throw new InvalidOperationException("codex.openNewChatWindow requires a safe non-root route");
        }

        return $"window.electronBridge.sendMessageFromView({{type:'open-in-new-window',path:{JsonSerializer.Serialize(path)}}})";
    }

    internal static bool IsSafeThreadRoute(string? route)
    {
        if (string.IsNullOrEmpty(route) || !route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal))
            return false;

        var candidate = route;
        for (var depth = 0; depth < 4; depth++)
        {
            if (candidate.Any(character => character == '\\' || char.IsControl(character))
                || Regex.IsMatch(candidate, "%2f|%5c", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return false;
            var path = candidate.Split('?', '#')[0];
            if (path == "/" || path.StartsWith("//", StringComparison.Ordinal)
                || path.Split('/').Any(segment => segment == ".."))
                return false;

            var decoded = Uri.UnescapeDataString(candidate);
            if (decoded == candidate) return true;
            candidate = decoded;
        }
        return false;
    }

    public static Task DispatchAsync(
        ICdpEvaluator evaluator,
        HostActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var target = request.SourceTarget
            ?? throw new InvalidOperationException("codex.openNewChatWindow requires its originating Codex target");
        if (request.Payload is null)
        {
            if (!string.Equals(request.HelperId, HostActionDispatcher.SystemHelperId, StringComparison.Ordinal))
                throw new InvalidOperationException("codex.openNewChatWindow requires a payload");
            return evaluator.EvaluateAsync(
                target,
                "window.electronBridge.sendMessageFromView({type:'open-in-new-window',path:'/'})",
                cancellationToken);
        }
        return evaluator.EvaluateAsync(target, BuildExpression(request.Payload.Value), cancellationToken);
    }
}
