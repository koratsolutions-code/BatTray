using BatTray.Core.Models;

namespace BatTray.Core.Settings;

/// <summary>User preferences, persisted as JSON.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Ids of devices the user chose not to show on the taskbar.</summary>
    public IReadOnlyList<string> HiddenDeviceIds { get; init; } = [];

    /// <summary>Below this percentage the level is shown as low (amber).</summary>
    public int LowThreshold { get; init; } = BatteryLevelPalette.DefaultLowThreshold;

    /// <summary>Below this percentage the level is shown as critical (red).</summary>
    public int CriticalThreshold { get; init; } = BatteryLevelPalette.DefaultCriticalThreshold;

    /// <summary>Show the widget on the taskbar of every monitor (otherwise only on the primary one).</summary>
    public bool ShowOnAllTaskbars { get; init; } = true;

    /// <summary>Show a Windows notification when a device reaches the critical level.</summary>
    public bool LowBatteryAlerts { get; init; } = true;

    /// <summary>User interface language: <c>auto</c> (follow Windows), <c>en</c> or <c>es</c>.</summary>
    public string Language { get; init; } = "auto";

    /// <summary>Menus and windows: <c>auto</c> (follow Windows), <c>light</c> or <c>dark</c>.</summary>
    public string Theme { get; init; } = "auto";

    /// <summary>Ask GitHub once a day whether a newer BatTray release exists.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>USB receivers the user chose not to list (e.g. a dongle whose device is used over Bluetooth).</summary>
    public IReadOnlyList<string> IgnoredReceiverIds { get; init; } = [];

    /// <summary>Notify when a device finishes charging (reminder to unplug it).</summary>
    public bool FullChargeAlerts { get; init; } = true;

    /// <summary>Order of the devices on the taskbar and in the panel (ids; unknown devices go last).</summary>
    public IReadOnlyList<string> DeviceOrder { get; init; } = [];

    /// <summary>Percentage next to each icon: <c>always</c>, <c>low</c> (only below <see cref="PercentBelow"/>) or <c>never</c> (icons only).</summary>
    public string WidgetPercent { get; init; } = "always";

    /// <summary>With <see cref="WidgetPercent"/> = <c>low</c>, the percentage shows below this level.</summary>
    public int PercentBelow { get; init; } = 50;

    /// <summary>Size of the taskbar items: <c>small</c>, <c>normal</c> or <c>large</c>.</summary>
    public string WidgetSize { get; init; } = "normal";

    /// <summary>Keep a 7-day battery history per device and show it as a chart in the panel.</summary>
    public bool BatteryHistory { get; init; } = true;

    /// <summary>Open the panel with a keyboard shortcut.</summary>
    public bool HotkeyEnabled { get; init; } = true;

    /// <summary>The shortcut, e.g. <c>Ctrl+Alt+B</c>.</summary>
    public string Hotkey { get; init; } = DefaultHotkey;

    public const string DefaultHotkey = "Ctrl+Alt+B";

    public static IReadOnlyList<string> Languages { get; } = ["auto", "en", "es"];
    public static IReadOnlyList<string> Themes { get; } = ["auto", "light", "dark"];
    public static IReadOnlyList<string> PercentModes { get; } = ["always", "low", "never"];
    public static IReadOnlyList<string> WidgetSizes { get; } = ["small", "normal", "large"];

    /// <summary>Level at which to notify about this device, or null when it should not be notified.</summary>
    public int? AlertThresholdFor(string deviceId)
    {
        if (!LowBatteryAlerts)
            return null;
        var custom = CustomizationFor(deviceId);
        if (custom?.NoAlerts == true)
            return null;
        return custom?.AlertAt ?? CriticalThreshold;
    }

    /// <summary>Sorts devices by <see cref="DeviceOrder"/>; devices not in it keep their relative order, after the others.</summary>
    public IReadOnlyList<Device> Order(IEnumerable<Device> devices)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < DeviceOrder.Count; i++)
            rank.TryAdd(DeviceOrder[i], i);
        return devices
            .Select((d, i) => (Device: d, Rank: rank.TryGetValue(d.Id, out var r) ? r : int.MaxValue, Index: i))
            .OrderBy(x => x.Rank).ThenBy(x => x.Index)
            .Select(x => x.Device)
            .ToList();
    }

    /// <summary>
    /// Puts <paramref name="orderedIds"/> first, in that order, followed by any other ids already saved
    /// (devices not connected now keep their place relative to each other).
    /// </summary>
    public AppSettings WithOrder(IEnumerable<string> orderedIds)
    {
        var ids = orderedIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rest = DeviceOrder.Where(id => !ids.Contains(id, StringComparer.OrdinalIgnoreCase));
        return this with { DeviceOrder = [.. ids, .. rest] };
    }

    private const int MaxOrderedDevices = 200;

    public bool IsIgnored(string receiverId) =>
        IgnoredReceiverIds.Contains(receiverId, StringComparer.OrdinalIgnoreCase);

    public AppSettings WithIgnored(string receiverId, bool ignored)
    {
        var others = IgnoredReceiverIds.Where(id => !string.Equals(id, receiverId, StringComparison.OrdinalIgnoreCase));
        return this with { IgnoredReceiverIds = ignored ? [.. others, receiverId] : [.. others] };
    }

    /// <summary>Custom names and icons, by device id.</summary>
    public IReadOnlyDictionary<string, DeviceCustomization> Customizations { get; init; } =
        new Dictionary<string, DeviceCustomization>(StringComparer.OrdinalIgnoreCase);

    public DeviceCustomization? CustomizationFor(string deviceId) =>
        Customizations.TryGetValue(deviceId, out var c) ? c : null;

    /// <summary>Sets (or removes, when null or empty) the customization of a device.</summary>
    public AppSettings WithCustomization(string deviceId, DeviceCustomization? customization)
    {
        var map = new Dictionary<string, DeviceCustomization>(Customizations, StringComparer.OrdinalIgnoreCase);
        var normalized = customization?.Normalize();
        if (normalized is null || normalized.IsEmpty)
            map.Remove(deviceId);
        else
            map[deviceId] = normalized;
        return this with { Customizations = map };
    }

    /// <summary>The device with the user's name and icon applied.</summary>
    public Device Customize(Device device) =>
        CustomizationFor(device.Id) is { } c
            ? device with { Name = c.Name ?? device.Name, Icon = c.Icon }
            : device;

    public bool IsHidden(string deviceId) =>
        HiddenDeviceIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase);

    public AppSettings WithHidden(string deviceId, bool hidden)
    {
        var others = HiddenDeviceIds.Where(id => !string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase));
        return this with { HiddenDeviceIds = hidden ? [.. others, deviceId] : [.. others] };
    }

    /// <summary>Repairs out-of-range values (e.g. from a hand-edited file).</summary>
    public AppSettings Normalize()
    {
        var low = Math.Clamp(LowThreshold, 1, 100);
        var critical = Math.Clamp(CriticalThreshold, 0, low);
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            HiddenDeviceIds = (HiddenDeviceIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LowThreshold = low,
            Customizations = (Customizations ?? new Dictionary<string, DeviceCustomization>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
                .Select(kv => (kv.Key, Value: kv.Value.Normalize()))
                .Where(kv => !kv.Value.IsEmpty)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase),
            CriticalThreshold = critical,
            Language = Languages.Contains(Language?.ToLowerInvariant()) ? Language!.ToLowerInvariant() : "auto",
            Theme = Themes.Contains(Theme?.ToLowerInvariant()) ? Theme!.ToLowerInvariant() : "auto",
            IgnoredReceiverIds = (IgnoredReceiverIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            DeviceOrder = (DeviceOrder ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxOrderedDevices).ToList(),
            WidgetPercent = PercentModes.Contains(WidgetPercent?.ToLowerInvariant()) ? WidgetPercent!.ToLowerInvariant() : "always",
            PercentBelow = Math.Clamp(PercentBelow, 5, 100),
            WidgetSize = WidgetSizes.Contains(WidgetSize?.ToLowerInvariant()) ? WidgetSize!.ToLowerInvariant() : "normal",
            Hotkey = string.IsNullOrWhiteSpace(Hotkey) || Hotkey.Length > 40 ? DefaultHotkey : Hotkey.Trim(),
        };
    }
}
