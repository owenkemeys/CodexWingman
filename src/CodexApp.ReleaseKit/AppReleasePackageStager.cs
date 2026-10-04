using System.IO.Compression;
using System.Security.Cryptography;

namespace CodexApp.ReleaseKit;

public sealed class AppReleasePackageStager(
    HttpClient http,
    string repository,
    string schema,
    string executableName,
    string archiveRoot,
    string extensionPrefix)
{
    private const long MaximumArchiveBytes = 500L * 1024 * 1024;

    public async Task<(string Package, string UpdateScript)> StageAsync(
        AppRelease release, CancellationToken cancellationToken = default)
    {
        if (archiveRoot is "." or ".."
            || !System.Text.RegularExpressions.Regex.IsMatch(archiveRoot, "^[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Archive root must be a single directory name.", nameof(archiveRoot));
        var root = Path.Combine(Path.GetTempPath(), "CodexApp-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "download.zip");
            using (var response = await http.GetAsync(
                release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumArchiveBytes)
                    throw new InvalidDataException("Release download is unexpectedly large.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(zip);
                var buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > MaximumArchiveBytes)
                        throw new InvalidDataException("Release download is unexpectedly large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            await using (var archiveStream = File.OpenRead(zip))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken));
                if (!digest.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Release download did not match GitHub's SHA-256 digest.");
            }

            var extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(extracted);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    if (!path.StartsWith(archiveRoot + "/", StringComparison.Ordinal)
                        || path.TrimEnd('/').Split('/').Any(part => part is "" or "." or "..")
                        || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new InvalidDataException("Release archive contains an unsafe path or link.");
                }
                archive.ExtractToDirectory(extracted);
            }
            var package = Path.Combine(extracted, archiveRoot);
            SealedReleasePackage.Verify(
                package, repository, release.Version, schema, executableName, extensionPrefix);
            var exeVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(
                Path.Combine(package, executableName)).FileVersion;
            if (!Version.TryParse(exeVersion, out var binaryVersion)
                || binaryVersion.Major != release.Version.Major
                || binaryVersion.Minor != release.Version.Minor
                || binaryVersion.Build != release.Version.Build)
                throw new InvalidDataException("Release executable version does not match the download.");
            var installedScript = Path.Combine(package, "Apply-CodexAppUpdate.ps1");
            var updateScript = Path.Combine(root, "Apply-CodexAppUpdate.ps1");
            File.Copy(installedScript, updateScript);
            return (package, updateScript);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }
}
