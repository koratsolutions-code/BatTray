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
            new Device("AA:BB", "Mouse", DeviceKind.Mouse, null, T0.AddMinutes(5)),
            new Device("aa:bb", "Mouse", DeviceKind.Mouse, 40, T0),
            new Device("AA:BB", "Mouse", DeviceKind.Mouse, 38, T0.AddMinutes(1)),
        ]);

        var device = Assert.Single(merged);
        Assert.Equal(38, device.BatteryPercent);
    }

    [Fact]
    public void Merge_SortsByName()
    {
        var merged = DeviceMonitor.Merge(
        [
            new Device("2", "Teclado", DeviceKind.Keyboard, 10, T0),
            new Device("1", "Auriculares", DeviceKind.Headset, 90, T0),
        ]);

        Assert.Equal(["Auriculares", "Teclado"], merged.Select(d => d.Name));
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
    public async Task FakeProvider_SimulatedDrain_DecreasesPercent()
    {
        var provider = new FakeBatteryProvider(simulateDrain: true);

        var first = await provider.GetDevicesAsync();
        var second = await provider.GetDevicesAsync();

        Assert.All(first.Zip(second), pair => Assert.Equal(pair.First.BatteryPercent - 1, pair.Second.BatteryPercent));
    }

    private sealed class ThrowingProvider : IBatteryProvider
    {
        public string Name => "Throwing";

        public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
