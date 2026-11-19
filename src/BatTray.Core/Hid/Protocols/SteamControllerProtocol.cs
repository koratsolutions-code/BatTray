using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Valve Steam Controller: 2015 wireless adapter and the newer controller / puck receivers.</summary>
/// <remarks>
/// Nothing is sent: the vendor collection (usage page FF00) pushes status reports on its own.
/// <list type="bullet">
///   <item>2015 adapter (28DE:1142, one collection per controller slot): report 0x01 with bytes 1–3 =
///         <c>00 04 0B</c> (battery status event) and byte 14 = percent.</item>
///   <item>Newer products (28DE:1302 cable, 1303 Bluetooth, 1304 puck, 1305 internal receiver): report 0x43,
///         byte 1 = 2 while charging, byte 2 = percent. The puck's second FF00 collection (charging contacts) is
///         ignored by requiring usage 1 there.</item>
/// </list>
/// Layout: Linux kernel, drivers/hid/hid-steam.c; cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class SteamControllerProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x28DE;
    public const ushort LegacyAdapter = 0x1142;
    public const ushort Puck = 0x1304;
    public const byte LegacyReportId = 0x01;
    public const byte StatusReportId = 0x43;

    /// <summary>Status reports come every few seconds; stay under the 4 s budget for push-only devices.</summary>
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMilliseconds(3500);

    public static IReadOnlyDictionary<ushort, string> Products { get; } = new Dictionary<ushort, string>
    {
        [LegacyAdapter] = "Steam Controller",
        [0x1302] = "Steam Controller",
        [0x1303] = "Steam Controller",
        [Puck] = "Steam Controller",
        [0x1305] = "Steam Controller",
    };

    public string Name => "Steam Controller";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.ContainsKey(receiver.ProductId) && FindChannels(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.VendorId != VendorId || !Products.TryGetValue(receiver.ProductId, out var name))
            return [];

        var legacy = receiver.ProductId == LegacyAdapter;
        var channels = FindChannels(receiver).ToList();
        var several = channels.Count > 1;

        // Slots are listened to in parallel so a multi-slot adapter still answers within one timeout.
        var frames = await Task.WhenAll(channels.Select(c =>
            HidTransact.ListenAsync(c, f => Parse(f, legacy) is not null, ListenTimeout, cancellationToken)));

        var readings = new List<HidBatteryReading>();
        for (var i = 0; i < channels.Count; i++)
        {
            if (frames[i] is not { } frame || Parse(frame, legacy) is not { } reading)
                continue;
            var subId = several ? $"slot{channels[i].InterfaceNumber?.ToString() ?? i.ToString()}" : null;
            readings.Add(reading with { Name = name, SubId = subId });
        }
        return readings;
    }

    /// <summary>Decodes a status report (byte 0 = report id).</summary>
    public static HidBatteryReading? Parse(ReadOnlySpan<byte> frame, bool legacy)
    {
        if (frame.Length < 15)
            return null;

        if (legacy)
        {
            if (frame[0] != LegacyReportId || frame[1] != 0x00 || frame[2] != 0x04 || frame[3] != 0x0B || frame[14] > 100)
                return null;
            return new HidBatteryReading(frame[14], Kind: DeviceKind.Gamepad);
        }

        if (frame[0] != StatusReportId || frame[2] > 100)
            return null;
        return new HidBatteryReading(frame[2], frame[1] == 2, Kind: DeviceKind.Gamepad);
    }

    private static IEnumerable<HidCollectionInfo> FindChannels(HidReceiver receiver) =>
        receiver.Collections.Where(c =>
            c.UsagePage == 0xFF00 && c.InputLength >= 15 && (receiver.ProductId != Puck || c.Usage == 0x0001));
}
