using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Headsets whose USB dongle is an Airoha part answering Airoha's RACE protocol: AKG N9 Hybrid (0ECB:20D9) and
/// ENDORFY Viro Wireless (3738:0072).
/// </summary>
/// <remarks>
/// Vendor collection FF13:0001, 62-byte reports.
/// <list type="bullet">
///   <item>Request, output report 0x06: <c>06 07 80 | 05 5A 03 00 D6 0C 00</c> — 7 valid bytes, recipient 0x80
///         (the headset behind the dongle), then the RACE command frame: type 0x5A, length 3, id 0x0CD6
///         ("get battery"), role 0.</item>
///   <item>Answer, input report 0x07 polled with GET_REPORT: <c>07 [valid length] 80</c> followed by RACE frames
///         <c>05 [type] [len:2] [id:2] [payload]</c>. The battery is the indication (type 0x5D) of id 0x0CD6 with
///         payload <c>[status 0][role][percent]</c>. Bytes past the valid length are left over from older answers
///         and are ignored. No charging flag is known.</item>
/// </list>
/// Only this single read-only query is sent. Sources: HeadsetControl (lib/devices/audeze_maxwell.hpp) and the
/// Headroom project (patalbansishashank/Headroom, bin/headroom_race.py), which send the same packet to other
/// Airoha dongles; cross-checked with BatteryHub's notes (AKG N9 Hybrid tested there, ENDORFY Viro untested).
/// </remarks>
public sealed class AirohaRaceDongleProtocol : IHidBatteryProtocol
{
    public const byte RequestReportId = 0x06;
    public const byte AnswerReportId = 0x07;
    public const int ReportLength = 62;
    public const ushort BatteryCommand = 0x0CD6;
    private const byte RaceHead = 0x05;
    private const byte RaceIndication = 0x5D;

    private const int Polls = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(60);

    /// <param name="Interface">Required USB interface of the vendor collection, or null when not checked.</param>
    public sealed record Model(ushort VendorId, ushort ProductId, string Name, int? Interface);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0ECB, 0x20D9, "AKG N9 Hybrid", 3),
        new(0x3738, 0x0072, "ENDORFY Viro Wireless", null),
    ];

    public static byte[] Request => [RequestReportId, 0x07, 0x80, RaceHead, 0x5A, 0x03, 0x00, 0xD6, 0x0C, 0x00];

    public string Name => "Airoha RACE";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        Models.FirstOrDefault(m => m.VendorId == vendorId && m.ProductId == productId);

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is { } model && FindCollection(receiver, model) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model || FindCollection(receiver, model) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        // The answer report keeps its last content: an unchanged buffer is not taken as the new answer,
        // except on the last poll so a steady level is still read.
        var before = NewAnswerBuffer(collection);
        if (!connection.GetInputReport(before))
            before = null;

        if (!await connection.WriteAsync(HidTransact.Pad(Request, collection.OutputLength), cancellationToken))
            return [];

        for (var i = 0; i < Polls; i++)
        {
            await Task.Delay(PollInterval, cancellationToken);
            var answer = NewAnswerBuffer(collection);
            if (!connection.GetInputReport(answer))
                continue;
            if (before is not null && i + 1 < Polls && answer.AsSpan().SequenceEqual(before))
                continue;
            if (ParseAnswer(answer) is { } percent)
                return [new HidBatteryReading(percent, Name: model.Name, Kind: DeviceKind.Headset)];
        }
        return [];
    }

    private static byte[] NewAnswerBuffer(HidCollectionInfo collection)
    {
        var buffer = new byte[collection.InputLength];
        buffer[0] = AnswerReportId;
        return buffer;
    }

    /// <summary>Finds the newest battery indication in an input report 0x07; null when there is none.</summary>
    public static int? ParseAnswer(ReadOnlySpan<byte> report)
    {
        if (report.Length < 4 || report[0] != AnswerReportId)
            return null;

        var end = Math.Min(report.Length, 3 + report[1]);
        int? percent = null;
        var i = 3;
        while (i + 6 <= end)
        {
            if (report[i] != RaceHead)
            {
                i++;
                continue;
            }
            var length = report[i + 2] | (report[i + 3] << 8);
            if (length < 2 || i + 4 + length > end)
                break;
            var id = report[i + 4] | (report[i + 5] << 8);
            if (report[i + 1] == RaceIndication && id == BatteryCommand && length >= 5 && report[i + 6] == 0 && report[i + 8] <= 100)
                percent = report[i + 8];
            i += 4 + length;
        }
        return percent;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Model model) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF13, 0x0001) && c.InputLength == ReportLength && c.OutputLength == ReportLength &&
            (model.Interface is null || c.InterfaceNumber == model.Interface));
}
