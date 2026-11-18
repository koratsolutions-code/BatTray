using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>
/// Decides when to warn about a low battery: once per discharge, when a device reaches the critical
/// threshold. The warning re-arms when the device charges or climbs back above the low threshold, so a
/// level hovering around the threshold does not cause repeated notifications.
/// </summary>
public sealed class BatteryAlertTracker
{
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the devices that should be announced now.</summary>
    public IReadOnlyList<Device> Evaluate(IEnumerable<Device> devices, int criticalThreshold, int rearmThreshold)
    {
        var toAnnounce = new List<Device>();
        foreach (var device in devices)
        {
            if (device.BatteryPercent is not { } percent)
                continue;

            if (device.Charging == true || percent > rearmThreshold)
            {
                _alerted.Remove(device.Id);
                continue;
            }

            if (percent <= criticalThreshold && _alerted.Add(device.Id))
                toAnnounce.Add(device);
        }
        return toAnnounce;
    }
}
