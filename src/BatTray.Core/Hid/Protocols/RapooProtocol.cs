using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Rapoo VT gaming mice: the VT3 PRO receiver 24AE:1231 (active query), and the VT family that pushes its status
/// passively on FF00:0002 (Nordic 54L report 0x07, or the legacy 7-byte <c>BB B0</c> report).
/// </summary>
/// <remarks>
/// <para><b>Passive VT status</b> (nothing is sent): report 0x07 = <c>07 [conn] [dpi gear] [x dpi LE] [y dpi LE] [state] [%]
/// … [mouse pid LE at 13..14]</c>; the low nibble of byte 1 is 0 receiver / 1 Bluetooth / 2 cable, state 1 = discharging,
/// 2 = charging. The input report is 16 or 19 bytes (13 on some VT3 Max Gen 2 receivers, 1417 only). Legacy firmware
/// (VT3 Pro 1215/4415, VT1 Pro Max 1233/4433, VT3 Pro Max receiver 1244) pushes <c>BB B0 [mode] [dpi lo] [dpi hi]
/// [state] [%]</c>, where 0x7F while charging means "unknown". Sources: the official Rapoo Hub web driver
/// (hub.rapoo.cn, VT_nrf54L decoder), github.com/PyFlat/Device-Battery-Info (RapooProtocol.cs),
/// github.com/zbndev/rapoo_battery and github.com/Nuitfanee/ClickSync; cross-checked with BatteryHub's notes, which
/// report hardware tests of the VT3 Max Gen 2 (1417/4617) and VT9 Gen 2 (1414/4614).</para>
/// <para><b>VT3 PRO 24AE:1231</b> (active):
/// The battery query of Rapoo's own Windows driver (RapooGameDevDriver): output report 0xBA with the frame
/// <c>A5 AA</c> (A5 = link through the receiver, AA = battery), then the 0xBA input report is polled.
/// Its first byte goes busy (0x02) and then OK (0x01); byte 1 = 1 discharging / 2 charging, byte 2 = percent.
/// Source: D3m0nZOnFire/mousectl, drivers/rapoo_vt3pro.py.
/// The same report id carries configuration writes, and on YICHIP receivers (VID 0x3151) similar frames
/// start firmware updates, so this protocol is limited to the exact Rapoo receiver.</para>
/// </remarks>
public sealed class RapooProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x24AE;
    public const ushort VT3ProReceiver = 0x1231;

    /// <summary>Passive VT models: (cable pid, receiver pid, name), from Rapoo Hub's manifests.</summary>
    private static readonly (ushort Cable, ushort Receiver, string Name)[] PassiveModels =
    [
        (0x4610, 0x1410, "Rapoo VT0"),
        (0x4620, 0x1420, "Rapoo VT0 Max"),
        (0x4615, 0x1415, "Rapoo VT1"),
        (0x4644, 0x1444, "Rapoo VT1 Max"),
        (0x4622, 0x1422, "Rapoo VT2"),
        (0x4623, 0x1423, "Rapoo VT2 Max"),
        (0x4612, 0x1412, "Rapoo VT3"),
        (0x4617, 0x1417, "Rapoo VT3 Max (Gen 2)"),
        (0x4B06, 0x1B06, "Rapoo VT3 Max V2"),
        (0x4611, 0x1411, "Rapoo VT3S"),
        (0x4621, 0x1421, "Rapoo VT3S Max"),
        (0x4613, 0x1413, "Rapoo VT7"),
        (0x4616, 0x1416, "Rapoo VT7 Max"),
        (0x4614, 0x1414, "Rapoo VT9 (Gen 2)"),
        (0x4618, 0x1418, "Rapoo VT9 Max"),
        (0x4415, 0x1215, "Rapoo VT3 Pro"),
        (0x4433, 0x1233, "Rapoo VT1 Pro Max"),
    ];

    /// <summary>Products whose firmware pushes the legacy 7-byte BB B0 report.</summary>
    private static readonly HashSet<ushort> LegacyStatusProducts = [0x1215, 0x4415, 0x1233, 0x4433, 0x1244];

    /// <summary>The receiver whose firmware revision exposes a 13-byte status collection.</summary>
    private const ushort ShortStatusReceiver = 0x1417;

    private static readonly TimeSpan PassiveTimeout = TimeSpan.FromSeconds(4);
    public const byte ReportId = 0xBA;
    private const byte LinkReceiver = 0xA5;
    private const byte CommandBattery = 0xAA;
    private const byte StatusOk = 0x01;

    public string Name => "Rapoo";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId &&
        (receiver.ProductId == VT3ProReceiver ? FindCollection(receiver) is not null : FindStatusCollection(receiver) is not null);

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.ProductId != VT3ProReceiver)
            return await ReadPassiveAsync(receiver, cancellationToken);

        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        if (!await connection.WriteAsync(BuildRequest(collection.OutputLength), cancellationToken))
            return [];

        // The input report keeps the previous answer until the receiver goes busy: only an OK seen after
        // busy belongs to this request.
        var busy = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            var input = new byte[collection.InputLength];
            input[0] = ReportId;
            if (!connection.GetInputReport(input))
                return [];

            if (input[1] != StatusOk)
                busy = true;
            else if (busy)
                return ParseReply(input) is { } reading ? [reading] : [];
        }
        return [];
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadPassiveAsync(HidReceiver receiver, CancellationToken ct)
    {
        if (FindStatusCollection(receiver) is not { } collection)
            return [];

        var legacy = collection.InputLength == 7;
        var frame = await HidTransact.ListenAsync(collection, f => ParseStatusReport(f, legacy) is not null, PassiveTimeout, ct);
        if (frame is null || ParseStatusReport(frame, legacy) is not { } status)
            return [];

        var identity = status.MousePid ?? receiver.ProductId;
        var name = ModelName(identity) ?? (receiver.ProductId == 0x1244 ? "Rapoo VT3 Pro Max" : null);
        return [status.Reading with { Name = name, Kind = DeviceKind.Mouse }];
    }

    /// <summary>Model name for a cable or receiver product id, or null when the table does not know it.</summary>
    public static string? ModelName(ushort productId) =>
        PassiveModels.FirstOrDefault(m => m.Cable == productId || m.Receiver == productId).Name;

    /// <summary>True for a product id of the passive VT table or the legacy list.</summary>
    public static bool IsPassiveProduct(ushort productId) =>
        LegacyStatusProducts.Contains(productId) || ModelName(productId) is not null;

    /// <summary>Connection reported by a Nordic status report.</summary>
    public enum Link
    {
        Unknown,
        Receiver,
        Bluetooth,
        Cable,
    }

    public sealed record StatusReport(HidBatteryReading Reading, Link Link, ushort? MousePid);

    /// <summary>
    /// Decodes a passive status report (byte 0 = report id). <paramref name="legacy"/> selects the <c>BB B0</c> layout;
    /// otherwise only report 0x07 is accepted. Returns null for transitions (state 0), bad lengths or levels.
    /// </summary>
    public static StatusReport? ParseStatusReport(ReadOnlySpan<byte> f, bool legacy)
    {
        byte state, percent;
        var link = Link.Unknown;
        ushort? mousePid = null;
        if (!legacy && f.Length >= 9 && f[0] == 0x07)
        {
            link = (f[1] & 0x0F) switch
            {
                0 => Link.Receiver,
                1 => Link.Bluetooth,
                2 => Link.Cable,
                _ => Link.Unknown,
            };
            if (link == Link.Unknown)
                return null;
            state = f[7];
            percent = f[8];
            if (f.Length >= 15)
                mousePid = (ushort)(f[13] | (f[14] << 8));
        }
        else if (legacy && f.Length >= 7 && f[0] == 0xBB && f[1] == 0xB0)
        {
            state = f[5];
            percent = f[6];
        }
        else
        {
            return null;
        }

        if (state is not (1 or 2))
            return null;
        var charging = state == 2;
        if (percent <= 100)
            return new StatusReport(new HidBatteryReading(percent, charging), link, mousePid is 0 ? null : mousePid);
        // Legacy firmware reports 0x7F while charging when it does not know the level.
        return legacy && charging && percent == 0x7F
            ? new StatusReport(new HidBatteryReading(null, true), link, null)
            : null;
    }

    private static HidCollectionInfo? FindStatusCollection(HidReceiver receiver)
    {
        var pid = receiver.ProductId;
        if (!IsPassiveProduct(pid))
            return null;
        // The other vendor collections are for configuration and firmware updates: never opened.
        return receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF00, 0x0002) &&
            (LegacyStatusProducts.Contains(pid)
                ? c.InputLength == 7
                : c.InputLength is 16 or 19 || (c.InputLength == 13 && pid == ShortStatusReceiver)));
    }

    public static byte[] BuildRequest(int outputLength)
    {
        var request = new byte[Math.Max(outputLength, 33)];
        request[0] = ReportId;
        request[1] = LinkReceiver;
        request[2] = CommandBattery;
        return request;
    }

    /// <summary>Decodes the 0xBA input report (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        // Battery answers carry the charge state (1/2) where block answers have 0.
        if (report.Length < 4 || report[0] != ReportId || report[1] != StatusOk || report[2] is not (1 or 2))
            return null;
        return new HidBatteryReading(Math.Min((int)report[3], 100), report[2] == 2, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage >= 0xFF00 && c.InputLength >= 33 && c.OutputLength >= 33 &&
            c.ValueCaps.Any(v => v.ReportId == ReportId));
}
