using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Audeze Maxwell headsets (VID 0x3329) on their USB dongle or USB-C cable.
/// </summary>
/// <remarks>
/// Vendor collection FF13:0001. One read-only Airoha RACE "get attribute 0x0CD6" request is written as the
/// 62-byte output report <c>06 07 80 05 5A 03 00 D6 0C</c>; the answer is fetched by polling input report 0x07,
/// which holds a rolling buffer of recent replies, so it is searched for <c>D6 0C 00 00 percent</c>.
/// Sources: github.com/HeyOkay/HaloBattery (audeze; dongle 4B19 and cable 4B1A tested there) and
/// github.com/Sapd/HeadsetControl (audeze_maxwell). HeadsetControl's longer initialisation sequence is not sent.
/// With no headset linked the dongle's product string ends in "Dongle" and it keeps returning a stale value,
/// so it is not queried then. There is no charging flag: the headset charges whenever it answers over its cable.
/// A freshly powered-on headset reports 0 % for a short while, which is ignored.
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class AudezeMaxwellProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3329;
    public const byte RequestReportId = 0x06;
    public const byte ReplyReportId = 0x07;

    private static readonly TimeSpan PowerOnGrace = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(3);
    private static readonly ConcurrentDictionary<string, (DateTime First, DateTime Last)> Seen = new();

    public sealed record Model(ushort ProductId, bool Cable);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x4B1A, true), new(0x4B1E, true),
        new(0x4B19, false), new(0x4B18, false), new(0x4B29, false), new(0x4B28, false),
    ];

    public string Name => "Audeze";

    public bool CanHandle(HidReceiver receiver) => Find(receiver) is not null;

    /// <summary>The battery request (byte 0 = report id 0x06).</summary>
    public static byte[] BuildRequest() => [RequestReportId, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];

    /// <summary>The battery percentage found in an input report 0x07, or null.</summary>
    public static int? FindLevel(ReadOnlySpan<byte> report)
    {
        if (report.IsEmpty || report[0] != ReplyReportId)
            return null;
        for (var i = 1; i + 4 < report.Length; i++)
        {
            if (report[i] == 0xD6 && report[i + 1] == 0x0C && report[i + 2] == 0x00 && report[i + 3] == 0x00 &&
                report[i + 4] <= 100)
            {
                return report[i + 4];
            }
        }
        return null;
    }

    /// <summary>A dongle whose product string ends in "Dongle" has no headset linked.</summary>
    public static bool IsUnlinkedDongle(Model model, string? product) =>
        !model.Cable && product?.TrimEnd().EndsWith("dongle", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not var (model, collection))
            return [];
        if (IsUnlinkedDongle(model, receiver.Product))
        {
            Seen.TryRemove(receiver.Id, out _);
            return [];
        }

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null ||
            !await connection.WriteAsync(HidTransact.Pad(BuildRequest(), collection.OutputLength), cancellationToken))
        {
            return [];
        }

        int? level = null;
        for (var i = 0; i < 3; i++) // the answer may land a little later; the newest one counts
        {
            await Task.Delay(60, cancellationToken);
            var report = new byte[collection.InputLength];
            report[0] = ReplyReportId;
            if (connection.GetInputReport(report) && FindLevel(report) is { } found)
                level = found;
        }
        if (level is not { } percent)
            return [];

        var now = DateTime.UtcNow;
        var seen = Seen.AddOrUpdate(receiver.Id, _ => (now, now),
            (_, old) => now - old.Last > ForgetAfter ? (now, now) : (old.First, now));
        if (percent == 0 && now - seen.First < PowerOnGrace)
            return []; // the power-on 0 %

        return [new HidBatteryReading(percent, model.Cable, "Audeze Maxwell", DeviceKind.Headset)];
    }

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        if (receiver.VendorId != VendorId)
            return null;
        var model = Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);
        var collection = receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF13, 0x0001) && c.InputLength >= 8 && c.OutputLength >= BuildRequest().Length);
        return model is null || collection is null ? null : (model, collection);
    }
}
