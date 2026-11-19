using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// ASTRO wireless headsets read through their USB base station: A50 X and A50 Gen 5 (Logitech VID 0x046D),
/// A50 Gen 4, A50 Gen 3 and A20 Wireless (ASTRO VID 0x9886).
/// </summary>
/// <remarks>
/// <para><b>A50 X / Gen 5</b>: vendor collection FF32:0074 with 64-byte reports, report id 0x02. Request
/// <c>02 0C 03 00 06 0C</c>; reply <c>02 0C len 00 06 handle percent … charging</c> (percent at [6], charging at [8]).
/// Source: github.com/Sapd/HeadsetControl (logitech_astro_a50).</para>
/// <para><b>A50 Gen 4 / Gen 3 / A20</b>: vendor collection on FF32 or FF31 (FF00 on some bases), 64-byte reports.
/// Request <c>02 cmd [len payload]</c>, reply <c>02 status len data…</c> (status 0x02 = ok, 0x01 = refused).
/// cmd 0x54 = headset status (bit 0 docked, bit 1 radio link), cmd 0x7C = battery (bits 0–6 percent, bit 7
/// charging). The A50 Gen 3 refuses 0x7C; its battery is the fourth group (data[12..15] = <c>02 04 value 00</c>)
/// of the read-only "current UI" request <c>02 72 01 01</c>. Some Gen 3 bases declare no report id, in which
/// case the same bytes are sent behind a 0x00 byte. Sources: github.com/tdryer/eh-fifty,
/// github.com/nichtlegacy/AstroBar, HeadsetControl (logitech_astro_a50_gen4) and github.com/BenStein1/OpenAstroCC.</para>
/// <para>Only these status reads are sent. While ASTRO Command Center holds the collection open exclusively the
/// base cannot be opened, nothing is read and the last reading is kept by the provider.
/// Cross-checked with BatteryHub's notes.</para>
/// </remarks>
public sealed class AstroProtocol : IHidBatteryProtocol
{
    public const ushort LogitechVendorId = 0x046D;
    public const ushort AstroVendorId = 0x9886;
    public const int ReportLength = 64;

    public const byte CommandHeadsetStatus = 0x54;
    public const byte CommandBattery = 0x7C;
    public const byte CommandCurrentUi = 0x72;

    private const byte LegacyStatusOk = 0x02;
    private const byte LegacyStatusRefused = 0x01;

    private static readonly TimeSpan ModernTimeout = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan LegacyTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Per product: true when the base wants the frame behind a 0x00 byte (no report id).</summary>
    private static readonly ConcurrentDictionary<ushort, bool> KnownFraming = new();

    public enum Generation
    {
        /// <summary>A50 X / A50 Gen 5.</summary>
        Modern,

        /// <summary>A50 Gen 4 / Gen 3 / A20 (status + battery commands).</summary>
        Legacy,
    }

    public sealed record Model(ushort VendorId, ushort ProductId, string Name, Generation Generation);

    public static IReadOnlyList<Model> Models { get; } = BuildModels();

    public string Name => "ASTRO";

    public bool CanHandle(HidReceiver receiver) => Find(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not var (model, collection))
            return [];

