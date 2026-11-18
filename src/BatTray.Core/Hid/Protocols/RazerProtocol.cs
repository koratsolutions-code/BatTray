using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Razer wireless mice and keyboards (VID 0x1532): the 90-byte feature-report protocol.
/// </summary>
/// <remarks>
/// The request (report id 0 + 90 bytes) is sent with SetFeature and the reply read back with GetFeature on
/// the collection whose feature report is 91 bytes long. Only two read commands are used:
/// class 0x07 / id 0x80 (battery, 0–255) and class 0x07 / id 0x84 (charging). Razer headsets use other
/// protocols that need dongle mode switches, so they are not handled here.
/// </remarks>
public sealed class RazerProtocol : IHidBatteryProtocol
{
    public const ushort RazerVendorId = 0x1532;
    public const int ReportLength = 91;

    private const byte StatusBusy = 0x01;
    private const byte StatusOk = 0x02;
    private const byte CommandClassPower = 0x07;
    private const byte CommandBattery = 0x80;
    private const byte CommandCharging = 0x84;

    private static readonly byte[] FallbackTransactionIds = [0x1F, 0x3F, 0xFF, 0x9F];

    /// <summary>Transaction id per product (receiver and wired PIDs).</summary>
    private static readonly Dictionary<ushort, byte> TransactionIds = BuildTransactionIds();

    /// <summary>Devices that need a longer pause between request and reply.</summary>
    private static readonly HashSet<ushort> SlowDevices = [0x0062, 0x0094, 0x0095, 0x00E0];

    public string Name => "Razer";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == RazerVendorId && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var delay = SlowDevices.Contains(receiver.ProductId) ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromMilliseconds(60);
        var ids = TransactionIds.TryGetValue(receiver.ProductId, out var known) ? [known] : FallbackTransactionIds;

        foreach (var transactionId in ids)
        {
            var battery = await QueryAsync(connection, transactionId, CommandBattery, delay, cancellationToken);
            if (battery is null)
                continue;

            var charging = await QueryAsync(connection, transactionId, CommandCharging, delay, cancellationToken);
            return [new HidBatteryReading(
                (int)Math.Round(battery.Value * 100 / 255.0),
                charging is null ? null : charging.Value == 1,
                Kind: receiver.InferKind() == DeviceKind.Other ? DeviceKind.Mouse : null)];
        }

        return [];
    }

    private static async Task<byte?> QueryAsync(HidConnection connection, byte transactionId, byte command, TimeSpan delay, CancellationToken ct)
    {
        var request = BuildRequest(transactionId, CommandClassPower, command, dataSize: 0x02);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt == 0 && !connection.SetFeature((byte[])request.Clone()))
                return null;

            await Task.Delay(delay, ct);
            var reply = new byte[ReportLength];
            if (!connection.GetFeature(reply))
                return null;

            switch (ParseReply(reply, transactionId, command))
            {
                case { } value:
                    return value;
                case null when reply[1] == StatusBusy:
                    continue; // busy: read again without re-sending
                default:
                    return null;
            }
        }
        return null;
    }

    /// <summary>Builds the 91-byte request (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var buffer = new byte[ReportLength];
        buffer[2] = transactionId;
        buffer[6] = dataSize;
        buffer[7] = commandClass;
        buffer[8] = commandId;
        buffer[89] = Crc(buffer);
        return buffer;
    }

    /// <summary>XOR of bytes 3–88 of the 91-byte buffer.</summary>
    public static byte Crc(ReadOnlySpan<byte> buffer)
    {
        byte crc = 0;
        for (var i = 3; i <= 88; i++)
            crc ^= buffer[i];
        return crc;
    }

    /// <summary>Returns the value byte (args[1], buffer byte 10) of a successful reply to the given command.</summary>
    public static byte? ParseReply(ReadOnlySpan<byte> reply, byte transactionId, byte commandId)
    {
        if (reply.Length < ReportLength || reply[1] != StatusOk || reply[2] != transactionId ||
            reply[7] != CommandClassPower || reply[8] != commandId || reply[89] != Crc(reply))
        {
            return null;
        }
        return reply[10];
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.FeatureLength == ReportLength);

    private static Dictionary<ushort, byte> BuildTransactionIds()
    {
        var map = new Dictionary<ushort, byte>();
        void Add(byte id, params ushort[] pids)
        {
            foreach (var pid in pids)
                map[pid] = id;
        }

        Add(0x3F, 0x0059, 0x005A, 0x0072, 0x0073, 0x007C, 0x007D, 0x025A);
        Add(0x1F,
            0x008F, 0x0090, 0x0086, 0x0088, 0x006F, 0x0070, 0x0062, 0x0094, 0x0095, 0x00E0, 0x0077, 0x0080, 0x009C,
            0x00A5, 0x00A6, 0x009E, 0x009F, 0x00DA, 0x00DB, 0x00AF, 0x00B0, 0x00B6, 0x00B7, 0x00C2, 0x00C3,
            0x00C4, 0x00C5, 0x00B3, 0x00AA, 0x00AB, 0x00CC, 0x00CD, 0x00D6, 0x00D7, 0x009A, 0x00A7, 0x00A8,
            0x00B4, 0x00B8, 0x00B9, 0x00D3, 0x00D4, 0x00BE, 0x00BF, 0x00E5, 0x00E6, 0x00C0, 0x00C1, 0x00DE,
            0x00DF, 0x00C7, 0x00C8, 0x00D0, 0x00D1, 0x0258, 0x02B9, 0x02CD, 0x0292, 0x0298, 0x02D4, 0x02D7);
        Add(0xFF, 0x0024, 0x0025, 0x0044, 0x0045, 0x007A, 0x007B, 0x0083);
        Add(0x9F, 0x025C, 0x0271, 0x02BA, 0x0290, 0x0296, 0x02D5, 0x02D2);
        return map;
    }
}
