using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>AULA SC580 / SC580X mouse: USB cable 088D:062E and 2.4 GHz receiver 089D:062F.</summary>
/// <remarks>
/// <para>Vendor page 0xFF02 / usage 0x0000, 33-byte reports (report id 0 + 32-byte payload). Payload layout:
/// [0] command, [1] status (0 in requests), [2] packets, [3] data length, [4–30] data, [31] = sum of bytes 4–30.</para>
/// <para>The vendor app polls command 0x30 (device info, read-only). Reply status: 0 OK, 1 busy, 2 mouse offline,
/// 3 data error. Data (from payload byte 4): "M137" identity, firmware (2 bytes), sensor id, one unknown byte, charge
/// state (0 battery, 1 charging, 2 full), level 0–100, online flag. The receiver pushes 0xD1/0xD2 notifications on the
/// same channel and may answer a write with the reply to an earlier command, which then means "ask again".</para>
/// <para>While charging the mouse reports a fixed 100, so no level is reported then (the last one is kept).</para>
/// <para>Source: github.com/Crucialjun/omarchy-aula-SC580 (PROTOCOL.md, sc580ctl), reverse engineered from the vendor's
/// CBMouse.exe; tested on an SC580 over cable and receiver according to the reference (cross-checked with
/// BatteryHub's notes).</para>
/// </remarks>
public sealed class AulaSc580Protocol : IHidBatteryProtocol
{
    public const byte InfoCommand = 0x30;
    private const int ReportLength = 33;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ResendAfter = TimeSpan.FromMilliseconds(250);

    private static readonly HashSet<(ushort Vid, ushort Pid)> Devices =
    [
        (0x088D, 0x062E), // USB cable
        (0x089D, 0x062F), // 2.4 GHz receiver
    ];

    public enum Status
    {
        Ok = 0,
        Busy = 1,
        Offline = 2,
        DataError = 3,
    }

    public string Name => "AULA SC580";

    public bool CanHandle(HidReceiver receiver) =>
        Devices.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        connection.FlushInput();
        var deadline = DateTime.UtcNow + Deadline;
        var request = HidTransact.Pad(BuildRequest(), collection.OutputLength);
        while (DateTime.UtcNow < deadline)
        {
            if (!await connection.WriteAsync(request, cancellationToken))
                return [];

            var resendAt = DateTime.UtcNow + ResendAfter;
            while (true)
            {
                var wait = (resendAt < deadline ? resendAt : deadline) - DateTime.UtcNow;
                if (wait <= TimeSpan.Zero)
                    break;
                var frame = await connection.ReadAsync(wait, cancellationToken);
                if (frame is null)
                    break;
                if (frame.Length < ReportLength || frame[0] != 0 || frame[1] is 0xD1 or 0xD2)
                    continue; // pushed notifications or unrelated traffic
                if (frame[1] != InfoCommand)
                    break; // reply to an earlier request: ask again

                var status = (Status)frame[2];
                if (status == Status.Offline)
                    return [new HidBatteryReading(null, Kind: DeviceKind.Mouse)];
                if (status == Status.Busy)
                {
                    var pause = TimeSpan.FromMilliseconds(300);
                    var left = deadline - DateTime.UtcNow;
                    if (left > TimeSpan.Zero)
                        await Task.Delay(pause < left ? pause : left, cancellationToken);
                    break;
                }
                if (ParseReply(frame) is { } reading)
                    return [reading];
                if (status != Status.Ok)
                    break; // data error: ask again
            }
        }
        return [];
    }

    /// <summary>The device-info request as written on Windows (byte 0 = report id 0); its empty data sums to 0.</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[1] = InfoCommand;
        request[3] = 0x01; // one packet
        return request;
    }

    /// <summary>
    /// Decodes an OK device-info reply read on Windows (byte 0 = report id 0). Returns null for anything else.
    /// A mouse marked offline yields a reading without a level; a charging mouse yields one without a level too.
    /// </summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < ReportLength || frame[0] != 0 || frame[1] != InfoCommand || frame[2] != (byte)Status.Ok)
            return null;
        var payload = frame[1..];
        var sum = 0;
        for (var i = 4; i <= 30; i++)
            sum += payload[i];
        if (payload[31] != (byte)sum)
            return null;

        var data = payload.Slice(4, 27);
        var charge = data[8];
        var level = data[9];
        var online = data[10] != 0;
        if (!online)
            return new HidBatteryReading(null, Name: "AULA SC580", Kind: DeviceKind.Mouse);
        if (charge > 2 || level > 100)
            return null;
        if (charge == 1)
            return new HidBatteryReading(null, true, "AULA SC580", DeviceKind.Mouse); // fixed 100 while charging
        return new HidBatteryReading(level, false, "AULA SC580", DeviceKind.Mouse); // 0 on battery, 2 full
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF02, 0x0000) && c.InputLength >= ReportLength && c.OutputLength >= ReportLength);
}
