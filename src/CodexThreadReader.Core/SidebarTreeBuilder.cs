namespace CodexThreadReader.Core;

public sealed record SidebarGroup(string Title, IReadOnlyList<ThreadSummary> Threads);

public static class SidebarTreeBuilder
{
    public static IReadOnlyList<SidebarGroup> Build(IEnumerable<ThreadSummary> threads)
    {
        var remaining = threads
            .OrderByDescending(t => t.UpdatedAt)
            .ToList();

        var groups = new List<SidebarGroup>();
        AddGroup("Anchored", t => t.IsAnchored);
        AddGroup("Recovery", IsRecoveryThread);
        AddGroup("Projects", t => !t.IsArchived);
        AddGroup("Archived", t => t.IsArchived);
        return groups;

        void AddGroup(string title, Func<ThreadSummary, bool> predicate)
        {
            var selected = remaining.Where(predicate).ToArray();
            if (selected.Length == 0)
            {
                return;
            }

            groups.Add(new SidebarGroup(title, selected));
            foreach (var item in selected)
            {
                remaining.Remove(item);
            }
        }
    }

    private static bool IsRecoveryThread(ThreadSummary thread)
    {
        return !thread.IsArchived
            && !thread.IsAnchored
            && thread.Flags.Any(flag => flag is RecoveryFlag.MissingFromSessionIndex
                or RecoveryFlag.ExtendedPath
                or RecoveryFlag.RolloutOnly
                or RecoveryFlag.DbOnlyMissingFile
                or RecoveryFlag.ProjectOrderMismatch);
    }
}
