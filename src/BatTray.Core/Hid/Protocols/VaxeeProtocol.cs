using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>VAXEE 4K (3057:1002) and 8K (3057:2002) receivers, ZYGEN NP-01S V2 on its USB cable (3057:1011).</summary>
/// <remarks>
/// 64-byte feature report 0x0E on the vendor collection FF05:0001. Read request: <c>0E A5 cmd 01 01</c> (byte 3 = 1
/// is the read flag; 2 would be a write and is never sent). After ~100 ms the reply is read back with GetFeature:
/// <c>0E A5 cmd xx 01 value</c>. Command 0x0B = battery in 5 % steps (0–20), 0x10 = charging (non-zero).
/// Source: github.com/stuffz/mouse-battery-monitor (docs/VAXEE.md); cross-checked with BatteryHub's notes, which
/// report the 4K and 8K receivers and the cable as tested on real hardware.
/// </remarks>
public sealed class VaxeeProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3057;
    public const int ReportLength = 64;
    public const byte ReportId = 0x0E;
    public const byte BatteryCommand = 0x0B;
    public const byte ChargingCommand = 0x10;
    private const ushort Cable = 0x1011;
    private const ushort Receiver8K = 0x2002;

    private static readonly HashSet<ushort> Products = [0x1002, Receiver8K, Cable];

    public string Name => "VAXEE";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        int? level = null;
        for (var attempt = 0; attempt < 2 && level is null; attempt++)
        {
            var reply = await QueryAsync(connection, BatteryCommand, cancellationToken);
            if (reply is not null && ParseValue(reply, BatteryCommand) is { } raw && raw <= 20)
                level = raw * 5;
        }
        if (level is null)
            return [];

        bool? charging = null;
        var chargeReply = await QueryAsync(connection, ChargingCommand, cancellationToken);
        if (chargeReply is not null && ParseValue(chargeReply, ChargingCommand) is { } flag)
            charging = flag != 0;

        var name = receiver.ProductId switch
        {
            Cable => "ZYGEN NP-01S V2 Wireless",
            Receiver8K => "VAXEE 8K Wireless",
            _ => "VAXEE wireless mouse",
        };
        return [new HidBatteryReading(level, charging, name, DeviceKind.Mouse)];
    }

    /// <summary>The 64-byte read request for <paramref name="command"/>.</summary>
    public static byte[] BuildRequest(byte command)
    {
        var request = new byte[ReportLength];
        request[0] = ReportId;
        request[1] = 0xA5;
        request[2] = command;
        request[3] = 0x01; // read
        request[4] = 0x01; // one data byte
        return request;
    }

    /// <summary>The data byte of a reply to <paramref name="command"/>, or null when the frame does not match.</summary>
    public static byte? ParseValue(ReadOnlySpan<byte> reply, byte command)
    {
        if (reply.Length < 6 || reply[0] != ReportId || reply[1] != 0xA5 || reply[2] != command || reply[4] != 0x01)
            return null;
        return reply[5];
    }

    /// <summary>Battery percent (5 % steps) from a battery reply, or null.</summary>
    public static int? ParseBattery(ReadOnlySpan<byte> reply) =>
        ParseValue(reply, BatteryCommand) is { } raw && raw <= 20 ? raw * 5 : null;

    private static Task<byte[]?> QueryAsync(HidConnection connection, byte command, CancellationToken ct) =>
        GamingMouseHelpers.FeatureQueryAsync(connection, BuildRequest(command), ReportId, ReportLength,
            TimeSpan.FromMilliseconds(100), 1, r => ParseValue(r, command) is not null, ct);

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFF05, 0x0001) && c.FeatureLength == ReportLength);
}
