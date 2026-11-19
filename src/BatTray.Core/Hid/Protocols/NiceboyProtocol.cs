using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Niceboy MW400 on its 2.4 GHz receiver (248A:FA02).</summary>
/// <remarks>
/// Interface 0, vendor collection with report 05 (usage 0001:0000, 33-byte input and output reports). Read-only
/// battery request <c>05 24 FA 48</c> (what the MW400 configuration app sends about every 27 s); reply
/// <c>05 xx FA 48 [lo] [hi] [charging]</c>: raw = lo | hi &lt;&lt; 8, millivolts = raw × 990 / 240, mapped to 10 % steps
/// with the app's own table. Raw and charging all 0 = mouse not linked. Example: <c>05 49 FA 48 EA 03 00</c> =
/// 4133 mV = 90 %. Source: the Niceboy MW400 app, as decoded in BatteryHub's notes (reported tested on an MW400).
/// VID 248A is shared by many Telink receivers, so only this exact product id is matched.
/// </remarks>
public sealed class NiceboyProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x248A;
    public const ushort Mw400Receiver = 0xFA02;
    public const byte ReportId = 0x05;
    private const int ReportLength = 33;

    private static readonly int[] TableMv = [4200, 4100, 4000, 3900, 3800, 3700, 3500, 3400, 3200];
    private static readonly int[] TablePercent = [100, 90, 80, 70, 60, 50, 40, 30, 20];

    public string Name => "Niceboy";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == Mw400Receiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(500), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    public static byte[] BuildRequest() => [ReportId, 0x24, 0xFA, 0x48];

    /// <summary>Decodes a battery reply; null when the frame is other report-05 traffic.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != ReportId || report[2] != 0xFA || report[3] != 0x48)
            return null;
        if ((report[4] | report[5] | report[6]) == 0)
            return new HidBatteryReading(null, Name: "Niceboy MW400", Kind: DeviceKind.Mouse); // mouse asleep or off
        var mv = (report[4] | (report[5] << 8)) * 990 / 240;
        return new HidBatteryReading(PercentFromMillivolts(mv), report[6] != 0, "Niceboy MW400", DeviceKind.Mouse);
    }

    public static int PercentFromMillivolts(int mv)
    {
        for (var i = 0; i < TableMv.Length; i++)
        {
            if (mv >= TableMv[i])
                return TablePercent[i];
        }
        return 10;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 0 && c.Is(0x0001, 0x0000) && c.InputLength == ReportLength && c.OutputLength == ReportLength);
}
