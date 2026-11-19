using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>AJAZZ AJ139 V2 MC and siblings: 2.4 GHz dongle A8A5:2255, USB cable A8A4:2255.</summary>
/// <remarks>
/// Vendor collection FF01:0010, 64-byte reports without report id. Battery request <c>55 30 A5 [chk] 2E 00 …</c>;
/// reply <c>AA 30 …</c> with payload byte 8 = percent and byte 9 = non-zero on USB power. Unrelated <c>AA FA</c> /
/// <c>AA ED</c> reports are skipped. The two sources differ on the request: one sends checksum byte 2E (low byte of
/// the sum of bytes 4–63), the other 0B with <c>01 01</c> in bytes 5–6; the second variant is tried only when the
/// first gets no answer. The product id is shared with other models (K-snake X11, NJ07/NJ08 V2), so the mouse gets
/// a generic name. Sources: github.com/werquess/ajazz-aj139-macos (PROTOCOL.md) and github.com/nico2525nn/AjazzAJ139V2;
/// cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class AjazzAj139Protocol : IHidBatteryProtocol
{
    public const ushort ProductId = 0x2255;
    public const ushort DongleVid = 0xA8A5;
    public const ushort CableVid = 0xA8A4;

    public string Name => "Ajazz AJ139";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.ProductId == ProductId && receiver.VendorId is DongleVid or CableVid && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        var cable = receiver.VendorId == CableVid;
        var frame = await GamingMouseHelpers.WriteReadAsync(connection, BuildRequest(alternate: false),
                        TimeSpan.FromMilliseconds(700), IsBatteryReply, cancellationToken)
                    ?? await GamingMouseHelpers.WriteReadAsync(connection, BuildRequest(alternate: true),
                        TimeSpan.FromMilliseconds(800), IsBatteryReply, cancellationToken);
        return frame is not null && ParseReply(frame, cable) is { } reading ? [reading] : [];
    }

    /// <summary>The request as written on Windows (report id 0 first).</summary>
    public static byte[] BuildRequest(bool alternate) => alternate
        ? [0x00, 0x55, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01]
        : [0x00, 0x55, 0x30, 0xA5, 0x2E, 0x2E];

    public static bool IsBatteryReply(byte[] report) =>
        report.Length >= 11 && report[0] == 0x00 && report[1] == 0xAA && report[2] == 0x30;

    /// <summary>Decodes the <c>AA 30</c> reply (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(byte[] report, bool cable)
    {
        if (!IsBatteryReply(report) || report[9] > 100)
            return null;
        var percent = report[9];
        var charging = report[10] != 0;
        const string name = "AJAZZ wireless mouse";
        // 0 from the dongle: the mouse is off or out of range.
        return !cable && percent == 0
            ? new HidBatteryReading(null, charging, name, DeviceKind.Mouse)
            : new HidBatteryReading(percent, charging, name, DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFF01, 0x0010) && c.OutputLength >= 65 && c.InputLength >= 65);
}
