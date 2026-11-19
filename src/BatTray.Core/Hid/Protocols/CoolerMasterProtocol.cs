using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Cooler Master MM712 / MM731 on the "Mice Dongle" (2516:01B7) or their cables (2516:0169 / 0167).</summary>
/// <remarks>
/// USB interface 3, vendor usage page FF0A, reports without report id (32 bytes on the dongle, 64 on the cable).
/// Read-only status request <c>46 81 00</c>; status reply <c>47 81 [model] …</c> with payload byte 10 = percent,
/// 11 = charging (0 = no), 12 = link (0 cable, 1 dongle). Model byte 0x50 = MM712, 0x70 = MM731.
/// Sources: github.com/syntactic-spice/coolermaster-usb-protocol (docs/PROTOCOL.md) and
/// github.com/riVFerd/MM731-Controller; cross-checked with BatteryHub's notes (MM712 tested on its dongle).
/// </remarks>
public sealed class CoolerMasterProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x2516;
    private const ushort Dongle = 0x01B7;
    private const ushort Mm712Cable = 0x0169;
    private const ushort Mm731Cable = 0x0167;

    public string Name => "Cooler Master";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Dongle or Mm712Cable or Mm731Cable &&
        FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f, receiver.ProductId) is not null, TimeSpan.FromMilliseconds(500), cancellationToken);
        return frame is not null && ParseReply(frame, receiver.ProductId) is { } reading ? [reading] : [];
    }

    /// <summary>The status request as written on Windows (report id 0 first).</summary>
    public static byte[] BuildRequest() => [0x00, 0x46, 0x81, 0x00];

    /// <summary>Decodes a status reply (byte 0 = report id 0). Null when the frame is not one.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report, ushort productId)
    {
        if (report.Length < 14 || report[0] != 0x00 || report[1] != 0x47 || report[2] != 0x81)
            return null;
        var percent = report[11];
        var name = ModelName(productId, report[3]);
        return new HidBatteryReading(percent is >= 1 and <= 100 ? percent : null, report[12] != 0, name, DeviceKind.Mouse);
    }

    public static string ModelName(ushort productId, byte model) => productId switch
    {
        Mm712Cable => "Cooler Master MM712",
        Mm731Cable => "Cooler Master MM731",
        _ when model == 0x50 => "Cooler Master MM712",
        _ when model == 0x70 => "Cooler Master MM731",
        _ => "Cooler Master wireless mouse",
    };

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        HidTransact.FindInterface(receiver, 3, 0xFF0A, c => c.InputLength >= 14);
}
