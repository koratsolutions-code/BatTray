using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>Fuente de dispositivos y niveles de batería (Bluetooth LE, Bluetooth clásico, HID++…).</summary>
public interface IBatteryProvider
{
    string Name { get; }

    Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default);
}
