using System.Text.Json;

namespace CodexThreadReader.Core;

public sealed class AnchorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AnchorStore(string path, IEnumerable<string> threadIds)
    {
        Path = path;
        ThreadIds = DistinctThreadIds(threadIds).ToArray();
    }

    public string Path { get; }

    public IReadOnlyList<string> ThreadIds { get; }

    public static async Task<AnchorStore> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new AnchorStore(path, Array.Empty<string>());
        }

        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var document = await JsonSerializer.DeserializeAsync<AnchorDocument>(stream, JsonOptions, cancellationToken);
        return new AnchorStore(path, document?.ThreadIds ?? Array.Empty<string>());
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new AnchorDocument(ThreadIds.ToArray());
        await using var stream = File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }

    public AnchorStore WithThreadIds(IEnumerable<string> threadIds)
    {
        return new AnchorStore(Path, threadIds);
    }

    private static IEnumerable<string> DistinctThreadIds(IEnumerable<string> threadIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in threadIds)
        {
            var trimmed = id.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private sealed record AnchorDocument(string[] ThreadIds);
}
