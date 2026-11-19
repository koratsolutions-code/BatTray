using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// ATK / VXE mice on ATK's two "Nano" dongles, which do not speak the GetPower protocol of the other 0x373B
/// receivers: the NearLink "NK mouse NANO dongle" (373B:1216) and the Holtek "ATK 8K Nano Dongle" (373B:1178).
/// Must run before <see cref="GetPowerProtocol"/>.
/// </summary>
/// <remarks>
/// <para><b>NK NANO (1216)</b>, ATK "BITMOUSE" framing on vendor collection 0xFF05/0x0001, 64-byte output report 0x08:
/// <c>08 [sum] 72 02 00 01 [cmd] 01 00 …</c>, where [sum] is the low byte of the sum of bytes 2–63, 0x02 the request
/// length, 0x01 the target (the mouse behind the dongle) and 0x01 the expected reply length. Command 0x07 reads the
/// level, 0x0F the charging flag. Reply on the same collection: <c>08 72 [status] [seq] 00 [cmd] 01 [value]</c>;
/// status 0xFF = error, no reply = mouse asleep.
/// Sources: github.com/HeyOkay/HaloBattery (PR #350, verified on a VXE R1) and github.com/qwr79/mouse-battery.</para>
/// <para><b>8K Nano (1178)</b>, vendor collection 0xFF10/0x0001 without report ids: the 8-byte feature report 0
/// <c>82 00 …</c> (GetConfigData, read-only) makes the dongle push an input report starting with 0x82:
/// <c>82 [rate] [dpi stage] [light] [brightness] [speed] [colour] [reserved] [battery] [link]</c>, battery bit 7 =
/// charging, bits 0–6 = percent; link 0 = no mouse awake. Source: ATK HUB (v3-hub.atkgear.com, Holtek controller).</para>
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class AtkNanoDongleProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x373B;
    public const ushort NkNanoDongle = 0x1216;
    public const ushort Nano8kDongle = 0x1178;

    public const byte BitmouseReportId = 0x08;
    public const byte BitmouseMagic = 0x72;
    public const byte CommandLevel = 0x07;
    public const byte CommandCharging = 0x0F;
    public const byte GetConfigData = 0x82;

    private const int BitmouseLength = 64;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(700);

    public string Name => "ATK Nano dongle";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId &&
        (receiver.ProductId == NkNanoDongle ? FindBitmouseCollection(receiver) is not null
            : receiver.ProductId == Nano8kDongle && FindHoltekCollection(receiver) is not null);

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.VendorId != VendorId)
            return [];
        if (receiver.ProductId == NkNanoDongle && FindBitmouseCollection(receiver) is { } bitmouse)
            return await ReadBitmouseAsync(bitmouse, cancellationToken);
        if (receiver.ProductId == Nano8kDongle && FindHoltekCollection(receiver) is { } holtek)
            return await ReadHoltekAsync(holtek, cancellationToken);
        return [];
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadBitmouseAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        var level = await QueryBitmouseAsync(collection, CommandLevel, ct);
        if (level is not (>= 1 and <= 100))
            return []; // asleep / off, or no valid reply

        var charging = await QueryBitmouseAsync(collection, CommandCharging, ct);
        return [new HidBatteryReading(level, charging is null ? null : charging == 1, "ATK / VXE NK mouse", DeviceKind.Mouse)];
    }

    private static async Task<int?> QueryBitmouseAsync(HidCollectionInfo collection, byte command, CancellationToken ct)
    {
        var frame = await HidTransact.WriteReadAsync(collection, BuildBitmouseRequest(command),
            f => ParseBitmouseReply(f, command) is not null, Timeout, ct);
        return frame is null ? null : ParseBitmouseReply(frame, command);
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadHoltekAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var reports = HidConnection.OpenForReports(collection);
        using var features = HidConnection.OpenForFeatures(collection);
        if (reports is null || features is null)
            return [];

        reports.FlushInput();
        if (!features.SetFeature(BuildConfigRequest(collection.FeatureLength)))
            return [];

        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await reports.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return [];
            if (!IsConfigReply(frame))
                continue; // DPI / rate notifications and other traffic
            return ParseConfigReply(frame) is { } reading
                ? [reading with { Name = "ATK 8K Nano mouse", Kind = DeviceKind.Mouse }]
                : [new HidBatteryReading(null, Kind: DeviceKind.Mouse)]; // dongle answered, no mouse awake
        }
        return [];
    }

    // ---- Frames (public for tests) ----

    /// <summary>The BITMOUSE request for <paramref name="command"/> (64 bytes, report id 0x08 first).</summary>
    public static byte[] BuildBitmouseRequest(byte command)
    {
        var frame = new byte[BitmouseLength];
        frame[0] = BitmouseReportId;
        frame[2] = BitmouseMagic;
        frame[3] = 0x02; // request length
        frame[5] = 0x01; // target: the mouse
        frame[6] = command;
        frame[7] = 0x01; // expected reply length
        var sum = 0;
        for (var i = 2; i < frame.Length; i++)
            sum += frame[i];
        frame[1] = (byte)sum;
        return frame;
    }

    /// <summary>The one-byte value of a BITMOUSE reply to <paramref name="command"/>, or null.</summary>
    public static int? ParseBitmouseReply(ReadOnlySpan<byte> frame, byte command)
    {
        if (frame.Length < 8 || frame[0] != BitmouseReportId || frame[1] != BitmouseMagic || frame[2] == 0xFF || frame[5] != command)
            return null;
        return frame[7];
    }

    /// <summary>GetConfigData as feature report 0 (report id byte first), padded to <paramref name="featureLength"/>.</summary>
    public static byte[] BuildConfigRequest(int featureLength)
    {
        var report = new byte[Math.Max(2, featureLength)];
        report[1] = GetConfigData;
        return report;
    }

    /// <summary>True for a GetConfigData reply as read on Windows (report id 0, then 0x82, at least 10 payload bytes).</summary>
    public static bool IsConfigReply(ReadOnlySpan<byte> frame) =>
        frame.Length >= 11 && frame[0] == 0x00 && frame[1] == GetConfigData;

    /// <summary>Decodes a GetConfigData reply; null when no mouse is linked or the level is out of range.</summary>
    public static HidBatteryReading? ParseConfigReply(ReadOnlySpan<byte> frame)
    {
        if (!IsConfigReply(frame))
            return null;
        var battery = frame[1 + 8];
        var link = frame[1 + 9];
        var level = battery & 0x7F;
        if (link == 0 || level is < 1 or > 100)
            return null;
        return new HidBatteryReading(level, (battery & 0x80) != 0);
    }

    // ---- Matching ----

    private static HidCollectionInfo? FindBitmouseCollection(HidReceiver receiver) =>
        receiver.Find(0xFF05, 0x0001).FirstOrDefault(c => c.OutputLength >= BitmouseLength && c.InputLength >= 8);

    private static HidCollectionInfo? FindHoltekCollection(HidReceiver receiver) =>
        receiver.Find(0xFF10, 0x0001).FirstOrDefault(c => c.FeatureLength >= 9 && c.InputLength >= 11);
}
