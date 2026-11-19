using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Corsair wireless peripherals (VID 0x1B1C):
/// <list type="bullet">
///   <item><b>Legacy Void / HS headsets</b>: output report <c>C9 64</c>; reply report 0x64 with the
///         percentage in bits 0–6 of byte 2 and a status in byte 4.</item>
///   <item><b>Bragi</b> (newer mice, keyboards, Slipstream receivers, headsets): GET property 0x0F
///         (battery, 0–1000), 0x10 (charge state) and 0x12 (paired product id) on the vendor collection.
///         Only GET (0x02) is used; the device is never switched into software mode.</item>
///   <item><b>Compx</b> mice (Sabre V2 Pro Ultralight Wireless): 17-byte report 0x08 on FF02:0002, command 0x03
///         (link state) and 0x04 (battery), additive checksum.</item>
/// </list>
/// </summary>
/// <remarks>
/// Sources: github.com/Sapd/HeadsetControl (Void family), OpenRGB's Corsair Bragi notes (properties),
/// github.com/HeyOkay/HaloBattery (Compx framing). Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class CorsairProtocol : IHidBatteryProtocol
{
    public const ushort CorsairVendorId = 0x1B1C;

    private const byte BragiGet = 0x02;
    private const byte PropertyBattery = 0x0F;
    private const byte PropertyBatteryStatus = 0x10;
    private const byte PropertyProductId = 0x12;
    private const byte TargetSelf = 0x08;

    /// <summary>Paired-device targets behind a receiver, then the device itself.</summary>
    private static readonly byte[] BragiTargets = [0x09, 0x0A, 0x0B];

    private const byte CompxReport = 0x08;
    private const byte CompxCommandLink = 0x03;
    private const byte CompxCommandBattery = 0x04;
    private const int CompxLength = 17;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<ushort> LegacyHeadsets =
    [
        0x0A14, 0x0A16, 0x0A17, 0x0A1A, 0x0A1D, 0x0A2B, 0x0A38, 0x0A4F, 0x0A51, 0x0A52,
        0x0A55, 0x0A56, 0x0A75, 0x1B23, 0x1B27, 0x1B29, 0x1B2A, 0x1B1C,
    ];

    /// <summary>Bragi products whose vendor collection is not on usage page 0xFF42.</summary>
    private static readonly HashSet<ushort> KnownBragiProducts = [0x2A08, 0x2A02, 0x0A97, 0x0A64, 0x0A62, 0x0A3E, 0x0A3D, 0x2B00];

    /// <summary>Compx-protocol mice.</summary>
    private static readonly Dictionary<ushort, string> CompxMice = new()
    {
        [0x2B2A] = "Corsair Sabre V2 Pro Ultralight",
    };

    /// <summary>Devices recognised behind a receiver by the product id they report (property 0x12).</summary>
    private static readonly Dictionary<ushort, (string Name, DeviceKind Kind)> PairedProducts = new()
    {
        [0x1B7E] = ("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse),
    };

    public string Name => "Corsair";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == CorsairVendorId &&
        (FindLegacy(receiver) is not null || FindCompx(receiver) is not null || FindBragi(receiver) is not null);

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindLegacy(receiver) is { } legacy)
        {
            var reply = await HidTransact.WriteReadAsync(legacy, [0xC9, 0x64], f => f.Length >= 5 && f[0] == 0x64, Timeout, cancellationToken);
            return reply is not null && ParseLegacy(reply) is { } reading
                ? [reading with { Kind = DeviceKind.Headset }]
                : [];
        }

        if (FindCompx(receiver) is { } compx)
            return await ReadCompxAsync(receiver, compx, cancellationToken);

        if (FindBragi(receiver) is { } bragi)
            return await ReadBragiAsync(receiver, bragi, cancellationToken);

        return [];
    }

    // ---- Bragi ----

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadBragiAsync(HidReceiver receiver, HidCollectionInfo bragi, CancellationToken ct)
    {
        var results = new List<HidBatteryReading>();
        foreach (var target in BragiTargets)
        {
            if (await ReadBragiTargetAsync(receiver, bragi, target, ct) is { } reading)
                results.Add(reading with { SubId = target.ToString("X2") });
        }

        // Nothing paired answered: the device itself (wired, Bluetooth, or a headset with a built-in link).
        if (results.Count == 0 && await ReadBragiTargetAsync(receiver, bragi, TargetSelf, ct) is { } self)
            results.Add(self);
        return results;
    }

    private static async Task<HidBatteryReading?> ReadBragiTargetAsync(HidReceiver receiver, HidCollectionInfo bragi, byte target, CancellationToken ct)
    {
        int? level = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            level = await GetPropertyAsync(bragi, target, PropertyBattery, ct);
            if (level is null || level is > 0 and <= 1000)
                break; // a 0 right after wake-up is retried
        }
        if (level is not { } value || value > 1000)
            return null;

        var status = await GetPropertyAsync(bragi, target, PropertyBatteryStatus, ct);
        var reading = new HidBatteryReading((int)Math.Round(value / 10.0), status is null ? null : status is 1 or 3);

        DeviceKind? kind = null;
        string? name = null;
        if (target != TargetSelf && await GetPropertyAsync(bragi, target, PropertyProductId, ct) is { } pairedPid)
            (name, kind) = DescribePairedProduct((ushort)pairedPid);
        if (kind is null && (receiver.ProductId >> 8) == 0x0A)
            kind = DeviceKind.Headset; // Corsair headsets and their receivers use 0x0Axx product ids
        return reading with { Name = name, Kind = kind };
    }

    /// <summary>Maps the product id a receiver reports for its paired device (property 0x12) to a name and kind.</summary>
    public static (string? Name, DeviceKind? Kind) DescribePairedProduct(ushort productId)
    {
        if (PairedProducts.TryGetValue(productId, out var known))
            return (known.Name, known.Kind);
        return (productId >> 8) == 0x0A ? (null, DeviceKind.Headset) : (null, null);
    }

    private static async Task<int?> GetPropertyAsync(HidCollectionInfo collection, byte target, byte property, CancellationToken ct)
    {
        // Unnumbered collections take report id 0 first; numbered (64-byte) ones use report 0x02.
        byte[] request = collection.OutputLength == 64
            ? [0x02, target, BragiGet, property]
            : [0x00, target, BragiGet, property];

        // Any echo ends the wait, so an error status ("no such property / device") does not cost the full timeout.
        var reply = await HidTransact.WriteReadAsync(collection, request, f => BragiStatus(f) is not null, Timeout, ct);
        if (reply is not null)
            return ParseBragiValue(reply);

        // Older unnumbered firmware wants the 0x02 prefix after the report id and answers with it too.
        if (collection.OutputLength == 64)
            return null;
        reply = await HidTransact.WriteReadAsync(collection, BuildPrefixedRequest(target, property),
            f => ParseBragiPrefixedReply(f, target) is not null, Timeout, ct);
        return reply is null ? null : ParseBragiPrefixedReply(reply, target);
    }

    /// <summary>Prefixed GET on an unnumbered collection: <c>00 02 &lt;target&gt; 02 &lt;property&gt;</c>.</summary>
    public static byte[] BuildPrefixedRequest(byte target, byte property) => [0x00, 0x02, target, BragiGet, property];

    /// <summary>
    /// Prefixed reply on an unnumbered collection: <c>00 02 &lt;source&gt; 02 &lt;status&gt; &lt;value LE&gt;</c>, where the
    /// source is 0 for the device itself (target 0x08) and 1 for a paired device. Returns null on any mismatch.
    /// </summary>
    public static int? ParseBragiPrefixedReply(ReadOnlySpan<byte> f, byte target)
    {
        if (f.Length < 8 || f[0] != 0x00 || f[1] != 0x02 || f[3] != BragiGet || f[4] != 0x00)
            return null;
        var source = target == TargetSelf ? 0x00 : 0x01;
        return f[2] == source ? f[5] | (f[6] << 8) : null;
    }

    /// <summary>
    /// Finds the GET echo (0x02) followed by status 0 within the first bytes, and returns the little-endian
    /// value after it. Searching avoids depending on whether the collection numbers its reports.
    /// </summary>
    public static int? ParseBragiValue(ReadOnlySpan<byte> f) =>
        BragiStatus(f) is 0 ? FindBragiValue(f) : null;

    /// <summary>Status byte following the GET echo (0 = ok, 5 = unsupported…), or null when the frame is no echo.</summary>
    public static byte? BragiStatus(ReadOnlySpan<byte> f)
    {
        // The source byte before the echo is 0 or 1, so the first 0x02 in bytes 1..3 is the echo.
        for (var i = 1; i <= 3 && i + 3 < f.Length; i++)
        {
            if (f[i] == BragiGet)
                return f[i + 1];
        }
        return null;
    }

    private static int? FindBragiValue(ReadOnlySpan<byte> f)
    {
        for (var i = 1; i <= 3 && i + 3 < f.Length; i++)
        {
            if (f[i] == BragiGet && f[i + 1] == 0x00)
                return f[i + 2] | (f[i + 3] << 8);
        }
        return null;
    }

    // ---- Legacy Void ----

    /// <summary>Legacy reply: [0]=0x64, [2] bits 0–6 = %, [4] status (0 off, 1 normal, 2 low, 4/5 charging).</summary>
    public static HidBatteryReading? ParseLegacy(ReadOnlySpan<byte> f)
    {
        if (f.Length < 5 || f[0] != 0x64 || f[4] is not (1 or 2 or 4 or 5))
            return null;
        var percent = f[2] & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, f[4] is 4 or 5);
    }

    // ---- Compx ----

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadCompxAsync(HidReceiver receiver, HidCollectionInfo collection, CancellationToken ct)
    {
        var link = await CompxQueryAsync(collection, CompxCommandLink, ct);
        if (link is not null && link[6] == 0)
            return []; // receiver reports the mouse offline

        // Some firmware does not implement the link query: the battery query decides.
        var battery = await CompxQueryAsync(collection, CompxCommandBattery, ct);
        return battery is not null && ParseCompxBattery(battery) is { } reading
            ? [reading with { Name = CompxMice[receiver.ProductId], Kind = DeviceKind.Mouse }]
            : [];
    }

    private static Task<byte[]?> CompxQueryAsync(HidCollectionInfo collection, byte command, CancellationToken ct) =>
        HidTransact.WriteReadAsync(collection, BuildCompxRequest(command), f => IsCompxReply(f, command), Timeout, ct);

    /// <summary>Compx request: report 0x08, command, zero payload, byte 16 = 0x55 − sum(bytes 0..15).</summary>
    public static byte[] BuildCompxRequest(byte command)
    {
        var frame = new byte[CompxLength];
        frame[0] = CompxReport;
        frame[1] = command;
        frame[16] = CompxChecksum(frame);
        return frame;
    }

    public static byte CompxChecksum(ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += frame[i];
        return (byte)(0x55 - sum);
    }

    public static bool IsCompxReply(ReadOnlySpan<byte> f, byte command) =>
        f.Length >= CompxLength && f[0] == CompxReport && f[1] == command && f[16] == CompxChecksum(f);

    /// <summary>Compx battery reply: [6] = %, [7] ≠ 0 charging, [8..9] = millivolts.</summary>
    public static HidBatteryReading? ParseCompxBattery(ReadOnlySpan<byte> f) =>
        IsCompxReply(f, CompxCommandBattery) && f[6] <= 100 ? new HidBatteryReading(f[6], f[7] != 0) : null;

    // ---- Collections ----

    private static HidCollectionInfo? FindLegacy(HidReceiver receiver) =>
        LegacyHeadsets.Contains(receiver.ProductId)
            ? HidTransact.FindInterface(receiver, 3, 0xFFC5) ??
              receiver.Collections.FirstOrDefault(c => c.UsagePage is 0xFFC5 or 0xFF00 && c.OutputLength > 0 && c.InputLength >= 5)
            : null;

    private static HidCollectionInfo? FindCompx(HidReceiver receiver) =>
        CompxMice.ContainsKey(receiver.ProductId)
            ? receiver.Collections.FirstOrDefault(c => c.Is(0xFF02, 0x0002) && c.OutputLength >= CompxLength && c.InputLength >= CompxLength)
            : null;

    private static HidCollectionInfo? FindBragi(HidReceiver receiver)
    {
        if (CompxMice.ContainsKey(receiver.ProductId) || LegacyHeadsets.Contains(receiver.ProductId))
            return null;
        var known = KnownBragiProducts.Contains(receiver.ProductId);
        return receiver.Collections.FirstOrDefault(c => c.UsagePage == 0xFF42 && c.OutputLength >= 64 && c.InputLength > 0)
               ?? (known
                   ? receiver.Collections.FirstOrDefault(c =>
                       (c.UsagePage >= 0xFF00 || c.InterfaceNumber == 4) &&
                       c.OutputLength is 64 or 65 && c.InputLength is 64 or 65)
                   : null);
    }
}
