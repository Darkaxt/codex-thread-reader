using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class SidebarTreeBuilderTests
{
    [Fact]
    public void Build_groups_threads_by_project_path_and_sorts_anchored_threads_first_inside_project()
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
                Assert.Equal("repo-a", group.Title);
                Assert.Equal(new[] { "Active", "Archived" }, group.StatusGroups.Select(status => status.Title));
                Assert.Equal(new[] { "anchor", "active-a" }, group.StatusGroups[0].Threads.Select(t => t.Id));
                Assert.Equal(new[] { "archived" }, group.StatusGroups[1].Threads.Select(t => t.Id));
            },
            group =>
            {
                Assert.Equal("repo-c", group.Title);
                Assert.Equal(new[] { "Active" }, group.StatusGroups.Select(status => status.Title));
                Assert.Equal(new[] { "active-c" }, group.StatusGroups[0].Threads.Select(t => t.Id));
            },
            group =>
            {
                Assert.Equal("repo-b", group.Title);
                Assert.Equal(new[] { "Active" }, group.StatusGroups.Select(status => status.Title));
                Assert.Equal(new[] { "hidden" }, group.StatusGroups[0].Threads.Select(t => t.Id));
            });
    }

    [Fact]
    public void Build_combines_threads_with_the_same_project_label_even_when_paths_differ()
    {
        var threads = new[]
        {
            MakeThread("one", "One", "C:\\worktrees\\aonsoku-basic-auth", true, false, RecoveryFlag.Archived),
            MakeThread("two", "Two", "D:\\old\\aonsoku-basic-auth", true, false, RecoveryFlag.Archived),
            MakeThread("three", "Three", "T:\\cloud\\aonsoku-basic-auth", false)
        };

        var groups = SidebarTreeBuilder.Build(threads);

        var group = Assert.Single(groups);
        Assert.Equal("aonsoku-basic-auth", group.Title);
        Assert.Equal(new[] { "Active", "Archived" }, group.StatusGroups.Select(status => status.Title));
        Assert.Equal(new[] { "three" }, group.StatusGroups[0].Threads.Select(t => t.Id));
        Assert.Equal(new[] { "one", "two" }, group.StatusGroups[1].Threads.Select(t => t.Id));
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
