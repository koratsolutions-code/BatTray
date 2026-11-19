using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Mice configured with Ninjutso's NinjaForce (and the same firmware family in Sprime's web driver):
/// Ninjutso Sora V2 and Sprime PM1 (Nordic VID 0x1915), Ninjutso Ten / Ten Air (VID 0x093A).
/// </summary>
/// <remarks>
/// <para><b>Sora V2 / Sprime PM1</b>: vendor collection 0xFFA0/0x0001, feature report 0x05 (32 bytes). Status query
/// <c>05 15 00 00 01 00 …</c> with SetFeature, then GetFeature report 0x05: byte 9 = percent, byte 10 = charging,
/// byte 11 = fully charged (PM1), byte 12 = mouse online (receiver only; the cable answers 0 there). The Sora V2 reports
/// a full battery as 0 %. Sources: NinjaForce's renderer bundle (createSoraV2, readState) and Sprime's web driver
/// (sprime.pro); verified on hardware for the Sora V2 receiver and cable and the PM1 receiver AC1C.</para>
/// <para><b>Ten / Ten Air</b>: vendor collection 0xFF01/0x0001, feature report 0x06 (16 bytes):
/// <c>06 [cmd] 00 00 01 00 …</c>, reply <c>06 [cmd] …</c> with the value at byte 8. Commands: 0x13 = online,
/// 0x12 = percent, 0x11 = charging. Sources: NinjaForce and github.com/DrStopor/ninjutso-battery-tray; verified on a
/// Ten Air (receiver and cable).</para>
/// VIDs 0x1915 (Nordic) and 0x093A (PixArt) are shared by many products, so only the listed ids are queried.
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class NinjaForceProtocol : IHidBatteryProtocol
{
    public const byte StatusReportId = 0x05;
    public const byte StatusCommand = 0x15;
    public const byte TenReportId = 0x06;
    public const byte TenOnline = 0x13;
    public const byte TenPercent = 0x12;
    public const byte TenCharging = 0x11;

    private const int StatusReportLength = 32;
    private const int TenReportLength = 16;

    public enum Family
    {
        SoraV2,
        SprimePm1,
        Ten,
    }

    public sealed record Model(ushort VendorId, ushort ProductId, Family Family, bool Cable, string Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x1915, 0xAE1C, Family.SoraV2, false, "Ninjutso Sora V2"),
        new(0x1915, 0xAE11, Family.SoraV2, true, "Ninjutso Sora V2"),
        new(0x1915, 0xAC1C, Family.SprimePm1, false, "Sprime PM1"),
        new(0x1915, 0xAD8C, Family.SprimePm1, false, "Sprime PM1"),
        new(0x1915, 0xAC11, Family.SprimePm1, true, "Sprime PM1"),
        new(0x1915, 0xAC12, Family.SprimePm1, true, "Sprime PM1"),
        new(0x1915, 0xAC13, Family.SprimePm1, true, "Sprime PM1"),
        new(0x093A, 0xEB01, Family.Ten, false, "Ninjutso Ten"),
        new(0x093A, 0xE020, Family.Ten, true, "Ninjutso Ten"),
    ];

    public string Name => "NinjaForce (Ninjutso / Sprime)";

    public bool CanHandle(HidReceiver receiver) => FindModel(receiver) is { } model && FindCollection(receiver, model) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model || FindCollection(receiver, model) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var reading = model.Family == Family.Ten
            ? await ReadTenAsync(connection, cancellationToken)
            : await ReadStatusAsync(connection, model, cancellationToken);
        if (reading is null)
            return [];

        var name = model.Family == Family.Ten && !string.IsNullOrWhiteSpace(receiver.Product) ? receiver.Product : model.Name;
        return [reading with { Name = name, Kind = DeviceKind.Mouse }];
    }

    // ---- Sora V2 / Sprime PM1 ----

    private static async Task<HidBatteryReading?> ReadStatusAsync(HidConnection connection, Model model, CancellationToken ct)
    {
        var length = Math.Max(StatusReportLength, connection.Collection.FeatureLength);
        HidBatteryReading? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(90, ct);
            if (!connection.SetFeature(BuildStatusRequest(length)))
                break;
            await Task.Delay(15, ct);

            var reply = new byte[length];
            reply[0] = StatusReportId;
            if (!connection.GetFeature(reply))
                continue;
            if (ParseStatusReply(reply, model.Family, model.Cable) is not { } reading)
                continue;
            last = reading;
            if (reading.Percent is not null)
                break; // online: a sleeping mouse gets a few more tries
        }
        return last;
    }

    public static byte[] BuildStatusRequest(int length = StatusReportLength)
    {
        var report = new byte[Math.Max(StatusReportLength, length)];
        report[0] = StatusReportId;
        report[1] = StatusCommand;
        report[4] = 0x01;
        return report;
    }

    /// <summary>
    /// Decodes a status reply; a reading with a null percent means the receiver answered but the mouse is offline.
    /// Returns null when the frame is not a valid status reply.
    /// </summary>
    public static HidBatteryReading? ParseStatusReply(ReadOnlySpan<byte> reply, Family family, bool cable)
    {
        if (reply.Length < 13 || reply[0] != StatusReportId || reply[1] != StatusCommand || reply[9] > 100)
            return null;

        var online = cable || reply[12] != 0;
        if (!online)
            return new HidBatteryReading(null);

        if (family == Family.SprimePm1)
        {
            var full = reply[11] != 0;
            return new HidBatteryReading(full ? 100 : reply[9], reply[10] != 0 && !full);
        }
        return new HidBatteryReading(reply[9] == 0 ? 100 : reply[9], reply[10] != 0); // Sora V2: 0 means full
    }

    // ---- Ten / Ten Air ----

    private static async Task<HidBatteryReading?> ReadTenAsync(HidConnection connection, CancellationToken ct)
    {
        var online = await QueryTenAsync(connection, TenOnline, ct);
        if (online is null)
            return null;
        if (online != 1)
            return new HidBatteryReading(null);

        var percent = await QueryTenAsync(connection, TenPercent, ct);
        if (percent is not (>= 0 and <= 100))
            return null;
        var charging = await QueryTenAsync(connection, TenCharging, ct);
        return new HidBatteryReading(percent, charging is null ? null : charging != 0);
    }

    private static async Task<int?> QueryTenAsync(HidConnection connection, byte command, CancellationToken ct)
    {
        if (!connection.SetFeature(BuildTenRequest(command)))
            return null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(30, ct);
            var reply = new byte[TenReportLength];
            reply[0] = TenReportId;
            if (connection.GetFeature(reply) && ParseTenReply(reply, command) is { } value)
                return value;
        }
        return null;
    }

    public static byte[] BuildTenRequest(byte command)
    {
        var report = new byte[TenReportLength];
        report[0] = TenReportId;
        report[1] = command;
        report[4] = 0x01;
        return report;
    }

    /// <summary>The value byte of a 16-byte reply to <paramref name="command"/>, or null.</summary>
    public static int? ParseTenReply(ReadOnlySpan<byte> reply, byte command) =>
        reply.Length == TenReportLength && reply[0] == TenReportId && reply[1] == command ? reply[8] : null;

    // ---- Matching ----

    private static Model? FindModel(HidReceiver receiver) =>
        Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId == receiver.ProductId);

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Model model) =>
        model.Family == Family.Ten
            ? receiver.Find(0xFF01, 0x0001).FirstOrDefault(c => c.FeatureLength == TenReportLength)
            : receiver.Find(0xFFA0, 0x0001).FirstOrDefault(c => c.FeatureLength >= StatusReportLength);
}
