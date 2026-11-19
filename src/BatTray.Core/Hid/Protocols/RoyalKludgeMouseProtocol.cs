using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Royal Kludge (RK) MG6 Pro mouse: 2.4 GHz dongle 2E3C:00A6 / 00E6 (vendor page 0xFF00) and USB cable
/// 2E3C:00A5 / 00E5 (vendor page 0xFFF2).
/// </summary>
/// <remarks>
/// Usage 0x0001 on the vendor page, 65-byte input and output reports (report id 0 + 64 bytes). RK's web driver
/// (drive.rkgaming.com, "QiWang" protocol) reads the battery with an unencrypted frame:
/// <c>01 01 02 01</c> = remote parameter, system-control group (data length 0), "get battery voltage", access READ.
/// The reply repeats the first three bytes (masked: byte 0 low 5 bits, byte 1 low nibble); bit 4 of byte 3 is set
/// when the mouse itself answered; bytes 4–5 are millivolts, 6–7 the percent (little endian), byte 10 is non-zero
/// with USB power. Cross-checked with BatteryHub's notes; not marked as tested on hardware.
/// </remarks>
public sealed class RoyalKludgeMouseProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x2E3C;
    private const int ReportLength = 65;
    private static readonly HashSet<ushort> Dongles = [0x00A6, 0x00E6];
    private static readonly HashSet<ushort> Cables = [0x00A5, 0x00E5];

    public string Name => "Royal Kludge";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        var cable = Cables.Contains(receiver.ProductId);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(100, cancellationToken);
            var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(), IsBatteryReply,
                TimeSpan.FromMilliseconds(600), cancellationToken);
            if (frame is null)
                continue;
            if (ParseReply(frame, cable) is { } reading)
                return [reading];
            // The receiver answered for a mouse that did not (asleep or off): retry, then keep the last level.
        }
        return [];
    }

    /// <summary>The battery request as written on Windows (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[1] = 0x01; // remote parameter (forwarded to the mouse)
        request[2] = 0x01; // system control, no data
        request[3] = 0x02; // get battery voltage
        request[4] = 0x01; // access: read
        return request;
    }

    /// <summary>True when the frame (byte 0 = report id 0) is a reply to the battery request.</summary>
    public static bool IsBatteryReply(byte[] frame) =>
        frame.Length >= 12 && frame[0] == 0 && (frame[1] & 0x1F) == 0x01 && (frame[2] & 0x0F) == 0x01 && frame[3] == 0x02;

    /// <summary>Decodes a battery reply; null when the mouse did not answer or the frame is invalid.</summary>
    public static HidBatteryReading? ParseReply(byte[] frame, bool onCable)
    {
        if (!IsBatteryReply(frame) || (frame[4] & 0x10) == 0)
            return null;
        var percent = frame[7] | frame[8] << 8;
        if (percent > 100)
            return null;
        var plugged = frame[11] != 0;
        return new HidBatteryReading(percent, plugged || onCable, "RK MG6 Pro", DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver)
    {
        ushort? page = Dongles.Contains(receiver.ProductId) ? 0xFF00 : Cables.Contains(receiver.ProductId) ? 0xFFF2 : null;
        if (page is null)
            return null;
        return receiver.Collections.FirstOrDefault(c =>
            c.Is(page.Value, 0x0001) && c.InputLength == ReportLength && c.OutputLength == ReportLength);
    }
}
