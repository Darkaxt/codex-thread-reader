using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class SidebarTreeBuilderTests
{
    [Fact]
    public void Build_groups_anchored_smoked_active_projects_and_archived_threads_for_sidebar_display()
    {
        var threads = new[]
        {
            MakeThread("anchor", "Anchored", "C:\\repo-a", false, true, RecoveryFlag.Anchored),
            MakeThread("hidden", "Hidden", "C:\\repo-b", false, false, RecoveryFlag.MissingFromSessionIndex),
            MakeThread("active-a", "Active A", "C:\\repo-a", false),
            MakeThread("active-c", "Active C", "C:\\repo-c", false),
            MakeThread("archived", "Archived", "C:\\repo-a", true, false, RecoveryFlag.Archived)
        };

        var groups = SidebarTreeBuilder.Build(threads);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal("Anchored", group.Title);
                Assert.Equal(new[] { "anchor" }, group.Threads.Select(t => t.Id));
            },
            group =>
            {
                Assert.Equal("Recovery", group.Title);
                Assert.Equal(new[] { "hidden" }, group.Threads.Select(t => t.Id));
            },
            group =>
            {
                Assert.Equal("Projects", group.Title);
                Assert.Equal(new[] { "active-a", "active-c" }, group.Threads.Select(t => t.Id));
            },
            group =>
            {
                Assert.Equal("Archived", group.Title);
                Assert.Equal(new[] { "archived" }, group.Threads.Select(t => t.Id));
            });
    }

    private static ThreadSummary MakeThread(string id, string title, string cwd, bool archived, bool anchored = false, params RecoveryFlag[] flags)
    {
        return new ThreadSummary(
            id,
            title,
            cwd,
            cwd,
            archived,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z").AddMinutes(id.Length),
            $"C:\\rollouts\\{id}.jsonl",
            true,
            100,
            anchored,
            flags);
    }
}
