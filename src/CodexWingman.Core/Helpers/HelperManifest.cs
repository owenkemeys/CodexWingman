using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWingman.Core.Helpers;

public sealed record HelperManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("refreshSeconds")] int RefreshSeconds,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("entrypoints")] HelperEntrypoints? Entrypoints,
    [property: JsonPropertyName("config")] JsonElement? Config = null,
    [property: JsonPropertyName("targets")] IReadOnlyDictionary<string, HelperEntrypoints>? Targets = null);

public static class HelperAppIds
{
    public const string Codex = "codex";
    public const string T3Code = "t3-code";
    /// <summary>The T3 web client in Wingman's own Chrome app window (an https origin from local settings).</summary>
    public const string T3Web = "t3-web";
    /// <summary>The Grok Bot desktop app, relaunched once with a localhost-only debugging port.</summary>
    public const string GrokBot = "grok-bot";

    public static bool IsKnown(string? value) => value is Codex or T3Code or T3Web or GrokBot;
}

public sealed record HelperEntrypoints(
    [property: JsonPropertyName("backend")] string? Backend,
    [property: JsonPropertyName("apply")] string Apply,
    [property: JsonPropertyName("remove")] string Remove);

public enum HelperPackageSource
{
    Bundled,
    User,
}

public sealed record HelperPackage(
    HelperManifest Manifest,
    string DirectoryPath,
    HelperPackageSource Source,
    string ApplySource,
    string RemoveSource,
    string? BackendSource,
    bool OverridesBundled = false)
{
    // Catalog projects the selected app's entrypoints into Manifest before creating a package.
    public string ApplyPath => Path.Combine(DirectoryPath, Manifest.Entrypoints!.Apply);
    public string RemovePath => Path.Combine(DirectoryPath, Manifest.Entrypoints!.Remove);
    public string? BackendPath => Manifest.Entrypoints!.Backend is { } value
        ? Path.Combine(DirectoryPath, value)
        : null;
}

public sealed record HelperDiagnostic(string? HelperId, string Message);

public sealed record HelperCatalogReport(
    IReadOnlyList<HelperPackage> Packages,
    IReadOnlyList<HelperDiagnostic> Diagnostics);

public sealed record HelperSummary(
    string Id,
    string Name,
    string Version,
    bool Enabled,
    string? Diagnostic,
    int RefreshSeconds,
    bool CanToggle = true,
    IReadOnlyList<string>? Capabilities = null,
    HelperPackageSource? Source = null);

public sealed record HelperHostReport(
    int HelpersAttempted,
    int TargetsDiscovered,
    int Succeeded,
    int Failed,
    IReadOnlyList<HelperDiagnostic> Diagnostics)
{
    public static HelperHostReport Empty { get; } = new(0, 0, 0, 0, []);
}
