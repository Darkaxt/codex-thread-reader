using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class ThreadExporterTests
{
    [Fact]
    public async Task ExportAsync_writes_html_normalized_json_raw_jsonl_and_handoff_prompt()
    {
        var root = Directory.CreateTempSubdirectory("ctr-export-");
        var rolloutPath = Path.Combine(root.FullName, "source.jsonl");
        var rawContent = """{"type":"event_msg","payload":{"type":"user_message","message":"raw"}}""" + Environment.NewLine;
        await File.WriteAllTextAsync(rolloutPath, rawContent, CancellationToken.None);

        var thread = new ThreadSummary(
            Id: "thread-1",
            Title: "Thread <Title>",
            Cwd: "C:\\repo",
            NormalizedCwd: "C:\\repo",
            IsArchived: false,
            CreatedAt: DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-01-02T03:05:05Z"),
            RolloutPath: rolloutPath,
            RolloutExists: true,
            SizeBytes: rawContent.Length,
            IsAnchored: true,
            Flags: new[] { RecoveryFlag.Anchored });
        var parsed = new ParsedThread(
            new RolloutMetadata("thread-1", DateTimeOffset.Parse("2026-01-02T03:04:05Z"), "C:\\repo", "Codex Desktop", "0.1.0", "vscode", "openai"),
            new[]
            {
                new TranscriptEntry(
                    RawLineNumber: 1,
                    Timestamp: DateTimeOffset.Parse("2026-01-02T03:04:06Z"),
                    Role: "user",
                    Phase: null,
                    Kind: TranscriptEntryKind.Message,
                    Text: "Please inspect <secret>")
            },
            new ParseReport(1, 1, 0, 0));

        var manifest = await ThreadExporter.ExportAsync(thread, parsed, root.FullName, CancellationToken.None);

        Assert.True(File.Exists(manifest.HtmlPath));
        Assert.True(File.Exists(manifest.NormalizedJsonPath));
        Assert.True(File.Exists(manifest.RawJsonlPath));
        Assert.True(File.Exists(manifest.HandoffMarkdownPath));
        Assert.Equal(rawContent, await File.ReadAllTextAsync(manifest.RawJsonlPath, CancellationToken.None));

        var html = await File.ReadAllTextAsync(manifest.HtmlPath, CancellationToken.None);
        Assert.Contains("Thread &lt;Title&gt;", html);
        Assert.Contains("Please inspect &lt;secret&gt;", html);
        Assert.DoesNotContain("Please inspect <secret>", html);

        var handoff = await File.ReadAllTextAsync(manifest.HandoffMarkdownPath, CancellationToken.None);
        Assert.Contains("thread-1", handoff);
        Assert.Contains("thread.normalized.json", handoff);
    }
}
