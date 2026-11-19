using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Fantech Helios Go XD5 (cable 25A7:FA7B, dongle 25A7:FA7C) and Redragon Invader Pro (dongle 25A7:FA7E).
/// </summary>
/// <remarks>
/// <para>Areson / Sino Wealth firmware with a 17-byte feature report 0x08: <c>[08][cmd][0 × 14][checksum]</c>, the
/// checksum making all 17 bytes sum to 0x55. The read-only command 0x04 ("get battery", the same frame as
/// <see cref="GetPowerProtocol.BuildRequest"/>) is sent with SetFeature; the reply arrives as input report 08 on
/// another collection of the interface (some firmware also leaves it in the feature report):
/// <c>[08][04] … byte 6 = percent, byte 7 = 1 while charging</c>. The Invader Pro reports the level in tens.</para>
/// <para>Source: github.com/jarjut/xd5-go-web (docs/PROTOCOL_SPECIFICATION.md), reply layout confirmed by captures in
/// libratbag issue #1893; cross-checked with BatteryHub's notes, which report the XD5 dongle as tested on hardware.</para>
/// </remarks>
public sealed class FantechProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x25A7;
    private const ushort Wired = 0xFA7B;
    private const ushort Dongle = 0xFA7C;
    private const ushort RedragonDongle = 0xFA7E;
    private const int FrameLength = GetPowerProtocol.FrameLength;
    private const byte ReportId = 0x08;
    private const byte BatteryCommand = 0x04;

    public string Name => "Fantech (Areson)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Wired or Dongle or RedragonDongle &&
        FindFeatureCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindFeatureCollection(receiver) is not { } featureCollection)
            return [];

        var tens = receiver.ProductId == RedragonDongle;
        var readers = new List<HidConnection>();
        try
        {
            foreach (var c in receiver.Collections.Where(c => c.InputLength >= 8))
            {
                if (HidConnection.OpenForReports(c) is { } r)
                {
                    r.FlushInput();
                    readers.Add(r);
                }
            }

            using var command = HidConnection.OpenForFeatures(featureCollection);
            if (command is null || !command.SetFeature(GamingMouseHelpers.Sized(GetPowerProtocol.BuildRequest(), featureCollection.FeatureLength)))
                return [];

            Candidate? best = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            var pending = readers.ToDictionary(r => r.ReadAsync(TimeSpan.FromMilliseconds(500), cancellationToken));
            while (pending.Count > 0 && best is not { ChecksumOk: true })
            {
                var done = await Task.WhenAny(pending.Keys);
                var reader = pending[done];
                pending.Remove(done);
                var frame = await done;
                if (frame is null)
                    continue;
                if (ParseReply(frame, tens) is { } candidate)
                    best = Prefer(best, candidate);
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero)
                    pending[reader.ReadAsync(left, cancellationToken)] = reader;
            }
            await Task.WhenAll(pending.Keys); // let outstanding reads time out before the handles close

            if (best is not { ChecksumOk: true })
            {
                var feature = new byte[featureCollection.FeatureLength];
                feature[0] = ReportId;
                if (command.GetFeature(feature) && ParseReply(feature, tens) is { } fromFeature)
                    best = Prefer(best, fromFeature);
            }

            if (best is null)
                return [];
            var name = tens ? "Redragon Invader Pro" : "Fantech Helios Go XD5";
            bool? charging = receiver.ProductId == Wired || best.Reading.Charging == true;
            return [best.Reading with { Name = name, Charging = charging }];
        }
        finally
        {
            foreach (var r in readers)
                r.Dispose();
        }
    }

    /// <summary>A plausible battery reply and whether its checksum held.</summary>
    public sealed record Candidate(HidBatteryReading Reading, bool ChecksumOk);

    /// <summary>
    /// Decodes a battery reply as read on Windows (byte 0 = report id, byte 1 = 0x04). Returns null when the frame is
    /// not a plausible battery reply. <paramref name="tens"/>: the level is 1–10 (× 10 %).
    /// </summary>
    public static Candidate? ParseReply(ReadOnlySpan<byte> frame, bool tens)
    {
        if (frame.Length < 8 || frame[0] is not (ReportId or 0x00) || frame[1] != BatteryCommand)
            return null;
        int level = frame[6];
        if (frame[7] > 1 || level < 1 || level > (tens ? 10 : 100))
            return null;
        var checksumOk = GetPowerProtocol.HasValidChecksum(frame);
        return new Candidate(new HidBatteryReading(tens ? level * 10 : level, frame[7] == 1, Kind: DeviceKind.Mouse), checksumOk);
    }

    private static Candidate Prefer(Candidate? current, Candidate next) =>
        current is null || (next.ChecksumOk && !current.ChecksumOk) ? next : current;

    private static HidCollectionInfo? FindFeatureCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.FeatureLength >= FrameLength);
}
