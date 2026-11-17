using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>Source of devices and battery levels (Bluetooth LE, classic Bluetooth, HID++…).</summary>
public interface IBatteryProvider : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Raised (from any thread) when the provider knows its data changed — a device connected or
    /// disconnected, or a battery notification arrived — so the monitor can refresh without waiting
    /// for the next poll.
    /// </summary>
    event EventHandler? Changed;

    Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default);
}
