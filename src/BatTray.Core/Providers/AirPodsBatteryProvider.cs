using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>A decoded Apple "proximity pairing" broadcast.</summary>
/// <param name="Model">Apple model id; equals the Bluetooth Device ID PID of the paired device (e.g. 0x2014).</param>
/// <param name="Battery">Levels in 10 % steps.</param>
public sealed record AppleProximityReading(ushort Model, EarbudBattery Battery);

/// <summary>
/// Decoder for the battery status AirPods and Beats broadcast in clear for the iPhone battery popup:
/// manufacturer data of company 0x004C holding a TLV list in which type 0x07 (length 0x19) is the
/// proximity-pairing message.
/// </summary>
/// <remarks>
/// Layout of the 0x07 message, counted from its type byte:
/// <list type="bullet">
/// <item>[2] 0x01 when the accessory is paired (0x00 = pairing mode, ignored).</item>
/// <item>[3..4] model id, little-endian, 0x20xx for Apple audio products.</item>
/// <item>[5] bit 5 set when the left earbud is the one sending.</item>
/// <item>[6] low nibble = sending earbud, high nibble = the other one (0–10 = ×10 %, other values unknown).
/// Single-battery models put their level in the low nibble.</item>
/// <item>[7] high nibble = charging flags (bit 0 sending bud / single battery, bit 1 other bud, bit 2 case);
/// low nibble = case level.</item>
/// </list>
/// Sources: OpenPods, MagicPodsCore, AirPodsDesktop and librepods; cross-checked with BatteryHub's notes
/// (verified there on AirPods Max 0x200A and AirPods Pro 3 0x2027).
/// </remarks>
public static class AppleProximity
{
    private const byte MessageType = 0x07;
    private const byte MessageLength = 0x19;

    /// <summary>Headphones and neckbands with one battery (no left/right/case split).</summary>
    private static readonly HashSet<ushort> SingleBatteryModels =
    [
        0x200A, // AirPods Max (Lightning)
        0x201F, // AirPods Max (USB-C)
        0x202D, // AirPods Max 2
        0x2003, // Powerbeats 3
        0x2005, // BeatsX
        0x2006, // Beats Solo 3
        0x2009, // Beats Studio 3
        0x200C, // Beats Solo Pro
        0x200D, // Powerbeats 4
        0x2010, // Beats Flex
        0x2017, // Beats Studio Pro
        0x2025, // Beats Solo 4
    ];

    /// <summary>True for Apple audio model ids (0x20xx), the only ones whose broadcasts are used.</summary>
    public static bool IsAudioModel(ushort? model) => model is { } m && (m & 0xFF00) == 0x2000;

    public static bool IsSingleBattery(ushort model) => SingleBatteryModels.Contains(model);

    /// <summary>Decodes Apple manufacturer data (company id already stripped); null when it holds no usable status.</summary>
    public static AppleProximityReading? Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset + 2 <= data.Length)
        {
            var type = data[offset];
            var length = data[offset + 1];
            if (offset + 2 + length > data.Length)
                return null;
            if (type == MessageType && ParseMessage(data.Slice(offset, 2 + length)) is { } reading)
                return reading;
            offset += 2 + length;
        }
        return null;
    }

    private static AppleProximityReading? ParseMessage(ReadOnlySpan<byte> m)
    {
        if (m.Length < 2 + MessageLength || m[1] != MessageLength || m[2] != 0x01)
            return null;
        var model = (ushort)(m[3] | m[4] << 8);
        if (!IsAudioModel(model))
            return null;

        var sender = m[6] & 0x0F;
        var other = m[6] >> 4;
        var chargingFlags = m[7] >> 4;

        if (IsSingleBattery(model))
        {
            var level = Level(sender);
            var charging = (chargingFlags & 0x01) != 0;
            // Some single-battery models report in the other nibble with the sender nibble left at 0.
            if (sender == 0 && other is >= 1 and <= 10)
            {
                level = Level(other);
                charging = (chargingFlags & 0x02) != 0;
            }
            return level is { } l ? new AppleProximityReading(model, EarbudBattery.Single(l, charging)) : null;
        }

        var leftSends = (m[5] & 0x20) != 0;
        var left = Level(leftSends ? sender : other);
        var right = Level(leftSends ? other : sender);
        var box = Level(m[7] & 0x0F);
        var battery = new EarbudBattery(
            left, right, box,
            LeftCharging: left is not null && (chargingFlags & (leftSends ? 0x01 : 0x02)) != 0,
            RightCharging: right is not null && (chargingFlags & (leftSends ? 0x02 : 0x01)) != 0,
            CaseCharging: box is not null && (chargingFlags & 0x04) != 0);
        return battery.HasAny ? new AppleProximityReading(model, battery) : null;
    }

    private static int? Level(int nibble) => nibble <= 10 ? nibble * 10 : null;
}

/// <summary>
/// Recent proximity broadcasts by sender address (addresses rotate), and the choice of the one that
/// belongs to the user's device. Thread-safe.
/// </summary>
public sealed class AppleProximityCache
{
    /// <summary>Broadcasts older than this are not used.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>A broadcast this recent is preferred over older ones regardless of signal.</summary>
    public static readonly TimeSpan FreshAge = TimeSpan.FromSeconds(90);

