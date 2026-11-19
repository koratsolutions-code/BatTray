using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Pulsar mice built on a Sonix controller (VID 0x3710): X2 V3 and X2 V3 eS on their USB cable, and the shared
/// 1K / 8K dongles (also used by the Xlite V4 and X3).
/// </summary>
/// <remarks>
/// Transport: 64-byte feature report 0 on a vendor collection (Windows length 65 with the report id).
/// Packet layout: <c>[0] = 00, [1] = category, [2] = register, [3] = sub-register, …, [62..63] = 16-bit sum</c>
/// of packet bytes 0–61, little endian. The battery register read is <c>08 81 01</c> (read-only). The host sends
/// it with SetFeature and then polls GetFeature until the reply echoes <c>08 81 01</c>; packet byte 0 is the status
/// (01 = answered, 02 = still busy, 05 = busy or mouse not linked depending on the source) and byte 6 the percent
/// (0xFF = no value). The dongle answers 0 % when the mouse is off. There is no charging register; on the cable the
/// mouse is charging.
/// Sources: github.com/darthsoup/PulsarBattery and github.com/bluesceada/pulsar-mouse-linux; cross-checked with
/// BatteryHub's notes. The newer XS-1 generation uses the GetPower protocol instead.
/// </remarks>
public sealed class PulsarSonixProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3710;
    public const int PacketLength = 64;
    public const int ReportLength = PacketLength + 1;
    private const byte StatusAnswered = 0x01;
    private const byte StatusNotLinked = 0x05;

    private static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(50);

    /// <param name="Name">Model name, or null for a dongle shared by several models.</param>
    public sealed record Model(ushort ProductId, bool Cable, string? Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x3402, true, "Pulsar X2 V3"),
        new(0x3406, true, "Pulsar X2 V3 eS"),
        new(0x5402, false, null), // 1K dongle
        new(0x5403, false, null), // 8K dongle
    ];

    public string Name => "Pulsar (Sonix)";

    public bool CanHandle(HidReceiver receiver) => FindModel(receiver) is not null && FindCollections(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model)
            return [];

        // The vendor collection appears on two interfaces and only one accepts SetFeature: try the higher one first.
        foreach (var collection in FindCollections(receiver).OrderByDescending(c => c.InterfaceNumber ?? -1))
        {
            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            var outcome = await QueryAsync(connection, cancellationToken);
            if (outcome is null)
                continue;

            var name = model.Name ?? DongleName(receiver);
            if (model.Cable)
                return [new HidBatteryReading(outcome, true, name, DeviceKind.Mouse)];
            // 0 % (or "not linked") from a dongle means the mouse is off or out of range.
            return [new HidBatteryReading(outcome > 0 ? outcome : null, null, name, DeviceKind.Mouse)];
        }
        return [];
    }

    /// <summary>Returns the percent, 0 when the dongle says the mouse is not linked, or null when nothing answered.</summary>
    private static async Task<int?> QueryAsync(HidConnection connection, CancellationToken ct)
    {
        var request = BuildRequest();
        var notLinked = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!connection.SetFeature(request))
                return null;

            for (var read = 0; read < 6; read++)
            {
                await Task.Delay(ReadInterval, ct);
                var reply = new byte[ReportLength];
                if (!connection.GetFeature(reply))
                    return null;

                var status = ReplyStatus(reply);
                if (status is null)
                    continue;
                if (status == StatusAnswered)
                    return ParseReply(reply);
                notLinked = status == StatusNotLinked;
            }
        }
        return notLinked ? 0 : null;
    }

    // ---- Frames (public for tests) ----

    /// <summary>The battery read request as sent with SetFeature: report id 0, then the 64-byte packet.</summary>
    public static byte[] BuildRequest()
    {
        var report = new byte[ReportLength];
        var packet = report.AsSpan(1);
        packet[1] = 0x08;
        packet[2] = 0x81;
        packet[3] = 0x01;
        var sum = PacketSum(packet);
        packet[62] = (byte)(sum & 0xFF);
        packet[63] = (byte)(sum >> 8);
        return report;
    }

    /// <summary>16-bit sum of packet bytes 0–61.</summary>
    public static ushort PacketSum(ReadOnlySpan<byte> packet)
    {
        var sum = 0;
        foreach (var b in packet[..62])
            sum += b;
        return (ushort)sum;
    }

    /// <summary>The status byte of a reply that echoes the battery register, or null for any other report.</summary>
    public static byte? ReplyStatus(ReadOnlySpan<byte> report)
    {
        if (report.Length < ReportLength || report[0] != 0)
            return null;
        var packet = report[1..];
        return packet[1] == 0x08 && packet[2] == 0x81 && packet[3] == 0x01 ? packet[0] : null;
    }

    /// <summary>Percent from an answered reply (report id 0 first), or null when it is not one or has no value.</summary>
    public static int? ParseReply(ReadOnlySpan<byte> report)
    {
        if (ReplyStatus(report) != StatusAnswered)
            return null;
        var percent = report[1 + 6];
        return percent <= 100 ? percent : null;
    }

    // ---- Matching ----

    private static Model? FindModel(HidReceiver receiver) =>
        receiver.VendorId != VendorId ? null : Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver) =>
        receiver.Collections.Where(c => c.UsagePage >= 0xFF00 && c.FeatureLength >= ReportLength);

    private static string DongleName(HidReceiver receiver)
    {
        var product = receiver.Product ?? string.Empty;
        var generic = product.Length == 0 ||
                      product.Contains("Dongle", StringComparison.OrdinalIgnoreCase) ||
                      product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
        return generic ? "Pulsar wireless mouse" : product;
    }
}
