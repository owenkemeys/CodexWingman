using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexWingman.Core.Helpers;

public sealed partial class HelperCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HelperCatalogReport Discover(string helpersRoot)
    {
        var diagnostics = new List<HelperDiagnostic>();
        var discovery = DiscoverRoot(helpersRoot, HelperPackageSource.User, diagnostics);
        return new(discovery.Packages, diagnostics);
    }

    public HelperCatalogReport Discover(string bundledRoot, string userRoot)
    {
        var diagnostics = new List<HelperDiagnostic>();
        var bundled = DiscoverRoot(bundledRoot, HelperPackageSource.Bundled, diagnostics);
        var user = DiscoverRoot(userRoot, HelperPackageSource.User, diagnostics);
        var selected = bundled.Packages.ToDictionary(package => package.Manifest.Id, StringComparer.Ordinal);
        var bundledIds = selected.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var identifiedUserId in user.IdentifiedIds)
            selected.Remove(identifiedUserId);
        foreach (var package in user.Packages)
            selected[package.Manifest.Id] = package with { OverridesBundled = bundledIds.Contains(package.Manifest.Id) };
        return new(
            selected.Values.OrderBy(package => package.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
    }

    private static RootDiscovery DiscoverRoot(
        string root,
        HelperPackageSource source,
        List<HelperDiagnostic> diagnostics)
    {
        if (!Directory.Exists(root)) return new([], new HashSet<string>(StringComparer.Ordinal));

        var candidates = new List<HelperPackage>();
        var identifiedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add(new(null, $"{Path.GetFileName(directory)}: package directory may not be a symbolic link or junction"));
                continue;
            }
            var manifestPath = Path.Combine(directory, "wingman.json");
            if (!File.Exists(manifestPath)) continue;
            string? identifiedId = null;
            try
            {
                var manifest = JsonSerializer.Deserialize<HelperManifest>(File.ReadAllText(manifestPath), JsonOptions)
                    ?? throw new InvalidDataException("Manifest is empty");
                if (!string.IsNullOrWhiteSpace(manifest.Id) && HelperIdPattern().IsMatch(manifest.Id))
                {
                    identifiedId = manifest.Id;
                    identifiedIds.Add(manifest.Id);
                }
                Validate(manifest, directory);
                candidates.Add(new(
                    manifest,
                    Path.GetFullPath(directory),
                    source,
                    File.ReadAllText(Path.Combine(directory, manifest.Entrypoints.Apply)),
                    File.ReadAllText(Path.Combine(directory, manifest.Entrypoints.Remove)),
                    manifest.Entrypoints.Backend is { } backend
                        ? File.ReadAllText(Path.Combine(directory, backend))
                        : null));
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnostics.Add(new(identifiedId, $"{Path.GetFileName(directory)}: {error.Message}"));
            }
        }

        var duplicateIds = candidates
            .GroupBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in duplicateIds)
            diagnostics.Add(new(id, $"Duplicate helper ID '{id}' in {source.ToString().ToLowerInvariant()} root"));
        return new(
            candidates.Where(package => !duplicateIds.Contains(package.Manifest.Id)).ToArray(),
            identifiedIds);
    }

    private static void Validate(HelperManifest manifest, string directory)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported schemaVersion {manifest.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !HelperIdPattern().IsMatch(manifest.Id))
            throw new InvalidDataException("ID must contain only lowercase ASCII letters, digits, and hyphens");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("Name is required");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Version is required");
        if (manifest.RefreshSeconds < 0)
            throw new InvalidDataException("refreshSeconds must not be negative");
        if (manifest.Capabilities is null)
            throw new InvalidDataException("Capabilities are required");
        if (manifest.Capabilities.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Capabilities must be nonblank strings");
        if (manifest.Entrypoints is null)
            throw new InvalidDataException("Entrypoints are required");
        ValidateEntrypoint(directory, manifest.Entrypoints.Apply, required: true, "apply");
        ValidateEntrypoint(directory, manifest.Entrypoints.Remove, required: true, "remove");
        if (manifest.Entrypoints.Backend is { } backend)
            ValidateEntrypoint(directory, backend, required: true, "backend");
    }

    private static void ValidateEntrypoint(string directory, string? relativePath, bool required, string name)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (required) throw new InvalidDataException($"{name} entrypoint is required");
            return;
        }
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{name} entrypoint must be relative");

        var packageRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!path.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{name} entrypoint escapes the package directory");
        if (!File.Exists(path))
            throw new InvalidDataException($"Missing {name} entrypoint '{relativePath}'");
        RejectReparsePoints(packageRoot, path, name);
    }

    private static void RejectReparsePoints(string packageRoot, string path, string name)
    {
        var root = packageRoot.TrimEnd(Path.DirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var component in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{name} entrypoint may not use symbolic links or junctions");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HelperIdPattern();

    private sealed record RootDiscovery(
        IReadOnlyList<HelperPackage> Packages,
        IReadOnlySet<string> IdentifiedIds);
}
