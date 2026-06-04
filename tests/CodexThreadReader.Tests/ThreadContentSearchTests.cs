using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class ThreadContentSearchTests
{
    [Fact]
    public async Task ContainsAsync_matches_transcript_and_tool_content_case_insensitively()
    {
        var root = Directory.CreateTempSubdirectory("ctr-search-");
        var rolloutPath = Path.Combine(root.FullName, "rollout-2026-01-02T03-04-05-thread-1.jsonl");
        await File.WriteAllLinesAsync(
            rolloutPath,
            new[]
            {
                """{"timestamp":"2026-01-02T03:04:05Z","type":"session_meta","payload":{"id":"thread-1","timestamp":"2026-01-02T03:04:05Z","cwd":"C:\\repo"}}""",
                """{"timestamp":"2026-01-02T03:04:06Z","type":"event_msg","payload":{"type":"user_message","message":"Can you build a shop helper?"}}""",
                """{"timestamp":"2026-01-02T03:04:07Z","type":"response_item","payload":{"type":"function_call","name":"shell_command","arguments":"{\"command\":\"python scripts/invoke_skroutz_cli.py -- search phone\"}"}}"""
            },
            CancellationToken.None);

        var thread = CreateThread(rolloutPath);

        Assert.True(await ThreadContentSearch.ContainsAsync(thread, "SKROUTZ", CancellationToken.None));
    }

    [Fact]
    public async Task ContainsAsync_ignores_developer_bootstrap_content()
    {
        var root = Directory.CreateTempSubdirectory("ctr-search-bootstrap-");
        var rolloutPath = Path.Combine(root.FullName, "rollout-2026-01-02T03-04-05-thread-1.jsonl");
        await File.WriteAllLinesAsync(
            rolloutPath,
            new[]
            {
                """{"timestamp":"2026-01-02T03:04:05Z","type":"session_meta","payload":{"id":"thread-1","timestamp":"2026-01-02T03:04:05Z","cwd":"C:\\repo"}}""",
                """{"timestamp":"2026-01-02T03:04:06Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"Available skills include skroutz-cli."}]}}""",
                """{"timestamp":"2026-01-02T03:04:07Z","type":"event_msg","payload":{"type":"user_message","message":"Unrelated request"}}"""
            },
            CancellationToken.None);

        var thread = CreateThread(rolloutPath);

        Assert.False(await ThreadContentSearch.ContainsAsync(thread, "skroutz", CancellationToken.None));
    }

    private static ThreadSummary CreateThread(string rolloutPath)
    {
        return new ThreadSummary(
            "thread-1",
            "Unrelated",
            "C:\\repo",
            "c:\\repo",
            IsArchived: false,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            rolloutPath,
            RolloutExists: true,
            SizeBytes: new FileInfo(rolloutPath).Length,
            IsAnchored: false,
            Array.Empty<RecoveryFlag>());
    }
}
