using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Incott G23 V2 / G24 on the PixArt ids 093A:522C (2.4 GHz dongle) and 093A:622C (USB cable).</summary>
/// <remarks>
/// <para>Vendor collection FF05, 8-byte feature report 09. Status query: SetFeature <c>09 89</c>, then GetFeature 09
/// until <c>09 89 [battery] [dpi/rate] …</c> comes back (bytes 2 and 3 both 0 = stale); battery bit 7 = charging,
/// bits 0–6 = percent. On the cable the first FF05 collection rejects SetFeature, so collections are tried last to
/// first.</para>
/// <para>The Wraith W1 uses the same ids with a different command map (there 0x89 reads profile settings), so the
/// read-only device-info query <c>09 8F</c> is sent first: byte 3 = model, 18 / 20 = W1 (not read by this class).
/// A receiver once identified as a W1 stays one, so an asleep W1 is never read with the Incott query.</para>
/// <para>Sources: github.com/OmarK6/Incott-Manager, github.com/HeHaoren/incott-mouse-app (docs/protocol.md),
/// github.com/FelixNg21/MouseBattery (battery byte, verified on a G23 V2 with the 522C dongle) and the Wraith web
/// driver (w1.software) for the model codes; cross-checked with BatteryHub's notes.</para>
/// </remarks>
public sealed class IncottProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x093A;
    public const ushort Dongle = 0x522C;
    public const ushort Cable = 0x622C;
    public const byte ReportId = 0x09;
    public const byte StatusCommand = 0x89;
    public const byte DeviceInfoCommand = 0x8F;

    private static readonly ConcurrentDictionary<string, bool> KnownWraith = new();

    public string Name => "Incott";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Dongle or Cable && FindCollections(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (KnownWraith.ContainsKey(receiver.Id))
            return [];
        var cable = receiver.ProductId == Cable;

        foreach (var collection in FindCollections(receiver))
        {
            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            var info = await QueryAsync(connection, collection.FeatureLength, DeviceInfoCommand, r => r.Length >= 4, cancellationToken);
            if (info is not null && IsWraith(info))
            {
                KnownWraith[receiver.Id] = true;
                return [];
            }

            var reply = await QueryAsync(connection, collection.FeatureLength, StatusCommand,
                r => r.Length >= 4 && (r[2] | r[3]) != 0, cancellationToken);
            if (reply is not null && ParseStatus(reply, cable) is { } reading)
                return [reading];
        }
        return [];
    }

    public static byte[] BuildRequest(byte command, int length)
    {
        var request = new byte[Math.Max(length, 2)];
        request[0] = ReportId;
        request[1] = command;
        return request;
    }

    /// <summary>True when a device-info reply names a Wraith W1 (model 18) or W1 Freestyle (model 20).</summary>
    public static bool IsWraith(ReadOnlySpan<byte> reply) =>
        reply.Length >= 4 && reply[0] == ReportId && reply[1] == DeviceInfoCommand && reply[3] is 18 or 20;

    /// <summary>Decodes a status reply; null when it is not one, stale, or out of range.</summary>
    public static HidBatteryReading? ParseStatus(ReadOnlySpan<byte> reply, bool cable)
    {
        if (reply.Length < 4 || reply[0] != ReportId || reply[1] != StatusCommand || (reply[2] | reply[3]) == 0)
            return null;
        var level = reply[2] & 0x7F;
        var charging = (reply[2] & 0x80) != 0;
        if (level > 100)
            return null;
        const string name = "Incott mouse"; // the ids are shared by the G23 V2 and the G24
        return cable || level > 0
            ? new HidBatteryReading(level, charging, name, DeviceKind.Mouse)
            : new HidBatteryReading(null, charging, name, DeviceKind.Mouse);
    }

    private static Task<byte[]?> QueryAsync(HidConnection connection, int length, byte command,
        Func<byte[], bool> extra, CancellationToken ct) =>
        GamingMouseHelpers.FeatureQueryAsync(connection, BuildRequest(command, length), ReportId, length,
            TimeSpan.FromMilliseconds(50), 10, r => r[0] == ReportId && r[1] == command && extra(r), ct);

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver) =>
        receiver.Collections.Where(c => c.UsagePage == 0xFF05 && c.FeatureLength >= 9).Reverse();
}