    /// <summary>Weaker signals are ignored: they are most likely somebody else's earbuds.</summary>
    public const int MinRssi = -90;

    /// <summary>Two earbuds of the same set broadcast separately; their messages are combined within this window.</summary>
    public static readonly TimeSpan CombineWindow = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<ulong, Entry> _entries = new();

    /// <summary>Stores a broadcast; returns true when its levels differ from the previous one of that sender.</summary>
    public bool Add(ulong address, int rssi, DateTimeOffset time, AppleProximityReading reading)
    {
        var changed = !_entries.TryGetValue(address, out var previous) || previous.Reading != reading;
        _entries[address] = new Entry(reading, rssi, time);
        foreach (var (key, entry) in _entries)
        {
            if (time - entry.Time > ForgetAfter)
                _entries.TryRemove(key, out _);
        }
        return changed;
    }

    /// <summary>
    /// The levels of the device with this model id: among recent broadcasts of that model with a usable signal,
    /// the strongest fresh one wins (or the latest when none is fresh); missing parts are filled from the other
    /// earbud's broadcast heard at about the same time. Null when nothing matches.
    /// </summary>
    public EarbudBattery? Pick(ushort model, DateTimeOffset now)
    {
        var candidates = _entries.Values
            .Where(e => e.Reading.Model == model && now - e.Time <= MaxAge && e.Rssi >= MinRssi)
            .ToList();
        if (candidates.Count == 0)
            return null;

        var best = candidates
            .OrderByDescending(e => now - e.Time <= FreshAge)
            .ThenByDescending(e => now - e.Time <= FreshAge ? e.Rssi : 0)
            .ThenByDescending(e => e.Time)
            .First();

        var battery = best.Reading.Battery;
        if (battery.SingleBattery)
            return battery;
        foreach (var other in candidates.Where(e => !ReferenceEquals(e, best)).OrderByDescending(e => e.Time))
        {
            if ((other.Time - best.Time).Duration() <= CombineWindow)
                battery = battery.FillFrom(other.Reading.Battery);
        }
        return battery;
    }

    private sealed record Entry(AppleProximityReading Reading, int Rssi, DateTimeOffset Time);
}

/// <summary>
/// Battery of Apple AirPods and Beats, which Windows does not read: taken from the proximity-pairing
/// broadcasts they send several times a second (see <see cref="AppleProximity"/>). Only broadcasts whose
/// model id matches a paired, connected Apple audio device are used; nothing is ever sent to the device.
/// </summary>
public sealed class AirPodsBatteryProvider : IBatteryProvider
{
    private static readonly TimeSpan FirstListen = TimeSpan.FromSeconds(3);

    private readonly BluetoothConnectionWatcher _connections;
    private readonly BleAdvertisementHub _hub = BleAdvertisementHub.Shared;
    private readonly AppleProximityCache _cache = new();
    private readonly TimeProvider _time;
    private volatile HashSet<ushort> _models = [];
    private bool _disposed;

    public AirPodsBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _connections = new BluetoothConnectionWatcher(includeClassic: true, includeLowEnergy: false);
        _connections.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _hub.Received += OnAdvertisement;
    }

    public string Name => "AirPods";

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var connected = await _connections.GetConnectedAddressesAsync(cancellationToken);
        var paired = await PairedBluetoothDevices.FindAsync(connected, cancellationToken);
        var targets = paired.Where(d => d.Connected && AppleProximity.IsAudioModel(d.AppleModel)).ToList();

        _models = targets.Select(d => d.AppleModel!.Value).ToHashSet();
        if (targets.Count == 0 || _disposed)
        {
            _hub.SetWanted(this, false);
            return [];
        }

        if (_hub.SetWanted(this, true))
        {
            // Just started listening: the earbuds broadcast about every 1–2 s.
            var deadline = _time.GetUtcNow() + FirstListen;
            while (_time.GetUtcNow() < deadline && targets.All(t => _cache.Pick(t.AppleModel!.Value, _time.GetUtcNow()) is null))
                await Task.Delay(200, cancellationToken);
        }

        var now = _time.GetUtcNow();
        var result = new List<Device>();
        foreach (var target in targets)
        {
            if (_cache.Pick(target.AppleModel!.Value, now)?.ToDevice(target.DeviceId, target.Name, now, Name) is { } device)
                result.Add(device);
        }
        return result;
    }

    private void OnAdvertisement(BleAdvertisement advertisement)
    {
        var models = _models;
        if (models.Count == 0)
            return;
        foreach (var data in advertisement.AppleData)
        {
            if (AppleProximity.Parse(data) is { } reading && models.Contains(reading.Model) &&
                _cache.Add(advertisement.Address, advertisement.Rssi, _time.GetUtcNow(), reading))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _hub.Received -= OnAdvertisement;
        _hub.SetWanted(this, false);
        _connections.Dispose();
    }
}
