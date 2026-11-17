using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// "GetPower" query used by receivers built on Compx/Areson and Nordic-based reference firmware
/// (Redragon, ATK, VXE, VGN, Dareu, Zaopin, Scyrox, G-Wolves…).
/// </summary>
/// <remarks>
/// Frame format (17 bytes): <c>[report id] [command] … [checksum]</c>, where the checksum makes the sum
/// of all 17 bytes equal 0x55 (mod 256). The request is report 0x08, command 0x04 (GetPower), which
/// is read-only. Two transports exist:
/// <list type="bullet">
///   <item><b>Areson</b>: request sent as a feature report on the vendor collection 0xFF02/0x0002, reply
///         (report 0x09) read from the input collection 0xFF01/0x0000; byte 6 = level 0–10 (×10 %).</item>
///   <item><b>Nordic</b>: request written as an output report on 0xFF02/0x0002 (or 0xFF04/0x0002) and the
///         reply (report 0x08) read from the same collection; byte 6 = percent, byte 7 = cable/charging.</item>
/// </list>
/// Only receivers listed in <see cref="Models"/> are ever queried.
/// </remarks>
public sealed class GetPowerProtocol : IHidBatteryProtocol
{
    public const int FrameLength = 17;
    public const byte Checksum = 0x55;
    public const byte RequestReportId = 0x08;
    public const byte AresonReplyReportId = 0x09;
    public const byte GetPowerCommand = 0x04;

    private static readonly TimeSpan AresonTimeout = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan NordicTimeout = TimeSpan.FromMilliseconds(1000);

    public enum Transport
    {
        Areson,
        Nordic,
    }

    /// <param name="ProductId">null matches every product of the vendor.</param>
    /// <param name="Name">Model name, or null to use the USB product string.</param>
    public sealed record Model(ushort VendorId, ushort? ProductId, Transport Transport, string? Name);

    /// <summary>Known receivers (wireless dongle and, where listed, the wired/charging cable PID).</summary>
    public static IReadOnlyList<Model> Models { get; } =
    [
        // Compx / Areson
        new(0x25A7, 0xFA07, Transport.Areson, "Redragon wireless mouse"),
        new(0x25A7, 0xFA08, Transport.Areson, "Redragon wireless mouse"),

        // ATK / VXE (0x373B)
        new(0x373B, 0x1031, Transport.Nordic, "ATK F1 Ultimate"),
        new(0x373B, 0x102E, Transport.Nordic, "ATK F1 Ultimate"),
        new(0x373B, 0x11D9, Transport.Nordic, "ATK A9 Ultimate"),
        new(0x373B, 0x11B6, Transport.Nordic, "ATK A9 Ultimate"),
        new(0x373B, 0x104D, Transport.Nordic, "VXE MAD R"),
        new(0x373B, 0x103F, Transport.Nordic, "VXE MAD R"),
        new(0x373B, 0x1040, Transport.Nordic, "VXE MAD R Major Plus"),
        new(0x373B, 0x104C, Transport.Nordic, "VXE MAD R Major Plus"),
        new(0x373B, 0x1085, Transport.Nordic, "VXE R1 SE+"),
        new(0x373B, null, Transport.Nordic, null),

        // VXE / VGN / Zaopin / Scyrox (0x3554)
        new(0x3554, 0xF58A, Transport.Nordic, "VXE R1 Pro Max"),
        new(0x3554, 0xF58C, Transport.Nordic, "VXE R1 Pro Max"),
        new(0x3554, 0xF58E, Transport.Nordic, "VXE R1 SE+"),
        new(0x3554, 0xF58F, Transport.Nordic, "VXE R1 SE+"),
        new(0x3554, 0xF503, Transport.Nordic, "VGN F1 Pro"),
        new(0x3554, 0xF502, Transport.Nordic, "VGN F1 Pro"),
        new(0x3554, 0xFB3E, Transport.Nordic, "VGN F2 Pro Max"),
        new(0x3554, 0xFB3D, Transport.Nordic, "VGN F2 Pro Max"),
        new(0x3554, 0xF524, Transport.Nordic, "Zaopin Z2 Mini"),
        new(0x3554, 0xF526, Transport.Nordic, "Zaopin Z2 Mini"),
        new(0x3554, 0xF5F7, Transport.Nordic, "Scyrox V8"),
        new(0x3554, 0xF5F6, Transport.Nordic, "Scyrox V8"),

        // Dareu (0x260D)
        new(0x260D, 0x1175, Transport.Nordic, "Dareu A950 Air"),
        new(0x260D, 0x1193, Transport.Nordic, "Dareu A950 Air"),
        new(0x260D, 0x1154, Transport.Nordic, "Dareu A950 Wing"),
        new(0x260D, 0x1196, Transport.Nordic, "Dareu A950 Wing"),

        // G-Wolves (0x33E4)
        new(0x33E4, 0x3854, Transport.Nordic, "G-Wolves Lycan"),
        new(0x33E4, 0x4719, Transport.Nordic, "G-Wolves Lycan"),
    ];

