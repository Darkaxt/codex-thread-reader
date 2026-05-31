using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexThreadReader.Core;

public static partial class ThreadCatalog
{
    private const long LargeRolloutThresholdBytes = 512L * 1024L * 1024L;

    public static async Task<ThreadCatalogResult> LoadAsync(ThreadCatalogOptions options, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var summaries = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
        var rolloutPathsById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionIndexIds = await LoadSessionIndexIdsAsync(options.CodexHome, cancellationToken);
        var anchorStore = string.IsNullOrWhiteSpace(options.AnchorStorePath)
            ? new AnchorStore(string.Empty, Array.Empty<string>())
            : await AnchorStore.LoadAsync(options.AnchorStorePath, cancellationToken);
        var anchoredIds = anchorStore.ThreadIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dbPath = Path.Combine(options.CodexHome, "state_5.sqlite");
        if (File.Exists(dbPath))
        {
            try
            {
                await LoadSqliteThreadsAsync(dbPath, sessionIndexIds, anchoredIds, summaries, rolloutPathsById, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to read state_5.sqlite: {ex.Message}");
            }
        }
        else
        {
            errors.Add($"state_5.sqlite was not found at {dbPath}");
        }

        await LoadRolloutOnlyThreadsAsync(options.CodexHome, sessionIndexIds, anchoredIds, summaries, rolloutPathsById, errors, cancellationToken);
        AddMissingAnchors(anchoredIds, summaries);

        var anchorOrder = anchorStore.ThreadIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        var ordered = summaries.Values
            .OrderBy(t => anchorOrder.TryGetValue(t.Id, out var index) ? 0 : 1)
            .ThenBy(t => anchorOrder.TryGetValue(t.Id, out var index) ? index : int.MaxValue)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ThreadCatalogResult(ordered, errors);
    }

    private static async Task LoadSqliteThreadsAsync(
        string dbPath,
        ISet<string> sessionIndexIds,
        ISet<string> anchoredIds,
        IDictionary<string, ThreadSummary> summaries,
        IDictionary<string, string> rolloutPathsById,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select id, title, cwd, archived, created_at, updated_at, rollout_path
            from threads
            order by updated_at desc
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var title = reader.GetString(1);
            var cwd = reader.GetString(2);
            var archived = reader.GetInt32(3) != 0;
            var createdAt = FromUnixSeconds(reader.GetInt64(4));
            var updatedAt = FromUnixSeconds(reader.GetInt64(5));
            var rolloutPath = PathUtilities.StripExtendedPrefix(reader.GetString(6));
            var rolloutExists = File.Exists(rolloutPath);
            var flags = BuildFlags(id, cwd, rolloutPath, archived, rolloutExists, sessionIndexIds, anchoredIds, false);
            var size = rolloutExists ? new FileInfo(rolloutPath).Length : 0;
            if (size >= LargeRolloutThresholdBytes && !flags.Contains(RecoveryFlag.LargeRollout))
            {
                flags.Add(RecoveryFlag.LargeRollout);
            }

            var summary = new ThreadSummary(
                id,
                title,
                cwd,
                PathUtilities.NormalizeForComparison(cwd),
                archived,
                createdAt,
                updatedAt,
                rolloutPath,
                rolloutExists,
                size,
                anchoredIds.Contains(id),
                flags);
            summaries[id] = summary;
            if (rolloutExists)
            {
                rolloutPathsById[id] = PathUtilities.NormalizeForComparison(rolloutPath);
            }
        }
    }

