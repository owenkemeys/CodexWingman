using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;

namespace CodexApp.ReleaseKit;

/// <summary>Shared GitHub release discovery contract for Codex desktop companions.</summary>
public sealed record AppRelease(
    Version Version,
    string Tag,
    string Summary,
    Uri DownloadUrl,
    string Sha256);

/// <summary>Which releases an update check may offer: stable only, or stable and prereleases.</summary>
public enum AppReleaseChannel { Stable, Prerelease }

public enum AppReleaseCheckStatus { Current, NoStableRelease, NoCompatibleDownload, UpdateAvailable }
public sealed record AppReleaseCheck(AppReleaseCheckStatus Status, AppRelease? Release, string Message);

public sealed class AppReleaseClient(HttpClient http, string productName)
{
    public async Task<AppRelease?> CheckAsync(
        string repository, string assetStem, Version installedVersion,
        CancellationToken cancellationToken = default) =>
        (await CheckLatestAsync(repository, assetStem, installedVersion, cancellationToken)).Release;

    public Task<AppReleaseCheck> CheckLatestAsync(
        string repository,
        string assetStem,
        Version installedVersion,
        CancellationToken cancellationToken = default) =>
        CheckLatestAsync(repository, assetStem, installedVersion, AppReleaseChannel.Stable, cancellationToken);

    /// <summary>
    /// Finds the newest installable release above <paramref name="installedVersion"/>, from any older version
    /// (never only the next one). Releases are read from the repository's release list and ordered by their
    /// version, not by publish date. Drafts are never offered. Prereleases (GitHub's flag or a "-suffix" tag) are
    /// offered only on <see cref="AppReleaseChannel.Prerelease"/>. Build metadata ("+sha") is ignored on both the
    /// tag and the installed version. Tags that are not "v" plus a three-part version (for example another
    /// package's tags in the same repository) are skipped.
    /// </summary>
    public async Task<AppReleaseCheck> CheckLatestAsync(
        string repository,
        string assetStem,
        Version installedVersion,
        AppReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Invalid GitHub repository", nameof(repository));
        if (!System.Text.RegularExpressions.Regex.IsMatch(assetStem, @"^[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Invalid release asset stem", nameof(assetStem));
        ArgumentNullException.ThrowIfNull(installedVersion);
        var installed = new ReleaseVersion(
            installedVersion.Major, installedVersion.Minor, Math.Max(installedVersion.Build, 0), null);

        using var request = CreateRequest($"https://api.github.com/repos/{repository}/releases?per_page=100");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // GitHub also uses 404 for missing/private repositories. Establish
            // access before treating this as a repository with no stable release.
            using var repositoryRequest = CreateRequest($"https://api.github.com/repos/{repository}");
            using var repositoryResponse = await http.SendAsync(repositoryRequest, cancellationToken);
            repositoryResponse.EnsureSuccessStatusCode();
            return Result(AppReleaseCheckStatus.NoStableRelease);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub release list is not an array");

        var eligible = new List<(ReleaseVersion Version, string Tag, JsonElement Release)>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            var tag = release.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            if (tag is null || !tag.StartsWith('v') || !ReleaseVersion.TryParse(tag[1..], out var version)) continue;
            var flagged = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            if ((flagged || version.IsPrerelease) && channel != AppReleaseChannel.Prerelease) continue;
            eligible.Add((version, tag, release.Clone()));
        }
        if (eligible.Count == 0) return Result(AppReleaseCheckStatus.NoStableRelease);
        var newer = eligible
            .Where(candidate => candidate.Version.CompareTo(installed) > 0)
            .OrderByDescending(candidate => candidate.Version)
            .ToList();
        if (newer.Count == 0) return Result(AppReleaseCheckStatus.Current);
        // The newest release whose verified download is ready; a newer one still uploading doesn't hide it.
        foreach (var candidate in newer)
        {
            var found = Installable(repository, assetStem, candidate.Version, candidate.Tag, candidate.Release);
            if (found is not null) return Result(AppReleaseCheckStatus.UpdateAvailable, found);
        }
        return Result(AppReleaseCheckStatus.NoCompatibleDownload);
    }

    private static AppRelease? Installable(
        string repository, string assetStem, ReleaseVersion version, string tag, JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var expectedAsset = $"{assetStem}-{tag}-win-x64.zip";
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), expectedAsset, StringComparison.Ordinal))
                continue;
            if (!string.Equals(asset.GetProperty("state").GetString(), "uploaded", StringComparison.Ordinal))
                return null;
            var digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
            if (digest is null || !System.Text.RegularExpressions.Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$"))
                return null;
            var download = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(download, UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps
                || url.Host != "github.com"
                || !url.AbsolutePath.StartsWith($"/{repository}/releases/download/{Uri.EscapeDataString(tag)}/", StringComparison.Ordinal)
                || !url.AbsolutePath.EndsWith("/" + expectedAsset, StringComparison.Ordinal))
                return null;
            return new AppRelease(
                new Version(version.Major, version.Minor, version.Patch),
                tag,
                Summarize(release.TryGetProperty("body", out var body) ? body.GetString() : null),
                url,
                digest[7..].ToLowerInvariant());
        }
        return null;
    }

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(productName, "1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return request;
    }

    private AppReleaseCheck Result(AppReleaseCheckStatus status, AppRelease? release = null) => new(
        status, release, status switch
        {
            AppReleaseCheckStatus.Current => $"{productName} is up to date.",
            AppReleaseCheckStatus.NoStableRelease => $"No stable release of {productName} has been published yet.",
            AppReleaseCheckStatus.NoCompatibleDownload => "The latest release has no verified Windows download available yet.",
            _ => $"{productName} {release!.Tag} is available.",
        });

    public static string Summarize(string? body)
    {
        var lines = (body ?? "").Split('\n')
            .Select(line => line.Trim().TrimEnd('\r'))
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Take(5)
            .Select(line => "• " + line[2..]);
        var summary = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(summary) ? "A newer version is available." : summary;
    }
}

/// <summary>A release tag's version: three numbers, an optional prerelease part, build metadata dropped (SemVer 2).</summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ReleaseVersion>
{
    private static readonly System.Text.RegularExpressions.Regex Shape = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        var match = text is null ? null : Shape.Match(text);
        if (match is null || !match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
            return false;
        version = new ReleaseVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;
        if (Prerelease is null || other.Prerelease is null)
            return Prerelease is null ? (other.Prerelease is null ? 0 : 1) : -1;
        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftNumeric = int.TryParse(left[i], out var l);
            var rightNumeric = int.TryParse(right[i], out var r);
            var part = leftNumeric && rightNumeric ? l.CompareTo(r)
                : leftNumeric ? -1
                : rightNumeric ? 1
                : string.CompareOrdinal(left[i], right[i]);
            if (part != 0) return part;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : "-" + Prerelease);
}
