using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Razer wireless headsets whose battery is read with a plain GET, without switching the dongle into another
/// mode: BlackShark V2 HyperSpeed, BlackShark V2 Pro (2020) and BlackShark V3 X. Razer mice and keyboards
/// (the 91-byte feature report) are handled by <see cref="RazerProtocol"/>.
/// </summary>
/// <remarks>
/// Three unrelated transports, all on VID 0x1532:
/// <list type="bullet">
///   <item><b>V2 HyperSpeed</b> (dongle 0x0565): 64-byte output report 0x02 on the vendor collection FF14:0001
///         (FF00:0001 on some firmware). Request <c>02 00 seq 00 00 00 04 00 00 80 cmd …</c>, byte 62 = XOR of
///         bytes 0–61; the reply echoes seq at [2] and cmd at [10], [12] = data length, [13] = value and byte 62 =
///         XOR of bytes 1–61. cmd 0x21 = battery %, 0x2A = charging. Source: github.com/justik13/razer-blackshark-v2-hyperspeed-webhid.</item>
///   <item><b>V2 Pro (2020)</b> (receiver 0x0528): feature report 0xFF (64 bytes) on interface 3, FF00:0001.
///         A fixed status request is set, then the feature report is read back until the receiver has fetched
///         the headset status. [11] = 0x09 charging, [12..13] = millivolts (0 = headset off), [14] = percent.
///         Sources: github.com/openrazer/openrazer issue #1280 and github.com/HeyOkay/HaloBattery (blackshark2020).</item>
///   <item><b>V3 X</b> (dongle 0x057D, cable 0x057C): 64-byte feature report 0x07 on interface 0, class 0x07
///         command 0x80 (battery %) / 0x84 (charging), byte 62 = XOR of bytes 3–60, status 0x02 = success,
///         0x01 = busy. Source: OpenRazer PR #2899. The headset may drop its radio link when it is queried too
///         often, so it is asked at most every five minutes and the last reading is reused in between.</item>
/// </list>
/// The BlackShark V2 Pro (2023), V3 / V3 Pro, Barracuda X and Barracuda Pro need the dongle to be switched into
/// a "remote" routing mode (or an undocumented handshake) before a query, so they are deliberately not handled.
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class RazerHeadsetProtocol : IHidBatteryProtocol
{
    public const ushort RazerVendorId = 0x1532;
    public const int FrameLength = 64;

    public const byte CommandBattery = 0x21;
    public const byte CommandCharging = 0x2A;
    public const byte V3XCommandBattery = 0x80;
    public const byte V3XCommandCharging = 0x84;

    private static readonly TimeSpan HyperSpeedTimeout = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan V3XMinInterval = TimeSpan.FromMinutes(5);

    private static readonly byte[] V2Pro2020Request = [0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05];
    private static readonly byte[] V2Pro2020FreshPrefix = [0xFF, 0x0F, 0x05, 0xFE, 0x12, 0x04, 0x1F, 0x08, 0x05];

    private static readonly ConcurrentDictionary<string, (DateTime At, HidBatteryReading Reading)> V3XCache = new();
    private static int _hyperSpeedSequence;
    private static int _v3xTransaction = 0x40;

    public enum Variant
    {
        V2HyperSpeed,
        V2Pro2020,
        V3X,
    }

    public sealed record Model(ushort ProductId, string Name, Variant Variant);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0565, "Razer BlackShark V2 HyperSpeed", Variant.V2HyperSpeed),
        new(0x0528, "Razer BlackShark V2 Pro (2020)", Variant.V2Pro2020),
        new(0x057D, "Razer BlackShark V3 X", Variant.V3X),
        new(0x057C, "Razer BlackShark V3 X", Variant.V3X),
    ];

    public string Name => "Razer headset";

    public bool CanHandle(HidReceiver receiver) => Find(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not var (model, collection))
            return [];

        var reading = model.Variant switch
        {
            Variant.V2HyperSpeed => await ReadHyperSpeedAsync(collection, cancellationToken),
            Variant.V2Pro2020 => await ReadV2Pro2020Async(collection, cancellationToken),
            Variant.V3X => await ReadV3XCachedAsync(receiver.Id, collection, cancellationToken),
            _ => null,
        };
        return reading is null ? [] : [reading with { Name = model.Name, Kind = DeviceKind.Headset }];
    }

    // ---- BlackShark V2 HyperSpeed ----

    private static async Task<HidBatteryReading?> ReadHyperSpeedAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        async Task<byte?> Query(byte command)
        {
            var sequence = NextHyperSpeedSequence();
            var reply = await HidTransact.WriteReadAsync(collection, BuildHyperSpeedRequest(command, sequence),
                f => ParseHyperSpeedReply(f, command, sequence) is not null, HyperSpeedTimeout, ct);
            return reply is null ? null : ParseHyperSpeedReply(reply, command, sequence);
        }

        if (await Query(CommandBattery) is not { } level || level > 100)
            return null;
        var charging = await Query(CommandCharging);
        return new HidBatteryReading(level, charging is null ? null : charging > 0);
    }

    private static byte NextHyperSpeedSequence() =>
        (byte)(0x60 | (Interlocked.Increment(ref _hyperSpeedSequence) & 0x1F));

    /// <summary>64-byte GET request (byte 0 = report id 0x02) for the V2 HyperSpeed dongle.</summary>
    public static byte[] BuildHyperSpeedRequest(byte command, byte sequence)
    {
        var frame = new byte[FrameLength];
        frame[0] = 0x02;
        frame[2] = sequence;
        frame[6] = 0x04;  // request size
        frame[9] = 0x80;  // forwarded from the dongle to the headset
        frame[10] = command;
        frame[62] = Xor(frame, 0, 61);
        return frame;
    }

    /// <summary>The value byte of a V2 HyperSpeed reply to <paramref name="command"/>, or null.</summary>
    public static byte? ParseHyperSpeedReply(ReadOnlySpan<byte> reply, byte command, byte sequence)
    {
        if (reply.Length < FrameLength || reply[0] != 0x02 || reply[2] != sequence || reply[10] != command ||
            reply[12] < 1 || reply[62] != Xor(reply, 1, 61))
        {
            return null;
        }
        return reply[13];
    }

    // ---- BlackShark V2 Pro (2020) ----

    private static async Task<HidBatteryReading?> ReadV2Pro2020Async(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null || !connection.SetFeature(HidTransact.Pad(V2Pro2020Request, collection.FeatureLength)))
            return null;

        const int attempts = 5;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await Task.Delay(100, ct); // the receiver needs a moment to fetch the headset status
            var reply = new byte[collection.FeatureLength];
            reply[0] = 0xFF;
            if (!connection.GetFeature(reply))
                continue;
            if (IsV2Pro2020Fresh(reply))
                return ParseV2Pro2020(reply);
            // Otherwise the previous (or "no headset") answer is still in place: read again.
        }
        return null;
    }

    private static bool IsV2Pro2020Fresh(ReadOnlySpan<byte> reply) =>
        reply.Length >= FrameLength && reply[..V2Pro2020FreshPrefix.Length].SequenceEqual(V2Pro2020FreshPrefix);

    /// <summary>
    /// Parses a fresh V2 Pro (2020) status report. Null when the headset is off. While charging the receiver's
    /// percentage is unreliable (it jumps up as soon as the cable is plugged in), so it is left out.
    /// </summary>
    public static HidBatteryReading? ParseV2Pro2020(ReadOnlySpan<byte> reply)
    {
        if (!IsV2Pro2020Fresh(reply))
            return null;
        var millivolts = (reply[12] << 8) | reply[13];
        if (millivolts == 0)
            return null;
        var charging = reply[11] == 0x09;
        if (charging)
            return new HidBatteryReading(null, true);
        return reply[14] <= 100 ? new HidBatteryReading(reply[14], false) : null;
    }

    // ---- BlackShark V3 X ----

    private static async Task<HidBatteryReading?> ReadV3XCachedAsync(string id, HidCollectionInfo collection, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (V3XCache.TryGetValue(id, out var cached) && now - cached.At < V3XMinInterval)
            return cached.Reading;

        var reading = await ReadV3XAsync(collection, ct);
        if (reading is not null)
            V3XCache[id] = (now, reading);
        return reading;
    }

    private static async Task<HidBatteryReading?> ReadV3XAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return null;

        async Task<byte?> Query(byte command)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var transaction = (byte)Interlocked.Increment(ref _v3xTransaction);
                if (!connection.SetFeature(HidTransact.Pad(BuildV3XRequest(command, transaction), collection.FeatureLength)))
                    return null;
                await Task.Delay(60, ct);
                var reply = new byte[collection.FeatureLength];
                reply[0] = 0x07;
                if (!connection.GetFeature(reply))
                    continue;
                switch (ParseV3XStatus(reply, command, transaction))
                {
                    case V3XStatus.Ok:
                        return reply[10];
                    case V3XStatus.Failed:
                        return null;
                    default:
                        continue; // busy or unrelated: ask again
                }
            }
            return null;
        }

        if (await Query(V3XCommandBattery) is not { } level || level > 100)
            return null;
        var charging = await Query(V3XCommandCharging);
        return new HidBatteryReading(level, charging is null ? null : charging == 1);
    }

    public enum V3XStatus
    {
        Ok,
        Busy,
        Failed,
        NotOurs,
    }

    /// <summary>64-byte power-class GET (byte 0 = report id 0x07) for the BlackShark V3 X.</summary>
    public static byte[] BuildV3XRequest(byte command, byte transaction)
    {
        var frame = new byte[FrameLength];
        frame[0] = 0x07;
        frame[2] = transaction;
        frame[6] = 0x02;  // argument count
        frame[7] = 0x07;  // power class
        frame[8] = command;
        frame[62] = Xor(frame, 3, 60);
        return frame;
    }

    /// <summary>Classifies a V3 X reply; on <see cref="V3XStatus.Ok"/> the value is byte 10.</summary>
    public static V3XStatus ParseV3XStatus(ReadOnlySpan<byte> reply, byte command, byte transaction)
    {
        if (reply.Length < 63 || reply[0] != 0x07 || reply[2] != transaction || reply[7] != 0x07 ||
            reply[8] != command || reply[6] < 2)
        {
            return V3XStatus.NotOurs;
        }
        return reply[1] switch
        {
            0x02 => V3XStatus.Ok,
            0x01 => V3XStatus.Busy,
            _ => V3XStatus.Failed,
        };
    }

    // ---- Helpers ----

    /// <summary>XOR of bytes <paramref name="first"/>..<paramref name="last"/> (inclusive).</summary>
    public static byte Xor(ReadOnlySpan<byte> data, int first, int last)
    {
        byte value = 0;
        for (var i = first; i <= last; i++)
            value ^= data[i];
        return value;
    }

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        if (receiver.VendorId != RazerVendorId)
            return null;
        var model = Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);
        if (model is null)
            return null;

        var collection = model.Variant switch
        {
            Variant.V2HyperSpeed => receiver.Collections
                .Where(c => (c.UsagePage == 0xFF14 || c.UsagePage == 0xFF00) && c.Usage == 0x0001 &&
                            c.OutputLength >= FrameLength && c.InputLength >= FrameLength)
                .OrderBy(c => c.UsagePage == 0xFF14 ? 0 : 1)
                .FirstOrDefault(),
            Variant.V2Pro2020 => receiver.Collections.FirstOrDefault(c =>
                c.InterfaceNumber == 3 && c.Is(0xFF00, 0x0001) && c.FeatureLength == FrameLength),
            Variant.V3X => receiver.Collections.FirstOrDefault(c =>
                c.InterfaceNumber == 0 && c.FeatureLength >= FrameLength),
            _ => null,
        };
        return collection is null ? null : (model, collection);
    }
}
