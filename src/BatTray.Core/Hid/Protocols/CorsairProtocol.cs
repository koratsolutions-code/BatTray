using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Corsair wireless peripherals (VID 0x1B1C):
/// <list type="bullet">
///   <item><b>Legacy Void / HS headsets</b>: output report <c>C9 64</c>; reply report 0x64 with the
///         percentage in bits 0–6 of byte 2 and a status in byte 4.</item>
///   <item><b>Bragi</b> (newer mice, keyboards, Slipstream receivers, headsets): GET property 0x0F
///         (battery, 0–1000) and 0x10 (status) on the 0xFF42 vendor collection. Only GET (0x02) is used.</item>
/// </list>
/// </summary>
public sealed class CorsairProtocol : IHidBatteryProtocol
{
    public const ushort CorsairVendorId = 0x1B1C;

    private const byte BragiGet = 0x02;
    private const byte PropertyBattery = 0x0F;
    private const byte PropertyBatteryStatus = 0x10;
    private const byte TargetSelf = 0x08;
    private const byte TargetPairedDevice = 0x09;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<ushort> LegacyHeadsets =
    [
        0x0A14, 0x0A16, 0x0A17, 0x0A1A, 0x0A1D, 0x0A2B, 0x0A38, 0x0A4F, 0x0A51, 0x0A52,
        0x0A55, 0x0A56, 0x0A75, 0x1B23, 0x1B27, 0x1B29, 0x1B2A, 0x1B1C,
    ];

    public string Name => "Corsair";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == CorsairVendorId && (FindLegacy(receiver) is not null || FindBragi(receiver) is not null);

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindLegacy(receiver) is { } legacy)
        {
            var reply = await HidTransact.WriteReadAsync(legacy, [0xC9, 0x64], f => f.Length >= 5 && f[0] == 0x64, Timeout, cancellationToken);
            return reply is not null && ParseLegacy(reply) is { } reading
                ? [reading with { Kind = DeviceKind.Headset }]
                : [];
        }

        if (FindBragi(receiver) is { } bragi)
        {
            foreach (var target in new[] { TargetPairedDevice, TargetSelf })
            {
                var level = await GetPropertyAsync(bragi, target, PropertyBattery, cancellationToken);
                if (level is not { } value || value is 0 or > 1000)
                    continue;

                var status = await GetPropertyAsync(bragi, target, PropertyBatteryStatus, cancellationToken);
                return [new HidBatteryReading((int)Math.Round(value / 10.0), status is null ? null : status == 1)];
            }
        }

        return [];
    }

    private static async Task<int?> GetPropertyAsync(HidCollectionInfo collection, byte target, byte property, CancellationToken ct)
    {
        // Unnumbered collections take report id 0 first; numbered (64-byte) ones use report 0x02.
        byte[] request = collection.OutputLength == 64
            ? [0x02, target, BragiGet, property]
            : [0x00, target, BragiGet, property];

        var reply = await HidTransact.WriteReadAsync(collection, request, f => ParseBragiValue(f) is not null, Timeout, ct);
        return reply is null ? null : ParseBragiValue(reply);
    }

    /// <summary>
    /// Finds the GET echo (0x02) followed by status 0 within the first bytes, and returns the little-endian
    /// value after it. Searching avoids depending on whether the collection numbers its reports.
    /// </summary>
    public static int? ParseBragiValue(ReadOnlySpan<byte> f)
    {
        for (var i = 1; i <= 3 && i + 3 < f.Length; i++)
        {
            if (f[i] == BragiGet && f[i + 1] == 0x00)
                return f[i + 2] | (f[i + 3] << 8);
        }
        return null;
    }

    /// <summary>Legacy reply: [0]=0x64, [2] bits 0–6 = %, [4] status (0 off, 1 normal, 2 low, 4/5 charging).</summary>
    public static HidBatteryReading? ParseLegacy(ReadOnlySpan<byte> f)
    {
        if (f.Length < 5 || f[0] != 0x64 || f[4] == 0)
            return null;
        var percent = f[2] & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, f[4] is 4 or 5);
    }

    private static HidCollectionInfo? FindLegacy(HidReceiver receiver) =>
        LegacyHeadsets.Contains(receiver.ProductId) ? HidTransact.FindInterface(receiver, 3, 0xFFC5) : null;

    private static HidCollectionInfo? FindBragi(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.UsagePage == 0xFF42 && c.OutputLength >= 64 && c.InputLength > 0);
}
