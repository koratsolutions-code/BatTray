using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// HyperX wireless headsets and mice (VIDs 0x0951 Kingston-era and 0x03F0 HP-era). Every request is a
/// read-only status query sent as an output report on the vendor collection.
/// </summary>
public sealed class HyperXProtocol : IHidBatteryProtocol
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public enum Family
    {
        /// <summary>Cloud Flight: <c>21 FF 05</c> → millivolts big-endian at [3..4].</summary>
        CloudFlight,

        /// <summary>Cloud Alpha Wireless: <c>21 BB 03/0C/0B</c> → connected / charging / percent at [3].</summary>
        CloudAlphaWireless,

        /// <summary>Cloud II Wireless (HP): <c>06 FF BB 02/03 00</c> → [7] = %, [4]==1 charging.</summary>
        CloudIIWireless,

        /// <summary>Cloud Alpha 2 / Pulsefire Haste 2: <c>52 01</c> connected, <c>50 02</c> → [2] = %, [3]==1 charging.</summary>
        Alpha2,
    }

    public sealed record Model(ushort VendorId, ushort ProductId, string Name, DeviceKind Kind, Family Family);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0951, 0x16C4, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x0951, 0x1723, "HyperX Cloud Flight", DeviceKind.Headset, Family.CloudFlight),
        new(0x03F0, 0x098D, "HyperX Cloud Alpha Wireless", DeviceKind.Headset, Family.CloudAlphaWireless),
        new(0x03F0, 0x0696, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIWireless),
        new(0x03F0, 0x018B, "HyperX Cloud II Wireless", DeviceKind.Headset, Family.CloudIIWireless),
        new(0x03F0, 0x08BE, "HyperX Cloud Alpha 2 Wireless", DeviceKind.Headset, Family.Alpha2),
        new(0x03F0, 0x0AB5, "HyperX Pulsefire Haste 2 Wireless", DeviceKind.Mouse, Family.Alpha2),
        new(0x03F0, 0x0D97, "HyperX Pulsefire Haste 2 Wireless", DeviceKind.Mouse, Family.Alpha2),
        new(0x03F0, 0x0F98, "HyperX Pulsefire Haste 2 Wireless", DeviceKind.Mouse, Family.Alpha2),
    ];

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

    /// <summary><c>51 02 &lt;%&gt; &lt;charge state&gt;</c>.</summary>
    public static HidBatteryReading? ParseAlpha2Status(ReadOnlySpan<byte> f) =>
        f.Length >= 4 && f[0] == 0x51 && f[1] == 0x02 && f[2] <= 100 ? new HidBatteryReading(f[2], f[3] == 1) : null;

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

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        var model = Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId == receiver.ProductId);
        if (model is null)
            return null;

        // Vendor-defined collection with room for the request.
        var collection = receiver.Collections
            .Where(c => c.UsagePage >= 0xFF00 && c.OutputLength >= 3 && c.InputLength >= 3)
            .OrderBy(c => model.Family == Family.CloudIIWireless && c.Is(0xFF90, 0x0303) ? 0 : 1)
            .ThenBy(c => model.Family == Family.Alpha2 && c.UsagePage == 0xFF13 ? 0 : 1)
            .FirstOrDefault();
        return collection is null ? null : (model, collection);
    }
}
