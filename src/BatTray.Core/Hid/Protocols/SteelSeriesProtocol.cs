using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// SteelSeries wireless mice and headsets (VID 0x1038). Each request is a read-only status query written
/// as an output report on a specific USB interface; the reply is the next matching input report.
/// </summary>
/// <remarks>
/// Offsets below count the report-id byte (0x00 for unnumbered reports) as byte 0.
/// Headset queries follow github.com/Sapd/HeadsetControl; mouse queries follow github.com/flozz/rivalcfg (Aerox 3
/// Wireless Gen 2: rivalcfg issue #276; Rival Pro: rivalcfg pull request #294). Cross-checked with BatteryHub's notes.
/// </remarks>
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

        /// <summary>Arctis GameBuds: echo 0xB0; bud n out of the case when [4+n]==3, its level at [6+n]; lowest wins.</summary>
        GameBuds,

        /// <summary>Arctis 9: [4] raw 0x64–0x9A mapped to 0–100, [5]==1 charging.</summary>
        Arctis9,

        /// <summary>Arctis Nova Pro Wireless: [15] status (1 offline, 2 charging), [6] level 0–8.</summary>
        NovaProWireless,

        /// <summary>Aerox 3 Wireless Gen 2 on its receiver: echo 0xD2, [2] 0/1/2 (1–2 = charging), [3] % (FF unknown); 00 00 = off.</summary>
        AeroxGen2Receiver,

        /// <summary>Aerox 3 Wireless Gen 2 on its cable: echo 0x92, same layout.</summary>
        AeroxGen2Wired,

        /// <summary>Rival Pro (UWB base station): echo 0x92, [2]==0, [3] = %.</summary>
        RivalPro,

        /// <summary>Arctis Pro Wireless: <c>41 AA</c> → [1]==2 offline; then <c>40 AA</c> → [1] level 0–4.</summary>
        ArctisProWireless,
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
        if (model.Parser == Parser.ArctisProWireless)
            return await ReadArctisProWirelessAsync(model, collection, cancellationToken);

        var reply = await HidTransact.WriteReadAsync(collection, model.Request, f => Accepts(model, f), Timeout, cancellationToken);
        return reply is not null && Parse(model.Parser, reply) is { } reading
            ? [reading with { Name = model.Name, Kind = model.Kind }]
            : [];
    }

    /// <summary>The base station answers two read-only queries: link state, then a coarse level.</summary>
    private static async Task<IReadOnlyList<HidBatteryReading>> ReadArctisProWirelessAsync(Model model, HidCollectionInfo collection, CancellationToken ct)
    {
        var link = await HidTransact.WriteReadAsync(collection, [0x00, 0x41, 0xAA], f => f.Length >= 2, Timeout, ct);
        if (link is null || link[1] == 0x02)
            return []; // headset off
        var level = await HidTransact.WriteReadAsync(collection, [0x00, 0x40, 0xAA], f => f.Length >= 2, Timeout, ct);
        return level is not null && ParseArctisProWirelessLevel(level) is { } reading
            ? [reading with { Name = model.Name, Kind = model.Kind }]
            : [];
    }

    /// <summary>Arctis Pro Wireless level reply: [1] = 0–4.</summary>
    public static HidBatteryReading? ParseArctisProWirelessLevel(ReadOnlySpan<byte> f) =>
        f.Length >= 2 && f[1] <= 4 ? new HidBatteryReading(f[1] * 25) : null;

    private static (Model, HidCollectionInfo)? Find(HidReceiver receiver)
    {
        var model = Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);
        if (model is null)
            return null;
        var collection = HidTransact.FindInterface(receiver, model.Interface, model.UsagePage);
        return collection is null ? null : (model, collection);
    }

    private static bool Accepts(Model model, byte[] f) => model.Parser switch
    {
        Parser.ArctisPlus or Parser.Nova7Discrete or Parser.Nova7Percent or Parser.Nova5 => f.Length > 5 && f[1] == 0xB0,
        Parser.GameBuds => f.Length > 7 && f[1] == 0xB0,
        Parser.NovaProWireless => f.Length > 15,
        Parser.Arctis9 => f.Length > 5,
        // Mice echo the command; 40 FF is the receiver saying it cannot reach the mouse (ends the wait).
        Parser.AeroxMouse or Parser.AeroxGen2Receiver or Parser.AeroxGen2Wired or Parser.RivalPro =>
            f.Length > 3 && (f[1] == model.Request[1] || (f[1] == 0x40 && f[2] == 0xFF)),
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
                if (f[1] == 0x40 && f[2] == 0xFF)
                    return null; // receiver cannot reach the mouse
                var v = f[2];
                if ((v & 0x7F) is 0 or > 21)
                    return null; // off/asleep, or not one of the 21 steps
                var percent = ((v & 0x7F) - 1) * 5;
                return new HidBatteryReading(percent, (v & 0x80) != 0);
            }
            case Parser.RivalMouse:
                return f[1] > 100 ? null : new HidBatteryReading(f[1], f[3] != 0);
            case Parser.GameBuds:
            {
                if (f.Length < 8 || f[1] != 0xB0)
                    return null;
                int? lowest = null;
                for (var bud = 0; bud < 2; bud++)
                {
                    if (f[4 + bud] == 0x03 && f[6 + bud] <= 100)
                        lowest = Math.Min(lowest ?? 100, f[6 + bud]);
                }
                return lowest is { } level ? new HidBatteryReading(level) : null;
            }
            case Parser.Arctis9:
                return f.Length < 6 ? null : new HidBatteryReading(MapToPercent(f[4], 0x64, 0x9A), f[5] == 0x01);
            case Parser.NovaProWireless:
                if (f.Length < 16 || f[15] == 0x01 || f[6] > 8)
                    return null;
                return new HidBatteryReading((int)Math.Round(f[6] * 100 / 8.0), f[15] == 0x02);
            case Parser.AeroxGen2Receiver or Parser.AeroxGen2Wired:
            {
                var echo = parser == Parser.AeroxGen2Receiver ? 0xD2 : 0x92;
                if (f.Length < 4 || f[1] != echo || f[2] > 0x02)
                    return null;
                if (parser == Parser.AeroxGen2Receiver && f[2] == 0 && f[3] == 0)
                    return null; // mouse off
                return f[3] <= 100 ? new HidBatteryReading(f[3], f[2] != 0) : null; // FF = not measured yet
            }
            case Parser.RivalPro:
                return f.Length >= 4 && f[1] == 0x92 && f[2] == 0x00 && f[3] <= 100 ? new HidBatteryReading(f[3]) : null;
            case Parser.ArctisProWireless:
                return ParseArctisProWirelessLevel(f);
            default:
                return null;
        }
    }

    private static HidBatteryReading? Percent(byte value) => value <= 100 ? new HidBatteryReading(value) : null;

    private static int MapToPercent(int value, int low, int high) =>
        Math.Clamp((value - low) * 100 / (high - low), 0, 100);

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
        Add("SteelSeries Arctis Nova 3P Wireless", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.Nova5, 0x2269, 0x226D);
        Add("SteelSeries Arctis GameBuds", DeviceKind.Headset, 3, 0xFFC0, [0x00, 0xB0], Parser.GameBuds, 0x230A);
        Add("SteelSeries Arctis 9", DeviceKind.Headset, 0, null, [0x00, 0x20], Parser.Arctis9, 0x12C2);
        Add("SteelSeries Arctis Pro Wireless", DeviceKind.Headset, 0, null, [0x00, 0x41, 0xAA], Parser.ArctisProWireless, 0x1290);
        Add("SteelSeries Arctis Nova Pro Wireless", DeviceKind.Headset, 4, null, [0x06, 0xB0], Parser.NovaProWireless, 0x12E0, 0x225D);

        // Mice: the 2.4 GHz receiver uses the wired command with bit 0x40 set (0x92 → 0xD2).
        Add("SteelSeries Aerox 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x183A, 0x187A);
        Add("SteelSeries Aerox 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1838, 0x1878);
        Add("SteelSeries Aerox 5 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x1854, 0x185E, 0x1862);
        Add("SteelSeries Aerox 5 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1852, 0x185C, 0x1860);
        Add("SteelSeries Aerox 9 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x185A, 0x1876);
        Add("SteelSeries Aerox 9 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1858, 0x1874);
        Add("SteelSeries Prime Wireless", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxMouse, 0x1842, 0x184A);
        Add("SteelSeries Prime Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxMouse, 0x1840, 0x1848);
        Add("SteelSeries Aerox 3 Wireless Gen 2", DeviceKind.Mouse, 3, null, [0x00, 0xD2], Parser.AeroxGen2Receiver, 0x1890);
        Add("SteelSeries Aerox 3 Wireless Gen 2", DeviceKind.Mouse, 3, null, [0x00, 0x92], Parser.AeroxGen2Wired, 0x1892);
        Add("SteelSeries Rival Pro", DeviceKind.Mouse, 2, null, [0x00, 0x92], Parser.RivalPro, 0x1880);
        Add("SteelSeries Rival 3 Wireless", DeviceKind.Mouse, 3, null, [0x00, 0xAA, 0x01], Parser.RivalMouse, 0x1830, 0x1872);
        Add("SteelSeries Rival 650", DeviceKind.Mouse, 0, null, [0x00, 0xAA, 0x01], Parser.RivalMouse, 0x172B, 0x1726);
        return models;
    }
}
