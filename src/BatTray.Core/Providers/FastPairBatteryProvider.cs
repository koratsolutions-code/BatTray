using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>
/// Decoder for the battery notification Google Fast Pair earbuds broadcast in their non-discoverable
/// advertisement (service data of UUID 0xFE2C).
/// </summary>
/// <remarks>
/// Payload after the UUID: a version byte (0x00), then fields each starting with a header byte whose high
/// nibble is the field length and low nibble the field type. Types 3 and 4 (battery, with / without UI) hold
/// one byte per part in the order left, right, case: bit 7 = charging, bits 0–6 = level 0–100
/// (0x7F = unknown). Discoverable-mode advertisements carry only a 3-byte model id and are ignored.
/// Source: Google Fast Pair specification, "Battery Notification" extension
/// (developers.google.com/nearby/fast-pair/specifications/extensions/batterynotification); cross-checked with
/// BatteryHub's notes (verified there on a Jabra Elite 8 Active).
/// </remarks>
public static class FastPairBattery
{
    private const byte BatteryShowUi = 3;
    private const byte BatteryHideUi = 4;

    /// <summary>Decodes Fast Pair service data (UUID already stripped); null when it holds no battery field.</summary>
    public static EarbudBattery? Parse(ReadOnlySpan<byte> data)
    {
        // Version 0. A discoverable advertisement is just the 3-byte model id and fails the field walk below.
        if (data.Length < 2 || data[0] != 0x00)
            return null;

        var offset = 1;
        while (offset < data.Length)
        {
            var length = data[offset] >> 4;
            var type = data[offset] & 0x0F;
            if (offset + 1 + length > data.Length)
                return null;
            if (type is BatteryShowUi or BatteryHideUi && length >= 1)
            {
                var field = data.Slice(offset + 1, length);
                var (left, leftCharging) = Part(field[0]);
                var (right, rightCharging) = length >= 2 ? Part(field[1]) : (null, false);
                var (box, boxCharging) = length >= 3 ? Part(field[2]) : (null, false);
                var battery = new EarbudBattery(left, right, box, leftCharging, rightCharging, boxCharging);
                return battery.HasAny ? battery : null;
            }
            offset += 1 + length;
        }
        return null;
    }

    private static (int? Level, bool Charging) Part(byte value)
    {
        var level = value & 0x7F;
        return level <= 100 ? (level, (value & 0x80) != 0) : (null, false);
    }
}

/// <summary>
/// Left / right / case battery of Google Fast Pair earbuds (Pixel Buds, Jabra Elite, many Sony, JBL and Bose
/// models), from their Fast Pair broadcasts. Windows itself shows at most one level for them. A broadcast is
/// only used when its sender address is the public address of a paired, connected audio device (earbuds
/// that broadcast from a rotating random address cannot be matched and are left to Windows). Listen-only.
/// </summary>
public sealed class FastPairBatteryProvider : IBatteryProvider
{
    /// <summary>Broadcasts older than this are not shown.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan FirstListen = TimeSpan.FromSeconds(3);

    private readonly BluetoothConnectionWatcher _connections;
    private readonly BleAdvertisementHub _hub = BleAdvertisementHub.Shared;
    private readonly ConcurrentDictionary<ulong, (EarbudBattery Battery, DateTimeOffset Time)> _latest = new();
    private readonly TimeProvider _time;
    private volatile HashSet<ulong> _addresses = [];
    private bool _disposed;

    public FastPairBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _connections = new BluetoothConnectionWatcher(includeClassic: true, includeLowEnergy: false);
        _connections.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _hub.Received += OnAdvertisement;
    }

    public string Name => "Fast Pair";

    public event EventHandler? Changed;

    /// <summary>Connected paired devices that may be Fast Pair earbuds (audio Class of Device, or an audio-like name).</summary>
    public static bool IsCandidate(PairedBluetoothDevice device) =>
        device.Connected &&
        !AppleProximity.IsAudioModel(device.AppleModel) &&
        DeviceKindClassifier.Resolve(
            device.ClassOfDevice is { } cod ? DeviceKindClassifier.FromClassOfDevice(cod) : DeviceKind.Other,
            device.Name) == DeviceKind.Headset;

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var connected = await _connections.GetConnectedAddressesAsync(cancellationToken);
        var paired = await PairedBluetoothDevices.FindAsync(connected, cancellationToken);
        var targets = paired.Where(IsCandidate).ToList();

        _addresses = targets.Select(d => d.Address).ToHashSet();
        if (targets.Count == 0 || _disposed)
        {
            _hub.SetWanted(this, false);
            _latest.Clear();
            return [];
        }

        if (_hub.SetWanted(this, true))
        {
            var deadline = _time.GetUtcNow() + FirstListen;
            while (_time.GetUtcNow() < deadline && !targets.Any(t => _latest.ContainsKey(t.Address)))
                await Task.Delay(200, cancellationToken);
        }

        var now = _time.GetUtcNow();
        var result = new List<Device>();
        foreach (var target in targets)
        {
            if (_latest.TryGetValue(target.Address, out var entry) && now - entry.Time <= MaxAge &&
                entry.Battery.ToDevice(target.DeviceId, target.Name, now, Name) is { } device)
            {
                result.Add(device);
            }
        }
        return result;
    }

    private void OnAdvertisement(BleAdvertisement advertisement)
    {
        if (!_addresses.Contains(advertisement.Address))
            return;
        foreach (var data in advertisement.FastPairData)
        {
            if (FastPairBattery.Parse(data) is not { } battery)
                continue;
            var changed = !_latest.TryGetValue(advertisement.Address, out var previous) || previous.Battery != battery;
            _latest[advertisement.Address] = (battery, _time.GetUtcNow());
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
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
