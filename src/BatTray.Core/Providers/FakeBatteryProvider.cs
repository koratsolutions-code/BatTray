using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>Proveedor de demostración con dispositivos simulados (prototipo y pruebas).</summary>
public sealed class FakeBatteryProvider : IBatteryProvider
{
    private readonly bool _simulateDrain;
    private readonly TimeProvider _time;
    private readonly List<(string Id, string Name, DeviceKind Kind, int Percent)> _devices =
    [
        ("fake-mouse", "Mouse (demo)", DeviceKind.Mouse, 50),
        ("fake-headset", "Headset (demo)", DeviceKind.Headset, 80),
        ("fake-keyboard", "Teclado (demo)", DeviceKind.Keyboard, 12),
    ];

    /// <param name="simulateDrain">Si es true, cada consulta baja 1 % la batería (vuelve a 100 al llegar a 5).</param>
    public FakeBatteryProvider(bool simulateDrain = false, TimeProvider? timeProvider = null)
    {
        _simulateDrain = simulateDrain;
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Name => "Demo";

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var result = new List<Device>(_devices.Count);

        for (var i = 0; i < _devices.Count; i++)
        {
            var d = _devices[i];
            result.Add(new Device(d.Id, d.Name, d.Kind, d.Percent, now));

            if (_simulateDrain)
                _devices[i] = d with { Percent = d.Percent <= 5 ? 100 : d.Percent - 1 };
        }

        return Task.FromResult<IReadOnlyList<Device>>(result);
    }
}
