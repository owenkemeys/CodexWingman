using System.Security.Cryptography;
using System.Text.Json;

namespace CodexApp.ReleaseKit;

/// <summary>Checks the exact package manifest before an update can touch an installation.</summary>
public static class SealedReleasePackage
{
    public static void Verify(
        string directory,
        string repository,
        Version version,
        string schema,
        string executableName,
        string extensionPrefix)
    {
        var root = Path.GetFullPath(directory);
        var recordPath = Path.Combine(root, "release.json");
        using var document = JsonDocument.Parse(File.ReadAllText(recordPath));
        var record = document.RootElement;
        if (record.GetProperty("schema").GetString() != schema
            || record.GetProperty("repository").GetString() != repository
            || record.GetProperty("version").GetString() != $"{version.Major}.{version.Minor}.{version.Build}")
            throw new InvalidDataException("Release identity does not match the selected update.");
        var expected = record.GetProperty("files").EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetString() ?? "", StringComparer.Ordinal);
        if (!expected.ContainsKey(executableName)
            || !expected.Keys.Any(key => key.StartsWith(extensionPrefix, StringComparison.Ordinal)))
            throw new InvalidDataException("Release package is incomplete.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Release package contains a linked directory.");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetFullPath(file);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Release package contains a link.");
            if (relative == "release.json") continue;
            if (!expected.TryGetValue(relative, out var sha256)
                || sha256.Length != 64
                || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Release package file failed verification: {relative}");
            found.Add(relative);
        }
        if (found.Count != expected.Count)
            throw new InvalidDataException("Release package is missing a file.");
    }
}
