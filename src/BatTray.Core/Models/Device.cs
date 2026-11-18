namespace BatTray.Core.Models;

/// <summary>A peripheral with its last known battery level.</summary>
/// <param name="Id">Stable identifier (e.g. <c>bt:AABBCCDDEEFF</c> for Bluetooth devices).</param>
/// <param name="Name">Display name of the device.</param>
/// <param name="Kind">Device type.</param>
/// <param name="BatteryPercent">0–100, or <c>null</c> when unknown.</param>
/// <param name="LastUpdated">Time of the last reading.</param>
/// <param name="Source">Name of the provider that produced the reading.</param>
/// <param name="Charging">True while charging, false when known to be discharging, null when unknown.</param>
/// <param name="Icon">Icon chosen by the user (see <c>DeviceCustomization.Icon</c>), or null for the default one.</param>
/// <param name="Estimated">True when the level is estimated from usage time instead of reported by the device.</param>
public sealed record Device(
    string Id,
    string Name,
    DeviceKind Kind,
    int? BatteryPercent,
    DateTimeOffset LastUpdated,
    string Source = "",
    bool? Charging = null,
    bool Estimated = false,
    string? Icon = null);
