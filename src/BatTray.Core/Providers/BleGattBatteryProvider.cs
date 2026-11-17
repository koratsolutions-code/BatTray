using System.Collections.Concurrent;
using BatTray.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BatTray.Core.Providers;

/// <summary>
/// Reads the standard GATT Battery Service (0x180F, characteristic 0x2A19) of connected Bluetooth LE
/// devices, and subscribes to its notifications when the device supports them.
/// </summary>
public sealed class BleGattBatteryProvider : IBatteryProvider
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BluetoothConnectionWatcher _connections;
    private readonly TimeProvider _time;
    private bool _disposed;

    public BleGattBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _connections = new BluetoothConnectionWatcher(includeClassic: false, includeLowEnergy: true);
        _connections.Changed += (_, _) => RaiseChanged();
    }

    public string Name => "Bluetooth LE";

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var selector = GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.Battery);
            var services = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);

            foreach (var info in services)
            {
                seen.Add(info.Id);
                var entry = await GetEntryAsync(info, cancellationToken);
                if (entry is null)
                    continue;

                if (entry.Device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    // Asleep/disconnected: no reading, but its type (from the GAP Appearance) still helps the
                    // monitor label a reading from another provider. Devices nobody measures are dropped.
                    result.TryAdd(entry.DeviceId, new Device(entry.DeviceId, entry.Name, entry.Kind, null, _time.GetUtcNow(), Name));
                    continue;
                }

                var percent = await entry.GetPercentAsync(cancellationToken);
                // A device may expose several battery services: keep the first reading per device.
                if (percent is not null)
                    result.Remove(entry.DeviceId);
                result.TryAdd(entry.DeviceId, new Device(entry.DeviceId, entry.Name, entry.Kind, percent, _time.GetUtcNow(), Name));
            }

            foreach (var stale in _entries.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                if (_entries.TryRemove(stale, out var removed))
                    removed.Dispose();
            }

            return result.Values.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Entry?> GetEntryAsync(DeviceInformation info, CancellationToken ct)
    {
        if (_entries.TryGetValue(info.Id, out var existing))
        {
            if (!existing.NeedsReopen || existing.Device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return existing;

            // The device reconnected: GATT objects from the previous session may be stale.
            _entries.TryRemove(info.Id, out _);
            existing.Dispose();
        }

        GattDeviceService? service = null;
        try
        {
            service = await GattDeviceService.FromIdAsync(info.Id).AsTask(ct);
            if (service?.Device is null)
            {
                service?.Dispose();
                return null;
            }

            var entry = new Entry(service, RaiseChanged);
            _entries[info.Id] = entry;
            CoreDiagnostics.Write($"[{Name}] Battery service found: {entry.Name} ({entry.DeviceId}, {entry.Kind}).");
            return entry;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CoreDiagnostics.Write($"[{Name}] Could not open battery service {info.Name}: {ex.Message}");
            service?.Dispose();
            return null;
        }
    }

    private void RaiseChanged()
    {
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        _connections.Dispose();
        foreach (var entry in _entries.Values)
            entry.Dispose();
        _entries.Clear();
    }

    /// <summary>An opened battery service of one LE device.</summary>
    private sealed class Entry : IDisposable
    {
        private readonly GattDeviceService _service;
        private readonly Action _onChanged;
        private GattCharacteristic? _characteristic;
        private bool _subscribed;
        private bool _subscribeAttempted;
        private int? _lastPercent;
        private string? _lastError;

        public Entry(GattDeviceService service, Action onChanged)
        {
            _service = service;
            _onChanged = onChanged;
            Device = service.Device;
            DeviceId = BluetoothAddress.ToDeviceId(Device.BluetoothAddress);
            Name = string.IsNullOrWhiteSpace(Device.Name) ? "Bluetooth LE device" : Device.Name.Trim();
            Kind = DeviceKindClassifier.Resolve(
                DeviceKindClassifier.FromBleAppearance(Device.Appearance?.RawValue ?? 0), Name);
            Device.ConnectionStatusChanged += OnConnectionStatusChanged;
        }

        public BluetoothLEDevice Device { get; }
        public string DeviceId { get; }
        public string Name { get; }
        public DeviceKind Kind { get; }
        public bool NeedsReopen { get; private set; }

        public async Task<int?> GetPercentAsync(CancellationToken ct)
        {
            try
            {
                _characteristic ??= await FindCharacteristicAsync(ct);
                if (_characteristic is null)
                    return null;

                if (!_subscribeAttempted)
                {
                    _subscribeAttempted = true;
                    await TrySubscribeAsync(ct);
                }

                // With notifications active the cached value stays fresh; otherwise read it.
                if (_subscribed && _lastPercent is not null)
                    return _lastPercent;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ReadTimeout);
                var read = await _characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                if (read.Status == GattCommunicationStatus.Success)
                {
                    _lastPercent = Parse(read.Value);
                    _lastError = null;
                }
                else
                {
                    Report($"read failed: {read.Status}");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Report("read timed out");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report(ex.Message);
            }

            return _lastPercent;
        }

        private async Task<GattCharacteristic?> FindCharacteristicAsync(CancellationToken ct)
        {
            var result = await _service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                .AsTask(ct);

            if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            {
                Report($"battery level characteristic unavailable: {result.Status}");
                return null;
            }

            return result.Characteristics[0];
        }

        private async Task TrySubscribeAsync(CancellationToken ct)
        {
            if (_characteristic is null || !_characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                return;

            try
            {
                _characteristic.ValueChanged += OnValueChanged;
                var status = await _characteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(ct);
                _subscribed = status == GattCommunicationStatus.Success;
                if (!_subscribed)
                {
                    _characteristic.ValueChanged -= OnValueChanged;
                    Report($"notifications unavailable: {status}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _characteristic.ValueChanged -= OnValueChanged;
                Report($"notifications unavailable: {ex.Message}");
            }
        }

        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var percent = Parse(args.CharacteristicValue);
            if (percent == _lastPercent)
                return;

            _lastPercent = percent;
            _onChanged();
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                // Subscriptions do not survive a disconnection; reopen everything on reconnect.
                NeedsReopen = true;
                _subscribed = false;
            }
            _onChanged();
        }

        private static int? Parse(IBuffer buffer)
        {
            if (buffer is null || buffer.Length < 1)
                return null;
            using var reader = DataReader.FromBuffer(buffer);
            var value = reader.ReadByte();
            return value <= 100 ? value : null;
        }

        private void Report(string error)
        {
            // Log each distinct problem once, not on every poll.
            if (error == _lastError)
                return;
            _lastError = error;
            CoreDiagnostics.Write($"[Bluetooth LE] {Name} ({DeviceId}): {error}");
        }

        public void Dispose()
        {
            Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            if (_characteristic is not null)
                _characteristic.ValueChanged -= OnValueChanged;
            _service.Dispose();
            Device.Dispose();
        }
    }
}
