using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Darmoshark mice: M3S Pro on the Nordic receiver 1915:0723 / cable 1915:073C, and the M3 / M3S on their Telink
/// receivers 248A:FF30 / 248A:FF31.
/// </summary>
/// <remarks>
/// Vendor collection 0xFF0A/0x0001, 64-byte reports without report id. Power query: <c>01 00 81 01</c> (bit 0x40 of
/// byte 0 set through the receiver), zero payload, byte 63 making the whole report sum to 0xA1.
/// Reply: <c>(r[0] &amp; 0x3F) == 1</c> and <c>r[3] == 1</c>; percent at r[6], or r[11] when r[6..7] = 15 19 (v2 layout).
/// Source: ayasaki-exs/darmoshark-battery, docs/PROTOCOL.md. VID 0x1915 is Nordic's and shared by many products,
/// so only these product ids are handled.
/// <para>M3 / M3S (248A:FF30 / FF31): interface 1, usage page 0x8C, 33-byte input/output and 1001-byte feature report 0x51.
/// SetFeature <c>51 03</c> (bond info) then GetFeature 0x51 returns <c>51 03 ?? vid:LE16 mouse pid:LE16 bonded</c>;
/// SetFeature <c>51 06</c> (identify) returns <c>51 06 01 ?? vid:LE16 mouse pid:LE16 … [11] = %</c>. Input report
/// <c>54 E4 02</c> means the receiver cannot reach the mouse. Source: github.com/Barba2k2/darmoshark-m3-configurator
/// (PROTOCOL.md); cross-checked with BatteryHub's notes. VID 0x248A is shared (Telink), so only these exact pairs are
/// handled.</para>
/// </remarks>
public sealed class DarmosharkProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1915;
    public const ushort Receiver = 0x0723;
    public const ushort Cable = 0x073C;
    private const int FrameLength = 64;

    public const ushort TelinkVendorId = 0x248A;
    private const byte TelinkReport = 0x51;
    private const byte TelinkBond = 0x03;
    private const byte TelinkIdentify = 0x06;

    /// <summary>Telink receivers and the product id of the mouse each one is bonded to.</summary>
    private static readonly Dictionary<ushort, (ushort Mouse, string Name)> TelinkModels = new()
    {
        [0xFF30] = (0xFF12, "Darmoshark M3"),
        [0xFF31] = (0xFF18, "Darmoshark M3S"),
    };

    public string Name => "Darmoshark";

    public bool CanHandle(HidReceiver receiver) =>
        (receiver.VendorId == VendorId && receiver.ProductId is Receiver or Cable && FindCollection(receiver) is not null) ||
        FindTelinkCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindTelinkCollection(receiver) is { } telink)
        {
            var model = TelinkModels[receiver.ProductId];
            var percent = await ReadTelinkAsync(telink, model.Mouse, cancellationToken);
            return percent is null ? [] : [new HidBatteryReading(percent, Name: model.Name, Kind: DeviceKind.Mouse)];
        }

        if (FindCollection(receiver) is not { } collection)
            return [];

        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(throughReceiver: receiver.ProductId == Receiver),
            f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(1500), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    /// <summary>The power query as written on Windows: report id 0, then the 64-byte frame.</summary>
    public static byte[] BuildRequest(bool throughReceiver)
    {
        var request = new byte[FrameLength + 1];
        request[1] = (byte)(throughReceiver ? 0x41 : 0x01); // GET_POWER (+ 2.4 GHz mode bit)
        request[3] = 0x81;                                  // BASE_INFO
        request[4] = 0x01;
        var sum = 0;
        for (var i = 1; i < FrameLength; i++)
            sum += request[i];
        request[FrameLength] = (byte)(0xA1 - sum);
        return request;
    }

    /// <summary>Decodes a power reply as read on Windows (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 13)
            return null;
        var r = report[1..];
        if ((r[0] & 0x3F) != 0x01 || r[3] != 0x01)
            return null;
        var percent = r[6] == 0x15 && r[7] == 0x19 ? r[11] : r[6];
        return percent > 100 ? null : new HidBatteryReading(percent, Kind: DeviceKind.Mouse);
    }

    // ---- Telink M3 / M3S ----

    private static async Task<int?> ReadTelinkAsync(HidCollectionInfo collection, ushort mousePid, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        // The bond query replaces any earlier reply in the receiver's feature buffer; one retry if the first try is lost.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        for (var attempt = 0; attempt < 2 && DateTime.UtcNow < deadline; attempt++)
        {
            var bond = await TelinkQueryAsync(connection, collection, TelinkBond, deadline, ct);
            if (bond is null || !IsTelinkBonded(bond, mousePid))
                continue;
            var identify = await TelinkQueryAsync(connection, collection, TelinkIdentify, deadline, ct);
            if (identify is not null && ParseTelinkIdentify(identify, mousePid) is { } percent)
                return percent;
        }
        return null;
    }

    private static async Task<byte[]?> TelinkQueryAsync(
        HidConnection connection, HidCollectionInfo collection, byte opcode, DateTime receiverDeadline, CancellationToken ct)
    {
        var deadline = Min(receiverDeadline, DateTime.UtcNow + TimeSpan.FromMilliseconds(500));
        connection.FlushInput();
        var request = new byte[collection.FeatureLength];
        request[0] = TelinkReport;
        request[1] = opcode;
        if (!connection.SetFeature(request))
            return null;

        while (DateTime.UtcNow < deadline)
        {
            // The receiver says so on the input channel when it cannot reach the mouse.
            var input = await connection.ReadAsync(TimeSpan.FromMilliseconds(20), ct);
            if (input is not null && IsTelinkUnreachable(input))
                return null;

            var reply = new byte[collection.FeatureLength];
            reply[0] = TelinkReport;
            if (connection.GetFeature(reply) && reply.Length >= 12 && reply[0] == TelinkReport && reply[1] == opcode)
                return reply;
        }
        return null;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>Input report <c>54 E4 02</c> (with or without a leading report-id 0): the mouse is unreachable.</summary>
    public static bool IsTelinkUnreachable(ReadOnlySpan<byte> input)
    {
        var r = input.Length > 0 && input[0] == 0x00 ? input[1..] : input;
        return r.Length >= 3 && r[0] == 0x54 && r[1] == 0xE4 && r[2] == 0x02;
    }

    /// <summary>Bond reply: <c>51 03 ?? vid:LE16 pid:LE16 bonded</c> with Darmoshark's vid 0x248A and the expected mouse.</summary>
    public static bool IsTelinkBonded(ReadOnlySpan<byte> r, ushort mousePid) =>
        r.Length >= 12 && r[0] == TelinkReport && r[1] == TelinkBond &&
        (r[3] | (r[4] << 8)) == TelinkVendorId && (r[5] | (r[6] << 8)) == mousePid && r[7] != 0;

    /// <summary>Identify reply: <c>51 06 01 ?? vid:LE16 pid:LE16 … [11] = %</c>.</summary>
    public static int? ParseTelinkIdentify(ReadOnlySpan<byte> r, ushort mousePid) =>
        r.Length >= 12 && r[0] == TelinkReport && r[1] == TelinkIdentify && r[2] == 0x01 &&
        (r[4] | (r[5] << 8)) == TelinkVendorId && (r[6] | (r[7] << 8)) == mousePid && r[11] <= 100
            ? r[11]
            : null;

    private static HidCollectionInfo? FindTelinkCollection(HidReceiver receiver) =>
        receiver.VendorId == TelinkVendorId && TelinkModels.ContainsKey(receiver.ProductId)
            ? receiver.Collections.FirstOrDefault(c =>
                c.InterfaceNumber == 1 && c.Is(0x008C, 0x0001) &&
                c.InputLength == 33 && c.OutputLength == 33 && c.FeatureLength == 1001)
            : null;

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF0A, 0x0001) && c.InputLength == FrameLength + 1 && c.OutputLength == FrameLength + 1);
}