        var reading = model.Generation == Generation.Modern
            ? await ReadModernAsync(collection, cancellationToken)
            : await ReadLegacyAsync(model.ProductId, collection, cancellationToken);
        return reading is null ? [] : [reading with { Name = model.Name, Kind = DeviceKind.Headset }];
    }

    // ---- A50 X / Gen 5 ----

    /// <summary>The battery request of the A50 X / Gen 5 (byte 0 = report id 0x02).</summary>
    public static byte[] BuildModernRequest() => [0x02, 0x0C, 0x03, 0x00, 0x06, 0x0C];

    private static async Task<HidBatteryReading?> ReadModernAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        var reply = await HidTransact.WriteReadAsync(collection, BuildModernRequest(),
            f => ParseModernReply(f) is not null, ModernTimeout, ct);
        return reply is null ? null : ParseModernReply(reply);
    }

    /// <summary>Parses an A50 X / Gen 5 battery reply; volume and mix events return null.</summary>
    public static HidBatteryReading? ParseModernReply(ReadOnlySpan<byte> f)
    {
        if (f.Length < 9 || f[0] != 0x02 || f[1] != 0x0C || f[2] < 6 || f[2] + 3 > f.Length ||
            f[3] != 0x00 || f[4] != 0x06 || (f[5] != 0x00 && f[5] != 0x0C) || f[6] > 100)
        {
            return null;
        }
        return new HidBatteryReading(f[6], f[8] != 0);
    }

    // ---- A50 Gen 4 / Gen 3 / A20 ----

    public sealed record LegacyReply(byte Status, byte[] Data)
    {
        public bool Ok => Status == LegacyStatusOk;
    }

    private static async Task<HidBatteryReading?> ReadLegacyAsync(ushort productId, HidCollectionInfo collection, CancellationToken ct)
    {
        // 0x54 is optional: some Gen 3 firmware may not answer it.
        var status = await QueryLegacyAsync(productId, collection, CommandHeadsetStatus, [], ct);
        var haveStatus = status is { Ok: true, Data.Length: >= 1 };
        var docked = haveStatus && (status!.Data[0] & 0x01) != 0;
        if (haveStatus && !docked && (status!.Data[0] & 0x02) == 0)
            return null; // headset off or out of range

        var battery = await QueryLegacyAsync(productId, collection, CommandBattery, [], ct);
        byte? value = battery is { Ok: true, Data.Length: >= 1 } ? battery.Data[0] : null;

        if (value is null && battery is { Ok: false } && haveStatus)
        {
            // A50 Gen 3: battery only in the "current UI" read.
            var ui = await QueryLegacyAsync(productId, collection, CommandCurrentUi, [0x01], ct);
            value = ui is { Ok: true } ? ParseGen3UiBattery(ui.Data) : null;
        }

        return value is { } v ? ParseLegacyBattery(v, docked) : null;
    }

    private static async Task<LegacyReply?> QueryLegacyAsync(
        ushort productId, HidCollectionInfo collection, byte command, byte[] payload, CancellationToken ct)
    {
        bool[] order = KnownFraming.TryGetValue(productId, out var known) ? [known] : [false, true];
        foreach (var withoutReportId in order)
        {
            var reply = await HidTransact.WriteReadAsync(collection, BuildLegacyRequest(command, payload, withoutReportId),
                f => ParseLegacyReply(f) is not null, LegacyTimeout, ct);
            if (reply is not null && ParseLegacyReply(reply) is { } parsed)
            {
                KnownFraming[productId] = withoutReportId;
                return parsed;
            }
        }
        return null;
    }

    /// <summary><c>02 cmd [len payload…]</c>, behind a 0x00 byte for bases without a report id.</summary>
    public static byte[] BuildLegacyRequest(byte command, ReadOnlySpan<byte> payload, bool withoutReportId)
    {
        var frame = new List<byte>(payload.Length + 4);
        if (withoutReportId)
            frame.Add(0x00);
        frame.Add(0x02);
        frame.Add(command);
        if (!payload.IsEmpty)
        {
            frame.Add((byte)payload.Length);
            frame.AddRange(payload.ToArray());
        }
        return [.. frame];
    }

    /// <summary>
    /// Parses <c>02 status len data…</c> (also behind a leading 0x00 byte). Null for other frames or statuses
    /// other than ok / refused.
    /// </summary>
    public static LegacyReply? ParseLegacyReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= 5 && frame[0] == 0x00 && frame[1] == 0x02)
            frame = frame[1..];
        if (frame.Length < 4 || frame[0] != 0x02 || (frame[1] != LegacyStatusOk && frame[1] != LegacyStatusRefused))
            return null;
        var length = Math.Min(frame[2] & 0x3F, frame.Length - 3);
        return new LegacyReply(frame[1], frame.Slice(3, length).ToArray());
    }

    /// <summary>Battery byte: bits 0–6 percent, bit 7 charging. A docked headset below 100 % is charging.</summary>
    public static HidBatteryReading? ParseLegacyBattery(byte value, bool docked)
    {
        var percent = value & 0x7F;
        if (percent > 100)
            return null;
        return new HidBatteryReading(percent, (value & 0x80) != 0 || (docked && percent < 100));
    }

    /// <summary>A50 Gen 3 battery byte from the "current UI" data (group <c>02 04 value 00</c> at [12..15]).</summary>
    public static byte? ParseGen3UiBattery(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[12] != 0x02 || data[13] != 0x04 || (data[14] & 0x7F) > 100)
            return null;
        return data[14];
    }

    // ---- Model table ----

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        var model = Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId == receiver.ProductId);
        if (model is null)
            return null;

        var collection = model.Generation == Generation.Modern
            ? receiver.Collections.FirstOrDefault(c =>
                c.Is(0xFF32, 0x0074) && c.InputLength == ReportLength && c.OutputLength == ReportLength)
            : receiver.Collections
                .Where(c => (c.UsagePage is 0xFF32 or 0xFF31 or 0xFF00) &&
                            c.InputLength >= ReportLength && c.OutputLength >= ReportLength)
                .OrderBy(c => c.UsagePage == 0xFF00 ? 1 : 0)
                .FirstOrDefault();
        return collection is null ? null : (model, collection);
    }

    private static List<Model> BuildModels()
    {
        var models = new List<Model>
        {
            new(LogitechVendorId, 0x0B0B, "ASTRO A50 X", Generation.Modern),
            new(LogitechVendorId, 0x0B1C, "ASTRO A50 Gen 5", Generation.Modern),
        };
        void Add(string name, params ushort[] pids) =>
            models.AddRange(pids.Select(pid => new Model(AstroVendorId, pid, name, Generation.Legacy)));

        Add("ASTRO A50 Gen 4", 0x002C, 0x002D, 0x002A);
        Add("ASTRO A50 Gen 3", 0x0015, 0x0014, 0x0018, 0x001D, 0x0026);
        Add("ASTRO A20 Wireless", 0x0037, 0x0038, 0x0039, 0x0043, 0x0044, 0x0045);
        return models;
    }
}
