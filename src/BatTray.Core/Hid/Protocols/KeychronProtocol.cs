using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Keychron mice on their receivers or cable (VID 0x3434), four read-only variants selected by exact product id:
/// <list type="bullet">
///   <item><b>Ultra-Link 8K</b> receiver 3434:D028 and the M5 / M6 on cable (D048, D049): output report 0xB3 with
///         command 0x06 (status), answered by 0xB4 echoing 0x06; byte 20 = bit 7 charging, bits 0–6 percent.</item>
///   <item><b>1K receiver</b> 3434:D030 (Type-A) / D031 (Type-C), older M1/M2/M3/M6/M7: feature report 0x51 on usage
///         page 0x8C. SetFeature <c>51 06</c> queues "device status", GetFeature 0x51 returns
///         <c>51 06 01 00 vid:LE16 pid:LE16 … [11]=% [12]=charging</c> with the mouse's own ids.</item>
///   <item><b>4K Link</b> receiver 3434:D038 on FF0A:0001 (64-byte frames, no report id, bytes sum to 0xA1):
///         <c>41 00 81 01</c> → <c>41 .. .. 01 .. state %</c>; <c>40 00 81 00</c> returns the paired mouse's product id.</item>
///   <item><b>Nape Pro</b> trackball on the Link-KM receiver 3434:D026 or its cable 3434:0440, raw HID FF60:0061 (32-byte
///         frames, no report id): <c>B2</c> lists the receiver slots, <c>A7 31</c> returns <c>A7 31 % charging</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Sources: csutcliff/keychron-battery-dkms (keychron_battery.c), Keychron's web Launcher (launcher.keychron.com),
/// the Keychron desktop app's 1K-receiver query, github.com/nvalis/nape_pro, github.com/gtun/KeychronNapeBatteryWidget
/// and github.com/ky0209/NapeBar. Cross-checked with BatteryHub's notes (1K receiver checked there on a real M3;
/// Nape Pro checked on a real D026).
/// </remarks>
public sealed class KeychronProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3434;
    private const int VendorInterface = 4;
    private const byte CommandReport = 0xB3;
    private const byte ResponseReport = 0xB4;
    private const byte CommandStatus = 0x06;
    private const int BatteryOffset = 20;

    private static readonly HashSet<ushort> UltraLinkProducts = [0xD028, 0xD048, 0xD049];

    public const ushort LegacyReceiverTypeA = 0xD030;
    public const ushort LegacyReceiverTypeC = 0xD031;
    private const byte LegacyReport = 0x51;

    public const ushort FourKLink = 0xD038;
    private const int LauncherFrame = 64;

    public const ushort NapeReceiver = 0xD026;
    public const ushort NapeCable = 0x0440;
    private const ushort NapeInReceiverSlot = 0x4004;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(600);

    public string Name => "Keychron";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Variant(receiver) is not null;

    private enum Kind
    {
        UltraLink,
        Legacy1K,
        FourK,
        Nape,
    }

    private static (Kind Kind, HidCollectionInfo Collection)? Variant(HidReceiver receiver)
    {
        var pid = receiver.ProductId;
        HidCollectionInfo? collection;
        if (UltraLinkProducts.Contains(pid))
        {
            collection = HidTransact.FindInterface(receiver, VendorInterface, extra: c => c.UsagePage >= 0xFF00)
                         ?? receiver.Collections.FirstOrDefault(c => c.Is(0xFFC1, 0x0001) && c.OutputLength >= 21 && c.InputLength >= 21);
            return collection is null ? null : (Kind.UltraLink, collection);
        }
        if (pid is LegacyReceiverTypeA or LegacyReceiverTypeC)
        {
            collection = receiver.Collections.FirstOrDefault(c => c.UsagePage == 0x8C && c.FeatureLength >= 16);
            return collection is null ? null : (Kind.Legacy1K, collection);
        }
        if (pid == FourKLink)
        {
            collection = receiver.Collections.FirstOrDefault(c =>
                c.Is(0xFF0A, 0x0001) && c.OutputLength == LauncherFrame + 1 && c.InputLength == LauncherFrame + 1);
            return collection is null ? null : (Kind.FourK, collection);
        }
        if (pid is NapeReceiver or NapeCable)
        {
            collection = receiver.Collections.FirstOrDefault(c => c.Is(0xFF60, 0x0061) && c.OutputLength == 33 && c.InputLength >= 8);
            return collection is null ? null : (Kind.Nape, collection);
        }
        return null;
    }

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Variant(receiver) is not var (kind, collection))
            return [];

        var reading = kind switch
        {
            Kind.UltraLink => await ReadUltraLinkAsync(collection, cancellationToken),
            Kind.Legacy1K => await ReadLegacyAsync(collection, cancellationToken),
            Kind.FourK => await ReadFourKAsync(collection, cancellationToken),
            Kind.Nape => await ReadNapeAsync(collection, receiver.ProductId == NapeReceiver, cancellationToken),
            _ => null,
        };
        return reading is null ? [] : [reading];
    }

    // ---- Ultra-Link 8K ----

    private static async Task<HidBatteryReading?> ReadUltraLinkAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var frame = await HidTransact.WriteReadAsync(collection, [CommandReport, CommandStatus],
                f => f.Length > BatteryOffset && f[0] == ResponseReport && f[1] == CommandStatus,
                TimeSpan.FromMilliseconds(500), ct);
            if (frame is not null)
                return ParseReply(frame);
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
        return null;
    }

    /// <summary>Decodes a 0xB4 status reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length <= BatteryOffset || reply[0] != ResponseReport || reply[1] != CommandStatus)
            return null;
        var value = reply[BatteryOffset];
        var percent = value & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (value & 0x80) != 0, Kind: DeviceKind.Mouse);
    }

    // ---- 1K receiver (feature report 0x51) ----

    private static async Task<HidBatteryReading?> ReadLegacyAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return null;

        var request = new byte[collection.FeatureLength];
        request[0] = LegacyReport;
        request[1] = CommandStatus;
        if (!connection.SetFeature(request))
            return null;

        // The reply is usually there at once; give the mouse up to ~200 ms.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
            var reply = new byte[collection.FeatureLength];
            reply[0] = LegacyReport;
            if (connection.GetFeature(reply) && reply[1] == CommandStatus)
                return ParseLegacyStatus(reply);
        }
        return null;
    }

    /// <summary>
    /// 1K-receiver status: <c>51 06 01 ?? vid:LE16 pid:LE16 ?? ?? ?? % charging</c>. The vendor id must be Keychron's.
    /// </summary>
    public static HidBatteryReading? ParseLegacyStatus(ReadOnlySpan<byte> r)
    {
        if (r.Length < 13 || r[0] != LegacyReport || r[1] != CommandStatus || r[2] != 0x01 ||
            (r[4] | (r[5] << 8)) != VendorId || r[11] > 100)
        {
            return null;
        }
        var mousePid = (ushort)(r[6] | (r[7] << 8));
        return new HidBatteryReading(r[11], r[12] == 0x01, LegacyMouseName(mousePid), DeviceKind.Mouse);
    }

    public static string LegacyMouseName(ushort mousePid) => mousePid switch
    {
        0xD032 or 0xD033 => "Keychron M3",
        0xD035 => "Keychron M1",
        0xD036 or 0xD03E => "Keychron M3 mini",
        0xD03B => "Keychron M2",
        0xD03D => "Keychron M2 mini",
        0xD03F => "Keychron M6",
        0xD044 => "Keychron M7",
        _ => "Keychron mouse",
    };

    // ---- 4K Link (Launcher "4k" protocol) ----

    private static async Task<HidBatteryReading?> ReadFourKAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        var status = await HidTransact.WriteReadAsync(collection, BuildLauncherRequest(0x41, 0x01),
            f => IsLauncherReply(f, 0x41, 0x01), Timeout, ct);
        if (status is null || ParseFourKStatus(status) is not { } reading)
            return null;

        var info = await HidTransact.WriteReadAsync(collection, BuildLauncherRequest(0x40, 0x00),
            f => IsLauncherReply(f, 0x40, 0x00), Timeout, ct);
        var name = info is null ? null : FourKMouseName((ushort)(info[5] | (info[6] << 8)));
        return reading with { Name = name };
    }

    /// <summary>
    /// Launcher request as written on Windows: report id 0, then <c>[cmd] 00 81 [sub]</c>, zero padding, and byte 63 of
    /// the 64-byte frame making the frame sum to 0xA1.
    /// </summary>
    public static byte[] BuildLauncherRequest(byte command, byte sub)
    {
        var request = new byte[LauncherFrame + 1];
        request[1] = command;
        request[3] = 0x81;
        request[4] = sub;
        var sum = 0;
        for (var i = 1; i < LauncherFrame; i++)
            sum += request[i];
        request[LauncherFrame] = (byte)(0xA1 - sum);
        return request;
    }

    private static bool IsLauncherReply(ReadOnlySpan<byte> f, byte command, byte sub) =>
        f.Length >= 14 && f[0] == 0x00 && f[1] == command && f[4] == sub;

    /// <summary>4K status reply (byte 0 = report id 0): [1]=0x41, [4]=0x01, [6] state (≠0 charging), [7] = %.</summary>
    public static HidBatteryReading? ParseFourKStatus(ReadOnlySpan<byte> f)
    {
        if (!IsLauncherReply(f, 0x41, 0x01) || f[7] is 0 or > 100)
            return null;
        return new HidBatteryReading(f[7], f[6] != 0, Kind: DeviceKind.Mouse);
    }

    public static string? FourKMouseName(ushort productId) => productId switch
    {
        0x0620 or 0x0622 => "Keychron M3 mini 4K",
        0x0621 => "Keychron M4 4K",
        0x0624 => "Keychron M6 4K",
        0x07A0 => "Keychron M3 4K",
        _ => null,
    };

    // ---- Nape Pro ----

    private static async Task<HidBatteryReading?> ReadNapeAsync(HidCollectionInfo collection, bool throughReceiver, CancellationToken ct)
    {
        if (throughReceiver)
        {
            var slots = await HidTransact.WriteReadAsync(collection, [0x00, 0xB2], f => f.Length >= 18 && f[1] == 0xB2, Timeout, ct);
            if (slots is null || !IsNapeLinked(slots))
                return null; // no Nape Pro paired, or it is off
        }

        var reply = await HidTransact.WriteReadAsync(collection, [0x00, 0xA7, 0x31],
            f => f.Length >= 5 && f[1] == 0xA7 && f[2] == 0x31, Timeout, ct);
        return reply is null ? null : ParseNapeBattery(reply);
    }

    /// <summary>
    /// Link-KM slot list (byte 0 = report id 0): <c>B2 ?? </c> then three 5-byte slots at bytes 3, 8, 13:
    /// vid BE16, pid BE16, status (1 = connected). True when a Nape Pro (pid 0x4004) is connected.
    /// </summary>
    public static bool IsNapeLinked(ReadOnlySpan<byte> f)
    {
        if (f.Length < 18 || f[1] != 0xB2)
            return false;
        foreach (var offset in (ReadOnlySpan<int>)[3, 8, 13])
        {
            if (((f[offset + 2] << 8) | f[offset + 3]) == NapeInReceiverSlot && f[offset + 4] == 0x01)
                return true;
        }
        return false;
    }

    /// <summary>Nape Pro battery reply (byte 0 = report id 0): <c>A7 31 % charging</c>.</summary>
    public static HidBatteryReading? ParseNapeBattery(ReadOnlySpan<byte> f) =>
        f.Length >= 5 && f[1] == 0xA7 && f[2] == 0x31 && f[3] <= 100
            ? new HidBatteryReading(f[3], f[4] != 0, "Keychron Nape Pro", DeviceKind.Mouse)
            : null;
}
