using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexApp.ReleaseKit;

/// <summary>Shared GitHub release discovery contract for Codex desktop companions.</summary>
public sealed record AppRelease(
    Version Version,
    string Tag,
    string Summary,
    Uri DownloadUrl,
    string Sha256);

public sealed class AppReleaseClient(HttpClient http, string productName)
{
    public async Task<AppRelease?> CheckAsync(
        string repository,
        string assetStem,
        Version installedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Invalid GitHub repository", nameof(repository));
        if (!System.Text.RegularExpressions.Regex.IsMatch(assetStem, @"^[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Invalid release asset stem", nameof(assetStem));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(productName, "1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = release.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version)
            || version.Revision >= 0 || version.Build < 0 || version <= installedVersion)
            return null;
        var expectedAsset = $"{assetStem}-{tag}-win-x64.zip";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), expectedAsset, StringComparison.Ordinal))
                continue;
            if (!string.Equals(asset.GetProperty("state").GetString(), "uploaded", StringComparison.Ordinal))
                return null;
            var digest = asset.GetProperty("digest").GetString();
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
                version,
                tag,
                Summarize(release.TryGetProperty("body", out var body) ? body.GetString() : null),
                url,
                digest[7..].ToLowerInvariant());
        }
        return null;
    }

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
