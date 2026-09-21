using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWingman.Core.Helpers;

internal static class HookTraceSessionReader
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ReadJson(string path, string threadId, CancellationToken cancellationToken) =>
        ReadJson([path], threadId, cancellationToken);

    public static string ReadJson(IEnumerable<string> paths, string threadId, CancellationToken cancellationToken)
    {
        var events = new Dictionary<string, List<TraceEvent>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
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
                    var explicitTurnId = ReadString(payload, "turn_id", "turnId")
                        ?? ReadString(ReadObject(payload, "internal_chat_message_metadata_passthrough"), "turn_id", "turnId");
                    if (rowType == "turn_context" || payloadType == "task_started")
                        currentTurnId = ValidId(explicitTurnId) ? explicitTurnId : null;
                    var turnId = ValidId(explicitTurnId) ? explicitTurnId : currentTurnId;
                    if (!ValidId(turnId)) continue;

                    var kind = Classify(payloadType, ReadString(payload, "role"));
                    var text = kind == EventKind.Context && ValidId(turnId)
                        ? ReadMessageText(payload)
                        : null;
                    if (kind == EventKind.Context && string.IsNullOrWhiteSpace(text)) continue;
                    if (kind == EventKind.Other) continue;
                    if (!events.TryGetValue(turnId!, out var turnEvents))
                    {
                        turnEvents = [];
                        events[turnId!] = turnEvents;
                    }
                    turnEvents.Add(new(kind, text));
                }
                catch (JsonException)
                {
                    // The active session can end with a partial row while Codex is writing it.
                }
            }
        }

        var records = new Dictionary<string, IReadOnlyList<HookTraceRecord>>(StringComparer.Ordinal);
        foreach (var (turnId, turnEvents) in events)
        {
            var output = new List<HookTraceRecord>();
            for (var index = 0; index < turnEvents.Count; index++)
            {
                if (turnEvents[index] is not { Kind: EventKind.Context, Text: { } text }) continue;
                var previous = Neighbor(turnEvents, index, -1);
                var next = Neighbor(turnEvents, index, 1);
                var (side, label) = Describe(turnEvents, index, previous, next);
                int? activityIndex = label switch
                {
                    "Before tool use" => turnEvents.Take(index).Count(item => item.Kind == EventKind.ToolCall),
                    "After tool use" => turnEvents.Take(index).Count(item => item.Kind == EventKind.ToolResult) - 1,
                    _ => null,
                };
                if (activityIndex < 0) activityIndex = null;
                output.Add(new(output.Count, "modelContext", side, label, text, "Source not recorded by Codex", activityIndex));
            }
            if (output.Count > 0) records[turnId] = output;
        }

        return JsonSerializer.Serialize(new { threadId, status = "available", records }, OutputOptions);
    }

    private static EventKind Neighbor(IReadOnlyList<TraceEvent> events, int start, int direction)
    {
        for (var index = start + direction; index >= 0 && index < events.Count; index += direction)
        {
            if (events[index].Kind != EventKind.Context) return events[index].Kind;
        }
        return EventKind.Other;
    }

    private static (string Side, string Label) Describe(IReadOnlyList<TraceEvent> events, int index, EventKind previous, EventKind next)
    {
        if (next == EventKind.User) return ("user", "Before your message");
        if (previous == EventKind.User) return ("user", "Prompt-submission context");
        if (next == EventKind.ToolCall) return ("response", "Before tool use");
        if (previous == EventKind.ToolResult) return ("response", "After tool use");
        if (previous == EventKind.Compaction || next == EventKind.Compaction) return ("response", "Compaction context");
        if (previous == EventKind.Assistant) return ("response", "Stop context");
        var firstWork = events.ToList().FindIndex(item => item.Kind is EventKind.Reasoning or EventKind.ToolCall or EventKind.ToolResult or EventKind.Assistant or EventKind.Compaction);
        if (firstWork < 0 || index < firstWork) return ("user", "Additional prompt context");
        return ("response", "During Codex work");
    }

    private static EventKind Classify(string? payloadType, string? role)
    {
        if (payloadType == "message")
        {
            if (role == "user") return EventKind.User;
            if (role is "developer" or "system") return EventKind.Context;
            if (role == "assistant") return EventKind.Assistant;
        }
        if (payloadType is "custom_tool_call" or "function_call" or "mcp_tool_call") return EventKind.ToolCall;
        if (payloadType is "custom_tool_call_output" or "function_call_output" or "mcp_tool_call_output") return EventKind.ToolResult;
        if (payloadType == "reasoning") return EventKind.Reasoning;
        if (payloadType?.Contains("compact", StringComparison.OrdinalIgnoreCase) == true) return EventKind.Compaction;
        return EventKind.Other;
    }

    private static string? ReadMessageText(JsonElement payload)
    {
        if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var type = ReadString(item, "type");
            if (type is not ("input_text" or "output_text" or "text")) continue;
            var text = ReadStringPreservingWhitespace(item, "text");
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
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

    private static string? ReadString(JsonElement value, params string[] names)
    {
        var result = ReadStringPreservingWhitespace(value, names);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static string? ReadStringPreservingWhitespace(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (value.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.String)
                return candidate.GetString();
        }
        return null;
    }

    private enum EventKind { Other, User, Context, Reasoning, ToolCall, ToolResult, Assistant, Compaction }
    private sealed record TraceEvent(EventKind Kind, string? Text);
    private sealed record HookTraceRecord(int Sequence, string Kind, string Side, string Label, string Text, string SourceLabel, int? ActivityIndex);
}
