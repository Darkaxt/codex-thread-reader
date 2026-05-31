using System.Text;
using System.Text.Json;

namespace CodexThreadReader.Core;

public static class RolloutParser
{
    public static async Task<ParsedThread> ParseAsync(string rolloutPath, CancellationToken cancellationToken)
    {
        return await ParseAsync(rolloutPath, maxMessages: null, cancellationToken);
    }

    public static async Task<ParsedThread> ParseAsync(string rolloutPath, int? maxMessages, CancellationToken cancellationToken)
    {
        var page = await ParseInternalAsync(rolloutPath, skipMessages: 0, takeMessages: maxMessages, detectHasMore: false, cancellationToken);
        return new ParsedThread(page.Metadata, page.Messages, page.Report);
    }

    public static async Task<ParsedThreadPage> ParsePageAsync(
        string rolloutPath,
        int skipMessages,
        int takeMessages,
        CancellationToken cancellationToken)
    {
        if (skipMessages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skipMessages), "Skip count cannot be negative.");
        }

        if (takeMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(takeMessages), "Page size must be positive.");
        }

        return await ParseInternalAsync(rolloutPath, skipMessages, takeMessages, detectHasMore: true, cancellationToken);
    }

    private static async Task<ParsedThreadPage> ParseInternalAsync(
        string rolloutPath,
        int skipMessages,
        int? takeMessages,
        bool detectHasMore,
        CancellationToken cancellationToken)
    {
        var messages = new List<TranscriptEntry>();
        RolloutMetadata? metadata = null;
        var totalLines = 0;
        var parsedLines = 0;
        var invalidLines = 0;
        var seenMessages = 0;
        var hasMore = false;

        await foreach (var parsedLine in ReadJsonLinesAsync(rolloutPath, cancellationToken))
        {
            totalLines++;
            if (parsedLine.Document is null)
            {
                invalidLines++;
                continue;
            }

            parsedLines++;
            using var document = parsedLine.Document;
            var root = document.RootElement;
            var timestamp = TryGetTimestamp(root);
            var type = GetString(root, "type");
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var lineMessages = new List<TranscriptEntry>();
            switch (type)
            {
                case "session_meta":
                    metadata ??= ReadMetadata(payload);
                    break;
                case "event_msg":
                    AddEventMessage(lineMessages, parsedLine.LineNumber, timestamp, payload);
                    break;
                case "response_item":
                    AddResponseItem(lineMessages, parsedLine.LineNumber, timestamp, payload);
                    break;
                case "compacted":
                    AddCompaction(lineMessages, parsedLine.LineNumber, timestamp, payload);
                    break;
                case "turn_context":
                    AddTurnContext(lineMessages, parsedLine.LineNumber, timestamp, payload);
                    break;
            }

            foreach (var message in lineMessages)
            {
                if (seenMessages < skipMessages)
                {
                    seenMessages++;
                    continue;
                }

                if (takeMessages is null || messages.Count < takeMessages.Value)
                {
                    messages.Add(message);
                    seenMessages++;
                    continue;
                }

                hasMore = true;
                break;
            }

            if (hasMore || (takeMessages is not null && !detectHasMore && messages.Count >= takeMessages.Value && metadata is not null))
            {
                break;
            }
        }

        return new ParsedThreadPage(
            metadata,
            messages,
            new ParseReport(totalLines, parsedLines, invalidLines, 0),
            skipMessages,
            takeMessages ?? messages.Count,
            hasMore);
    }

    public static async Task<RolloutMetadata?> ReadMetadataAsync(string rolloutPath, CancellationToken cancellationToken)
    {
        await foreach (var parsedLine in ReadJsonLinesAsync(rolloutPath, cancellationToken))
        {
            if (parsedLine.Document is null)
            {
                continue;
            }

            using var document = parsedLine.Document;
            var root = document.RootElement;
            if (GetString(root, "type") != "session_meta")
            {
                continue;
            }

            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                return ReadMetadata(payload);
            }
        }

        return null;
    }

    public static async Task<string?> ReadThreadNameAsync(string rolloutPath, int maxLines, CancellationToken cancellationToken)
    {
        string? threadName = null;
        await foreach (var parsedLine in ReadJsonLinesAsync(rolloutPath, cancellationToken))
        {
            if (parsedLine.LineNumber > maxLines)
            {
                break;
            }

            if (parsedLine.Document is null)
            {
                continue;
            }

            using var document = parsedLine.Document;
            var root = document.RootElement;
            if (GetString(root, "type") != "event_msg")
            {
                continue;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (GetString(payload, "type") == "thread_name_updated")
            {
                threadName = GetString(payload, "thread_name") ?? threadName;
            }
        }

        return string.IsNullOrWhiteSpace(threadName) ? null : threadName;
    }

    private static async IAsyncEnumerable<ParsedJsonLine> ReadJsonLinesAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1024 * 64, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024 * 64);
        var lineNumber = 0;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
            }

            yield return new ParsedJsonLine(lineNumber, document);
        }
    }

    private static RolloutMetadata? ReadMetadata(JsonElement payload)
    {
        var id = GetString(payload, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new RolloutMetadata(
            id,
            TryParseTimestamp(GetString(payload, "timestamp")) ?? DateTimeOffset.MinValue,
            GetString(payload, "cwd") ?? string.Empty,
            GetString(payload, "originator") ?? string.Empty,
            GetString(payload, "cli_version") ?? string.Empty,
            GetString(payload, "source") ?? string.Empty,
            GetString(payload, "model_provider") ?? string.Empty);
    }

    private static void AddEventMessage(List<TranscriptEntry> messages, int lineNumber, DateTimeOffset? timestamp, JsonElement payload)
    {
        switch (GetString(payload, "type"))
        {
            case "user_message":
                AddText(messages, lineNumber, timestamp, "user", null, TranscriptEntryKind.Message, GetString(payload, "message"));
                break;
            case "agent_message":
                AddText(messages, lineNumber, timestamp, "assistant", GetString(payload, "phase"), TranscriptEntryKind.Message, GetString(payload, "message"));
                break;
            case "context_compacted":
                messages.Add(new TranscriptEntry(lineNumber, timestamp, "system", null, TranscriptEntryKind.Compaction, "Context compacted"));
                break;
            case "exec_command_end":
            case "patch_apply_end":
            case "web_search_end":
            case "item_completed":
                AddText(messages, lineNumber, timestamp, "tool", null, TranscriptEntryKind.ToolOutput, FormatToolOutput(payload));
                break;
        }
    }

    private static void AddResponseItem(List<TranscriptEntry> messages, int lineNumber, DateTimeOffset? timestamp, JsonElement payload)
    {
        var type = GetString(payload, "type");
        switch (type)
        {
            case "message":
                AddText(messages, lineNumber, timestamp, GetString(payload, "role") ?? "assistant", null, TranscriptEntryKind.Message, ExtractContentText(payload));
                break;
            case "function_call":
            case "custom_tool_call":
            case "web_search_call":
                AddText(messages, lineNumber, timestamp, "tool", null, TranscriptEntryKind.ToolCall, FormatToolCall(payload));
                break;
            case "function_call_output":
            case "custom_tool_call_output":
                AddText(messages, lineNumber, timestamp, "tool", null, TranscriptEntryKind.ToolOutput, GetString(payload, "output"));
                break;
        }
    }

    private static void AddCompaction(List<TranscriptEntry> messages, int lineNumber, DateTimeOffset? timestamp, JsonElement payload)
    {
        AddText(messages, lineNumber, timestamp, "system", null, TranscriptEntryKind.Compaction, GetString(payload, "message"));
    }

    private static void AddTurnContext(List<TranscriptEntry> messages, int lineNumber, DateTimeOffset? timestamp, JsonElement payload)
    {
        var summary = GetString(payload, "summary");
        if (!string.IsNullOrWhiteSpace(summary) && !summary.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            AddText(messages, lineNumber, timestamp, "system", null, TranscriptEntryKind.Context, summary);
        }
    }

    private static void AddText(
        List<TranscriptEntry> messages,
        int lineNumber,
        DateTimeOffset? timestamp,
        string role,
        string? phase,
        TranscriptEntryKind kind,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        messages.Add(new TranscriptEntry(lineNumber, timestamp, role, phase, kind, text));
    }

    private static string? ExtractContentText(JsonElement payload)
    {
        if (!payload.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return content.GetRawText();
        }

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(text.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string FormatToolCall(JsonElement payload)
    {
        var name = GetString(payload, "name") ?? GetString(payload, "type") ?? "tool_call";
        var callId = GetString(payload, "call_id");
        var details = GetString(payload, "arguments") ?? GetString(payload, "input");
        var builder = new StringBuilder(name);
        if (!string.IsNullOrWhiteSpace(callId))
        {
            builder.Append(" [").Append(callId).Append(']');
        }

        if (!string.IsNullOrWhiteSpace(details))
        {
            builder.AppendLine().Append(details);
        }

        return builder.ToString();
    }

    private static string FormatToolOutput(JsonElement payload)
    {
        var type = GetString(payload, "type") ?? "tool_output";
        var callId = GetString(payload, "call_id");
        var output = GetString(payload, "aggregated_output")
            ?? GetString(payload, "formatted_output")
            ?? GetString(payload, "stdout")
            ?? GetString(payload, "stderr")
            ?? payload.GetRawText();

        var builder = new StringBuilder(type);
        if (!string.IsNullOrWhiteSpace(callId))
        {
            builder.Append(" [").Append(callId).Append(']');
        }

        if (payload.TryGetProperty("exit_code", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number)
        {
            builder.Append(" exit=").Append(exitCode.GetInt32());
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            builder.AppendLine().Append(output);
        }

        return builder.ToString();
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement root)
    {
        return TryParseTimestamp(GetString(root, "timestamp"));
    }

    private static DateTimeOffset? TryParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => property.GetRawText()
        };
    }

    private sealed record ParsedJsonLine(int LineNumber, JsonDocument? Document);
}
