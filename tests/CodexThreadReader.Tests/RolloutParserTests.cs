using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class RolloutParserTests
{
    [Fact]
    public async Task ParseAsync_extracts_readable_messages_tool_events_compactions_and_parse_errors()
    {
        var root = Directory.CreateTempSubdirectory("ctr-rollout-");
        var rolloutPath = Path.Combine(root.FullName, "rollout-2026-01-02T03-04-05-thread-1.jsonl");
        await File.WriteAllLinesAsync(
            rolloutPath,
            new[]
            {
                """{"timestamp":"2026-01-02T03:04:05Z","type":"session_meta","payload":{"id":"thread-1","timestamp":"2026-01-02T03:04:05Z","cwd":"C:\\repo","originator":"Codex Desktop","cli_version":"0.1.0","source":"vscode","model_provider":"openai"}}""",
                """{"timestamp":"2026-01-02T03:04:06Z","type":"event_msg","payload":{"type":"user_message","message":"Can you inspect this?"}}""",
                """{"timestamp":"2026-01-02T03:04:07Z","type":"event_msg","payload":{"type":"agent_message","message":"I am checking the files.","phase":"commentary"}}""",
                """{"timestamp":"2026-01-02T03:04:08Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Found the issue."}]}}""",
                """{"timestamp":"2026-01-02T03:04:09Z","type":"response_item","payload":{"type":"function_call","name":"shell_command","call_id":"call-1","arguments":"{\"command\":\"dir\"}"}}""",
                """{"timestamp":"2026-01-02T03:04:10Z","type":"event_msg","payload":{"type":"exec_command_end","call_id":"call-1","stdout":"ok","stderr":"","exit_code":0,"aggregated_output":"ok"}}""",
                """{"timestamp":"2026-01-02T03:04:11Z","type":"compacted","payload":{"message":"Earlier context summary"}}""",
                "{not valid json"
            },
            CancellationToken.None);

        var parsed = await RolloutParser.ParseAsync(rolloutPath, CancellationToken.None);

        Assert.Equal("thread-1", parsed.Metadata?.Id);
        Assert.Equal("C:\\repo", parsed.Metadata?.Cwd);
        Assert.Equal(8, parsed.Report.TotalLines);
        Assert.Equal(7, parsed.Report.ParsedLines);
        Assert.Equal(1, parsed.Report.InvalidLines);
        Assert.Contains(parsed.Messages, m => m.Role == "user" && m.Kind == TranscriptEntryKind.Message && m.Text == "Can you inspect this?");
        Assert.Contains(parsed.Messages, m => m.Role == "assistant" && m.Phase == "commentary" && m.Text == "I am checking the files.");
        Assert.Contains(parsed.Messages, m => m.Role == "assistant" && m.Text == "Found the issue.");
        Assert.Contains(parsed.Messages, m => m.Kind == TranscriptEntryKind.ToolCall && m.Text.Contains("shell_command"));
        Assert.Contains(parsed.Messages, m => m.Kind == TranscriptEntryKind.ToolOutput && m.Text.Contains("ok"));
        Assert.Contains(parsed.Messages, m => m.Kind == TranscriptEntryKind.Compaction && m.Text == "Earlier context summary");
    }
}
