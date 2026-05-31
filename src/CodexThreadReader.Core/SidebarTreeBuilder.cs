namespace CodexThreadReader.Core;

public sealed record SidebarGroup(string Title, IReadOnlyList<ThreadSummary> Threads);

public static class SidebarTreeBuilder
{
    public static IReadOnlyList<SidebarGroup> Build(IEnumerable<ThreadSummary> threads)
    {
        return threads
            .GroupBy(ProjectKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SidebarGroup(ProjectTitle(group.Key), SortThreads(group)))
            .OrderByDescending(group => group.Threads.Any(t => t.IsAnchored))
            .ThenByDescending(group => group.Threads.Max(t => t.UpdatedAt))
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ProjectKey(ThreadSummary thread)
    {
        return string.IsNullOrWhiteSpace(thread.NormalizedCwd)
            ? "No project"
            : thread.NormalizedCwd;
    }

    private static string ProjectTitle(string projectKey)
    {
        if (projectKey == "No project")
        {
            return projectKey;
        }

        var title = Path.GetFileName(projectKey);
        return string.IsNullOrWhiteSpace(title) ? projectKey : title;
    }

    private static IReadOnlyList<ThreadSummary> SortThreads(IEnumerable<ThreadSummary> threads)
    {
        return threads
            .OrderByDescending(t => t.IsAnchored)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
