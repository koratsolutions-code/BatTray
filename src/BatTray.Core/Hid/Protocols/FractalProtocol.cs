using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Fractal Design Scape headset: 2.4 GHz dongle (36BC:0001) or the headset on its USB-C cable (36BC:2001).</summary>
/// <remarks>
/// Vendor collection FF00:0001, 64-byte reports, report id 0x02. The read-only GET_DEVICE_STATE command (0x21) is
/// sent as <c>02 [device type] 21</c> and answered with input report 0x02 echoing type and command, then the state.
/// Device type 0x11 is the dongle (state byte 3 = 1 while the headset is linked), 0xF1 the headset (state byte 14 =
/// percent, byte 22 = 1 on the USB cable, used as charging). Through the dongle the headset request is relayed
/// over the air. Offsets count from the device-type byte.
/// Source: Fractal's own WebHID configurator, Adjust Pro (BTDongle / Headset classes, getUpdatedDeviceState);
/// cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class FractalProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x36BC;
    public const ushort ScapeDongle = 0x0001;
    public const ushort ScapeCable = 0x2001;
    public const byte ReportId = 0x02;
    public const byte TypeDongle = 0x11;
    public const byte TypeHeadset = 0xF1;
    public const byte GetDeviceState = 0x21;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    public string Name => "Fractal Design";

    public static byte[] BuildRequest(byte deviceType) => [ReportId, deviceType, GetDeviceState];

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is (ScapeDongle or ScapeCable) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        if (receiver.ProductId == ScapeDongle)
        {
            var dongle = await ExchangeAsync(connection, TypeDongle, cancellationToken);
            if (dongle is null || ParseDongleLinked(dongle) != true)
                return []; // headset off, out of range or on its stand
        }

        var headset = await ExchangeAsync(connection, TypeHeadset, cancellationToken);
        return headset is not null && ParseHeadsetState(headset) is { } reading ? [reading] : [];
    }

    private static async Task<byte[]?> ExchangeAsync(HidConnection connection, byte deviceType, CancellationToken ct)
    {
        connection.FlushInput();
        if (!await connection.WriteAsync(HidTransact.Pad(BuildRequest(deviceType), connection.Collection.OutputLength), ct))
            return null;
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (IsReply(frame, deviceType))
                return frame;
        }
        return null;
    }

    public static bool IsReply(ReadOnlySpan<byte> frame, byte deviceType) =>
        frame.Length >= 24 && frame[0] == ReportId && frame[1] == deviceType && frame[2] == GetDeviceState;

    /// <summary>Whether the dongle reports a linked headset; null when the frame is not its state reply.</summary>
    public static bool? ParseDongleLinked(ReadOnlySpan<byte> frame) =>
        IsReply(frame, TypeDongle) ? frame[4] == 1 : null;

    /// <summary>Decodes the headset state reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseHeadsetState(ReadOnlySpan<byte> frame)
    {
        if (!IsReply(frame, TypeHeadset) || frame[15] > 100)
            return null;
        return new HidBatteryReading(frame[15], frame[23] == 1, "Fractal Scape", DeviceKind.Headset);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFF00, 0x0001) && c.OutputLength >= 4 && c.InputLength >= 24);
}
