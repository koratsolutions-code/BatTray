namespace BatTray.Core.Models;

/// <summary>A peripheral with its last known battery level.</summary>
/// <param name="Id">Stable identifier (e.g. <c>bt:AABBCCDDEEFF</c> for Bluetooth devices).</param>
/// <param name="Name">Display name of the device.</param>
/// <param name="Kind">Device type.</param>
/// <param name="BatteryPercent">0–100, or <c>null</c> when unknown.</param>
/// <param name="LastUpdated">Time of the last reading.</param>
/// <param name="Source">Name of the provider that produced the reading.</param>
public sealed record Device(
    string Id,
    string Name,
    DeviceKind Kind,
    int? BatteryPercent,
    DateTimeOffset LastUpdated,
    string Source = "");
