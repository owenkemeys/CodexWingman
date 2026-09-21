using System.Text.Json;

namespace CodexWingman.Core;

public sealed record QuotaWindow(double UsedPercent, int? WindowMinutes, long? ResetsAtUnixSeconds);

public sealed record QuotaSnapshot(QuotaWindow? Primary, QuotaWindow? Secondary)
{
    public static QuotaSnapshot Unavailable { get; } = new(null, null);
}

public enum QuotaVisualState
{
    Unavailable,
    Neutral,
    Warning,
    Danger,
}

public static class QuotaPolicy
{
    public static QuotaVisualState Evaluate(QuotaWindow? window, DateTimeOffset now)
    {
        if (window is null) return QuotaVisualState.Unavailable;
        if (window.WindowMinutes is not > 0 || window.ResetsAtUnixSeconds is null)
            return QuotaVisualState.Neutral;

        var reset = DateTimeOffset.FromUnixTimeSeconds(window.ResetsAtUnixSeconds.Value);
        var duration = TimeSpan.FromMinutes(window.WindowMinutes.Value);
        var start = reset - duration;
        var elapsed = Math.Clamp((now - start).TotalMilliseconds / duration.TotalMilliseconds * 100d, 0d, 100d);
        if (window.UsedPercent <= elapsed + 1d) return QuotaVisualState.Neutral;
        return window.UsedPercent > 95d ? QuotaVisualState.Danger : QuotaVisualState.Warning;
    }
}

public static class QuotaPayloadNormalizer
{
    public static QuotaSnapshot Normalize(JsonElement payload)
    {
        var source = FindObject(payload, "rateLimitsByLimitId", "rate_limits_by_limit_id");
        if (source is { } byId && TryGet(byId, "codex", out var codex) && codex.ValueKind == JsonValueKind.Object)
            return NormalizeSnapshot(codex);

        var direct = FindObject(payload, "rateLimits", "rate_limits");
        return direct is { } snapshot ? NormalizeSnapshot(snapshot) : QuotaSnapshot.Unavailable;
    }

    private static QuotaSnapshot NormalizeSnapshot(JsonElement value)
    {
        var primary = TryGet(value, "primary", out var primaryValue) ? NormalizeWindow(primaryValue) : null;
        var secondary = TryGet(value, "secondary", out var secondaryValue) ? NormalizeWindow(secondaryValue) : null;
        var fiveHour = primary?.WindowMinutes == 300 ? primary : secondary?.WindowMinutes == 300 ? secondary : null;
        var weekly = primary?.WindowMinutes == 10080 ? primary : secondary?.WindowMinutes == 10080 ? secondary : null;
        return fiveHour is not null || weekly is not null
            ? new(
                fiveHour ?? (primary?.WindowMinutes == 10080 ? null : primary),
                weekly ?? (secondary?.WindowMinutes == 300 ? null : secondary))
            : new(primary, secondary);
    }

    private static QuotaWindow? NormalizeWindow(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var used = ReadDouble(value, "usedPercent", "used_percent");
        if (used is null) return null;
        return new(
            used.Value,
            ReadInt(value, "windowDurationMins", "window_minutes"),
            ReadLong(value, "resetsAt", "resets_at"));
    }

    private static JsonElement? FindObject(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(value, name, out var found) && found.ValueKind == JsonValueKind.Object) return found;
        return null;
    }

    private static bool TryGet(JsonElement value, string name, out JsonElement found)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.TryGetProperty(name, out found);
        found = default;
        return false;
    }

    private static double? ReadDouble(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(value, name, out var found) && found.TryGetDouble(out var result)) return result;
        return null;
    }

    private static int? ReadInt(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(value, name, out var found) && found.TryGetInt32(out var result)) return result;
        return null;
    }

    private static long? ReadLong(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(value, name, out var found) && found.TryGetInt64(out var result)) return result;
        return null;
    }
}
