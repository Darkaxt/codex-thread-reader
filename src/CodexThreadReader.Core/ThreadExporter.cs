using System.Net;
using System.Text;
using System.Text.Json;

namespace CodexThreadReader.Core;

public static class ThreadExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<ExportManifest> ExportAsync(
        ThreadSummary thread,
        ParsedThread parsed,
        string exportRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(exportRoot);
        var folderName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{PathUtilities.SafeFileName(thread.Title.Length > 0 ? thread.Title : thread.Id)}";
        var exportDirectory = Path.Combine(exportRoot, folderName);
        Directory.CreateDirectory(exportDirectory);

        var htmlPath = Path.Combine(exportDirectory, "thread.html");
        var normalizedJsonPath = Path.Combine(exportDirectory, "thread.normalized.json");
        var rawJsonlPath = Path.Combine(exportDirectory, "thread.raw.jsonl");
        var handoffMarkdownPath = Path.Combine(exportDirectory, "handoff.md");

        await File.WriteAllTextAsync(htmlPath, BuildHtml(thread, parsed), Encoding.UTF8, cancellationToken);

        var normalized = new
        {
            metadata = new
            {
                thread.Id,
                thread.Title,
                thread.Cwd,
                thread.NormalizedCwd,
                archived = thread.IsArchived,
                thread.CreatedAt,
                thread.UpdatedAt,
                thread.RolloutPath,
                thread.RolloutExists,
                thread.SizeBytes,
                recoveryFlags = thread.Flags.Select(f => f.ToString()).ToArray(),
                rolloutMetadata = parsed.Metadata
            },
            messages = parsed.Messages,
            parseReport = parsed.Report
        };

        await File.WriteAllTextAsync(normalizedJsonPath, JsonSerializer.Serialize(normalized, JsonOptions), Encoding.UTF8, cancellationToken);

        if (thread.RolloutExists && File.Exists(thread.RolloutPath))
        {
            File.Copy(thread.RolloutPath, rawJsonlPath, overwrite: true);
        }
        else
        {
            await File.WriteAllTextAsync(rawJsonlPath, string.Empty, Encoding.UTF8, cancellationToken);
        }

        await File.WriteAllTextAsync(handoffMarkdownPath, BuildHandoff(thread, htmlPath, normalizedJsonPath, rawJsonlPath), Encoding.UTF8, cancellationToken);

        return new ExportManifest(exportDirectory, htmlPath, normalizedJsonPath, rawJsonlPath, handoffMarkdownPath);
    }

    private static string BuildHtml(ThreadSummary thread, ParsedThread parsed)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><title>");
        builder.Append(WebUtility.HtmlEncode(thread.Title));
        builder.AppendLine("</title>");
        builder.AppendLine("""
            <style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:24px;line-height:1.45;color:#1f2933;background:#f7f8fa}
            header{border-bottom:1px solid #d7dde5;margin-bottom:18px;padding-bottom:12px}
            h1{font-size:24px;margin:0 0 8px}
            .meta{color:#52606d;font-size:13px}
            .entry{background:#fff;border:1px solid #d7dde5;border-radius:8px;margin:12px 0;padding:12px}
            .role{font-weight:600;margin-bottom:6px;color:#102a43}
            .tool{background:#f1f5f9}
            pre{white-space:pre-wrap;word-break:break-word;margin:0;font-family:Consolas,monospace;font-size:13px}
            details{margin:0}
            summary{cursor:pointer;font-weight:600}
            </style></head><body>
            """);
        builder.Append("<header><h1>").Append(WebUtility.HtmlEncode(thread.Title)).AppendLine("</h1>");
        builder.Append("<div class=\"meta\">ID: ").Append(WebUtility.HtmlEncode(thread.Id)).Append("<br>");
        builder.Append("CWD: ").Append(WebUtility.HtmlEncode(thread.Cwd)).Append("<br>");
        builder.Append("Updated: ").Append(WebUtility.HtmlEncode(thread.UpdatedAt.ToString("u"))).Append("<br>");
        builder.Append("Flags: ").Append(WebUtility.HtmlEncode(string.Join(", ", thread.Flags))).AppendLine("</div></header>");

        foreach (var message in parsed.Messages)
        {
            var isTool = message.Kind is TranscriptEntryKind.ToolCall or TranscriptEntryKind.ToolOutput;
            builder.Append("<section class=\"entry");
            if (isTool)
            {
                builder.Append(" tool");
            }

            builder.AppendLine("\">");
            if (isTool)
            {
                builder.Append("<details><summary>")
                    .Append(WebUtility.HtmlEncode($"{message.Kind} line {message.RawLineNumber}"))
                    .AppendLine("</summary><pre>")
                    .Append(WebUtility.HtmlEncode(message.Text))
                    .AppendLine("</pre></details>");
            }
            else
            {
                builder.Append("<div class=\"role\">")
                    .Append(WebUtility.HtmlEncode($"{message.Role} / {message.Kind}"))
                    .AppendLine("</div><pre>")
                    .Append(WebUtility.HtmlEncode(message.Text))
                    .AppendLine("</pre>");
            }

            builder.AppendLine("</section>");
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string BuildHandoff(ThreadSummary thread, string htmlPath, string normalizedJsonPath, string rawJsonlPath)
    {
        return $$"""
            # Codex Thread Handoff

            Analyze this recovered Codex thread and summarize the useful context for continuing the work in a fresh thread.

            Thread ID: `{{thread.Id}}`
            Title: `{{thread.Title}}`
            CWD: `{{thread.Cwd}}`

            Files:
            - Readable transcript: `{{htmlPath}}`
            - Normalized JSON: `{{normalizedJsonPath}}`
            - Raw rollout JSONL: `{{rawJsonlPath}}`

            Start with the normalized JSON. Use the raw rollout JSONL only if the normalized transcript is missing needed detail.
            """;
    }
}
