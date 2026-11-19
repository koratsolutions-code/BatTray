using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>
/// Decides when to warn about a low battery: once per discharge, when a device reaches its alert level
/// (the general critical threshold or the device's own). The warning re-arms when the device charges or
/// climbs back above the low threshold (or its alert level, if higher), so a level hovering around the
/// threshold does not cause repeated notifications.
/// </summary>
public sealed class BatteryAlertTracker
{
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the devices that should be announced now (same alert level for every device).</summary>
    public IReadOnlyList<Device> Evaluate(IEnumerable<Device> devices, int criticalThreshold, int rearmThreshold) =>
        Evaluate(devices, _ => criticalThreshold, rearmThreshold);

    /// <summary>Returns the devices that should be announced now.</summary>
    /// <param name="alertAt">Alert level of each device, or null for a device that must not be announced.</param>
    public IReadOnlyList<Device> Evaluate(IEnumerable<Device> devices, Func<Device, int?> alertAt, int rearmThreshold)
    {
        var toAnnounce = new List<Device>();
        foreach (var device in devices)
        {
            if (device.BatteryPercent is not { } percent)
                continue;

            var threshold = alertAt(device);
            if (device.Charging == true || percent > Math.Max(rearmThreshold, threshold ?? 0))
            {
                _alerted.Remove(device.Id);
                continue;
            }

            if (threshold is { } t && percent <= t && _alerted.Add(device.Id))
                toAnnounce.Add(device);
        }
        return toAnnounce;
    }
}

/// <summary>
/// Decides when to say that a device finished charging: once, when it reaches 100 % while charging (or when
/// its level climbs to 100 % from a lower reading, for devices that do not report charging). Re-arms when the
/// level drops below <see cref="RearmBelow"/>. Estimated levels are never announced.
/// </summary>
public sealed class FullChargeTracker
{
    public const int FullAt = 100;
    public const int RearmBelow = 95;

    private readonly HashSet<string> _announced = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _charging = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _last = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Device> Evaluate(IEnumerable<Device> devices)
    {
        var toAnnounce = new List<Device>();
        foreach (var device in devices)
        {
            if (device.Estimated || device.BatteryPercent is not { } percent)
                continue;

            var rose = _last.TryGetValue(device.Id, out var previous) && previous < FullAt && percent > previous;
            _last[device.Id] = percent;

            if (percent < RearmBelow)
                _announced.Remove(device.Id);
            if (device.Charging == true)
                _charging.Add(device.Id);
            else if (percent < FullAt)
                _charging.Remove(device.Id);

            if (percent >= FullAt && (_charging.Contains(device.Id) || rose) && _announced.Add(device.Id))
                toAnnounce.Add(device);
        }
        return toAnnounce;
    }
}
