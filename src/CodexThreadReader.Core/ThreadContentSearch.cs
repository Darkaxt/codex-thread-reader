using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CodexThreadReader.Core;

public static class ThreadContentSearch
{
    public static async Task<bool> ContainsAsync(ThreadSummary thread, string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || !thread.RolloutExists || !File.Exists(thread.RolloutPath))
        {
            return false;
        }

        var needle = query.Trim();
        await foreach (var text in ReadSearchableTextsAsync(thread.RolloutPath, cancellationToken))
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async IAsyncEnumerable<string> ReadSearchableTextsAsync(
        string rolloutPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(rolloutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1024 * 64, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024 * 64);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
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
                continue;
            }

            using (document)
            {
                foreach (var text in ExtractSearchableTexts(document.RootElement))
                {
                    yield return text;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractSearchableTexts(JsonElement root)
    {
        var type = GetString(root, "type");
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        switch (type)
        {
            case "event_msg":
                foreach (var text in GetTextFields(payload, "message", "thread_name", "aggregated_output", "formatted_output", "stdout", "stderr"))
                {
                    yield return text;
                }

                break;
            case "response_item":
                if (GetString(payload, "type") == "message" && GetString(payload, "role") == "developer")
                {
                    yield break;
                }

                if (payload.TryGetProperty("content", out var content))
                {
                    foreach (var text in GetContentTexts(content))
                    {
                        yield return text;
                    }
                }

                foreach (var text in GetTextFields(payload, "name", "arguments", "input", "output"))
                {
                    yield return text;
                }

                break;
            case "compacted":
                foreach (var text in GetTextFields(payload, "message"))
                {
                    yield return text;
                }

                break;
            case "turn_context":
                foreach (var text in GetTextFields(payload, "summary"))
                {
                    yield return text;
                }

                break;
        }
    }

    private static IEnumerable<string> GetContentTexts(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var value = content.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }

            yield break;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> GetTextFields(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(payload, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
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
}
