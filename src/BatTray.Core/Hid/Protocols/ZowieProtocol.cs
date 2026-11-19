using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>BenQ ZOWIE wireless mice (VID 04A5): EC-CW series, DW series and U2 receivers.</summary>
/// <remarks>
/// <para><b>EC-CW</b> (receiver 8006, cable 8005): unnumbered 64-byte output report <c>A5 03</c> on the vendor
/// collection (usage page FFxx, or 00FF on the Enhanced Receiver). The input reply carries the battery voltage
/// (mV, little endian) in bytes 0–1 and a percent in byte 2 of the payload; 0 mV means no mouse is linked.
/// Source: github.com/yleshchuk/zowie-battery.</para>
/// <para><b>DW / U2</b> (receivers 800A, 8008): output report 08 <c>B4 15</c> on the 16-byte FF03 collection; the
/// reply is input report 09 on the FF04 collection: voltage = (byte 1 × 256 + byte 2) × 10 mV, byte 4 = 0xC8.
/// Source: github.com/IgnacioBarb/zowie-battery-tray.</para>
/// <para>Only those two read commands are ever sent: other EC-CW commands can reset the receiver and unpair the
/// mouse. The percentage follows the mouse's own LED thresholds. Cross-checked with BatteryHub's notes.</para>
/// </remarks>
public sealed class ZowieProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x04A5;
    private const ushort CwCable = 0x8005;
    private const ushort CwReceiver = 0x8006;
    private const ushort U2Receiver = 0x8008;
    private const ushort DwReceiver = 0x800A;
    private const int DwReportLength = 16;

    private static readonly int[] CurveMv = [4200, 4000, 3890, 3760, 3680, 3500];
    private static readonly int[] CurvePercent = [100, 75, 50, 25, 10, 0];

    public string Name => "ZOWIE";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId switch
        {
            CwCable or CwReceiver => FindCwCollection(receiver) is not null,
            DwReceiver or U2Receiver => FindDwCollections(receiver) is not null,
            _ => false,
        };

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.ProductId is CwCable or CwReceiver)
        {
            if (FindCwCollection(receiver) is not { } collection)
                return [];
            var frame = await HidTransact.WriteReadAsync(collection, BuildCwRequest(),
                f => ParseCwReply(f, false) is not null, TimeSpan.FromSeconds(1), cancellationToken);
            return frame is not null && ParseCwReply(frame, receiver.ProductId == CwCable) is { } r
                ? [r with { Name = "ZOWIE EC-CW" }]
                : [];
        }

        if (FindDwCollections(receiver) is not { } pair)
            return [];
        var reply = await QueryDwAsync(pair.Command, pair.Reply, cancellationToken);
        var name = receiver.ProductId == U2Receiver ? "ZOWIE U2" : "ZOWIE DW mouse";
        return reply is not null && ParseDwReply(reply) is { } reading ? [reading with { Name = name }] : [];
    }

    /// <summary>EC-CW request as written on Windows: report id 0, then <c>A5 03</c>.</summary>
    public static byte[] BuildCwRequest() => [0x00, 0xA5, 0x03];

    /// <summary>DW / U2 request: output report 08, <c>B4 15</c>.</summary>
    public static byte[] BuildDwRequest() => [0x08, 0xB4, 0x15];

    /// <summary>Decodes an EC-CW reply (byte 0 = report id 0). Null when it is not a battery reply.</summary>
    public static HidBatteryReading? ParseCwReply(ReadOnlySpan<byte> report, bool cable)
    {
        if (report.Length < 4 || report[0] != 0x00)
            return null;
        var mv = report[1] | (report[2] << 8);
        if (mv == 0)
            return new HidBatteryReading(null, Kind: DeviceKind.Mouse); // receiver without a linked mouse
        if (mv is < 3000 or > 4500)
            return null;
        var percent = report[3] is >= 1 and <= 100 ? report[3] : PercentFromMillivolts(mv);
        return new HidBatteryReading(percent, cable ? true : null, Kind: DeviceKind.Mouse);
    }

    /// <summary>Decodes a DW / U2 reply (input report 09). Null when it is not a battery reply.</summary>
    public static HidBatteryReading? ParseDwReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 5 || report[0] != 0x09 || report[1] == 0xE6 || report[4] != 0xC8)
            return null;
        var mv = (report[1] * 256 + report[2]) * 10;
        if (mv == 0)
            return new HidBatteryReading(null, Kind: DeviceKind.Mouse);
        if (mv is < 3000 or > 4500)
            return null;
        return new HidBatteryReading(PercentFromMillivolts(mv), Kind: DeviceKind.Mouse);
    }

    public static int PercentFromMillivolts(int mv) => GamingMouseHelpers.Interpolate(mv, CurveMv, CurvePercent);

    private static async Task<byte[]?> QueryDwAsync(HidCollectionInfo command, HidCollectionInfo replyCollection, CancellationToken ct)
    {
        // Open the reply channel first so the answer is not missed.
        using var input = HidConnection.OpenForReports(replyCollection);
        using var output = HidConnection.OpenForReports(command);
        if (input is null || output is null)
            return null;
        input.FlushInput();
        if (!await output.WriteAsync(HidTransact.Pad(BuildDwRequest(), command.OutputLength), ct))
            return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
        while (deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            var frame = await input.ReadAsync(left, ct);
            if (frame is null)
                return null;
            if (frame.Length >= 5 && frame[0] == 0x09)
                return frame;
        }
        return null;
    }

    private static HidCollectionInfo? FindCwCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            (c.UsagePage >= 0xFF00 || c.UsagePage == 0x00FF) && c.OutputLength == 65 && c.InputLength >= 4);

    private static (HidCollectionInfo Command, HidCollectionInfo Reply)? FindDwCollections(HidReceiver receiver)
    {
        var command = receiver.Collections.FirstOrDefault(c => c.UsagePage == 0xFF03 && c.OutputLength == DwReportLength);
        var reply = receiver.Collections.FirstOrDefault(c => c.UsagePage == 0xFF04 && c.InputLength == DwReportLength);
        return command is not null && reply is not null ? (command, reply) : null;
    }
}
