using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexWingman.Core.Helpers;

public sealed partial class HelperCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HelperCatalogReport Discover(string helpersRoot) => DiscoverForApp(HelperAppIds.Codex, helpersRoot);

    public HelperCatalogReport DiscoverForApp(string appId, string helpersRoot)
    {
        var diagnostics = new List<HelperDiagnostic>();
        var discovery = DiscoverRoot(helpersRoot, HelperPackageSource.User, appId, diagnostics);
        return new(discovery.Packages, diagnostics);
    }

    public HelperCatalogReport Discover(string bundledRoot, string userRoot) =>
        DiscoverForApp(HelperAppIds.Codex, bundledRoot, userRoot);

    public HelperCatalogReport DiscoverForApp(string appId, string bundledRoot, string userRoot)
    {
        var diagnostics = new List<HelperDiagnostic>();
        var bundled = DiscoverRoot(bundledRoot, HelperPackageSource.Bundled, appId, diagnostics);
        var user = DiscoverRoot(userRoot, HelperPackageSource.User, appId, diagnostics);
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
        string appId,
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
                if (!ClaimsApp(manifest, appId)) continue;
                if (!string.IsNullOrWhiteSpace(manifest.Id) && HelperIdPattern().IsMatch(manifest.Id))
                {
                    identifiedId = manifest.Id;
                    identifiedIds.Add(manifest.Id);
                }
                var entrypoints = Validate(manifest, directory, appId);
                var selectedManifest = manifest with { Entrypoints = entrypoints };
                candidates.Add(new(
                    selectedManifest,
                    Path.GetFullPath(directory),
                    source,
                    File.ReadAllText(Path.Combine(directory, entrypoints.Apply)),
                    File.ReadAllText(Path.Combine(directory, entrypoints.Remove)),
                    entrypoints.Backend is { } backend
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

    private static bool ClaimsApp(HelperManifest manifest, string appId) => manifest.SchemaVersion switch
    {
        1 => appId == HelperAppIds.Codex,
        2 => manifest.Targets is null or { Count: 0 }
            ? appId == HelperAppIds.Codex
            : manifest.Targets.ContainsKey(appId),
        _ => appId == HelperAppIds.Codex,
    };

    private static HelperEntrypoints Validate(HelperManifest manifest, string directory, string appId)
    {
        if (manifest.SchemaVersion is not (1 or 2))
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
        if (appId != HelperAppIds.Codex
            && (manifest.Capabilities.Contains("files.codexSessions")
                || manifest.Capabilities.Contains("files.codexTargetSession")
                || manifest.Capabilities.Contains(HostActionDispatcher.OpenNewChatWindow)))
            throw new InvalidDataException("Codex-only capability cannot target another app");
        if (manifest.SchemaVersion == 1)
        {
            if (manifest.Entrypoints is null || manifest.Targets is not null)
                throw new InvalidDataException("Schema v1 requires entrypoints and does not support targets");
            ValidateEntrypoints(directory, manifest.Entrypoints);
            return manifest.Entrypoints;
        }
        if (manifest.Entrypoints is not null || manifest.Targets is not { Count: > 0 })
            throw new InvalidDataException("Schema v2 requires targets instead of entrypoints");
        foreach (var (targetApp, entrypoints) in manifest.Targets)
        {
            if (!HelperIdPattern().IsMatch(targetApp) || entrypoints is null)
                throw new InvalidDataException("Invalid target app entry");
            ValidateEntrypoints(directory, entrypoints);
        }
        return manifest.Targets[appId];
    }

    private static void ValidateEntrypoints(string directory, HelperEntrypoints entrypoints)
    {
        ValidateEntrypoint(directory, entrypoints.Apply, required: true, "apply");
        ValidateEntrypoint(directory, entrypoints.Remove, required: true, "remove");
        if (entrypoints.Backend is { } backend)
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
