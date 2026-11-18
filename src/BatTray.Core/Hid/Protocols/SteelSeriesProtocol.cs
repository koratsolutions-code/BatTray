using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// SteelSeries wireless mice and headsets (VID 0x1038). Each request is a read-only status query written
/// as an output report on a specific USB interface; the reply is the next matching input report.
/// </summary>
/// <remarks>Offsets below count the report-id byte (0x00 for unnumbered reports) as byte 0.</remarks>
public sealed class SteelSeriesProtocol : IHidBatteryProtocol
{
    public const ushort SteelSeriesVendorId = 0x1038;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    public enum Parser
    {
        /// <summary>Arctis 1 / 7X / 7P: [2]==1 offline, [3] = %.</summary>
        Arctis12,

        /// <summary>Arctis 7 / Pro 2019: [2] = % (0 = off).</summary>
        Arctis18,

        /// <summary>Arctis 7+: echo 0xB0 at [1], [2]==1 offline, [3] = level 0–4, [4]==1 charging.</summary>
        ArctisPlus,

        /// <summary>Nova 7 (discrete): echo 0xB0, [3] = level 0–4, [4]: 0 offline, 1/2 charging.</summary>
        Nova7Discrete,

        /// <summary>Nova 7 (percent): echo 0xB0, [3] = %, [4]: 0 offline, 1/2 charging.</summary>
        Nova7Percent,

        /// <summary>Nova 5 family: echo 0xB0, [2]==2 offline, [4] = %, [5]==1 charging.</summary>
        Nova5,

        /// <summary>Aerox / Prime mice: [2] = v; 0 = off; % = ((v &amp; 0x7F) − 1) × 5; bit 7 = charging.</summary>
        AeroxMouse,

        /// <summary>Rival 3 Wireless / Rival 650: [1] = %, [3] ≠ 0 charging.</summary>
        RivalMouse,
    }

    public sealed record Model(ushort ProductId, string Name, DeviceKind Kind, int Interface, ushort? UsagePage, byte[] Request, Parser Parser);

    public static IReadOnlyList<Model> Models { get; } = BuildModels();

    public string Name => "SteelSeries";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == SteelSeriesVendorId && Find(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not { } match)
            return [];

        var (model, collection) = match;
        var reply = await HidTransact.WriteReadAsync(collection, model.Request, f => Accepts(model.Parser, f), Timeout, cancellationToken);
        return reply is not null && Parse(model.Parser, reply) is { } reading
            ? [reading with { Name = model.Name, Kind = model.Kind }]
            : [];
    }

    private static (Model, HidCollectionInfo)? Find(HidReceiver receiver)
    {
        var model = Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);
        if (model is null)
            return null;
        var collection = HidTransact.FindInterface(receiver, model.Interface, model.UsagePage);
        return collection is null ? null : (model, collection);
    }

    private static bool Accepts(Parser parser, byte[] f) => parser switch
    {
        Parser.ArctisPlus or Parser.Nova7Discrete or Parser.Nova7Percent or Parser.Nova5 => f.Length > 5 && f[1] == 0xB0,
        _ => f.Length > 4,
    };

    /// <summary>Parses a reply (public for tests).</summary>
    public static HidBatteryReading? Parse(Parser parser, ReadOnlySpan<byte> f)
    {
        switch (parser)
        {
            case Parser.Arctis12:
                return f[2] == 0x01 ? null : Percent(f[3]);
            case Parser.Arctis18:
                return f[2] == 0 ? null : Percent(f[2]);
            case Parser.ArctisPlus:
                return f[2] == 0x01 || f[3] > 4 ? null : new HidBatteryReading(f[3] * 25, f[4] == 1);
            case Parser.Nova7Discrete:
                return f[4] == 0 || f[3] > 4 ? null : new HidBatteryReading(f[3] * 25, f[4] is 1 or 2);
            case Parser.Nova7Percent:
                return f[4] == 0 || f[3] > 100 ? null : new HidBatteryReading(f[3], f[4] is 1 or 2);
            case Parser.Nova5:
                return f[2] == 0x02 || f[4] > 100 ? null : new HidBatteryReading(f[4], f[5] == 1);
            case Parser.AeroxMouse:
            {
                var v = f[2];
                if (v == 0)
                    return null;
                var percent = ((v & 0x7F) - 1) * 5;
                return percent is < 0 or > 100 ? null : new HidBatteryReading(percent, (v & 0x80) != 0);
            }
            case Parser.RivalMouse:
                return f[1] > 100 ? null : new HidBatteryReading(f[1], f[3] != 0);
            default:
                return null;
        }
    }

    private static HidBatteryReading? Percent(byte value) => value <= 100 ? new HidBatteryReading(value) : null;

    private static List<Model> BuildModels()
    {
        var models = new List<Model>();
        void Add(string name, DeviceKind kind, int iface, ushort? page, byte[] request, Parser parser, params ushort[] pids)
        {
            foreach (var pid in pids)
                models.Add(new Model(pid, name, kind, iface, page, request, parser));
        }

        // Headsets
        Add("SteelSeries Arctis 1 Wireless", DeviceKind.Headset, 3, 0xFF43, [0x06, 0x12], Parser.Arctis12, 0x12B3, 0x12B6);
        Add("SteelSeries Arctis 7X", DeviceKind.Headset, 3, 0xFF43, [0x06, 0x12], Parser.Arctis12, 0x12D7);
        Add("SteelSeries Arctis 7P", DeviceKind.Headset, 3, 0xFF43, [0x06, 0x12], Parser.Arctis12, 0x12D5);
        Add("SteelSeries Arctis 7", DeviceKind.Headset, 5, 0xFF43, [0x06, 0x18], Parser.Arctis18, 0x1260, 0x12AD);
        Add("SteelSeries Arctis Pro", DeviceKind.Headset, 5, 0xFF43, [0x06, 0x18], Parser.Arctis18, 0x1252, 0x1280);
        Add("SteelSeries Arctis 7+", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.ArctisPlus, 0x220E, 0x2212, 0x2216, 0x2236);
        Add("SteelSeries Arctis Nova 7", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.Nova7Discrete,
            0x2202, 0x2206, 0x220A, 0x223A, 0x227A, 0x22A4);
        Add("SteelSeries Arctis Nova 7", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.Nova7Percent,
            0x22A1, 0x227E, 0x2258, 0x229E, 0x22AD, 0x22A9, 0x22A5, 0x22A7, 0x2298);
        Add("SteelSeries Arctis Nova 5", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.Nova5, 0x2232, 0x2253);
        Add("SteelSeries Arctis Nova 3P Wireless", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.Nova5, 0x2269);

        // Mice: the 2.4 GHz receiver uses the wired command with bit 0x40 set (0x92 → 0xD2).
        Add("SteelSeries Aerox 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x183A, 0x187A);
        Add("SteelSeries Aerox 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1838, 0x1878);
        Add("SteelSeries Aerox 5 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x1854, 0x185E, 0x1862);
        Add("SteelSeries Aerox 5 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1852, 0x185C, 0x1860);
        Add("SteelSeries Aerox 9 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x185A, 0x1876);
        Add("SteelSeries Aerox 9 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1858, 0x1874);
        Add("SteelSeries Prime Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x1842, 0x184A);
        Add("SteelSeries Prime Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1840, 0x1848);
        Add("SteelSeries Rival 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xAA, 0x01], Parser.RivalMouse, 0x1830, 0x1872);
        Add("SteelSeries Rival 650", DeviceKind.Mouse, 0, null, [0x00, 0xAA, 0x01], Parser.RivalMouse, 0x172B, 0x1726);
        return models;
    }
}
