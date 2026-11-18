using BatTray.Core.Models;
using BatTray.Core.Settings;

namespace BatTray.Tests;

public class DeviceCustomizationTests
{
    private const string Id = "bt:D104C6AECB01";
    private static readonly Device Keyboard = new(Id, "RK100RGB5.0", DeviceKind.Keyboard, 90, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Customize_AppliesNameAndIcon()
    {
        var settings = new AppSettings().WithCustomization(Id, new DeviceCustomization("Work keyboard", "keyboard"));
        var device = settings.Customize(Keyboard);

        Assert.Equal("Work keyboard", device.Name);
        Assert.Equal("keyboard", device.Icon);
        Assert.Equal(90, device.BatteryPercent);
    }

    [Fact]
    public void Customize_IconOnly_KeepsTheDeviceName()
    {
        var settings = new AppSettings().WithCustomization(Id, new DeviceCustomization(Icon: "pen"));
        Assert.Equal("RK100RGB5.0", settings.Customize(Keyboard).Name);
    }

    [Fact]
    public void WithCustomization_EmptyOrNull_Removes()
    {
        var settings = new AppSettings().WithCustomization(Id, new DeviceCustomization("A"));
        Assert.Empty(settings.WithCustomization(Id, null).Customizations);
        Assert.Empty(settings.WithCustomization(Id, new DeviceCustomization("  ", "")).Customizations);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var settings = new AppSettings().WithCustomization(Id, new DeviceCustomization("A"));
        Assert.NotNull(settings.CustomizationFor(Id.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("earbuds", true)]
    [InlineData("file:0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("file:../../Windows/evil.png", false)]
    [InlineData(@"file:C:\secret.png", false)]
    [InlineData("file:0123456789abcdef0123456789abcdef.exe", false)]
    [InlineData("Icon.Mouse", false)]
    [InlineData("x", false)]
    public void IsValidIcon_OnlyAllowsBuiltInKeysAndStoredFiles(string icon, bool valid) =>
        Assert.Equal(valid, DeviceCustomization.IsValidIcon(icon));

    [Fact]
    public void Normalize_TrimsAndDropsInvalidValues()
    {
        var c = new DeviceCustomization("  " + new string('n', 80) + "  ", "file:../x.png").Normalize();
        Assert.Equal(DeviceCustomization.MaxNameLength, c.Name!.Length);
        Assert.Null(c.Icon);
    }

    [Fact]
    public void Settings_RoundTripCustomizations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings().WithCustomization(Id, new DeviceCustomization("Work keyboard", "keyboard")));
            var loaded = store.Load();
            Assert.Equal(new DeviceCustomization("Work keyboard", "keyboard"), loaded.CustomizationFor(Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_HandEditedInvalidIcon_IsDroppedOnLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "customizations": { "bt:1": { "name": "X", "icon": "file:..\\..\\evil.png" } } }""");
        try
        {
            var loaded = new SettingsStore(path).Load();
            Assert.Equal(new DeviceCustomization("X"), loaded.CustomizationFor("bt:1"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
