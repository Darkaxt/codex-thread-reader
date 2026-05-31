using CodexThreadReader.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace CodexThreadReader.Tests;

public sealed class ThreadCatalogTests
{
    [Fact]
    public async Task LoadAsync_combines_sqlite_rollout_only_archived_and_persistent_anchor_sources()
    {
        var codexHome = Directory.CreateTempSubdirectory("ctr-home-");
        Directory.CreateDirectory(Path.Combine(codexHome.FullName, "sessions", "2026", "01", "02"));
        Directory.CreateDirectory(Path.Combine(codexHome.FullName, "archived_sessions"));

        var visibleRollout = Path.Combine(codexHome.FullName, "sessions", "2026", "01", "02", "rollout-2026-01-02T03-04-05-visible-id.jsonl");
        var hiddenRollout = Path.Combine(codexHome.FullName, "sessions", "2026", "01", "02", "rollout-2026-01-02T03-04-06-hidden-id.jsonl");
        var rolloutOnly = Path.Combine(codexHome.FullName, "sessions", "2026", "01", "02", "rollout-2026-01-02T03-04-07-rollout-only-id.jsonl");
        var archivedRollout = Path.Combine(codexHome.FullName, "archived_sessions", "rollout-2026-01-02T03-04-08-archived-id.jsonl");

        await WriteRolloutAsync(visibleRollout, "visible-id", "C:\\visible");
        await WriteRolloutAsync(hiddenRollout, "hidden-id", "C:\\hidden");
        await WriteRolloutAsync(rolloutOnly, "rollout-only-id", "C:\\rollout-only");
        await WriteRolloutAsync(archivedRollout, "archived-id", "C:\\archived");

        await CreateStateDatabaseAsync(
            Path.Combine(codexHome.FullName, "state_5.sqlite"),
            new ThreadRow("visible-id", "Visible thread", "C:\\visible", false, visibleRollout, 10, 20),
            new ThreadRow("hidden-id", "Hidden thread", "\\\\?\\C:\\hidden", false, hiddenRollout, 11, 21),
            new ThreadRow("archived-id", "Archived thread", "C:\\archived", true, archivedRollout, 12, 22));

        await File.WriteAllTextAsync(
            Path.Combine(codexHome.FullName, "session_index.jsonl"),
            """{"id":"visible-id","thread_name":"Visible thread","updated_at":20}""" + Environment.NewLine,
            CancellationToken.None);

        var anchorPath = Path.Combine(codexHome.FullName, "anchors.json");
        await new AnchorStore(anchorPath, new[] { "hidden-id" }).SaveAsync(CancellationToken.None);

        var result = await ThreadCatalog.LoadAsync(new ThreadCatalogOptions(codexHome.FullName, anchorPath), CancellationToken.None);

        Assert.Equal(4, result.Threads.Count);
        Assert.Equal("hidden-id", result.Threads[0].Id);
        Assert.True(result.Threads[0].IsAnchored);
        Assert.Contains(RecoveryFlag.ExtendedPath, result.Threads[0].Flags);
        Assert.Contains(RecoveryFlag.MissingFromSessionIndex, result.Threads[0].Flags);
        Assert.Contains(result.Threads, t => t.Id == "rollout-only-id" && t.Flags.Contains(RecoveryFlag.RolloutOnly));
        Assert.Contains(result.Threads, t => t.Id == "archived-id" && t.IsArchived && t.Flags.Contains(RecoveryFlag.Archived));
        Assert.Empty(result.Errors);
    }

    private static async Task WriteRolloutAsync(string path, string id, string cwd)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = "2026-01-02T03:04:05Z",
            type = "session_meta",
            payload = new
            {
                id,
                timestamp = "2026-01-02T03:04:05Z",
                cwd,
                originator = "Codex Desktop",
                cli_version = "0.1.0",
                source = "vscode",
                model_provider = "openai"
            }
        });
        await File.WriteAllTextAsync(path, line + Environment.NewLine, CancellationToken.None);
    }

    private static async Task CreateStateDatabaseAsync(string path, params ThreadRow[] rows)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            create table threads (
                id text primary key,
                rollout_path text not null,
                created_at integer not null,
                updated_at integer not null,
                source text not null,
                model_provider text not null,
                cwd text not null,
                title text not null,
                sandbox_policy text not null,
                approval_mode text not null,
                tokens_used integer not null,
                has_user_event integer not null,
                archived integer not null,
                archived_at integer null,
                git_sha text null,
                git_branch text null,
                git_origin_url text null,
                cli_version text not null,
                first_user_message text not null,
                agent_nickname text null,
                agent_role text null,
                memory_mode text not null,
                model text null,
                reasoning_effort text null,
                agent_path text null,
                created_at_ms integer null,
                updated_at_ms integer null,
                thread_source text null,
                preview text not null
            );
            """;
        await command.ExecuteNonQueryAsync();

        foreach (var row in rows)
        {
            command = connection.CreateCommand();
            command.CommandText = """
                insert into threads (
                    id, rollout_path, created_at, updated_at, source, model_provider, cwd, title,
                    sandbox_policy, approval_mode, tokens_used, has_user_event, archived,
                    cli_version, first_user_message, memory_mode, preview
                )
                values (
                    $id, $rollout_path, $created_at, $updated_at, 'vscode', 'openai', $cwd, $title,
                    '{}', 'never', 0, 1, $archived, '0.1.0', '', 'default', ''
                );
                """;
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$rollout_path", row.RolloutPath);
            command.Parameters.AddWithValue("$created_at", row.CreatedAt);
            command.Parameters.AddWithValue("$updated_at", row.UpdatedAt);
            command.Parameters.AddWithValue("$cwd", row.Cwd);
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$archived", row.Archived ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record ThreadRow(string Id, string Title, string Cwd, bool Archived, string RolloutPath, long CreatedAt, long UpdatedAt);
}
