using System.Text.Json;

namespace CodexWingman.Core.Helpers;

public static class ProviderUsageSnapshotFile
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };

    public static string Read(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("providerUsageFile", out var setting)
            || setting.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(setting.GetString()))
            return "{\"providerUsage\":[],\"providerUsageConfigured\":false}";
        try
        {
            using var stream = File.OpenRead(Environment.ExpandEnvironmentVariables(setting.GetString()!));
            if (stream.Length > 65536) throw new InvalidDataException("Usage snapshot too large");
            var snapshot = JsonSerializer.Deserialize<Snapshot>(stream, Options);
            if (snapshot?.SchemaVersion != 1 || snapshot.Entries is not { Length: <= 64 })
                throw new InvalidDataException("Invalid usage snapshot");
            // Typed projection prevents extra fields, including secrets in an
            // accidentally configured file, from reaching a renderer.
            return JsonSerializer.Serialize(new { providerUsage = snapshot.Entries, providerUsageConfigured = true }, Options);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return "{\"providerUsage\":[],\"providerUsageConfigured\":true}";
        }
    }

    private sealed record Snapshot(int SchemaVersion, Entry[]? Entries);
    private sealed record Entry(string? EnvironmentId, string? InstanceId, string? Driver, string? AccountEmail, Limits? UsageLimits);
    private sealed record Limits(string? CheckedAt, Window[]? Windows);
    private sealed record Window(string? Id, string? Kind, string? Label, double? UsedPercent, string? ResetsAt, int? WindowDurationMins);
}
