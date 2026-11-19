using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>ARDOR GAMING Fury V2 mouse on its 2.4 GHz receiver (1D57:FA60). Passive: nothing is ever sent.</summary>
/// <remarks>
/// The receiver's collection on usage page 0x000A (usage 0x0000) pushes 5-byte input reports
/// <c>03 00 40 [tier] [x]</c>. The low nibble of the tier byte is 3 and the high nibble encodes the level:
/// 1 = 25 %, 3 = 50 %, 7 = 75 %, F = 100 %. The fifth byte changes with the level and is not a signature.
/// Observed on hardware: <c>03 00 40 33 07</c> = 50 % and <c>03 00 40 73 08</c> = 75 %; the 25 % and 100 % codes
/// come from the vendor application's decoding. VID 0x1D57 (Xenta) and this receiver are shared with other mice,
/// so a level is only reported after two frames agree (within one listen, or with the previous poll's frame).
/// Source: an end-user capture documented by BatteryHub (notes only; no code reused).
/// </remarks>
public sealed class ArdorFuryProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1D57;
    public const ushort FuryV2Receiver = 0xFA60;
    public const ushort UsagePage = 0x000A;

    private static readonly TimeSpan ListenTime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CandidateLifetime = TimeSpan.FromMinutes(3);

    /// <summary>Last decoded but unconfirmed level per collection path, carried to the next poll.</summary>
    private static readonly ConcurrentDictionary<string, (int Percent, DateTime At)> Candidates = new();

    public string Name => "ARDOR Fury V2";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == FuryV2Receiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (!CanHandle(receiver) || FindCollection(receiver) is not { } collection)
            return [];

        int? candidate = Candidates.TryGetValue(collection.Path, out var previous) && DateTime.UtcNow - previous.At < CandidateLifetime
            ? previous.Percent
            : null;
        int? confirmed = null;

        var frame = await HidTransact.ListenAsync(collection, f =>
        {
            if (ParseReport(f) is not { } percent)
                return false;
            if (candidate == percent)
            {
                confirmed = percent;
                return true;
            }
            candidate = percent;
            return false;
        }, ListenTime, cancellationToken);

        if (candidate is { } last)
            Candidates[collection.Path] = (last, DateTime.UtcNow);

        return frame is not null && confirmed is { } level
            ? [new HidBatteryReading(level, null, "ARDOR GAMING Fury V2", DeviceKind.Mouse)]
            : [];
    }

    /// <summary>Level from a <c>03 00 40 [tier] [x]</c> report, or null for any other report.</summary>
    public static int? ParseReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 5 || report[0] != 0x03 || report[1] != 0x00 || report[2] != 0x40 || (report[3] & 0x0F) != 0x03)
            return null;
        return (report[3] >> 4) switch
        {
            0x1 => 25,
            0x3 => 50,
            0x7 => 75,
            0xF => 100,
            _ => null,
        };
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.UsagePage == UsagePage && c.Usage == 0x0000 && c.InputLength >= 5);
}
