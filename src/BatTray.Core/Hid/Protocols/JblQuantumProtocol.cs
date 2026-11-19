using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>JBL Quantum wireless headsets (VID 0ECB), on their 2.4 GHz dongle or USB cable.</summary>
/// <remarks>
/// Two generations of JBL's HID protocol, both used read-only:
/// <list type="bullet">
///   <item><b>HIDV1</b> (Quantum 350, 360, 610 dongles): feature report 0x68 = wireless link (1 = headset
///         connected) and feature report 0x49 = battery percent, one byte each, read with GET_REPORT. The firmware
///         answers an unknown id with the previous one, so the id echoed in byte 0 is checked.</item>
///   <item><b>HIDV3</b> (Quantum 520, 650, 850, 950 and X variants): on the vendor collection FF12, feature report
///         0x88 <c>00 DD 01 00 01 00 02 00 0D 00</c> = "get device info" (command 0x0001) for feature 0x000D
///         (battery) of the device itself (0xDD00). The answer is input report 0x86
///         <c>00 DD 01 00 01 00 [len] [status:2] [feature:2] [size:2] [value]</c>; status bit 15 = error;
///         value bits 0–6 = percent, bit 7 = charging. With the headset off the dongle stays silent.</item>
/// </list>
/// Sources: JBL QuantumENGINE 2.9.4 (HIDV1 GET 49 / 68, HIDV3 device-info messages),
/// github.com/Tymon3310/jbl-quantum-linux (docs/QUANTUM610_PACKETS.md, docs/QUANTUM_LINEUP.md),
/// github.com/sidmax7/QuantumEngine (product ids); cross-checked with BatteryHub's notes.
/// Hardware-tested there: Quantum 650 Wireless (HIDV3, dongle) and Quantum 350 Wireless (HIDV1).
/// </remarks>
public sealed class JblQuantumProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x0ECB;
    public const byte Hidv1LinkReport = 0x68;
    public const byte Hidv1BatteryReport = 0x49;
    public const byte Hidv3RequestReportId = 0x88;
    public const byte Hidv3ReplyReportId = 0x86;
    public const ushort BatteryFeature = 0x000D;

    private static readonly TimeSpan Hidv3Timeout = TimeSpan.FromMilliseconds(800);

    public enum Generation
    {
        Hidv1,
        Hidv3,
    }

    /// <param name="Interface">HIDV1: USB interface of the feature collection.</param>
    /// <param name="GenericDesktopUsage0">HIDV1: the collection must be Generic Desktop usage 0.</param>
    public sealed record Model(ushort ProductId, Generation Generation, string Name, int Interface = -1, bool GenericDesktopUsage0 = false);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x206B, Generation.Hidv1, "JBL Quantum 350 Wireless", Interface: 3, GenericDesktopUsage0: true),
        new(0x205C, Generation.Hidv1, "JBL Quantum 610 Wireless", Interface: 5),
        new(0x2115, Generation.Hidv1, "JBL Quantum 360 Wireless", Interface: 5, GenericDesktopUsage0: true),

        new(0x2125, Generation.Hidv3, "JBL Quantum 650 Wireless"),
        new(0x2126, Generation.Hidv3, "JBL Quantum 650 Wireless"),
        new(0x213B, Generation.Hidv3, "JBL Quantum 650X Wireless"),
        new(0x213D, Generation.Hidv3, "JBL Quantum 650X Wireless"),
        new(0x2127, Generation.Hidv3, "JBL Quantum 950 Wireless"),
        new(0x2128, Generation.Hidv3, "JBL Quantum 950 Wireless"),
        new(0x213E, Generation.Hidv3, "JBL Quantum 950X Wireless"),
        new(0x2140, Generation.Hidv3, "JBL Quantum 950X Wireless"),
        new(0x218D, Generation.Hidv3, "JBL Quantum 520 Wireless"),
        new(0x218E, Generation.Hidv3, "JBL Quantum 520 Wireless"),
        new(0x21A7, Generation.Hidv3, "JBL Quantum 520X Wireless"),
        new(0x21A9, Generation.Hidv3, "JBL Quantum 520X Wireless"),
        new(0x219A, Generation.Hidv3, "JBL Quantum 850 Wireless"),
        new(0x219B, Generation.Hidv3, "JBL Quantum 850 Wireless"),
    ];

    public string Name => "JBL Quantum";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == productId) : null;

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is { } model && FindCollection(receiver, model) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model || FindCollection(receiver, model) is not { } collection)
            return [];

        var reading = model.Generation == Generation.Hidv1
            ? ReadHidv1(collection)
            : await ReadHidv3Async(collection, cancellationToken);
        return reading is null ? [] : [reading with { Name = model.Name }];
    }

    private static HidBatteryReading? ReadHidv1(HidCollectionInfo collection)
    {
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return null;

        // Two-byte GET_REPORT buffers ([id][value]), as JBL's own software reads them.
        var link = new byte[2];
        link[0] = Hidv1LinkReport;
        if (!connection.GetFeature(link) || ParseHidv1Byte(link, Hidv1LinkReport) != 1)
            return null; // dongle present, headset off

        var battery = new byte[2];
        battery[0] = Hidv1BatteryReport;
        if (!connection.GetFeature(battery) || ParseHidv1Byte(battery, Hidv1BatteryReport) is not { } percent || percent > 100)
            return null;
        return new HidBatteryReading(percent, Kind: DeviceKind.Headset);
    }

    /// <summary>The one-byte value of a HIDV1 feature report, or null when another report id answered.</summary>
    public static int? ParseHidv1Byte(ReadOnlySpan<byte> report, byte reportId) =>
        report.Length >= 2 && report[0] == reportId ? report[1] : null;

    private static async Task<HidBatteryReading?> ReadHidv3Async(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        connection.FlushInput();
        var request = HidTransact.Pad(BuildHidv3Request(), collection.FeatureLength);
        if (!connection.SetFeature(request))
            return null;

        var deadline = DateTime.UtcNow + Hidv3Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            switch (ClassifyHidv3Reply(frame))
            {
                case Hidv3Reply.Battery:
                    return ParseHidv3Battery(frame);
                case Hidv3Reply.Error:
                    return null;
            }
        }
        return null;
    }

    public static byte[] BuildHidv3Request() =>
        [Hidv3RequestReportId, 0x00, 0xDD, 0x01, 0x00, 0x01, 0x00, 0x02, 0x00, (byte)BatteryFeature, (byte)(BatteryFeature >> 8)];

    public enum Hidv3Reply
    {
        Other,
        Error,
        Battery,
    }

    public static Hidv3Reply ClassifyHidv3Reply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 16 || frame[0] != Hidv3ReplyReportId || frame[1] != 0x00 || frame[2] != 0xDD ||
            frame[3] != 0x01 || frame[4] != 0x00 || frame[5] != 0x01)
        {
            return Hidv3Reply.Other;
        }
        if ((frame[10] & 0x80) != 0)
            return Hidv3Reply.Error;
        if (frame[11] != (byte)BatteryFeature || frame[12] != (byte)(BatteryFeature >> 8) || (frame[13] == 0 && frame[14] == 0))
            return Hidv3Reply.Other;
        return Hidv3Reply.Battery;
    }

    /// <summary>Decodes the HIDV3 battery reply (input report 0x86, byte 0 = report id).</summary>
    public static HidBatteryReading? ParseHidv3Battery(ReadOnlySpan<byte> frame)
    {
        if (ClassifyHidv3Reply(frame) != Hidv3Reply.Battery)
            return null;
        var percent = frame[15] & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (frame[15] & 0x80) != 0, Kind: DeviceKind.Headset);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Model model) =>
        model.Generation == Generation.Hidv3
            ? receiver.Collections.FirstOrDefault(c => c.UsagePage == 0xFF12 && c.FeatureLength >= 11 && c.InputLength >= 16)
            : receiver.Collections.FirstOrDefault(c =>
                c.InterfaceNumber == model.Interface && c.FeatureLength >= 2 &&
                (!model.GenericDesktopUsage0 || c.Is(0x01, 0x00)));
}
