using System.Globalization;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// ASUS ROG wireless keyboards and mice (receiver or cable), the ROG Omni Receiver (0B05:1ACE), the ROG Pelta /
/// Delta II headsets and the ROG Cetra Open Wireless earbuds.
/// </summary>
/// <remarks>
/// <para><b>Keyboards and mice</b>: write <c>[report id] 12 [sub]</c> on the vendor collection (sub 01 = keyboard,
/// 07 = mouse) and wait for the echo <c>[report id] 12 [sub] …</c>. Keyboards: byte 6 = percent (byte 11 on the
/// Falchion family), byte 9 = 1 while charging. Mice: byte 5 = level (× 25 on older 0–4 gauges), byte 10 = charging.
/// <c>FF AA</c> in bytes 1–2 means the device behind the receiver is asleep or off. Receivers and cables use the
/// unnumbered channel of a fixed interface; the Omni-style channels use report 02 (keyboard, col02) or 03 (mouse,
/// col03) on interface 2.</para>
/// <para><b>Omni Receiver</b>: <c>01 A0 00</c> on col01 lists the paired products (little-endian product ids in
/// 4-byte slots from byte 5); each one is then read on its own channel.</para>
/// <para><b>Pelta / Delta II / Cetra Open</b>: interface 3, FF00:0001, report CC: <c>CC 12 07</c> → byte 6 = percent
/// (FE = unknown); <c>CC 12 08</c> → byte 5 = 1 while charging; Cetra Open <c>CC 12 12</c> → bytes 5/6/7 = left /
/// right / case percent. <c>FF AA</c> means asleep.</para>
/// <para>Only these read queries are sent. Sources: github.com/SteveWang92/peripheral-battery-tray (rog-battery,
/// itself based on G-Helper) and github.com/1gcat/BatteryTool; cross-checked with BatteryHub's notes.</para>
/// </remarks>
public sealed class RogProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x0B05;
    public const ushort OmniReceiver = 0x1ACE;
    public const byte BatteryCommand = 0x12;
    public const byte KeyboardSub = 0x01;
    public const byte MouseSub = 0x07;
    private const byte HeadsetReport = 0xCC;
    private const ushort CetraOpen = 0x1CAC;

    /// <param name="ReportId">0 = unnumbered receiver/cable channel; 2 or 3 = Omni-style channel.</param>
    /// <param name="Interface">USB interface, or -1 for any vendor collection.</param>
    /// <param name="CollectionTag">Required "colNN" tag in the interface path, or null.</param>
    public sealed record Model(ushort ProductId, string Name, DeviceKind Kind, byte ReportId, int Interface,
        string? CollectionTag, int Offset, int Scale);

    private const DeviceKind Kb = DeviceKind.Keyboard;
    private const DeviceKind Ms = DeviceKind.Mouse;

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x1A85, "ROG Azoth", Kb, 2, 2, "col02", 6, 1),
        new(0x1A85, "ROG Azoth", Kb, 0, 1, null, 6, 1),
        new(0x1A83, "ROG Azoth", Kb, 0, 1, null, 6, 1),
        new(0x1B42, "ROG Azoth Extreme", Kb, 2, 2, "col02", 6, 1),
        new(0x1B3F, "ROG Azoth Extreme", Kb, 0, 1, null, 6, 1),
        new(0x1CF1, "ROG Azoth Extreme SE", Kb, 2, 2, "col02", 6, 1),
        new(0x1CEF, "ROG Azoth Extreme SE", Kb, 0, 1, null, 6, 1),
        new(0x1C25, "ROG Azoth X", Kb, 0, 1, null, 6, 1),
        new(0x1C24, "ROG Azoth X", Kb, 0, 1, null, 6, 1),
        new(0x193E, "ROG Falchion", Kb, 0, 1, null, 11, 1),
        new(0x193C, "ROG Falchion", Kb, 0, 1, null, 11, 1),
        new(0x1B04, "ROG Falchion RX", Kb, 0, -1, null, 11, 1),
        new(0x1B06, "ROG Falchion RX Low Profile", Kb, 2, 2, "col02", 11, 1),
        new(0x1AB0, "ROG Strix Scope II 96", Kb, 2, 2, "col02", 6, 1),
        new(0x1AAE, "ROG Strix Scope II 96", Kb, 0, 1, null, 6, 1),
        new(0x1B7A, "ROG Strix Scope II 96 RX", Kb, 2, 2, "col02", 6, 1),
        new(0x1B78, "ROG Strix Scope II 96 RX", Kb, 0, 1, null, 6, 1),
        new(0x1A07, "ROG Strix Scope RX TKL", Kb, 0, -1, null, 6, 1),

        new(0x18E5, "ROG Chakram", Ms, 0, 0, null, 5, 25),
        new(0x18E3, "ROG Chakram", Ms, 0, 0, null, 5, 25),
        new(0x1A1A, "ROG Chakram X", Ms, 0, 0, null, 5, 1),
        new(0x1A18, "ROG Chakram X", Ms, 0, 0, null, 5, 1),
        new(0x18A0, "ROG Gladius II Wireless", Ms, 0, 2, null, 5, 25),
        new(0x197F, "ROG Gladius III Wireless", Ms, 0, 0, null, 5, 1),
        new(0x197D, "ROG Gladius III Wireless", Ms, 0, 0, null, 5, 1),
        new(0x1A72, "ROG Gladius III Aimpoint", Ms, 3, 2, "col03", 5, 1),
        new(0x1A72, "ROG Gladius III Aimpoint", Ms, 0, 0, null, 5, 1),
        new(0x1A70, "ROG Gladius III Aimpoint", Ms, 0, 0, null, 5, 1),
        new(0x1B0C, "ROG Gladius III Aimpoint EVA-02", Ms, 0, 0, null, 5, 1),
        new(0x1B0A, "ROG Gladius III Aimpoint EVA-02", Ms, 0, 0, null, 5, 1),
        new(0x1A94, "ROG Harpe Ace Aim Lab Edition", Ms, 3, 2, "col03", 5, 1),
        new(0x1A94, "ROG Harpe Ace Aim Lab Edition", Ms, 0, 0, null, 5, 1),
        new(0x1A92, "ROG Harpe Ace Aim Lab Edition", Ms, 0, 0, null, 5, 1),
        new(0x1B68, "ROG Harpe Ace Extreme", Ms, 3, 2, "col03", 5, 1),
        new(0x1B69, "ROG Harpe Ace Extreme", Ms, 3, 2, "col03", 5, 1),
        new(0x1B67, "ROG Harpe Ace Extreme", Ms, 0, 0, null, 5, 1),
        new(0x1B65, "ROG Harpe Ace Mini", Ms, 3, 2, "col03", 5, 1),
        new(0x1B63, "ROG Harpe Ace Mini", Ms, 0, 0, null, 5, 1),
        new(0x1AD0, "ROG Harpe II Ace", Ms, 3, 2, "col03", 5, 1),
        new(0x1C69, "ROG Harpe II Ace", Ms, 0, 0, null, 5, 1),
        new(0x1960, "ROG Keris Wireless", Ms, 0, 0, null, 5, 25),
        new(0x195E, "ROG Keris Wireless", Ms, 0, 0, null, 5, 25),
        new(0x1A59, "ROG Keris Wireless EVA Edition", Ms, 0, 0, null, 5, 25),
        new(0x1A57, "ROG Keris Wireless EVA Edition", Ms, 0, 0, null, 5, 25),
        new(0x1A68, "ROG Keris Wireless Aimpoint", Ms, 3, 2, "col03", 5, 1),
        new(0x1A6A, "ROG Keris Wireless Aimpoint", Ms, 3, 2, "col03", 5, 1),
        new(0x1A68, "ROG Keris Wireless Aimpoint", Ms, 0, 0, null, 5, 1),
        new(0x1A66, "ROG Keris Wireless Aimpoint", Ms, 0, 0, null, 5, 1),
        new(0x1B1A, "ROG Keris II Ace", Ms, 3, 2, "col03", 5, 1),
        new(0x1B18, "ROG Keris II Ace", Ms, 3, 2, "col03", 5, 1),
        new(0x1B16, "ROG Keris II Ace", Ms, 0, 0, null, 5, 1),
        new(0x1C0E, "ROG Keris II Origin", Ms, 3, 2, "col03", 5, 1),
        new(0x1C0C, "ROG Keris II Origin", Ms, 0, 0, null, 5, 1),
        new(0x1D4E, "ROG Keris II Origin KJP", Ms, 3, 2, "col03", 5, 1),
        new(0x1D4C, "ROG Keris II Origin KJP", Ms, 0, 0, null, 5, 1),
        new(0x1908, "ROG Pugio II", Ms, 0, 0, null, 5, 25),
        new(0x1906, "ROG Pugio II", Ms, 0, 0, null, 5, 25),
        new(0x1979, "ROG Spatha X", Ms, 0, 0, null, 5, 1),
        new(0x1977, "ROG Spatha X", Ms, 0, 0, null, 5, 1),
        new(0x18B4, "ROG Strix Carry", Ms, 0, 1, null, 7, 25),
        new(0x1949, "ROG Strix Impact II Wireless", Ms, 0, 0, null, 5, 25),
        new(0x1947, "ROG Strix Impact II Wireless", Ms, 0, 0, null, 5, 25),
        new(0x1AD7, "ROG Strix Impact III Wireless", Ms, 3, 2, "col03", 5, 1),
    ];

    private static readonly Dictionary<ushort, string> Headsets = new()
    {
        [0x1B84] = "ROG Pelta",
        [0x1AFA] = "ROG Delta II",
    };

    public string Name => "ASUS ROG";

    public bool CanHandle(HidReceiver receiver)
    {
        if (receiver.VendorId != VendorId)
            return false;
        if (receiver.ProductId == OmniReceiver)
            return FindOmniList(receiver) is not null;
        if (Headsets.ContainsKey(receiver.ProductId) || receiver.ProductId == CetraOpen)
            return FindHeadsetCollection(receiver) is not null;
        return ModelsFor(receiver.ProductId).Any(m => FindCollections(receiver, m).Any());
    }

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.ProductId == OmniReceiver)
            return await ReadOmniAsync(receiver, cancellationToken);
        if (Headsets.TryGetValue(receiver.ProductId, out var headsetName))
            return await ReadHeadsetAsync(receiver, headsetName, cancellationToken);
        if (receiver.ProductId == CetraOpen)
            return await ReadCetraAsync(receiver, cancellationToken);

        foreach (var model in ModelsFor(receiver.ProductId))
        {
            foreach (var collection in FindCollections(receiver, model))
            {
                if (await ReadModelAsync(collection, model, cancellationToken) is { } reading)
                    return [reading];
            }
        }
        return [];
    }

    /// <summary>The keyboard / mouse battery query.</summary>
    public static byte[] BuildRequest(byte reportId, DeviceKind kind) =>
        [reportId, BatteryCommand, kind == DeviceKind.Keyboard ? KeyboardSub : MouseSub];

    /// <summary>True when the reply says the device behind the receiver is asleep (<c>FF AA</c>).</summary>
    public static bool IsAsleep(ReadOnlySpan<byte> reply, byte reportId) =>
        reply.Length >= 3 && reply[0] == reportId && reply[1] == 0xFF && reply[2] == 0xAA;

    /// <summary>
    /// Decodes a keyboard / mouse reply (byte 0 = report id). Null when it is not the echo of the query; a reading
    /// without percent when the device is asleep or reports no level.
    /// </summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply, Model model)
    {
        if (reply.Length < 12 || reply[0] != model.ReportId)
            return null;
        if (IsAsleep(reply, model.ReportId))
            return new HidBatteryReading(null, Name: model.Name, Kind: model.Kind);
        var sub = model.Kind == DeviceKind.Keyboard ? KeyboardSub : MouseSub;
        if (reply[1] != BatteryCommand || reply[2] != sub)
            return null;
        if (model.Kind == DeviceKind.Keyboard)
        {
            var level = reply[model.Offset];
            return new HidBatteryReading(level <= 100 ? level : null, reply[9] == 1, model.Name, model.Kind);
        }
        var value = reply[model.Offset] * model.Scale;
        return new HidBatteryReading(value is > 0 and <= 100 ? value : null, reply[10] != 0, model.Name, model.Kind);
    }

    /// <summary>Product ids listed in an Omni Receiver's pairing reply (byte 0 = report id 01).</summary>
    public static IReadOnlyList<ushort> ParsePairedProducts(ReadOnlySpan<byte> reply)
    {
        var products = new List<ushort>();
        if (reply.Length < 7 || reply[0] != 0x01 || reply[1] != 0xA0 || reply[2] != 0x00)
            return products;
        for (var at = 5; at + 1 < reply.Length; at += 4)
        {
            var pid = (ushort)(reply[at] | (reply[at + 1] << 8));
            if (pid == 0)
                break;
            products.Add(pid);
        }
        return products;
    }

    /// <summary>Decodes a Pelta / Delta II battery reply (<c>CC 12 07</c>). Null when it is not one.</summary>
    public static HidBatteryReading? ParseHeadsetBattery(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8 || reply[0] != HeadsetReport)
            return null;
        if ((reply[1] == 0xFF && reply[2] == 0xAA) || (reply[1] == BatteryCommand && reply[5] == 0xFF && reply[6] == 0xAA))
            return new HidBatteryReading(null, Kind: DeviceKind.Headset);
        if (reply[1] != BatteryCommand || reply[2] != 0x07)
            return null;
        return new HidBatteryReading(reply[6] <= 100 ? reply[6] : null, Kind: DeviceKind.Headset);
    }

    /// <summary>Decodes a Cetra Open reply (<c>CC 12 12</c>): the lower earbud level, null when it is not one.</summary>
    public static HidBatteryReading? ParseCetraBattery(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8 || reply[0] != HeadsetReport)
            return null;
        if ((reply[1] == 0xFF && reply[2] == 0xAA) || (reply[1] == BatteryCommand && reply[5] == 0xFF && reply[6] == 0xAA))
            return new HidBatteryReading(null, Kind: DeviceKind.Headset);
        if (reply[1] != BatteryCommand || reply[2] != 0x12)
            return null;
        int? left = reply[5] is >= 1 and <= 100 ? reply[5] : null;
        int? right = reply[6] is >= 1 and <= 100 ? reply[6] : null;
        int? percent = left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);
        return new HidBatteryReading(percent, Kind: DeviceKind.Headset);
    }

    private static IEnumerable<Model> ModelsFor(ushort productId) => Models.Where(m => m.ProductId == productId);

    private static async Task<HidBatteryReading?> ReadModelAsync(HidCollectionInfo collection, Model model, CancellationToken ct)
    {
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(model.ReportId, model.Kind),
            f => ParseReply(f, model) is not null, TimeSpan.FromMilliseconds(500), ct);
        return frame is null ? null : ParseReply(frame, model);
    }

    private async Task<IReadOnlyList<HidBatteryReading>> ReadOmniAsync(HidReceiver receiver, CancellationToken ct)
    {
        if (FindOmniList(receiver) is not { } list)
            return [];
        var frame = await HidTransact.WriteReadAsync(list, [0x01, 0xA0, 0x00],
            f => f.Length >= 7 && f[0] == 0x01 && f[1] == 0xA0 && f[2] == 0x00, TimeSpan.FromMilliseconds(500), ct);
        if (frame is null)
            return [];

        var readings = new List<HidBatteryReading>();
        foreach (var pid in ParsePairedProducts(frame))
        {
            // Only the Omni-channel entry of each paired model.
            if (Models.FirstOrDefault(m => m.ProductId == pid && m.ReportId != 0) is not { } model)
                continue;
            foreach (var collection in FindOmniChannel(receiver, model))
            {
                if (await ReadModelAsync(collection, model, ct) is { } reading)
                {
                    readings.Add(reading with { SubId = pid.ToString("X4", CultureInfo.InvariantCulture) });
                    break;
                }
            }
        }
        return readings;
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadHeadsetAsync(HidReceiver receiver, string name, CancellationToken ct)
    {
        if (FindHeadsetCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];
        // The first answer after the headset reconnects can take ~750 ms.
        var frame = await GamingMouseHelpers.WriteReadAsync(connection, [HeadsetReport, BatteryCommand, 0x07],
            TimeSpan.FromMilliseconds(1200), f => ParseHeadsetBattery(f) is not null, ct);
        if (frame is null || ParseHeadsetBattery(frame) is not { } reading)
            return [];
        if (reading.Percent is not null)
        {
            var charge = await GamingMouseHelpers.WriteReadAsync(connection, [HeadsetReport, BatteryCommand, 0x08],
                TimeSpan.FromMilliseconds(800), f => f.Length >= 8 && f[0] == HeadsetReport && f[1] == BatteryCommand && f[2] == 0x08, ct);
            if (charge is not null)
                reading = reading with { Charging = charge[5] == 1 };
        }
        return [reading with { Name = name }];
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadCetraAsync(HidReceiver receiver, CancellationToken ct)
    {
        if (FindHeadsetCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, [HeadsetReport, BatteryCommand, 0x12],
            f => ParseCetraBattery(f) is not null, TimeSpan.FromMilliseconds(1200), ct);
        return frame is not null && ParseCetraBattery(frame) is { } reading
            ? [reading with { Name = "ROG Cetra Open Wireless" }]
            : [];
    }

    private static bool PathHas(HidCollectionInfo c, string tag) =>
        c.Path.Contains(tag, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver, Model model) =>
        receiver.Collections.Where(c =>
            c.OutputLength >= 12 && c.InputLength >= 12 && c.UsagePage >= 0xFF00 &&
            (model.Interface < 0 || c.InterfaceNumber == model.Interface) &&
            (model.CollectionTag is null || PathHas(c, model.CollectionTag)));

    private static IEnumerable<HidCollectionInfo> FindOmniChannel(HidReceiver receiver, Model model) =>
        receiver.Collections.Where(c =>
            c.InterfaceNumber == 2 && c.OutputLength >= 12 && c.InputLength >= 12 &&
            model.CollectionTag is not null && PathHas(c, model.CollectionTag));

    private static HidCollectionInfo? FindOmniList(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 2 && c.OutputLength >= 12 && c.InputLength >= 7 && PathHas(c, "col01"));

    private static HidCollectionInfo? FindHeadsetCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 3 && c.Is(0xFF00, 0x0001) && c.InputLength >= 8 && c.OutputLength >= 8);
}
