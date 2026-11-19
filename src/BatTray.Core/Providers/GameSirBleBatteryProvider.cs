using System.Runtime.InteropServices.WindowsRuntime;
using BatTray.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BatTray.Core.Providers;

/// <summary>
/// GameSir Cyclone Pro (T4cp). On its 2.4 GHz dongle (3537:1015) Windows only sees a wired Xbox 360 pad without a
/// real level, but the pad keeps a Bluetooth LE link as "GameSir-Cyclone Pro" and answers GameSir's phone-app
/// "get base info" request there. The pad has to be paired with Windows once.
/// </summary>
/// <remarks>
/// GATT service 0x8650, characteristic 0x865F: notifications are switched on, <c>01 01</c> ("get base info", a
/// read-only request) is written, and the pad notifies a 19-byte packet (20 with a leading 0xC9 on some
/// firmware): MAC [0..5], hardware/BT/firmware versions [6..11], build date [12..17], battery [18] = 0–100 %, or
/// 0xFF while charging (no level then). Notifications are switched off again afterwards. Service 0xFF10 (firmware
/// update) is never touched. Source: github.com/bpbp-boop/GameSirBattery (from GameSir's Android app); cross-checked
/// with BatteryHub's notes (tested there on a real pad).
/// Connecting can take seconds, so a query runs at most every 2 minutes (1 minute after a failure) in the
/// background; a poll waits for it at most <see cref="PollWait"/> and otherwise reports the cached level.
/// </remarks>
public sealed class GameSirBleBatteryProvider : IBatteryProvider
{
    public static readonly Guid InfoServiceUuid = new("00008650-0000-1000-8000-00805f9b34fb");
    public static readonly Guid InfoCharacteristicUuid = new("0000865f-0000-1000-8000-00805f9b34fb");
    public const string NamePrefix = "GameSir-Cyclone";
    public const byte ChargingMarker = 0xFF;

    private static readonly TimeSpan QueryEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollWait = TimeSpan.FromSeconds(4);

    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, PadState> _pads = new(StringComparer.OrdinalIgnoreCase);

    public GameSirBleBatteryProvider(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public string Name => "GameSir (Bluetooth LE)";

    /// <summary>Readings are picked up by the regular poll.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <summary>A decoded base-info packet: Percent is null while charging.</summary>
    public sealed record BaseInfo(int? Percent, bool Charging);

    /// <summary>Decodes a base-info notification; null for anything else.</summary>
    public static BaseInfo? ParseBaseInfo(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 20 && packet[0] == 0xC9)
            packet = packet[1..];
        if (packet.Length != 19)
            return null;
        var battery = packet[18];
        if (battery == ChargingMarker)
            return new BaseInfo(null, true);
        return battery <= 100 ? new BaseInfo(battery, false) : null;
    }

    public static bool IsCyclonePad(string? name) =>
        name is not null && name.TrimStart().StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        DeviceInformationCollection services;
        try
        {
            var selector = GattDeviceService.GetDeviceSelectorFromUuid(InfoServiceUuid);
            services = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CoreDiagnostics.Write($"[{Name}] Service lookup failed: {ex.Message}");
            return [];
        }

        var now = _time.GetUtcNow();
        var pending = new List<Task>();
        var states = new List<PadState>();
        lock (_lock)
        {
            foreach (var info in services)
            {
                if (!_pads.TryGetValue(info.Id, out var state))
                {
                    state = new PadState(info.Id);
                    _pads[info.Id] = state;
                }
                states.Add(state);

                var due = state.LastAttempt is not { } last ||
                          now - last >= (state.LastSuccess >= last ? QueryEvery : RetryEvery);
                if (state.Running is null && due)
                {
                    state.LastAttempt = now;
                    state.Running = Task.Run(() => QueryAsync(state, CancellationToken.None), CancellationToken.None);
                }
                if (state.Running is { } running)
                    pending.Add(running);
            }
        }

        if (pending.Count > 0)
        {
            try
            {
                await Task.WhenAny(Task.WhenAll(pending), Task.Delay(PollWait, cancellationToken));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        var result = new List<Device>();
        now = _time.GetUtcNow();
        lock (_lock)
        {
            foreach (var state in states)
            {
                if (state.DeviceId is null || state.Name is null || state.LastSuccess is not { } ok || now - ok >= StaleAfter)
                    continue;
                if (state.Percent is null)
                    continue; // charging since start: no level known yet
                result.Add(new Device(state.DeviceId, state.Name, DeviceKind.Gamepad, state.Percent, ok, Name, state.Charging));
            }
        }
        return result;
    }

    private async Task QueryAsync(PadState state, CancellationToken ct)
    {
        try
        {
            var reading = await ReadOnceAsync(state, ct);
            lock (_lock)
            {
                if (reading is not null)
                {
                    // While charging the pad sends no level: keep the last one next to the charging flag.
                    state.Percent = reading.Percent ?? state.Percent;
                    state.Charging = reading.Charging;
                    state.LastSuccess = _time.GetUtcNow();
                }
            }
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[{Name}] {state.Name ?? state.ServiceId}: {ex.Message}");
        }
        finally
        {
            lock (_lock)
                state.Running = null;
        }
    }

    private async Task<BaseInfo?> ReadOnceAsync(PadState state, CancellationToken ct)
    {
        using var service = await GattDeviceService.FromIdAsync(state.ServiceId).AsTask(ct);
        if (service?.Device is not { } device)
            return null;

        using (device)
        {
            if (!IsCyclonePad(device.Name))
                return null;
            lock (_lock)
            {
                state.Name = "GameSir Cyclone Pro";
                state.DeviceId = BluetoothAddress.ToDeviceId(device.BluetoothAddress);
            }

            var characteristics = await service
                .GetCharacteristicsForUuidAsync(InfoCharacteristicUuid, BluetoothCacheMode.Uncached).AsTask(ct);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                return null;

            var characteristic = characteristics.Characteristics[0];
            if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                return null;

            var answer = new TaskCompletionSource<BaseInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnValue(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                if (ParseBaseInfo(args.CharacteristicValue.ToArray()) is { } info)
                    answer.TrySetResult(info);
            }

            characteristic.ValueChanged += OnValue;
            try
            {
                var subscribed = await characteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(ct);
                if (subscribed != GattCommunicationStatus.Success)
                    return null;

                try
                {
                    var option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                        ? GattWriteOption.WriteWithoutResponse
                        : GattWriteOption.WriteWithResponse;
                    var written = await characteristic.WriteValueAsync(new byte[] { 0x01, 0x01 }.AsBuffer(), option).AsTask(ct);
                    if (written != GattCommunicationStatus.Success)
                        return null;

                    var finished = await Task.WhenAny(answer.Task, Task.Delay(NotificationTimeout, ct));
                    return finished == answer.Task ? answer.Task.Result : null;
                }
                finally
                {
                    await characteristic
                        .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                        .AsTask(CancellationToken.None);
                }
            }
            finally
            {
                characteristic.ValueChanged -= OnValue;
            }
        }
    }

    public void Dispose()
    {
    }

    private sealed class PadState(string serviceId)
    {
        public string ServiceId { get; } = serviceId;
        public string? Name { get; set; }
        public string? DeviceId { get; set; }
        public int? Percent { get; set; }
        public bool Charging { get; set; }
        public DateTimeOffset? LastAttempt { get; set; }
        public DateTimeOffset? LastSuccess { get; set; }
        public Task? Running { get; set; }
    }
}
