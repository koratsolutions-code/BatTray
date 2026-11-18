using BatTray.App.Icons;
using BatTray.App.Localization;
using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.App.Panel;

/// <summary>One row of the detail panel.</summary>
public sealed record DetailPanelItem(
    string Name,
    DeviceKind Kind,
    int? Percent,
    BatteryLevel Level,
    bool IsCharging,
    bool IsHidden,
    string Details,
    bool IsEstimated = false,
    DeviceIcon? Icon = null)
{
    public string PercentText => Percent is { } p ? $"{(IsEstimated ? "~" : string.Empty)}{p}%" : "—";

    /// <summary>Bar fill, 0–1.</summary>
    public double Fraction => (Percent ?? 0) / 100.0;

    public static DetailPanelItem From(Device device, int low, int critical, bool hidden, string? estimateDetails = null)
    {
        var details = device.Estimated && estimateDetails is not null
            ? estimateDetails
            : $"{L.Updated(device.LastUpdated)} · {device.Source}";
        if (hidden)
            details += $" · {L.HiddenFromTaskbar}";
        return new DetailPanelItem(
            device.Name,
            device.Kind,
            device.BatteryPercent,
            BatteryLevelPalette.Classify(device.BatteryPercent, low, critical),
            device.Charging == true,
            hidden,
            details,
            device.Estimated,
            DeviceIconProvider.Resolve(device.Kind, device.Icon));
    }
}
