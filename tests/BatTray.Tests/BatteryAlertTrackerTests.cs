using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.Tests;

public class BatteryAlertTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static Device Mouse(int? percent, bool? charging = null) =>
        new("bt:1", "Mouse", DeviceKind.Mouse, percent, T0, "test", charging);

    [Fact]
    public void AnnouncesOnce_WhenReachingCritical()
    {
        var tracker = new BatteryAlertTracker();

        Assert.Empty(tracker.Evaluate([Mouse(20)], 15, 30));
        Assert.Single(tracker.Evaluate([Mouse(15)], 15, 30));
        Assert.Empty(tracker.Evaluate([Mouse(12)], 15, 30));
        Assert.Empty(tracker.Evaluate([Mouse(5)], 15, 30));
    }

    [Fact]
    public void HoveringAroundThreshold_DoesNotRepeat()
    {
        var tracker = new BatteryAlertTracker();
        tracker.Evaluate([Mouse(14)], 15, 30);

        Assert.Empty(tracker.Evaluate([Mouse(16)], 15, 30));
        Assert.Empty(tracker.Evaluate([Mouse(14)], 15, 30));
    }

    [Fact]
    public void Charging_RearmsAndSuppresses()
    {
        var tracker = new BatteryAlertTracker();
        tracker.Evaluate([Mouse(10)], 15, 30);

        Assert.Empty(tracker.Evaluate([Mouse(10, charging: true)], 15, 30));
        Assert.Single(tracker.Evaluate([Mouse(10, charging: false)], 15, 30));
    }

    [Fact]
    public void RisingAboveLowThreshold_Rearms()
    {
        var tracker = new BatteryAlertTracker();
        tracker.Evaluate([Mouse(10)], 15, 30);
        tracker.Evaluate([Mouse(80)], 15, 30); // batteries replaced

        Assert.Single(tracker.Evaluate([Mouse(12)], 15, 30));
    }

    [Fact]
    public void UnknownLevel_IsIgnored() =>
        Assert.Empty(new BatteryAlertTracker().Evaluate([Mouse(null)], 15, 30));
}
