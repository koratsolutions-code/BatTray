using BatTray.Core;
using BatTray.Core.Estimation;
using BatTray.Core.Hid;
using BatTray.Core.Models;

namespace BatTray.Tests;

public class BatteryEstimatorTests
{
    private const string Id = "usb:32060861:cc2ec71609755cdaa9216e9f43d2683b";
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewEstimate_StartsAtTheGivenLevel()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, ratedHours: 30, currentPercent: 80, T0);
        Assert.Equal(80, estimator.EstimatePercent(Id, T0));
        Assert.Equal(6, estimator.Get(Id)!.UsedHours, 3); // 20 % of 30 h
    }

    [Fact]
    public void ContinuousUse_CountsElapsedTime()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 10, 100, T0);

        // Activity every 5 minutes for 3 hours (gaps shorter than the tail): 3 h counted.
        for (var t = T0; t <= T0.AddHours(3); t = t.AddMinutes(5))
            estimator.RecordActivity(Id, t);

        Assert.Equal(3, estimator.Get(Id)!.UsedHours, 3);
        // Ten more minutes count as in use after the last signal: 3 h 10 min of 10 h → 68 %.
        Assert.Equal(68, estimator.EstimatePercent(Id, T0.AddHours(5)));
    }

    [Fact]
    public void LongGap_CountsOnlyTheTail()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 10, 100, T0);
        estimator.RecordActivity(Id, T0);
        estimator.RecordActivity(Id, T0.AddDays(1)); // off overnight

        Assert.Equal(BatteryEstimator.ActivityTail.TotalHours, estimator.Get(Id)!.UsedHours, 6);
    }

    [Fact]
    public void Percent_IsClampedAtZero()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 1, 10, T0);
        for (var t = T0; t <= T0.AddHours(2); t = t.AddMinutes(5))
            estimator.RecordActivity(Id, t);
        Assert.Equal(0, estimator.EstimatePercent(Id, T0.AddHours(2)));
    }

    [Fact]
    public void MarkCharged_ResetsToFull()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 10, 40, T0);
        estimator.MarkCharged(Id, T0.AddHours(1));
        Assert.Equal(100, estimator.EstimatePercent(Id, T0.AddHours(1)));
    }

    [Fact]
    public void Activity_ForUnknownDevice_IsIgnored()
    {
        var estimator = new BatteryEstimator();
        estimator.RecordActivity(Id, T0);
        Assert.Null(estimator.EstimatePercent(Id, T0));
        Assert.False(estimator.IsDirty);
    }

    [Fact]
    public void ClockGoingBack_IsIgnored()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 10, 100, T0);
        estimator.RecordActivity(Id, T0.AddHours(1));
        estimator.RecordActivity(Id, T0);
        Assert.Equal(0, estimator.Get(Id)!.UsedHours, 6);
    }

    [Fact]
    public void Changed_IsRaisedForUserActionsOnly()
    {
        var estimator = new BatteryEstimator();
        var changes = 0;
        estimator.Changed += (_, _) => changes++;

        estimator.Configure(Id, "OW810", DeviceKind.Headset, 10, 100, T0);
        estimator.RecordActivity(Id, T0.AddMinutes(1));
        estimator.MarkCharged(Id, T0.AddMinutes(2));
        estimator.Remove(Id);

        Assert.Equal(3, changes);
    }

    [Fact]
    public void Store_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-estimates-{Guid.NewGuid():N}.json");
        try
        {
            var store = new EstimateStore(path);
            var estimator = new BatteryEstimator();
            estimator.Configure(Id, "OW810", DeviceKind.Headset, 30, 75, T0);
            estimator.RecordActivity(Id, T0.AddMinutes(1));
            store.Save(estimator.Snapshot(markSaved: true));

            var loaded = new BatteryEstimator(store.Load());
            Assert.Equal(estimator.Get(Id), loaded.Get(Id));
            Assert.False(estimator.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_CorruptFile_LoadsNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-estimates-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Empty(new EstimateStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Provider_ReportsOnlyPresentReceivers_AsEstimated()
    {
        var estimator = new BatteryEstimator();
        estimator.Configure(Id, "OW810", DeviceKind.Headset, 30, 90, T0);
        estimator.Configure("usb:31511025:fcc45fde04c25b0b8097cb00db18d985", "Mouse", DeviceKind.Mouse, 100, 50, T0);

        var receiver = new HidReceiver([new HidCollectionInfo(@"\\?\HID#x", @"HID\VID_3206&PID_0861\X",
            Guid.Parse("cc2ec716-0975-5cda-a921-6e9f43d2683b"), 0x3206, 0x0861, 1, 0x0C, 0x01, 3, 0, 0, 3, "Beibinghe", null, [])]);
        using var provider = new EstimatedBatteryProvider(estimator, () => [receiver]);

        var devices = await provider.GetDevicesAsync();

        var device = Assert.Single(devices);
        Assert.Equal(Id, device.Id);
        Assert.True(device.Estimated);
        Assert.Equal("OW810", device.Name);
    }

    [Fact]
    public void Merge_PrefersMeasuredReadingOverEstimate()
    {
        var estimate = new Device(Id, "OW810", DeviceKind.Headset, 90, T0.AddMinutes(5), Estimated: true);
        var measured = new Device(Id, "OW810", DeviceKind.Headset, 42, T0);

        var merged = Assert.Single(DeviceMonitor.Merge([estimate, measured]));
        Assert.Equal(42, merged.BatteryPercent);
        Assert.False(merged.Estimated);
    }

    [Theory]
    [InlineData("usb:32060861:cc2ec71609755cdaa9216e9f43d2683b", 0x3206, 0x0861, true)]
    [InlineData(@"usb:25A7FA70:HID\VID_25A7&PID_FA70\X", 0x25A7, 0xFA70, false)]
    public void ReceiverId_Parses(string id, int vid, int pid, bool hasContainer)
    {
        Assert.True(ReceiverId.TryParse(id, out var v, out var p, out var container));
        Assert.Equal(vid, v);
        Assert.Equal(pid, p);
        Assert.Equal(hasContainer, container.HasValue);
    }

    [Theory]
    [InlineData("bt:D104C6AECB01")]
    [InlineData("usb:XYZ")]
    public void ReceiverId_RejectsOtherIds(string id) =>
        Assert.False(ReceiverId.TryParse(id, out _, out _, out _));
}
