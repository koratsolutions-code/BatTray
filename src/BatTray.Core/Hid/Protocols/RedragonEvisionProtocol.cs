using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Redragon wireless mice (K1NG M999) on the Evision 2.4 GHz receiver 320F:226F.</summary>
/// <remarks>
/// Vendor page 0xFF1C / usage 0x0092, report id 0x04. Request (output report): <c>04 [seq lo] [seq hi] 1A 02 00 …</c>,
/// where 0x1A is the read-only battery command and the sequence number is echoed. Reply (input report):
/// <c>04 [seq lo] [seq hi] 1A … 00 00 … [percent] [state]</c> with bytes 5–6 zero, byte 8 the percent and byte 9 the
/// state (0 on battery, 1 or 2 charging). With the USB cable in, the receiver keeps repeating the last level.
/// Source: a USB capture of the vendor software on a Redragon M999 (tested on hardware according to the reference),
/// same Evision battery command as the ATK HUB driver documented in github.com/HeyOkay/HaloBattery; cross-checked
/// with BatteryHub's notes. The receiver id may be shared by other Redragon models, so no model name is given.
/// </remarks>
public sealed class RedragonEvisionProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x320F;
    public const ushort Receiver = 0x226F;
    public const byte ReportId = 0x04;
    public const byte BatteryCommand = 0x1A;
    private const int RequestLength = 32;

    private static int _sequence;

    public string Name => "Redragon (Evision)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == Receiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        var sequence = (ushort)(Interlocked.Increment(ref _sequence) % 0xFFFF + 1);
        var request = BuildRequest(sequence);
        if (collection.OutputLength < request.Length)
            request = request[..collection.OutputLength]; // only zero padding is dropped
        var frame = await HidTransact.WriteReadAsync(collection, request,
            f => ParseReply(f, sequence) is not null, TimeSpan.FromSeconds(1), cancellationToken);
        return frame is not null && ParseReply(frame, sequence) is { } reading ? [reading] : [];
    }

    /// <summary>The battery request with sequence number <paramref name="sequence"/>.</summary>
    public static byte[] BuildRequest(ushort sequence)
    {
        var request = new byte[RequestLength];
        request[0] = ReportId;
        request[1] = (byte)sequence;
        request[2] = (byte)(sequence >> 8);
        request[3] = BatteryCommand;
        request[4] = 0x02; // sent by the vendor software for this receiver
        return request;
    }

    /// <summary>Decodes the reply to the request with <paramref name="sequence"/>, or null for any other frame.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> frame, ushort sequence)
    {
        if (frame.Length < 10 || frame[0] != ReportId || frame[1] != (byte)sequence || frame[2] != (byte)(sequence >> 8) ||
            frame[3] != BatteryCommand || frame[5] != 0 || frame[6] != 0)
            return null;
        var percent = frame[8];
        var state = frame[9];
        if (percent > 100 || state > 2)
            return null;
        return new HidBatteryReading(percent, state is 1 or 2, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF1C, 0x0092) && c.OutputLength >= 5 && c.InputLength >= 10);
}
