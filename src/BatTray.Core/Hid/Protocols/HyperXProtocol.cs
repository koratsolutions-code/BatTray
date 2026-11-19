using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// HyperX wireless headsets and mice (VIDs 0x0951 Kingston-era and 0x03F0 HP-era). Every request is a
/// read-only status query, sent as an output report (or, for the Cloud Stinger Core, a feature report) on the
/// vendor collection.
/// </summary>
/// <remarks>
/// Sources: github.com/Sapd/HeadsetControl (Cloud Flight, Cloud Alpha Wireless, Cloud II Wireless),
/// github.com/Medprj/HyperXWM (Cloud III Wireless), github.com/LennardKittner/HyperHeadset (Cloud III S, Cloud II Core,
/// Kingston Cloud II / Cloud Flight S), github.com/davidsantanaalves/HyperBatteryMonitor (Cloud Stinger Core),
/// github.com/PMarinov1994/ngenuity-linux-cli (Pulsefire Haste 2). Cross-checked with BatteryHub's notes, which report
/// hardware tests of Cloud III Wireless 03F0:0C9D, Haste 2 Core 03F0:0AB5, Haste 2 03F0:0F98, Haste 2 Mini 03F0:0DA0,
/// Stinger Core 03F0:0A93 / 0A8E and Kingston 0951:1713 / 173F.
/// </remarks>
public sealed class HyperXProtocol : IHidBatteryProtocol
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(800);

    /// <summary>A Haste 2 reports about 40 % for a few seconds after it powers on; readings this soon are ignored.</summary>
    private static readonly TimeSpan HasteSettleTime = TimeSpan.FromSeconds(6);

    public enum Family
    {
        /// <summary>Cloud Flight: <c>21 FF 05</c> → millivolts big-endian at [3..4].</summary>
        CloudFlight,

        /// <summary>Cloud Alpha Wireless: <c>21 BB 03/0C/0B</c> → connected / charging / percent at [3].</summary>
        CloudAlphaWireless,

        /// <summary>Cloud II Wireless (HP): <c>06 FF BB 02/03 00</c> → [7] = %, [4]==1 charging.</summary>
        CloudIIWireless,

        /// <summary>Cloud Alpha 2: <c>52 01</c> connected, <c>50 02</c> → [2] = %, [3]==1 charging.</summary>
        Alpha2,

        /// <summary>Pulsefire Haste 2 dongles: same pair as Alpha 2; [3] 1 charging / 2 full on the charger.</summary>
        Haste2,

        /// <summary>Cloud III Wireless / Cloud II Core Wireless: <c>66 82</c> connected, <c>66 8A</c> charging, <c>66 89</c> → [4] = %.</summary>
        Cloud3,

        /// <summary>Cloud III S Wireless: <c>0C 02 03 01 00 cmd</c> → [5] = cmd, [6] = value (FF invalid).</summary>
        Cloud3S,

        /// <summary>Kingston Cloud II Wireless / Cloud Flight S: fixed 17-byte frame, reply <c>0B ?? BB cmd</c>, [7] = %.</summary>
        CloudIIKingston,

        /// <summary>Cloud Stinger Core Wireless: feature-report exchange, report 0xFF; battery in millivolts.</summary>
        StingerCore,
    }

    public sealed record Model(ushort VendorId, ushort ProductId, string Name, DeviceKind Kind, Family Family);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0951, 0x16C4, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x0951, 0x1723, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x0951, 0x1749, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x03F0, 0x0E90, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x03F0, 0x098D, "HyperX Cloud Alpha Wireless", DeviceKind.Headset, Family.CloudAlphaWireless),
        new(0x03F0, 0x0696, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIWireless),
        new(0x03F0, 0x018B, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIWireless),
        new(0x03F0, 0x0D93, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIWireless),
        new(0x0951, 0x1718, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIKingston),
        new(0x0951, 0x16EA, "HyperX Cloud Flight S", DeviceKind.Headset, Family.CloudIIKingston),
        new(0x0951, 0x16EB, "HyperX Cloud Flight S", DeviceKind.Headset, Family.CloudIIKingston),
        new(0x03F0, 0x08BE, "HyperX Cloud Alpha 2 Wireless", DeviceKind.Headset, Family.Alpha2),
        new(0x03F0, 0x05B7, "HyperX Cloud III Wireless", DeviceKind.Headset, Family.Cloud3),
        new(0x03F0, 0x0C9D, "HyperX Cloud III Wireless", DeviceKind.Headset, Family.Cloud3),
        new(0x03F0, 0x069F, "HyperX Cloud II Core Wireless", DeviceKind.Headset, Family.Cloud3),
        new(0x03F0, 0x0995, "HyperX Cloud II Core Wireless", DeviceKind.Headset, Family.Cloud3),
        new(0x03F0, 0x06BE, "HyperX Cloud III S Wireless", DeviceKind.Headset, Family.Cloud3S),
        new(0x03F0, 0x02CC, "HyperX Cloud III S Wireless", DeviceKind.Headset, Family.Cloud3S),
        new(0x03F0, 0x0A93, "HyperX Cloud Stinger Core Wireless", DeviceKind.Headset, Family.StingerCore),
        new(0x03F0, 0x0A8E, "HyperX Cloud Stinger Core Wireless DTS", DeviceKind.Headset, Family.StingerCore),
        new(0x0951, 0x170B, "HyperX Cloud Stinger Core Wireless", DeviceKind.Headset, Family.StingerCore),
        new(0x0951, 0x1713, "HyperX Cloud Stinger Core Wireless", DeviceKind.Headset, Family.StingerCore),
        new(0x0951, 0x173F, "HyperX Cloud Core Wireless", DeviceKind.Headset, Family.StingerCore),
        new(0x03F0, 0x0AB5, "HyperX Pulsefire Haste 2 Core Wireless", DeviceKind.Mouse, Family.Haste2),
        new(0x03F0, 0x0D97, "HyperX Pulsefire Haste 2 Wireless", DeviceKind.Mouse, Family.Haste2),
        new(0x03F0, 0x0F98, "HyperX Pulsefire Haste 2 Wireless", DeviceKind.Mouse, Family.Haste2),
        new(0x03F0, 0x0DA0, "HyperX Pulsefire Haste 2 Mini", DeviceKind.Mouse, Family.Haste2),
    ];

    /// <summary>Stinger Core receivers that may hand back a stale feature report and need re-reads.</summary>
    private static readonly HashSet<(ushort, ushort)> StingerRereadReceivers =
        [(0x03F0, 0x0A93), (0x03F0, 0x0A8E), (0x0951, 0x1713), (0x0951, 0x173F)];

    /// <summary>When each Haste 2 dongle last reported its mouse linked (keyed by receiver id); first sight counts as settled.</summary>
    private static readonly ConcurrentDictionary<string, DateTime> HasteLinkedSince = new();
    private static readonly ConcurrentDictionary<string, byte> HasteSeen = new();

    public string Name => "HyperX";

    public bool CanHandle(HidReceiver receiver) => Find(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not var (model, collection))
            return [];

        var reading = model.Family switch
        {
            Family.CloudFlight => await ReadCloudFlightAsync(collection, cancellationToken),
            Family.CloudAlphaWireless => await ReadCloudAlphaAsync(collection, cancellationToken),
            Family.CloudIIWireless => await ReadCloudIIAsync(collection, cancellationToken),
            Family.Alpha2 => await ReadAlpha2Async(collection, cancellationToken),
            Family.Haste2 => await ReadHaste2Async(receiver.Id, collection, cancellationToken),
            Family.Cloud3 => await ReadCloud3Async(collection, cancellationToken),
            Family.Cloud3S => await ReadCloud3SAsync(collection, cancellationToken),
            Family.CloudIIKingston => await ReadCloudIIKingstonAsync(collection, cancellationToken),
            Family.StingerCore => await ReadStingerCoreAsync(collection,
                StingerRereadReceivers.Contains((receiver.VendorId, receiver.ProductId)), cancellationToken),
            _ => null,
        };

        return reading is null ? [] : [reading with { Name = model.Name, Kind = model.Kind }];
    }

    private static async Task<HidBatteryReading?> ReadCloudFlightAsync(HidCollectionInfo c, CancellationToken ct)
    {
        var reply = await HidTransact.WriteReadAsync(c, [0x21, 0xFF, 0x05], f => f.Length >= 5, Timeout, ct);
        return reply is null ? null : ParseCloudFlight(reply);
    }

    private static async Task<HidBatteryReading?> ReadCloudAlphaAsync(HidCollectionInfo c, CancellationToken ct)
    {
        async Task<byte?> Query(byte cmd)
        {
            var reply = await HidTransact.WriteReadAsync(c, [0x21, 0xBB, cmd],
                f => f.Length >= 4 && f[0] == 0x21 && f[1] == 0xBB && f[2] == cmd, Timeout, ct);
            return reply?[3];
        }

        if (await Query(0x03) is not { } connected || connected == 1)
            return null; // [3]==1: headset not connected
        var percent = await Query(0x0B);
        var charging = await Query(0x0C);
        return percent is { } p and <= 100 ? new HidBatteryReading(p, charging is null ? null : charging == 1) : null;
    }

    private static async Task<HidBatteryReading?> ReadCloudIIAsync(HidCollectionInfo c, CancellationToken ct)
    {
        async Task<byte[]?> Query(byte cmd) =>
            await HidTransact.WriteReadAsync(c, [0x06, 0xFF, 0xBB, cmd, 0x00],
                f => f.Length >= 8 && f[0] == 0x06 && f[1] == 0xFF && f[2] == 0xBB && f[3] == cmd, Timeout, ct);

        var level = await Query(0x02);
        if (level is null || level[7] is 0 or > 100)
            return null;
        var charging = await Query(0x03);
        return new HidBatteryReading(level[7], charging is null ? null : charging[4] == 1);
    }

    private static async Task<HidBatteryReading?> ReadAlpha2Async(HidCollectionInfo c, CancellationToken ct)
    {
        var connected = await HidTransact.WriteReadAsync(c, [0x52, 0x01],
            f => f.Length >= 3 && f[0] == 0x53 && f[1] == 0x01, Timeout, ct);
        if (connected is null || connected[2] == 0)
            return null;

        var status = await HidTransact.WriteReadAsync(c, [0x50, 0x02],
            f => f.Length >= 4 && f[0] == 0x51 && f[1] == 0x02, Timeout, ct);
        return status is null ? null : ParseAlpha2Status(status);
    }

    private static async Task<HidBatteryReading?> ReadHaste2Async(string receiverId, HidCollectionInfo c, CancellationToken ct)
    {
        // After a reconnect the dongle floods the channel with unrelated reports, so a missing link reply is not
        // fatal: the battery query decides.
        var link = await HidTransact.WriteReadAsync(c, [0x52, 0x01],
            f => f.Length >= 3 && f[0] == 0x53 && f[1] == 0x01, TimeSpan.FromSeconds(1), ct);
        var linked = link is null || link[2] != 0;
        if (!linked)
        {
            HasteLinkedSince.TryRemove(receiverId, out _);
            return null; // mouse off or out of range
        }

        var status = await HidTransact.WriteReadAsync(c, [0x50, 0x02],
            f => f.Length >= 4 && f[0] == 0x51 && f[1] == 0x02, TimeSpan.FromSeconds(1), ct);
        if (status is null || ParseHaste2Status(status) is not { } reading)
            return null;

        // First sight of this dongle in this session: assume the mouse has been on for a while. After the mouse
        // was seen off, the link-up time starts the settle window.
        var now = DateTime.UtcNow;
        var firstSight = HasteSeen.TryAdd(receiverId, 0);
        var since = HasteLinkedSince.GetOrAdd(receiverId, firstSight ? now - HasteSettleTime : now);
        return IsHasteSettled(since, now) ? reading : null;
    }

    /// <summary>True once the mouse has been linked for the settle time (its first readings are unreliable).</summary>
    public static bool IsHasteSettled(DateTime linkedSince, DateTime now) => now - linkedSince >= HasteSettleTime;

    private static async Task<HidBatteryReading?> ReadCloud3Async(HidCollectionInfo c, CancellationToken ct)
    {
        async Task<byte[]?> Query(byte cmd) =>
            await HidTransact.WriteReadAsync(c, [0x66, cmd], f => f.Length >= 5 && f[0] == 0x66 && f[1] == cmd, ShortTimeout, ct);

        if (await Query(0x82) is not { } link || link[2] == 0)
            return null; // headset off
        var charging = await Query(0x8A);
        var level = await Query(0x89);
        return level is not null && ParseCloud3Level(level) is { } reading
            ? reading with { Charging = charging is null ? null : charging[2] == 1 }
            : null;
    }

    private static async Task<HidBatteryReading?> ReadCloud3SAsync(HidCollectionInfo c, CancellationToken ct)
    {
        async Task<byte?> Query(byte cmd)
        {
            var reply = await HidTransact.WriteReadAsync(c, BuildCloud3SRequest(cmd), f => ParseCloud3SValue(f, cmd) is not null, Timeout, ct);
            return reply is null ? null : ParseCloud3SValue(reply, cmd);
        }

        if (await Query(0x02) != 0x02)
            return null; // headset not connected
        if (await Query(0x06) is not { } percent || percent > 100)
            return null;
        var charge = await Query(0x48);
        return new HidBatteryReading(percent, charge is null ? null : charge is 1 or 2);
    }

    private static async Task<HidBatteryReading?> ReadCloudIIKingstonAsync(HidCollectionInfo c, CancellationToken ct)
    {
        async Task<byte[]?> Query(byte cmd) =>
            await HidTransact.WriteReadAsync(c, BuildCloudIIKingstonRequest(cmd), f => IsCloudIIKingstonReply(f, cmd), Timeout, ct);

        var level = await Query(0x02);
        if (level is null || level[7] > 100)
            return null;
        var charging = await Query(0x03);
        return new HidBatteryReading(level[7], charging is null ? null : charging[4] == 1);
    }

    private static async Task<HidBatteryReading?> ReadStingerCoreAsync(HidCollectionInfo c, bool reread, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForFeatures(c);
        if (connection is null)
            return null;

        async Task<byte[]?> Exchange(byte[] command, Func<byte[], bool> good, bool acceptLastStale)
        {
            if (!connection.SetFeature(HidTransact.Pad(command, c.FeatureLength)))
                return null;
            byte[]? last = null;
            for (var i = 0; i < (reread ? 10 : 1); i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
                var reply = new byte[c.FeatureLength];
                reply[0] = 0xFF;
                if (!connection.GetFeature(reply) || reply.Length < 13)
                    return null;
                last = reply;
                if (!reread || good(reply))
                    return reply;
            }
            return acceptLastStale ? last : null;
        }

        // The connection reply's header is undocumented: after ten stale-looking reads the last one is used.
        var link = await Exchange(StingerConnectionQuery, r => !(r[1] == 0x01 && r[2] == 0x00), acceptLastStale: true);
        if (link is null || link[11] != 0x01)
            return null; // headset off
        var battery = await Exchange(StingerBatteryQuery, r => r[0] == 0xFF && r[1] == 0x12 && r[2] == 0x05, acceptLastStale: false);
        return battery is null ? null : ParseStingerBattery(battery);
    }

    // ---- Requests and parsers (public for tests) ----

    /// <summary><c>51 02 &lt;%&gt; &lt;charge state&gt;</c>.</summary>
    public static HidBatteryReading? ParseAlpha2Status(ReadOnlySpan<byte> f) =>
        f.Length >= 4 && f[0] == 0x51 && f[1] == 0x02 && f[2] <= 100 ? new HidBatteryReading(f[2], f[3] == 1) : null;

    /// <summary>Haste 2: <c>51 02 &lt;%&gt; &lt;charge: 1 charging, 2 full on the charger&gt;</c>.</summary>
    public static HidBatteryReading? ParseHaste2Status(ReadOnlySpan<byte> f) =>
        f.Length >= 4 && f[0] == 0x51 && f[1] == 0x02 && f[2] <= 100 ? new HidBatteryReading(f[2], f[3] is 1 or 2) : null;

    /// <summary>Cloud III level reply: <c>66 89 &lt;mV hi&gt; &lt;mV lo&gt; &lt;%&gt;</c>.</summary>
    public static HidBatteryReading? ParseCloud3Level(ReadOnlySpan<byte> f) =>
        f.Length >= 5 && f[0] == 0x66 && f[1] == 0x89 && f[4] <= 100 ? new HidBatteryReading(f[4]) : null;

    /// <summary>Cloud III S request: report 0x0C, <c>02 03 01 00 &lt;cmd&gt;</c>.</summary>
    public static byte[] BuildCloud3SRequest(byte command) => [0x0C, 0x02, 0x03, 0x01, 0x00, command];

    /// <summary>Cloud III S reply value: [0]=0x0C, [5]=cmd, [6]=value (0xFF = invalid → null).</summary>
    public static byte? ParseCloud3SValue(ReadOnlySpan<byte> f, byte command) =>
        f.Length >= 7 && f[0] == 0x0C && f[5] == command && f[6] != 0xFF ? f[6] : null;

    /// <summary>Kingston Cloud II Wireless / Cloud Flight S fixed request frame.</summary>
    public static byte[] BuildCloudIIKingstonRequest(byte command) =>
        [0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB, command, 0x00];

    public static bool IsCloudIIKingstonReply(ReadOnlySpan<byte> f, byte command) =>
        f.Length >= 8 && f[0] == 0x0B && f[2] == 0xBB && f[3] == command;

    /// <summary>Stinger Core connection query (feature report 0xFF).</summary>
    public static readonly byte[] StingerConnectionQuery =
        [0xFF, 0x07, 0x00, 0xFD, 0x04, 0x00, 0x00, 0x02, 0x82, 0x03, 0x05, 0x29, 0x0F, 0x00, 0x00, 0x03, 0x68, 0x10, 0x00, 0x00];

    /// <summary>Stinger Core battery query (feature report 0xFF).</summary>
    public static readonly byte[] StingerBatteryQuery =
        [0xFF, 0x07, 0x00, 0xFD, 0x04, 0x0C, 0xF1, 0x02, 0x01, 0x04, 0xF0, 0x0C];

    private static readonly (int Millivolts, int Percent)[] StingerChargingCurve =
        [(4130, 100), (4090, 90), (4050, 80), (4000, 70), (3967, 60), (3946, 50), (3930, 40), (3916, 30), (3871, 20), (3831, 10), (3400, 0)];

    private static readonly (int Millivolts, int Percent)[] StingerDischargingCurve =
        [(4121, 100), (4042, 90), (3954, 80), (3905, 70), (3838, 60), (3785, 50), (3763, 40), (3738, 30), (3700, 20), (3651, 10), (3517, 0)];

    /// <summary>Stinger Core battery reply: [9] 3 discharging / 5 charging / 6 full, [11..12] millivolts little-endian.</summary>
    public static HidBatteryReading? ParseStingerBattery(ReadOnlySpan<byte> f)
    {
        if (f.Length < 13 || f[0] != 0xFF || f[9] is not (0x03 or 0x05 or 0x06))
            return null;
        if (f[9] == 0x06)
            return new HidBatteryReading(100, false);
        var millivolts = f[11] | (f[12] << 8);
        if (millivolts is < 3000 or > 4500)
            return null;
        var charging = f[9] == 0x05;
        return new HidBatteryReading(Interpolate(charging ? StingerChargingCurve : StingerDischargingCurve, millivolts), charging);
    }

    private static int Interpolate((int Millivolts, int Percent)[] curve, int millivolts)
    {
        if (millivolts >= curve[0].Millivolts)
            return curve[0].Percent;
        for (var i = 1; i < curve.Length; i++)
        {
            var (hiMv, hiPct) = curve[i - 1];
            var (loMv, loPct) = curve[i];
            if (millivolts >= loMv)
                return loPct + (hiPct - loPct) * (millivolts - loMv) / (hiMv - loMv);
        }
        return curve[^1].Percent;
    }

    /// <summary>Cloud Flight: millivolts big-endian at [3..4]; above 4107 mV means charging.</summary>
    public static HidBatteryReading? ParseCloudFlight(ReadOnlySpan<byte> f)
    {
        if (f.Length < 5)
            return null;
        var millivolts = (f[3] << 8) | f[4];
        if (millivolts is < 3000 or > 4500)
            return null;
        return new HidBatteryReading(CloudFlightPercent(millivolts), millivolts > 4107);
    }

    /// <summary>Discharge curve used by HeadsetControl for the Cloud Flight.</summary>
    public static int CloudFlightPercent(int millivolts)
    {
        if (millivolts <= 3648)
            return (int)Math.Clamp(Math.Round(0.00125 * millivolts), 0, 100);
        if (millivolts > 3975)
            return 100;
        double v = millivolts;
        var p = 2.547505e-8 * Math.Pow(v, 4) - 3.900299e-4 * Math.Pow(v, 3) + 2.238321 * v * v - 5706.256 * v + 5452299;
        return (int)Math.Clamp(Math.Round(p), 0, 100);
    }

    // ---- Collections ----

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        var model = Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId == receiver.ProductId);
        if (model is null)
            return null;

        var collection = receiver.Collections.Where(c => Fits(model, c))
            .OrderBy(c => Preference(model, c))
            .FirstOrDefault();
        return collection is null ? null : (model, collection);
    }

    /// <summary>The collection shape each family's protocol lives on.</summary>
    private static bool Fits(Model model, HidCollectionInfo c) => model.Family switch
    {
        Family.Cloud3 when model.ProductId is 0x05B7 or 0x0C9D => c.OutputLength == 62 && c.InputLength == 62,
        Family.Cloud3 => c.UsagePage >= 0xFF00 && c.OutputLength >= 62 && c.InputLength >= 62,
        // Windows lists the 64/64-byte collection under page 0x01C0, not a vendor page.
        Family.Cloud3S => c.OutputLength == 64 && c.InputLength == 64,
        Family.CloudIIKingston when model.ProductId == 0x1718 => c.OutputLength == 62 && c.InputLength > 0,
        Family.CloudIIKingston => c.Is(0xFF13, 0x0001) && c.InputLength == 62 && c.OutputLength == 62,
        Family.StingerCore => c.Is(0xFF00, 0x0001) && c.FeatureLength >= 32,
        Family.Haste2 => c.UsagePage >= 0xFF00 && c.OutputLength >= 2 && c.InputLength >= 8,
        _ => c.UsagePage >= 0xFF00 && c.OutputLength >= 3 && c.InputLength >= 3,
    };

    private static int Preference(Model model, HidCollectionInfo c) => model.Family switch
    {
        Family.CloudIIWireless => c.Is(0xFF90, 0x0303) ? 0 : 1,
        Family.Alpha2 => c.Is(0xFF13, 0xFF00) ? 0 : c.UsagePage == 0xFF13 ? 1 : 2,
        _ => 0,
    };
}
