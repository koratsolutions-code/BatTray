using BatTray.Core.Models;

namespace BatTray.Core.Hid;

/// <summary>A battery reading obtained from a HID receiver.</summary>
/// <param name="Percent">0–100, or null when the peripheral is asleep/off.</param>
/// <param name="Charging">True/false when the protocol reports it, null when unknown.</param>
/// <param name="Name">Model name when the protocol knows it better than the USB strings.</param>
/// <param name="Kind">Device type when the protocol knows it.</param>
public sealed record HidBatteryReading(int? Percent, bool? Charging = null, string? Name = null, DeviceKind? Kind = null);

/// <summary>
/// A way to read the battery of a family of HID receivers. Implementations must only send
/// read-only queries, and only to devices they positively recognise in <see cref="CanHandle"/>.
/// </summary>
public interface IHidBatteryProtocol
{
    string Name { get; }

    bool CanHandle(HidReceiver receiver);

    /// <summary>Queries the battery. Returns null when the peripheral did not answer. Blocking I/O is allowed.</summary>
    Task<HidBatteryReading?> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken);
}
