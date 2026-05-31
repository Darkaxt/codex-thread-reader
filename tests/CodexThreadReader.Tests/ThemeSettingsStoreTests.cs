using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class ThemeSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_defaults_to_system_when_settings_file_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        var settings = await ThemeSettingsStore.LoadAsync(path, CancellationToken.None);

        Assert.Equal(ThemeMode.System, settings.Mode);
        Assert.Equal(path, settings.Path);
    }

    [Fact]
    public async Task SaveAsync_persists_selected_theme_mode()
    {
        var root = Directory.CreateTempSubdirectory("ctr-theme-");
        var path = Path.Combine(root.FullName, "settings.json");
        var settings = new ThemeSettingsStore(path, ThemeMode.Dark);

        await settings.SaveAsync(CancellationToken.None);
        var reloaded = await ThemeSettingsStore.LoadAsync(path, CancellationToken.None);

        Assert.Equal(ThemeMode.Dark, reloaded.Mode);
    }
}
