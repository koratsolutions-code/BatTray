using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>Demo provider with simulated devices (prototyping and tests).</summary>
public sealed class FakeBatteryProvider : IBatteryProvider
{
    private readonly bool _simulateDrain;
    private readonly TimeProvider _time;
    /// <summary>
    /// Demo devices: one on a 2.4 GHz receiver and three over Bluetooth (ids use the real prefixes so the menu
    /// groups them like real devices; the addresses and receiver id are made up).
    /// </summary>
    private readonly List<(string Id, string Name, DeviceKind Kind, int Percent, string Source)> _devices =
    [
        ("usb:046DC548:demo", "MX Master 3S", DeviceKind.Mouse, 50, "Logitech Bolt receiver (demo)"),
        ("bt:D0A1B2C3D4E5", "WH-1000XM5", DeviceKind.Headset, 80, "Windows Bluetooth (demo)"),
        ("bt:A0A1B2C3D4E7", "AirPods Pro", DeviceKind.Headset, 72, "L 72% · R 78% · case 60% (demo)"),
        ("bt:F0A1B2C3D4E6", "Keychron K3", DeviceKind.Keyboard, 12, "Bluetooth LE (demo)"),
    ];

    /// <param name="simulateDrain">When true, every query lowers each battery by 1 % (wraps back to 100 at 5 %).</param>
    /// <param name="timeProvider">Clock used for <see cref="Device.LastUpdated"/>.</param>
    public FakeBatteryProvider(bool simulateDrain = false, TimeProvider? timeProvider = null)
    {
        _simulateDrain = simulateDrain;
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Name => "Demo";

    /// <summary>Never raised: the demo data only changes when polled.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var result = new List<Device>(_devices.Count);

        for (var i = 0; i < _devices.Count; i++)
        {
            var d = _devices[i];
            result.Add(new Device(d.Id, d.Name, d.Kind, d.Percent, now, d.Source));

            if (_simulateDrain)
                _devices[i] = d with { Percent = d.Percent <= 5 ? 100 : d.Percent - 1 };
        }

        return Task.FromResult<IReadOnlyList<Device>>(result);
    }

    public void Dispose()
    {
    }
}
