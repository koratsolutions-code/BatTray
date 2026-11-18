using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>
/// Sony DualShock 4 and DualSense controllers over USB or Bluetooth. The battery is part of the regular
/// input report, so nothing is sent to the controller: reports are only listened to. Over Bluetooth the
/// controller starts with a short report without battery; reading feature report 0x05 (a read) makes it
/// switch to the extended report.
/// </summary>
public sealed class SonyControllerBatteryProvider : IBatteryProvider
{
    public const ushort SonyVendorId = 0x054C;

    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMilliseconds(600);

    private static readonly Dictionary<ushort, (string Name, bool DualSense)> Products = new()
    {
        [0x05C4] = ("DualShock 4", false),
        [0x09CC] = ("DualShock 4", false),
        [0x0BA0] = ("DualShock 4 (wireless adapter)", false),
        [0x0CE6] = ("DualSense", true),
        [0x0DF2] = ("DualSense Edge", true),
    };

    private readonly TimeProvider _time;

    public SonyControllerBatteryProvider(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public string Name => "PlayStation controller";

    /// <summary>Connections are picked up by the regular poll and by the Bluetooth/HID watchers of other providers.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var controllers = await Task.Run(() => HidEnumerator.EnumerateCollections()
            .Where(c => c.VendorId == SonyVendorId && Products.ContainsKey(c.ProductId) && c.Is(0x01, 0x05))
            .ToList(), cancellationToken);

        var now = _time.GetUtcNow();
        var result = new List<Device>();
        foreach (var collection in controllers)
        {
            var (name, dualSense) = Products[collection.ProductId];
            var bluetooth = collection.InstanceId.StartsWith(@"HID\{", StringComparison.OrdinalIgnoreCase);

            var reading = await ReadAsync(collection, dualSense, bluetooth, cancellationToken);
            if (reading?.Percent is not { } percent)
                continue;

            var id = $"sony:{(collection.ContainerId is { } c ? c.ToString("N") : collection.InstanceId)}";
            result.Add(new Device(id, name, DeviceKind.Gamepad, percent, now,
                $"{Name} ({(bluetooth ? "Bluetooth" : "USB")})", reading.Charging));
        }
        return result;
    }

    private static async Task<HidBatteryReading?> ReadAsync(HidCollectionInfo collection, bool dualSense, bool bluetooth, CancellationToken ct)
    {
        Func<byte[], bool> hasBattery = f => Parse(f, dualSense) is not null || IsAdapterWithoutController(f, collection.ProductId);

        var frame = await HidTransact.ListenAsync(collection, hasBattery, ListenTimeout, ct);
        if (frame is null && bluetooth && collection.FeatureLength >= 41)
        {
            // Read-only feature report 0x05 switches Bluetooth controllers to the extended input report.
            using (var connection = HidConnection.OpenForFeatures(collection))
            {
                var feature = new byte[collection.FeatureLength];
                feature[0] = 0x05;
                connection?.GetFeature(feature);
            }
            frame = await HidTransact.ListenAsync(collection, hasBattery, ListenTimeout, ct);
        }

        return frame is null || IsAdapterWithoutController(frame, collection.ProductId) ? null : Parse(frame, dualSense);
    }

    /// <summary>The DualShock 4 USB wireless adapter reports byte 31 bit 2 when no controller is paired/on.</summary>
    private static bool IsAdapterWithoutController(byte[] f, ushort productId) =>
        productId == 0x0BA0 && f.Length >= 32 && f[0] == 0x01 && (f[31] & 0x04) != 0;

    /// <summary>
    /// Parses an input report. DualShock 4: USB report 0x01 (64 bytes) status at [30], Bluetooth 0x11 at [32];
    /// DualSense: USB 0x01 (64 bytes) at [53], Bluetooth 0x31 at [54]. Short Bluetooth 0x01 reports carry no battery.
    /// </summary>
    public static HidBatteryReading? Parse(ReadOnlySpan<byte> f, bool dualSense)
    {
        if (f.Length < 2)
            return null;

        int? statusIndex = (dualSense, f[0]) switch
        {
            (false, 0x01) when f.Length >= 64 => 30,
            (false, 0x11) when f.Length >= 33 => 32,
            (true, 0x01) when f.Length >= 64 => 53,
            (true, 0x31) when f.Length >= 55 => 54,
            _ => null,
        };
        if (statusIndex is not { } index)
            return null;

        var status = f[index];
        return dualSense ? ParseDualSenseStatus(status) : ParseDualShock4Status(status);
    }

    /// <summary>Bits 0–3 = level, bit 4 = cable connected.</summary>
    public static HidBatteryReading? ParseDualShock4Status(byte status)
    {
        var level = status & 0x0F;
        var cable = (status & 0x10) != 0;
        if (!cable)
            return new HidBatteryReading(Math.Min(level * 10 + 5, 100), false);
        return level switch
        {
            <= 9 => new HidBatteryReading(level * 10 + 5, true),
            10 => new HidBatteryReading(100, true),
            11 => new HidBatteryReading(100, false), // full
            _ => null,                               // error / unknown
        };
    }

    /// <summary>Low nibble = level 0–10, high nibble = 0 discharging, 1 charging, 2 full, others error.</summary>
    public static HidBatteryReading? ParseDualSenseStatus(byte status)
    {
        var level = status & 0x0F;
        return (status >> 4) switch
        {
            0 => new HidBatteryReading(Math.Min(level * 10 + 5, 100), false),
            1 => new HidBatteryReading(Math.Min(level * 10 + 5, 100), true),
            2 => new HidBatteryReading(100, false),
            _ => null,
        };
    }

    public void Dispose()
    {
    }
}
