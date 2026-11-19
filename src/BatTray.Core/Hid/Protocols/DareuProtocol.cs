using System.Globalization;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// DAREU devices not covered by <see cref="GetPowerProtocol"/>: EM901X mouse (receiver 260D:1074, cable 260D:1090),
/// Sharkoon Skiller SGM50W (260D:1125), A84 keyboard (260D:0022) and the "Togran" receiver 260D:1131 with the
/// A980 Pro cable 260D:1148.
/// </summary>
/// <remarks>
/// <para><b>EM901X</b>: read-only GetPower over two collections of interface 1. The 17-byte feature report 0x08
/// <c>08 04 00 … 49</c> (bytes sum to 0x55) is sent on 0xFF02/0x0002 and the 17-byte input report 0x09
/// <c>09 04 00 … [percent] [charging]</c> (bytes 6 and 7) arrives on 0xFF01/0x0000. Unlike the Areson mice of
/// <see cref="GetPowerProtocol"/>, byte 6 is already a percentage. From the official EM901X AIM-WL driver, as analysed
/// in github.com/wushuai0802/myfinder-device-battery (upstream-protocols.md); tested on hardware according to the
/// reference. The SGM50W uses the same Compx firmware tools and collection pair (untested).</para>
/// <para><b>A84</b>: the official A84 driver's only command: feature report 0 <c>04 F6 00 …</c> (65 bytes) on the
/// keyboard collection of interface 0, then GetFeature; the first three reads are stale and discarded, a valid one has
/// a non-zero byte 15 and the percent in byte 5. Untested.</para>
/// <para><b>Togran</b>: 65-byte feature report 0x0A on the consumer-control collection: <c>0A [slot&lt;&lt;4] [len]
/// [class] [cmd]</c>; the reply echoes class and command with 2 in the low nibble of byte 1. Slot 0, class 0x00,
/// command 0xA0 lists the paired mice (count in byte 7, then three bytes per slot: present, PID high, PID low); class
/// 0x07, command 0x80 reads a slot's battery (byte 7 status, 1 = charging; byte 8 level; byte 9 maximum, 0 = 100).
/// Over the cable the battery is read on slot 0. Read from DAREU's own driver; level and charging tested according to
/// the reference.</para>
/// <para>Cross-checked with BatteryHub's notes. Every transport requires exactly one matching collection, so
/// collections of different devices sharing a product id are never paired.</para>
/// </remarks>
public sealed class DareuProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x260D;
    public const ushort Em901xReceiver = 0x1074;
    public const ushort Em901xCable = 0x1090;
    public const ushort SharkoonSgm50w = 0x1125;
    public const ushort A84Keyboard = 0x0022;
    public const ushort TogranReceiver = 0x1131;
    public const ushort A980ProCable = 0x1148;

    private const int GetPowerLength = 17;
    private const int LongReportLength = 65;
    private const byte TogranReportId = 0x0A;

    public string Name => "DAREU";

    public bool CanHandle(HidReceiver receiver)
    {
        if (receiver.VendorId != VendorId)
            return false;
        return receiver.ProductId switch
        {
            Em901xReceiver or Em901xCable or SharkoonSgm50w => FindGetPowerPair(receiver) is not null,
            A84Keyboard => FindA84Collection(receiver) is not null,
            TogranReceiver or A980ProCable => FindTogranCollection(receiver) is not null,
            _ => false,
        };
    }

    public Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken) =>
        receiver.ProductId switch
        {
            Em901xReceiver or Em901xCable or SharkoonSgm50w => ReadGetPowerAsync(receiver, cancellationToken),
            A84Keyboard => ReadA84Async(receiver, cancellationToken),
            TogranReceiver or A980ProCable => ReadTogranAsync(receiver, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<HidBatteryReading>>([]),
        };

    // ---------- EM901X / SGM50W ----------

    /// <summary>The 17-byte GetPower feature report (its bytes sum to 0x55).</summary>
    public static byte[] BuildGetPowerRequest()
    {
        var request = new byte[GetPowerLength];
        request[0] = 0x08;
        request[1] = 0x04;
        request[16] = 0x49; // 0x55 − (0x08 + 0x04)
        return request;
    }

    /// <summary>Decodes the 0x09 input report (percent in byte 6, charging in byte 7), or null for another frame.</summary>
    public static HidBatteryReading? ParseGetPowerReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != GetPowerLength || frame[0] != 0x09 || frame[1] != 0x04 || frame[2] != 0x00 || frame[6] > 100)
            return null;
        return new HidBatteryReading(frame[6], frame[7] != 0, Kind: DeviceKind.Mouse);
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadGetPowerAsync(HidReceiver receiver, CancellationToken ct)
    {
        if (FindGetPowerPair(receiver) is not var (featureCollection, inputCollection))
            return [];

        using var input = HidConnection.OpenForReports(inputCollection);
        using var feature = HidConnection.OpenForFeatures(featureCollection);
        if (input is null || feature is null)
            return [];

        input.FlushInput();
        if (!feature.SetFeature(BuildGetPowerRequest()))
            return [];

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        for (var reports = 0; reports < 32 && DateTime.UtcNow < deadline; reports++)
        {
            var frame = await input.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                break;
            if (ParseGetPowerReply(frame) is { } reading)
            {
                var name = receiver.ProductId == SharkoonSgm50w ? "Sharkoon Skiller SGM50W" : "DAREU EM901X";
                return [reading with { Name = name }];
            }
        }
        return [];
    }

    private static (HidCollectionInfo Feature, HidCollectionInfo Input)? FindGetPowerPair(HidReceiver receiver)
    {
        var inputs = receiver.Collections
            .Where(c => c.InterfaceNumber == 1 && c.Is(0xFF01, 0x0000) && c.InputLength == GetPowerLength).ToList();
        var features = receiver.Collections
            .Where(c => c.InterfaceNumber == 1 && c.Is(0xFF02, 0x0002) && c.FeatureLength == GetPowerLength).ToList();
        return inputs.Count == 1 && features.Count == 1 ? (features[0], inputs[0]) : null;
    }

    // ---------- A84 ----------

    /// <summary>The A84 battery query (feature report 0, 65 bytes).</summary>
    public static byte[] BuildA84Request()
    {
        var request = new byte[LongReportLength];
        request[1] = 0x04;
        request[2] = 0xF6;
        return request;
    }

    /// <summary>The percent of a fresh A84 reply (byte 15 non-zero), or null for a stale or invalid one.</summary>
    public static int? ParseA84Reply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 16 || reply[0] != 0 || reply[15] == 0 || reply[5] > 100)
            return null;
        return reply[5];
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadA84Async(HidReceiver receiver, CancellationToken ct)
    {
        if (FindA84Collection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null || !connection.SetFeature(BuildA84Request()))
            return [];

        await Task.Delay(10, ct);
        var reply = new byte[LongReportLength];
        for (var i = 0; i < 3; i++) // the official driver discards these reads
        {
            connection.GetFeature(reply);
            await Task.Delay(10, ct);
        }
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
                await Task.Delay(10, ct);
            Array.Clear(reply);
            if (!connection.GetFeature(reply) || reply[15] == 0)
                continue;
            return ParseA84Reply(reply) is { } percent
                ? [new HidBatteryReading(percent, Name: "DAREU A84", Kind: DeviceKind.Keyboard)]
                : [];
        }
        return [];
    }

    private static HidCollectionInfo? FindA84Collection(HidReceiver receiver)
    {
        var matches = receiver.Collections
            .Where(c => c.InterfaceNumber == 0 && c.Is(0x01, 0x06) && c.FeatureLength == LongReportLength).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // ---------- Togran ----------

    /// <summary>A Togran query: <c>0A [slot&lt;&lt;4] [len] [class] [cmd]</c>, 65 bytes.</summary>
    public static byte[] BuildTogranRequest(byte slot, byte length, byte commandClass, byte command)
    {
        var request = new byte[LongReportLength];
        request[0] = TogranReportId;
        request[1] = (byte)(slot << 4);
        request[2] = length;
        request[3] = commandClass;
        request[4] = command;
        return request;
    }

    /// <summary>True when <paramref name="reply"/> answers the query <paramref name="commandClass"/>/<paramref name="command"/>.</summary>
    public static bool IsTogranReply(ReadOnlySpan<byte> reply, byte commandClass, byte command) =>
        reply.Length >= 11 && reply[0] == TogranReportId && (reply[1] & 0x0F) == 2 &&
        reply[3] == commandClass && reply[4] == command;

    /// <summary>Product ids of the mice listed by a slot-list reply, indexed by slot (1-based); 0 when a slot is empty.</summary>
    public static IReadOnlyList<(int Slot, ushort MousePid)> ParseTogranSlots(ReadOnlySpan<byte> reply)
    {
        if (!IsTogranReply(reply, 0x00, 0xA0))
            return [];
        var slots = new List<(int, ushort)>();
        var count = Math.Min((int)reply[7], 5);
        for (var slot = 1; slot <= count; slot++)
        {
            var at = 8 + (slot - 1) * 3;
            if (at + 2 >= reply.Length)
                break;
            if (reply[at] != 0)
                slots.Add((slot, (ushort)(reply[at + 1] << 8 | reply[at + 2])));
        }
        return slots;
    }

    /// <summary>Decodes a battery reply (percent scaled by the reported maximum), or null.</summary>
    public static HidBatteryReading? ParseTogranBattery(ReadOnlySpan<byte> reply)
    {
        if (!IsTogranReply(reply, 0x07, 0x80))
            return null;
        int max = reply[9] == 0 ? 100 : reply[9];
        if (reply[8] > max)
            return null;
        return new HidBatteryReading(reply[8] * 100 / max, reply[7] == 1, Kind: DeviceKind.Mouse);
    }

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadTogranAsync(HidReceiver receiver, CancellationToken ct)
    {
        if (FindTogranCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        if (receiver.ProductId == A980ProCable)
        {
            var wired = await TogranQueryAsync(connection, 0, 4, 0x07, 0x80, ct);
            return wired is not null && ParseTogranBattery(wired) is { } reading
                ? [reading with { Name = "DAREU A980 Pro" }]
                : [];
        }

        var list = await TogranQueryAsync(connection, 0, 6, 0x00, 0xA0, ct);
        if (list is null)
            return [];

        var readings = new List<HidBatteryReading>();
        foreach (var (slot, mousePid) in ParseTogranSlots(list))
        {
            var reply = await TogranQueryAsync(connection, (byte)slot, 4, 0x07, 0x80, ct);
            if (reply is null || ParseTogranBattery(reply) is not { } reading)
                continue;
            var name = mousePid == A980ProCable
                ? "DAREU A980 Pro"
                : string.Create(CultureInfo.InvariantCulture, $"DAREU mouse {mousePid:X4}");
            readings.Add(reading with
            {
                Name = name,
                SubId = string.Create(CultureInfo.InvariantCulture, $"{mousePid:X4}"),
            });
        }
        return readings;
    }

    private static async Task<byte[]?> TogranQueryAsync(HidConnection connection, byte slot, byte length, byte commandClass,
        byte command, CancellationToken ct)
    {
        if (!connection.SetFeature(BuildTogranRequest(slot, length, commandClass, command)))
            return null;
        for (var tries = 0; tries < 15; tries++)
        {
            await Task.Delay(30, ct);
            var reply = new byte[LongReportLength];
            reply[0] = TogranReportId;
            if (!connection.GetFeature(reply))
                return null;
            if (IsTogranReply(reply, commandClass, command))
                return reply;
        }
        return null;
    }

    private static HidCollectionInfo? FindTogranCollection(HidReceiver receiver)
    {
        var matches = receiver.Collections.Where(c => c.Is(0x000C, 0x0001) && c.FeatureLength == LongReportLength).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
