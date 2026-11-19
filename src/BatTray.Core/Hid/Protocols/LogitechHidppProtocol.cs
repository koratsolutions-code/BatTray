using System.Text;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Logitech HID++ 2.0: Unifying, Bolt and Lightspeed receivers (up to six paired devices each) and
/// Logitech devices plugged in directly over USB.
/// </summary>
/// <remarks>
/// Only read-only functions are used:
/// <list type="bullet">
///   <item>Root (feature index 0): getFeature (fn 0) and getProtocolVersion/ping (fn 1).</item>
///   <item>0x1004 Unified Battery: get_status (fn 1).</item>
///   <item>0x1000 Battery Status: getBatteryLevelStatus (fn 0).</item>
///   <item>0x1001 Battery Voltage: getBatteryInfo (fn 0) — the voltage is mapped to a percentage.</item>
///   <item>0x1F20 ADC Measurement (G headsets): fn 0 — millivolts mapped through a per-model curve.</item>
///   <item>0x0005 Device Name &amp; Type: getCount (fn 0), getDeviceName (fn 1), getDeviceType (fn 2).</item>
///   <item>HID++ 1.0 devices (they answer the 2.0 ping with "invalid sub id"): short register reads
///         0x0D (battery %) and 0x07 (battery level 1–7), sub id 0x81.</item>
/// </list>
/// The PRO X3 SUPERSTRIKE dongle (046D:C54F) relays a single mouse on index 1 and exposes HID++ on FF43:0301/0302.
/// Headset voltage curves follow github.com/Sapd/HeadsetControl; the passthrough dongle follows
/// github.com/pwr-Solaar/Solaar; cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class LogitechHidppProtocol : IHidBatteryProtocol
{
    public const ushort LogitechVendorId = 0x046D;

    public const byte DirectDeviceIndex = 0xFF;
    public const byte RootFeatureIndex = 0x00;
    public const ushort UnifiedBatteryFeature = 0x1004;
    public const ushort BatteryStatusFeature = 0x1000;
    public const ushort BatteryVoltageFeature = 0x1001;
    public const ushort DeviceNameFeature = 0x0005;
    public const ushort AdcMeasurementFeature = 0x1F20;

    /// <summary>HID++ 1.0 error code returned when a 1.0 device receives a 2.0 request.</summary>
    private const byte ErrorInvalidSubId = 0x01;
    private const byte RegisterBatteryPercent = 0x0D;
    private const byte RegisterBatteryLevel = 0x07;

    private const byte PingMarker = 0x5A;
    private const int MaxPairedDevices = 6;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1000);

    /// <summary>Typical Li-ion discharge curve (mV → %), used for devices that only report voltage.</summary>
    private static readonly (int Millivolts, int Percent)[] VoltageCurve =
    [
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    ];

    public string Name => "Logitech HID++";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == LogitechVendorId && HidppChannel.FindCollections(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        using var channel = HidppChannel.Open(receiver);
        if (channel is null)
            return [];

        var session = new Session(channel, cancellationToken);
        var results = new List<HidBatteryReading>();

        // The SUPERSTRIKE dongle answers the ping itself on 0xFF but has no battery: its mouse is on index 1.
        if (receiver.ProductId == HidppChannel.SuperstrikeDongle)
        {
            if (await ReadIndexAsync(session, receiver.ProductId, 1, subId: null) is { } relayed)
                results.Add(relayed with { Kind = relayed.Kind is null or DeviceKind.Other ? DeviceKind.Mouse : relayed.Kind });
            return results;
        }

        // A Bluetooth link is always a single device; a sleepy one may miss the first ping, so never scan slots.
        var bluetooth = IsBluetoothPath(HidppChannel.FindCollections(receiver)?.Long.Path);
        var looksLikeReceiver = receiver.Product?.Contains("Receiver", StringComparison.OrdinalIgnoreCase) == true;

        // A device plugged in directly answers HID++ 2.0 on index 0xFF; a receiver answers HID++ 1.0 errors there.
        if (bluetooth || (!looksLikeReceiver && await session.ProtocolVersionAsync(DirectDeviceIndex) >= 2))
        {
            if (await ReadIndexAsync(session, receiver.ProductId, DirectDeviceIndex, subId: null) is { } reading)
                results.Add(reading);
            return results;
        }

        for (byte index = 1; index <= MaxPairedDevices; index++)
        {
            if (await ReadIndexAsync(session, receiver.ProductId, index, subId: index.ToString("X2")) is { } reading)
                results.Add(reading);
        }

        return results;
    }

    /// <summary>True for a HID interface reached over Bluetooth (HOGP service 0x1812 or classic HID 0x1124).</summary>
    public static bool IsBluetoothPath(string? path) =>
        path is not null &&
        (path.Contains("{00001812-", StringComparison.OrdinalIgnoreCase) ||
         path.Contains("{00001124-", StringComparison.OrdinalIgnoreCase));

    private static async Task<HidBatteryReading?> ReadIndexAsync(Session session, ushort productId, byte index, string? subId)
    {
        var ping = await session.PingAsync(index);
        if (ping.Version >= 2)
            return await ReadDeviceAsync(session, productId, index, subId);
        if (ping.LegacyDevice)
            return await ReadLegacyBatteryAsync(session, index) is { } legacy ? legacy with { SubId = subId } : null;
        return null; // empty slot or device off/asleep
    }

    private static async Task<HidBatteryReading?> ReadDeviceAsync(Session session, ushort productId, byte index, string? subId)
    {
        var battery = await ReadBatteryAsync(session, productId, index);
        if (battery is null)
            return null;

        var (name, kind) = await ReadIdentityAsync(session, index);
        if (kind is null or DeviceKind.Other && battery.Kind is { } batteryKind)
            kind = batteryKind;
        return battery with { Name = name, Kind = kind, SubId = subId };
    }

    /// <summary>HID++ 1.0: register 0x0D (percent) first, then 0x07 (coarse level).</summary>
    private static async Task<HidBatteryReading?> ReadLegacyBatteryAsync(Session session, byte index)
    {
        if (await session.ReadRegisterAsync(index, RegisterBatteryPercent) is { } percent &&
            ParseRegisterBatteryPercent(percent) is { } fromPercent)
        {
            return fromPercent;
        }

        return await session.ReadRegisterAsync(index, RegisterBatteryLevel) is { } level
            ? ParseRegisterBatteryLevel(level)
            : null;
    }

    private static async Task<HidBatteryReading?> ReadBatteryAsync(Session session, ushort productId, byte index)
    {
        if (await session.FeatureIndexAsync(index, UnifiedBatteryFeature) is { } unified &&
            await session.RequestAsync(index, unified, 1) is { } status)
        {
            return ParseUnifiedBattery(status);
        }

        if (await session.FeatureIndexAsync(index, BatteryStatusFeature) is { } legacy &&
            await session.RequestAsync(index, legacy, 0) is { } levelStatus)
        {
            return ParseBatteryStatus(levelStatus);
        }

        if (await session.FeatureIndexAsync(index, BatteryVoltageFeature) is { } voltage &&
            await session.RequestAsync(index, voltage, 0) is { } info)
        {
            return ParseBatteryVoltage(info);
        }

        if (await session.FeatureIndexAsync(index, AdcMeasurementFeature) is { } adc &&
            await session.RequestAsync(index, adc, 0) is { } measurement)
        {
            return ParseAdcMeasurement(measurement, productId) is { } headset ? headset with { Kind = DeviceKind.Headset } : null;
        }

        return null;
    }

    private static async Task<(string? Name, DeviceKind? Kind)> ReadIdentityAsync(Session session, byte index)
    {
        if (await session.FeatureIndexAsync(index, DeviceNameFeature) is not { } feature)
            return (null, null);

        string? name = null;
        if (await session.RequestAsync(index, feature, 0) is { } count && count[0] is > 0 and < 64)
        {
            var builder = new StringBuilder();
            while (builder.Length < count[0])
            {
                var chunk = await session.RequestAsync(index, feature, 1, (byte)builder.Length);
                if (chunk is null)
                    break;
                var text = Encoding.ASCII.GetString(chunk).TrimEnd('\0');
                if (text.Length == 0)
                    break;
                builder.Append(text, 0, Math.Min(text.Length, count[0] - builder.Length));
            }
            name = builder.Length > 0 ? builder.ToString().Trim() : null;
        }

        var type = await session.RequestAsync(index, feature, 2);
        var kind = type is null ? (DeviceKind?)null : KindFromDeviceType(type[0]);
        if (kind == DeviceKind.Other)
            kind = DeviceKindClassifier.FromName(name);
        return (name is null ? null : $"Logitech {name}".Replace("Logitech Logitech", "Logitech"), kind);
    }

    // ---- Parsing (public for tests) ----

    /// <summary>0x1004 get_status: [state of charge %, level flags, charging status, external power].</summary>
    public static HidBatteryReading? ParseUnifiedBattery(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3)
            return null;

        int? percent = p[0] is > 0 and <= 100
            ? p[0]
            : p[1] switch // devices without a percentage report coarse level flags
            {
                _ when (p[1] & 0x08) != 0 => 100, // full
                _ when (p[1] & 0x04) != 0 => 60,  // good
                _ when (p[1] & 0x02) != 0 => 20,  // low
                _ when (p[1] & 0x01) != 0 => 5,   // critical
                _ => null,
            };

        // Charging status: 0 discharging, 1 charging, 2 slow charging, 3 charge complete (still plugged in).
        return percent is null ? null : new HidBatteryReading(percent, Charging: p[2] is 1 or 2 or 3);
    }

    /// <summary>0x1000 getBatteryLevelStatus: [level %, next level %, status].</summary>
    public static HidBatteryReading? ParseBatteryStatus(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3 || p[0] > 100)
            return null;
        // Status: 0 discharging, 1 recharging, 2 almost full, 3 full, 4 slow recharge, 5–6 errors.
        // Some devices report level 0 together with "full" while on the charger.
        if (p[0] == 0)
            return p[2] == 3 ? new HidBatteryReading(100, Charging: true) : null;
        return new HidBatteryReading(p[0], Charging: p[2] is >= 1 and <= 4);
    }

    /// <summary>0x1F20 ADC measurement (headsets): [millivolts big-endian, 2 bytes][state, 3 = charging].</summary>
    public static HidBatteryReading? ParseAdcMeasurement(ReadOnlySpan<byte> p, ushort productId)
    {
        if (p.Length < 3)
            return null;
        var millivolts = (p[0] << 8) | p[1];
        if (millivolts is < 2500 or > 5000)
            return null;
        return new HidBatteryReading(HeadsetPercentFromVoltage(productId, millivolts), Charging: p[2] == 0x03);
    }

    /// <summary>Per-model headset discharge curves (calibrations published by HeadsetControl).</summary>
    public static int HeadsetPercentFromVoltage(ushort productId, int millivolts)
    {
        var curve = productId switch
        {
            0x0A66 => G533Curve,
            0x0AC4 => G535Curve,
            0x0A5C or 0x0A89 or 0x0A5B or 0x0A87 or 0x0AB5 or 0x0AFE => G633Curve,
            0x0AA7 or 0x0AAA or 0x0ABA or 0x0AFB => GProCurve,
            _ => DefaultHeadsetCurve,
        };
        return Interpolate(curve, millivolts);
    }

    private static readonly (int Millivolts, int Percent)[] G533Curve =
        [(4200, 100), (3850, 50), (3790, 30), (3750, 20), (3680, 5), (3330, 0)];
    private static readonly (int Millivolts, int Percent)[] G535Curve =
        [(4175, 100), (3817, 50), (3766, 30), (3730, 20), (3664, 5), (3310, 0)];
    private static readonly (int Millivolts, int Percent)[] G633Curve =
        [(4100, 100), (3950, 80), (3850, 60), (3750, 40), (3650, 20), (3500, 10), (3300, 5), (3150, 0)];
    private static readonly (int Millivolts, int Percent)[] GProCurve =
        [(4150, 100), (3830, 50), (3780, 30), (3740, 20), (3670, 5), (3320, 0)];
    private static readonly (int Millivolts, int Percent)[] DefaultHeadsetCurve =
        [(4100, 100), (3850, 50), (3780, 30), (3730, 20), (3650, 5), (3300, 0)];

    /// <summary>HID++ 1.0 register 0x0D: [percent 1–100, ?, charge state (0x21–0x26 = charging)].</summary>
    public static HidBatteryReading? ParseRegisterBatteryPercent(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3 || p[0] is 0 or > 100)
            return null;
        return new HidBatteryReading(p[0], Charging: p[2] is >= 0x21 and <= 0x26);
    }

    /// <summary>HID++ 1.0 register 0x07: [level 1–7, charge state (0x21–0x26 = charging)].</summary>
    public static HidBatteryReading? ParseRegisterBatteryLevel(ReadOnlySpan<byte> p)
    {
        if (p.Length < 2 || p[0] == 0)
            return null;
        ReadOnlySpan<int> levels = [0, 5, 10, 20, 35, 50, 75, 100];
        return new HidBatteryReading(levels[Math.Min((int)p[0], 7)], Charging: p[1] is >= 0x21 and <= 0x26);
    }

    /// <summary>0x1001 getBatteryInfo: [voltage mV (big-endian, 2 bytes), flags (bit 7 = charging)].</summary>
    public static HidBatteryReading? ParseBatteryVoltage(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3)
            return null;
        var millivolts = (p[0] << 8) | p[1];
        if (millivolts is < 2500 or > 5000)
            return null;
        return new HidBatteryReading(PercentFromVoltage(millivolts), Charging: (p[2] & 0x80) != 0);
    }

    /// <summary>Linear interpolation over a typical Li-ion discharge curve.</summary>
    public static int PercentFromVoltage(int millivolts) => Interpolate(VoltageCurve, millivolts);

    private static int Interpolate((int Millivolts, int Percent)[] curve, int millivolts)
    {
        if (millivolts >= curve[0].Millivolts)
            return curve[0].Percent;
        for (var i = 1; i < curve.Length; i++)
        {
            var (hiMv, hiPct) = curve[i - 1];
            var (loMv, loPct) = curve[i];
            if (millivolts >= loMv)
                return (int)Math.Round(loPct + (double)(millivolts - loMv) * (hiPct - loPct) / (hiMv - loMv));
        }
        return curve[^1].Percent;
    }

    /// <summary>0x0005 getDeviceType values.</summary>
    public static DeviceKind KindFromDeviceType(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,      // keyboard, numpad
        3 or 4 or 5 => DeviceKind.Mouse,    // mouse, trackpad, trackball
        8 => DeviceKind.Headset,
        11 or 12 => DeviceKind.Gamepad,     // joystick, gamepad
        _ => DeviceKind.Other,
    };

    /// <summary>Per-read helper that caches feature indexes for each device index.</summary>
    private sealed class Session(HidppChannel channel, CancellationToken ct)
    {
        private readonly Dictionary<(byte Index, ushort Feature), byte?> _features = [];

        public async Task<int> ProtocolVersionAsync(byte index) => (await PingAsync(index)).Version;

        /// <summary>HID++ 2.0 ping; a 1.0 device answers it with the "invalid sub id" error instead.</summary>
        public async Task<(int Version, bool LegacyDevice)> PingAsync(byte index)
        {
            var result = await channel.RequestDetailedAsync(index, RootFeatureIndex, 1, [0, 0, PingMarker], ProbeTimeout, ct);
            if (result.IsReply && result.Parameters is { Length: >= 3 } p && p[2] == PingMarker)
                return (p[0], false);
            return (0, result.IsError && result.ErrorCode == ErrorInvalidSubId);
        }

        public Task<byte[]?> ReadRegisterAsync(byte index, byte address) =>
            channel.ReadRegisterAsync(index, address, RequestTimeout, ct);

        public async Task<byte?> FeatureIndexAsync(byte index, ushort feature)
        {
            if (_features.TryGetValue((index, feature), out var cached))
                return cached;

            var reply = await channel.RequestAsync(index, RootFeatureIndex, 0, [(byte)(feature >> 8), (byte)feature], RequestTimeout, ct);
            byte? result = reply is { Length: >= 1 } && reply[0] != 0 ? reply[0] : null;
            _features[(index, feature)] = result;
            return result;
        }

        public Task<byte[]?> RequestAsync(byte index, byte featureIndex, byte function, params byte[] parameters) =>
            channel.RequestAsync(index, featureIndex, function, parameters, RequestTimeout, ct);
    }
}
