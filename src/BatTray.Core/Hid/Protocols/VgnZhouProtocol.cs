using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>VGN OMNISOUND (ZHOUTING) headset on its 2.4 GHz dongle (372E:10BB), VGN's "ZHOU" protocol.</summary>
/// <remarks>
/// Collection 0x000C/0x0001 (consumer control) with report id 0x55: 64-byte output and 32-byte input reports.
/// The battery read (read-only) is output report <c>55 65 01</c>, zero-padded; the dongle answers with an input report
/// <c>55 65 [percent]</c>. No charging flag is known (the vendor app always shows "not charging").
/// Source: VGN V HUB 4.0.8 (getBattery for this product id), verified there on hardware; cross-checked with
/// BatteryHub's notes. The wired variant and the Siren V2 are not included because they are untested.
/// </remarks>
public sealed class VgnZhouProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x372E;
    public const ushort OmnisoundDongle = 0x10BB;
    public const byte ReportId = 0x55;
    public const byte ReadBattery = 0x65;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(800);

    public string Name => "VGN (ZHOU)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == OmnisoundDongle && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (!CanHandle(receiver) || FindCollection(receiver) is not { } collection)
            return [];

        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(collection.OutputLength),
            f => ParseReply(f) is not null, Timeout, cancellationToken);
        if (frame is null || ParseReply(frame) is not { } percent)
            return [];

        var name = string.IsNullOrWhiteSpace(receiver.Product) ? "VGN OMNISOUND (ZHOUTING)" : receiver.Product;
        return [new HidBatteryReading(percent, null, name, DeviceKind.Headset)];
    }

    // ---- Frames (public for tests) ----

    public static byte[] BuildRequest(int outputLength = 64)
    {
        var report = new byte[Math.Max(3, outputLength)];
        report[0] = ReportId;
        report[1] = ReadBattery;
        report[2] = 0x01;
        return report;
    }

    /// <summary>Percent from <c>55 65 [percent]</c> (or an unnumbered <c>00 65 [percent]</c>), else null.</summary>
    public static int? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3 || (frame[0] != ReportId && frame[0] != 0x00) || frame[1] != ReadBattery)
            return null;
        return frame[2] <= 100 ? frame[2] : null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Find(0x000C, 0x0001).FirstOrDefault(c => c.OutputLength >= 63 && c.InputLength >= 3);
}
