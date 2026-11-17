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
///   <item>0x0005 Device Name &amp; Type: getCount (fn 0), getDeviceName (fn 1), getDeviceType (fn 2).</item>
/// </list>
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

        // A device plugged in directly answers HID++ 2.0 on index 0xFF; a receiver answers HID++ 1.0 errors there.
        if (await session.ProtocolVersionAsync(DirectDeviceIndex) >= 2)
        {
            if (await ReadDeviceAsync(session, DirectDeviceIndex, subId: null) is { } reading)
                results.Add(reading);
            return results;
        }

        for (byte index = 1; index <= MaxPairedDevices; index++)
        {
            if (await session.ProtocolVersionAsync(index) < 2)
                continue; // empty slot, device off/asleep, or legacy HID++ 1.0 device

            if (await ReadDeviceAsync(session, index, subId: index.ToString("X2")) is { } reading)
                results.Add(reading);
        }

        return results;
    }

    private static async Task<HidBatteryReading?> ReadDeviceAsync(Session session, byte index, string? subId)
    {
        var battery = await ReadBatteryAsync(session, index);
        if (battery is null)
            return null;

        var (name, kind) = await ReadIdentityAsync(session, index);
        return battery with { Name = name, Kind = kind, SubId = subId };
    }

    private static async Task<HidBatteryReading?> ReadBatteryAsync(Session session, byte index)
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

        return percent is null ? null : new HidBatteryReading(percent, Charging: p[2] is 1 or 2);
    }

    /// <summary>0x1000 getBatteryLevelStatus: [level %, next level %, status].</summary>
    public static HidBatteryReading? ParseBatteryStatus(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3 || p[0] is 0 or > 100)
            return null;
        // Status: 0 discharging, 1 recharging, 2 almost full, 3 full, 4 slow recharge, 5–6 errors.
        return new HidBatteryReading(p[0], Charging: p[2] is 1 or 2 or 4);
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
    public static int PercentFromVoltage(int millivolts)
    {
        if (millivolts >= VoltageCurve[0].Millivolts)
            return 100;
        for (var i = 1; i < VoltageCurve.Length; i++)
        {
            var (hiMv, hiPct) = VoltageCurve[i - 1];
            var (loMv, loPct) = VoltageCurve[i];
            if (millivolts >= loMv)
                return (int)Math.Round(loPct + (double)(millivolts - loMv) * (hiPct - loPct) / (hiMv - loMv));
        }
        return 0;
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

        public async Task<int> ProtocolVersionAsync(byte index)
        {
            var reply = await channel.RequestAsync(index, RootFeatureIndex, 1, [0, 0, PingMarker], ProbeTimeout, ct);
            return reply is { Length: >= 3 } && reply[2] == PingMarker ? reply[0] : 0;
        }

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
