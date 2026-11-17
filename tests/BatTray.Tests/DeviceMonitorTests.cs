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
        // the Windows PnP provider does not, and its reading may be the most recent one.
        var merged = DeviceMonitor.Merge(
        [
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Keyboard, 100, T0, "Bluetooth LE"),
            new Device("bt:D104C6AECB01", "RK100RGB5.0", DeviceKind.Other, 100, T0.AddSeconds(1), "Windows Bluetooth"),
        ]);

        var device = Assert.Single(merged);
        Assert.Equal(DeviceKind.Keyboard, device.Kind);
        Assert.Equal("Windows Bluetooth", device.Source);
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

        Assert.Equal(3, monitor.Devices.Count);
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