    private static async Task LoadRolloutOnlyThreadsAsync(
        string codexHome,
        ISet<string> sessionIndexIds,
        ISet<string> anchoredIds,
        IDictionary<string, ThreadSummary> summaries,
        IDictionary<string, string> rolloutPathsById,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (var rolloutPath in EnumerateRollouts(codexHome))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = PathUtilities.NormalizeForComparison(rolloutPath);
            if (rolloutPathsById.Values.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            RolloutMetadata? metadata = null;
            try
            {
                metadata = await RolloutParser.ReadMetadataAsync(rolloutPath, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to read rollout metadata from {rolloutPath}: {ex.Message}");
            }

            var id = metadata?.Id ?? TryGetThreadIdFromRolloutName(rolloutPath);
            if (string.IsNullOrWhiteSpace(id) || summaries.ContainsKey(id))
            {
                continue;
            }

            var file = new FileInfo(rolloutPath);
            var archived = IsArchivedRollout(codexHome, rolloutPath);
            var flags = BuildFlags(id, metadata?.Cwd ?? string.Empty, rolloutPath, archived, true, sessionIndexIds, anchoredIds, true);
            if (file.Length >= LargeRolloutThresholdBytes && !flags.Contains(RecoveryFlag.LargeRollout))
            {
                flags.Add(RecoveryFlag.LargeRollout);
            }

            summaries[id] = new ThreadSummary(
                id,
                metadata?.Id ?? id,
                metadata?.Cwd ?? string.Empty,
                PathUtilities.NormalizeForComparison(metadata?.Cwd ?? string.Empty),
                archived,
                metadata?.Timestamp ?? file.CreationTimeUtc,
                file.LastWriteTimeUtc,
                rolloutPath,
                true,
                file.Length,
                anchoredIds.Contains(id),
                flags);
        }
    }

    private static List<RecoveryFlag> BuildFlags(
        string id,
        string cwd,
        string rolloutPath,
        bool archived,
        bool rolloutExists,
        ISet<string> sessionIndexIds,
        ISet<string> anchoredIds,
        bool rolloutOnly)
    {
        var flags = new List<RecoveryFlag>();
        if (anchoredIds.Contains(id))
        {
            flags.Add(RecoveryFlag.Anchored);
        }

        if (archived)
        {
            flags.Add(RecoveryFlag.Archived);
        }

        if (!sessionIndexIds.Contains(id))
        {
            flags.Add(RecoveryFlag.MissingFromSessionIndex);
        }

        if (PathUtilities.HasExtendedPrefix(cwd) || PathUtilities.HasExtendedPrefix(rolloutPath))
        {
            flags.Add(RecoveryFlag.ExtendedPath);
        }

        if (rolloutOnly)
        {
            flags.Add(RecoveryFlag.RolloutOnly);
        }

        if (!rolloutExists)
        {
            flags.Add(RecoveryFlag.DbOnlyMissingFile);
        }

        return flags;
    }

    private static void AddMissingAnchors(ISet<string> anchoredIds, IDictionary<string, ThreadSummary> summaries)
    {
        foreach (var id in anchoredIds)
        {
            if (summaries.ContainsKey(id))
            {
                continue;
            }

            summaries[id] = new ThreadSummary(
                id,
                $"Anchored missing thread {id}",
                string.Empty,
                string.Empty,
                false,
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                string.Empty,
                false,
                0,
                true,
                new[] { RecoveryFlag.Anchored, RecoveryFlag.DbOnlyMissingFile });
        }
    }

    private static async Task<HashSet<string>> LoadSessionIndexIdsAsync(string codexHome, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexPath = Path.Combine(codexHome, "session_index.jsonl");
        if (!File.Exists(indexPath))
        {
            return ids;
        }

        await using var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1024 * 16, useAsync: true);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    ids.Add(id.GetString()!);
                }
            }
            catch (JsonException)
            {
                // Ignore corrupt index rows; rollout and SQLite state remain authoritative.
            }
        }

        return ids;
    }

    private static IEnumerable<string> EnumerateRollouts(string codexHome)
    {
        var roots = new[]
        {
            Path.Combine(codexHome, "sessions"),
            Path.Combine(codexHome, "archived_sessions")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    private static bool IsArchivedRollout(string codexHome, string rolloutPath)
    {
        var archivedRoot = PathUtilities.NormalizeForComparison(Path.Combine(codexHome, "archived_sessions"));
        var normalizedPath = PathUtilities.NormalizeForComparison(rolloutPath);
        return normalizedPath.StartsWith(archivedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset FromUnixSeconds(long value)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static string? TryGetThreadIdFromRolloutName(string rolloutPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(rolloutPath);
        var match = RolloutIdRegex().Match(fileName);
        return match.Success ? match.Groups["id"].Value : null;
    }

    [GeneratedRegex(@"rollout-\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-(?<id>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RolloutIdRegex();
}
