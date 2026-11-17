using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>Demo provider with simulated devices (prototyping and tests).</summary>
public sealed class FakeBatteryProvider : IBatteryProvider
{
    private readonly bool _simulateDrain;
    private readonly TimeProvider _time;
    private readonly List<(string Id, string Name, DeviceKind Kind, int Percent)> _devices =
    [
        ("fake-mouse", "Mouse (demo)", DeviceKind.Mouse, 50),
        ("fake-headset", "Headset (demo)", DeviceKind.Headset, 80),
        ("fake-keyboard", "Keyboard (demo)", DeviceKind.Keyboard, 12),
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
            result.Add(new Device(d.Id, d.Name, d.Kind, d.Percent, now, Name));

            if (_simulateDrain)
                _devices[i] = d with { Percent = d.Percent <= 5 ? 100 : d.Percent - 1 };
        }

        return Task.FromResult<IReadOnlyList<Device>>(result);
    }

    public void Dispose()
    {
    }
}