    public string Name => "GetPower (Compx/Areson/Nordic)";

    public bool CanHandle(HidReceiver receiver) => FindModel(receiver) is { } model && FindRequestCollection(receiver, model) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model || FindRequestCollection(receiver, model) is not { } request)
            return [];

        var reading = model.Transport == Transport.Areson
            ? await ReadAresonAsync(receiver, request, cancellationToken)
            : await ReadNordicAsync(request, cancellationToken);

        return reading is null ? [] : [reading with { Name = model.Name, Kind = DeviceKind.Mouse }];
    }

    // ---- Frames (public for tests) ----

    /// <summary>Builds the GetPower request, zero-padded to <paramref name="length"/> (≥ 17).</summary>
    public static byte[] BuildRequest(int length = FrameLength)
    {
        var frame = new byte[Math.Max(FrameLength, length)];
        frame[0] = RequestReportId;
        frame[1] = GetPowerCommand;
        frame[FrameLength - 1] = ComputeChecksum(frame.AsSpan(0, FrameLength - 1));
        return frame;
    }

    /// <summary>Value of the last byte so that the 17-byte frame sums to 0x55.</summary>
    public static byte ComputeChecksum(ReadOnlySpan<byte> firstSixteen)
    {
        var sum = 0;
        foreach (var b in firstSixteen)
            sum += b;
        return unchecked((byte)(Checksum - sum));
    }

    public static bool HasValidChecksum(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < FrameLength)
            return false;
        var sum = 0;
        foreach (var b in frame[..FrameLength])
            sum += b;
        return (byte)sum == Checksum;
    }

    /// <summary>Parses an Areson reply: <c>09 04 00 …</c>, byte 6 = level 0–10, byte 7 = charging.</summary>
    public static HidBatteryReading? ParseAresonReply(ReadOnlySpan<byte> frame)
    {
        if (!HasValidChecksum(frame) || frame[0] != AresonReplyReportId || frame[1] != GetPowerCommand || frame[2] != 0 || frame[6] > 10)
            return null;
        return new HidBatteryReading(frame[6] * 10, frame[7] != 0);
    }

    /// <summary>Parses a Nordic reply: <c>08 04 …</c>, byte 6 = percent (0 = asleep), byte 7 = cable connected.</summary>
    public static HidBatteryReading? ParseNordicReply(ReadOnlySpan<byte> frame)
    {
        if (!HasValidChecksum(frame) || frame[0] != RequestReportId || frame[1] != GetPowerCommand)
            return null;
        var percent = frame[6];
        if (percent is 0 or > 100)
            return null;
        var wired = frame[7] != 0;
        return new HidBatteryReading(percent, wired && percent < 100);
    }

    // ---- Transports ----

    private static async Task<HidBatteryReading?> ReadAresonAsync(HidReceiver receiver, HidCollectionInfo request, CancellationToken ct)
    {
        var replyCollection = receiver.Find(0xFF01, 0x0000).FirstOrDefault(c => c.InputLength >= FrameLength);
        if (replyCollection is null)
            return null;

        using var reply = HidConnection.OpenForReports(replyCollection);
        using var feature = HidConnection.OpenForFeatures(request);
        if (reply is null || feature is null)
            return null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(200, ct);

            reply.FlushInput();
            if (!feature.SetFeature(BuildRequest(request.FeatureLength)))
                return null;

            var parsed = await ReadUntilAsync(reply, AresonTimeout, ParseAresonReply, ct);
            if (parsed is not null)
                return parsed;
        }

        return null;
    }

    private static async Task<HidBatteryReading?> ReadNordicAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        connection.FlushInput();
        if (!await connection.WriteAsync(BuildRequest(collection.OutputLength), ct))
            return null;

        return await ReadUntilAsync(connection, NordicTimeout, ParseNordicReply, ct);
    }

    private delegate HidBatteryReading? FrameParser(ReadOnlySpan<byte> frame);

    /// <summary>Reads input reports until one parses or the deadline passes (other traffic is ignored).</summary>
    private static async Task<HidBatteryReading?> ReadUntilAsync(HidConnection connection, TimeSpan timeout, FrameParser parse, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (parse(frame) is { } reading)
                return reading;
        }
        return null;
    }

    // ---- Matching ----

    private static Model? FindModel(HidReceiver receiver) =>
        Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId == receiver.ProductId)
        ?? Models.FirstOrDefault(m => m.VendorId == receiver.VendorId && m.ProductId is null);

    private static HidCollectionInfo? FindRequestCollection(HidReceiver receiver, Model model) =>
        model.Transport == Transport.Areson
            ? receiver.Find(0xFF02, 0x0002).FirstOrDefault(c => c.FeatureLength >= FrameLength)
            : receiver.Find(0xFF02, 0x0002).Concat(receiver.Find(0xFF04, 0x0002))
                .FirstOrDefault(c => c.OutputLength >= FrameLength && c.InputLength >= FrameLength);
}
