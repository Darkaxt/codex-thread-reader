namespace CodexThreadReader.Core;

public static class PathUtilities
{
    public static string NormalizeForComparison(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = StripExtendedPrefix(path.Trim());
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
            // Keep the best-effort normalized string when a historical cwd no longer parses.
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path[4..];
        }

        return path;
    }

    public static bool HasExtendedPrefix(string path)
    {
        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase);
    }

    public static string SafeFileName(string value, int maxLength = 80)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value
            .Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '-' : ch)
            .ToArray();
        var safe = new string(chars).Trim(' ', '.', '-');
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "thread";
        }

        return safe.Length <= maxLength ? safe : safe[..maxLength].TrimEnd(' ', '.', '-');
    }
}
