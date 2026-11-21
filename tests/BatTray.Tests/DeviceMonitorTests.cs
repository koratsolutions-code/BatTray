using BatTray.Core;
using BatTray.Core.Models;
using BatTray.Core.Providers;

namespace BatTray.Tests;

public class DeviceMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_SameId_PrefersKnownPercentThenMostRecent()
    {
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:AABBCCDDEEFF", "Mouse", DeviceKind.Mouse, null, T0.AddMinutes(5)),
            new Device("BT:aabbccddeeff", "Mouse", DeviceKind.Mouse, 40, T0),
            new Device("bt:AABBCCDDEEFF", "Mouse", DeviceKind.Mouse, 38, T0.AddMinutes(1)),
        ]);

        var device = Assert.Single(merged);
        Assert.Equal(38, device.BatteryPercent);
    }

    [Fact]
    public void Merge_KeepsKnownKind_WhenWinningReadingHasNone()
    {
        // Real case: RK100 over Bluetooth — the GATT provider knows it is a keyboard (Appearance),
        // the Windows PnP provider does not. A reading much more recent wins and keeps the known kind.
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Keyboard, 100, T0, "Bluetooth LE"),
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Other, 100, T0.AddMinutes(5), "Windows Bluetooth"),
        ]);

        var device = Assert.Single(merged);
        Assert.Equal(DeviceKind.Keyboard, device.Kind);
        Assert.Equal("Windows Bluetooth", device.Source);
    }

    [Fact]
    public void Merge_SamePoll_FirstProviderWins()
    {
        // Per-earbud reading (listed first) and Windows' single level taken a second later in the same poll.
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:A1B2C3D4E5F6", "Buds", DeviceKind.Headset, 70, T0, "L 70% · R 90% · case 50%"),
            new Device("bt:A1B2C3D4E5F6", "Buds", DeviceKind.Headset, 80, T0.AddSeconds(1), "Windows Bluetooth"),
        ]);

        Assert.Equal("L 70% · R 90% · case 50%", Assert.Single(merged).Source);
    }

    [Fact]
    public void Merge_UsesKindFromMetadataOnlyEntry_AndDropsUnmeasuredDevices()
    {
        // Real case: RK100 asleep — GATT only knows it is a keyboard; Windows still reports 100 %.
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Keyboard, null, T0, "Bluetooth LE"),
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Other, 100, T0, "Windows Bluetooth"),
            new Device("bt:111111111111", "Sleeping mouse", DeviceKind.Mouse, null, T0, "Bluetooth LE"),
        ]);

        var device = Assert.Single(merged);
        Assert.Equal(DeviceKind.Keyboard, device.Kind);
        Assert.Equal(100, device.BatteryPercent);
    }

    [Fact]
    public void Merge_SameNamedControllers_BecomeOne_PreferringChargingInfo()
    {
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:AABBCCDDEEFF", "Xbox Wireless Controller", DeviceKind.Gamepad, 60, T0, "Bluetooth LE"),
            new Device("gip:{123}", "Xbox Wireless Controller", DeviceKind.Gamepad, 61, T0, "Game controller", Charging: false),
            new Device("bt:111111111111", "Mouse", DeviceKind.Mouse, 50, T0),
            new Device("bt:222222222222", "Mouse", DeviceKind.Mouse, 40, T0),
        ]);

        Assert.Equal(3, merged.Count); // identical mouse names are NOT merged
        var controller = Assert.Single(merged, d => d.Kind == DeviceKind.Gamepad);
        Assert.Equal("Game controller", controller.Source);
    }

    [Fact]
    public void Merge_SortsByName()
    {
        var merged = DeviceMonitor.Merge(
        [
            new Device("2", "Keyboard", DeviceKind.Keyboard, 10, T0),
            new Device("1", "Headset", DeviceKind.Headset, 90, T0),
        ]);

        Assert.Equal(["Headset", "Keyboard"], merged.Select(d => d.Name));
    }

    [Fact]
    public async Task Refresh_FailingProvider_DoesNotHideOthers()
    {
        using var monitor = new DeviceMonitor([new ThrowingProvider(), new FakeBatteryProvider()], TimeSpan.FromMinutes(1));
        Exception? reported = null;
        monitor.ProviderFailed += (_, f) => reported = f.Error;

        await monitor.RefreshAsync();

        Assert.Equal(4, monitor.Devices.Count); // the four demo devices
        Assert.IsType<InvalidOperationException>(reported);
    }

    [Fact]
    public async Task ProviderChanged_TriggersRefreshBeforeNextPoll()
    {
        var provider = new SignalingProvider();
        using var monitor = new DeviceMonitor([provider], TimeSpan.FromHours(1));
        var refreshed = new TaskCompletionSource<IReadOnlyList<Device>>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.DevicesChanged += (_, devices) =>
        {
            if (devices.Count > 0)
                refreshed.TrySetResult(devices);
        };

        monitor.Start();
        await Task.Delay(100);
        provider.Device = new Device("bt:1", "Mouse", DeviceKind.Mouse, 77, T0);
        provider.RaiseChanged();

        var devices = await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(77, Assert.Single(devices).BatteryPercent);
    }

    [Fact]
    public async Task FakeProvider_SimulatedDrain_DecreasesPercent()
    {
        using var provider = new FakeBatteryProvider(simulateDrain: true);

        var first = await provider.GetDevicesAsync();
        var second = await provider.GetDevicesAsync();

        Assert.All(first.Zip(second), pair => Assert.Equal(pair.First.BatteryPercent - 1, pair.Second.BatteryPercent));
    }

    private sealed class ThrowingProvider : IBatteryProvider
    {
        public string Name => "Throwing";

        public event EventHandler? Changed { add { } remove { } }

        public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");

        public void Dispose()
        {
        }
    }

    private sealed class SignalingProvider : IBatteryProvider
    {
        public Device? Device { get; set; }

        public string Name => "Signaling";

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Device>>(Device is null ? [] : [Device]);

        public void Dispose()
        {
        }
    }
}
