using System.Text.Json;

namespace CodexThreadReader.Core;

public sealed class ThemeSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ThemeSettingsStore(string path, ThemeMode mode)
    {
        Path = path;
        Mode = mode;
    }

    public string Path { get; }

    public ThemeMode Mode { get; }

    public static async Task<ThemeSettingsStore> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new ThemeSettingsStore(path, ThemeMode.System);
        }

        try
        {
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = await JsonSerializer.DeserializeAsync<ThemeSettingsDocument>(stream, JsonOptions, cancellationToken);
            return new ThemeSettingsStore(path, document?.ThemeMode ?? ThemeMode.System);
        }
        catch (JsonException)
        {
            return new ThemeSettingsStore(path, ThemeMode.System);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new ThemeSettingsDocument(Mode);
        await using var stream = File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }

    public ThemeSettingsStore WithMode(ThemeMode mode)
    {
        return new ThemeSettingsStore(Path, mode);
    }

    private sealed record ThemeSettingsDocument(ThemeMode ThemeMode);
}
