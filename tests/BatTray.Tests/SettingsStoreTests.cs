using BatTray.Core.Settings;

namespace BatTray.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BatTray.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = new SettingsStore(FilePath).Load();

        Assert.Empty(settings.HiddenDeviceIds);
        Assert.Equal(30, settings.LowThreshold);
        Assert.Equal(15, settings.CriticalThreshold);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var saved = new AppSettings { LowThreshold = 40, CriticalThreshold = 10 }.WithHidden("bt:D104C6AECB01", hidden: true);

        store.Save(saved);
        var loaded = store.Load();

        Assert.Equal(["bt:D104C6AECB01"], loaded.HiddenDeviceIds);
        Assert.Equal(40, loaded.LowThreshold);
        Assert.Equal(10, loaded.CriticalThreshold);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(new AppSettings().LowThreshold, new SettingsStore(FilePath).Load().LowThreshold);
    }

    [Fact]
    public void Load_HandEditedFile_IsNormalised()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
            {
              // comments and trailing commas are tolerated
              "lowThreshold": 250,
              "criticalThreshold": 500,
              "hiddenDeviceIds": ["a", "A", ""],
            }
            """);

        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal(100, settings.LowThreshold);
        Assert.Equal(100, settings.CriticalThreshold);
        Assert.Equal(["a"], settings.HiddenDeviceIds);
    }

    [Fact]
    public void WithHidden_TogglesCaseInsensitively()
    {
        var settings = new AppSettings().WithHidden("bt:AA", true);

        Assert.True(settings.IsHidden("BT:aa"));
        Assert.False(settings.WithHidden("BT:aa", false).IsHidden("bt:AA"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
