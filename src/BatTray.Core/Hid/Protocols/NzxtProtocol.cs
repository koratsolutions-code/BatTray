using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>NZXT Lift Elite Wireless mouse on its dongle (1E71:2101) or USB cable (1E71:2129).</summary>
/// <remarks>
/// Vendor collection FFCA:0001, 64-byte reports without report id. The read-only telemetry query is
/// <c>4E 02 81 00 B0</c> (0x02 = the mouse, 0x81 = read property, 0xB0 = telemetry). The answer starts with
/// <c>4E 02 97</c>: bytes 17–18 = percent (little endian), bytes 8–9 = cell voltage in mV (big endian), byte 22
/// bit 0 = on the charging cable. The dongle first acknowledges with <c>4E E5</c>, which is skipped. While NZXT CAM
/// is open the dongle may not answer. Offsets above count from the first payload byte (Windows adds a leading 0).
/// Source: github.com/HeyOkay/HaloBattery (providers/nzxt.py, decoded from NZXT CAM 4.76.5 captures);
/// cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class NzxtProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1E71;
    public const ushort LiftEliteReceiver = 0x2101;
    public const ushort LiftEliteCable = 0x2129;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1200);

    /// <summary>Telemetry query; byte 0 is the (absent) report id.</summary>
    public static byte[] Request => [0x00, 0x4E, 0x02, 0x81, 0x00, 0xB0];

    public string Name => "NZXT";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is (LiftEliteReceiver or LiftEliteCable) &&
        FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, Request, f => IsTelemetry(f), Timeout, cancellationToken);
        return frame is not null && ParseTelemetry(frame) is { } reading ? [reading] : [];
    }

    private static bool IsTelemetry(ReadOnlySpan<byte> frame) =>
        frame.Length >= 28 && frame[0] == 0x00 && frame[1] == 0x4E && frame[2] == 0x02 && frame[3] == 0x97;

    /// <summary>Decodes the telemetry answer as Windows returns it (byte 0 = 0, no report id).</summary>
    public static HidBatteryReading? ParseTelemetry(ReadOnlySpan<byte> frame)
    {
        if (!IsTelemetry(frame))
            return null;
        var percent = frame[18] | (frame[19] << 8);
        if (percent > 100)
            return null;
        return new HidBatteryReading(percent, (frame[23] & 0x01) != 0, "NZXT Lift Elite", DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFFCA, 0x0001) && c.OutputLength >= 6 && c.InputLength >= 28);
}
