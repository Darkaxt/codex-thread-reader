using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class AnchorStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_empty_store_when_config_file_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "anchors.json");

        var store = await AnchorStore.LoadAsync(path, CancellationToken.None);

        Assert.Empty(store.ThreadIds);
        Assert.Equal(path, store.Path);
    }

    [Fact]
    public async Task SaveAsync_persists_unique_thread_ids_in_insertion_order()
    {
        var root = Directory.CreateTempSubdirectory("ctr-anchors-");
        var path = Path.Combine(root.FullName, "anchors.json");
        var store = new AnchorStore(path, new[] { "thread-a", "thread-b", "thread-a" });

        await store.SaveAsync(CancellationToken.None);
        var reloaded = await AnchorStore.LoadAsync(path, CancellationToken.None);

        Assert.Equal(new[] { "thread-a", "thread-b" }, reloaded.ThreadIds);
    }
}
