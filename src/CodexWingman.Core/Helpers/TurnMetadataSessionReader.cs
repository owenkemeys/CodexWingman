using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWingman.Core.Helpers;

internal static class TurnMetadataSessionReader
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ReadJson(string path, string threadId, CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, MutableTurn>(StringComparer.Ordinal);
        string? sessionProvider = null;
        string? currentTurnId = null;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                var rowType = ReadString(row, "type");
                var payload = ReadObject(row, "payload");
                var payloadType = ReadString(payload, "type");
                var timestamp = ReadTimestamp(row, "timestamp");

                if (rowType == "session_meta")
                {
                    sessionProvider = ReadString(payload, "model_provider", "modelProvider") ?? sessionProvider;
                    continue;
                }

                var explicitTurnId = ReadString(payload, "turn_id", "turnId")
                    ?? ReadString(ReadObject(payload, "internal_chat_message_metadata_passthrough"), "turn_id", "turnId");
                if (rowType == "turn_context" || payloadType == "task_started")
                    currentTurnId = ValidId(explicitTurnId) ? explicitTurnId : null;
                var turnId = ValidId(explicitTurnId) ? explicitTurnId : currentTurnId;
                if (!ValidId(turnId)) continue;

                var turn = records.GetValueOrDefault(turnId!) ?? new MutableTurn(turnId!);
                records[turnId!] = turn;
                if (rowType == "turn_context")
                {
                    turn.Model = ReadString(payload, "model") ?? turn.Model;
                    turn.ProviderId = ReadString(payload, "model_provider", "modelProvider") ?? turn.ProviderId;
                    turn.StartedAt ??= timestamp;
                }
                if (payloadType == "task_started")
                    turn.StartedAt ??= timestamp;
                if (payloadType is "task_complete" or "task_completed")
                    turn.CompletedAt = timestamp ?? turn.CompletedAt;
                if (payloadType == "token_count")
                    turn.AddUsage(ReadObject(ReadObject(payload, "info"), "last_token_usage", "lastTokenUsage"));
                ReadSubagents(payload, turn);
            }
            catch (JsonException)
            {
                // Session files can contain a partial final line while Codex is writing.
            }
        }

        var output = records.Values.ToDictionary(
            turn => turn.TurnId,
            turn => turn.ToOutput(sessionProvider),
            StringComparer.Ordinal);
        return JsonSerializer.Serialize(new { threadId, records = output }, OutputOptions);
    }

    private static void ReadSubagents(JsonElement payload, MutableTurn turn)
    {
        var type = ReadString(payload, "type");
        if (type == "subAgentActivity")
        {
            turn.AddSubagent(
                ReadString(payload, "agentThreadId", "agent_thread_id"),
                ReadString(payload, "model"),
                ReadString(payload, "reasoningEffort", "reasoning_effort"),
                ReadString(payload, "status"));
            return;
        }
        if (type != "collabAgentToolCall") return;
        var receiverIds = ReadArray(payload, "receiverThreadIds", "receiver_thread_ids");
        foreach (var value in receiverIds.EnumerateArray())
        {
            turn.AddSubagent(
                value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                ReadString(payload, "model"),
                ReadString(payload, "reasoningEffort", "reasoning_effort"),
                ReadString(payload, "status"));
        }
    }

    private static string? ProviderLabel(string? providerId)
    {
        var normalized = providerId?.Trim().ToLowerInvariant().Replace('_', '-');
        if (normalized == "openai") return "OpenAI";
        if (normalized?.Contains("openrouter", StringComparison.Ordinal) == true) return "OpenRouter";
        if (normalized == "opencode-zen") return "OpenCode Zen";
        if (normalized == "custom") return "Custom endpoint";
        return null;
    }

    private static bool ValidId(string? value) => Guid.TryParse(value, out _);

    private static JsonElement ReadObject(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return default;
        foreach (var name in names)
        {
            if (value.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Object)
                return candidate;
        }
        return default;
    }

    private static JsonElement ReadArray(JsonElement value, params string[] names)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (value.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                    return candidate;
            }
        }
        using var empty = JsonDocument.Parse("[]");
        return empty.RootElement.Clone();
    }

    private static string? ReadString(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (value.TryGetProperty(name, out var candidate)
                && candidate.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(candidate.GetString()))
                return candidate.GetString()!.Trim();
        }
        return null;
    }

    private static long? ReadNumber(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (value.TryGetProperty(name, out var candidate)
                && candidate.ValueKind == JsonValueKind.Number
                && candidate.TryGetInt64(out var number))
                return number;
        }
        return null;
    }

    private static long? ReadTimestamp(JsonElement value, params string[] names)
    {
        var text = ReadString(value, names);
        return DateTimeOffset.TryParse(text, out var timestamp) ? timestamp.ToUnixTimeMilliseconds() : null;
    }

    private sealed class MutableTurn(string turnId)
    {
        private readonly Dictionary<string, TurnMetadataSubagent> subagents = new(StringComparer.Ordinal);
        private TurnMetadataUsage? usage;

        public string TurnId { get; } = turnId;
        public string? Model { get; set; }
        public string? ProviderId { get; set; }
        public long? StartedAt { get; set; }
        public long? CompletedAt { get; set; }

        public void AddUsage(JsonElement source)
        {
            if (source.ValueKind != JsonValueKind.Object) return;
            usage ??= new();
            usage.InputTokens += ReadNumber(source, "inputTokens", "input_tokens") ?? 0;
            usage.CachedInputTokens += ReadNumber(source, "cachedInputTokens", "cached_input_tokens") ?? 0;
            usage.OutputTokens += ReadNumber(source, "outputTokens", "output_tokens") ?? 0;
            usage.ReasoningOutputTokens += ReadNumber(source, "reasoningOutputTokens", "reasoning_output_tokens") ?? 0;
            usage.TotalTokens += ReadNumber(source, "totalTokens", "total_tokens") ?? 0;
        }

        public void AddSubagent(string? threadId, string? model, string? reasoningEffort, string? status)
        {
            if (!ValidId(threadId) || subagents.ContainsKey(threadId!)) return;
            subagents[threadId!] = new(threadId!, model, reasoningEffort, status);
        }

        public TurnMetadataRecord ToOutput(string? sessionProvider)
        {
            var providerId = ProviderId ?? sessionProvider;
            var durationMs = StartedAt is { } start && CompletedAt is { } completed
                ? Math.Max(0, completed - start)
                : (long?)null;
            var completeness = Model is not null
                && providerId is not null
                && durationMs is not null
                && usage is not null
                    ? "complete"
                    : "partial";
            return new(
                TurnId,
                Model,
                providerId,
                ProviderLabel(providerId),
                StartedAt,
                CompletedAt,
                durationMs,
                usage,
                [.. subagents.Values],
                completeness);
        }
    }

    private sealed class TurnMetadataUsage
    {
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ReasoningOutputTokens { get; set; }
        public long TotalTokens { get; set; }
    }

    private sealed record TurnMetadataSubagent(
        string ThreadId,
        string? Model,
        string? ReasoningEffort,
        string? Status);

    private sealed record TurnMetadataRecord(
        string TurnId,
        string? Model,
        string? ProviderId,
        string? ProviderLabel,
        long? StartedAt,
        long? CompletedAt,
        long? DurationMs,
        TurnMetadataUsage? TokenUsage,
        IReadOnlyList<TurnMetadataSubagent> Subagents,
        string Completeness);
}
