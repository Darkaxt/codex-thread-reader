namespace CodexThreadReader.Core;

public sealed record SidebarGroup(string Title, IReadOnlyList<SidebarStatusGroup> StatusGroups);

public sealed record SidebarStatusGroup(string Title, IReadOnlyList<ThreadSummary> Threads);

public static class SidebarTreeBuilder
{
    public static IReadOnlyList<SidebarGroup> Build(IEnumerable<ThreadSummary> threads)
    {
        return threads
            .GroupBy(thread => ProjectTitle(ProjectKey(thread)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SidebarGroup(group.Key, BuildStatusGroups(group)))
            .Where(group => group.StatusGroups.Count > 0)
            .OrderByDescending(group => group.StatusGroups.SelectMany(status => status.Threads).Any(t => t.IsAnchored))
            .ThenByDescending(group => group.StatusGroups.SelectMany(status => status.Threads).Max(t => t.UpdatedAt))
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

    private static IReadOnlyList<SidebarStatusGroup> BuildStatusGroups(IEnumerable<ThreadSummary> threads)
    {
        var active = SortThreads(threads.Where(t => !t.IsArchived));
        var archived = SortThreads(threads.Where(t => t.IsArchived));
        var groups = new List<SidebarStatusGroup>();
        if (active.Count > 0)
        {
            groups.Add(new SidebarStatusGroup("Active", active));
        }

        if (archived.Count > 0)
        {
            groups.Add(new SidebarStatusGroup("Archived", archived));
        }

        return groups;
    }
}
