using System.Collections.Concurrent;
using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;
using Windows.Devices.Enumeration;

namespace BatTray.Core.Providers;

/// <summary>
/// Reads the battery of USB wireless receivers (2.4 GHz dongles) through pluggable
/// <see cref="IHidBatteryProtocol"/> implementations. Receivers no protocol recognises are
/// recorded as unsupported (and never sent anything) so they can be reported and added later.
/// </summary>
public sealed class HidDongleBatteryProvider : IBatteryProvider, IDiagnosticsSource
{
    /// <summary>How long a reading is kept when the peripheral stops answering (it may just be asleep).</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(30);

    private readonly IReadOnlyList<IHidBatteryProtocol> _protocols;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Device> _lastGood = new();
    private readonly ConcurrentDictionary<string, byte> _loggedUnsupported = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DeviceWatcher? _watcher;
    private volatile bool _watcherReady;
    private IReadOnlyList<HidReceiver> _lastReceivers = [];

    public HidDongleBatteryProvider(IEnumerable<IHidBatteryProtocol>? protocols = null, TimeProvider? timeProvider = null)
    {
        _protocols = protocols?.ToList() ?? DefaultProtocols();
        _time = timeProvider ?? TimeProvider.System;
        _watcher = CreateWatcher();
    }

    public static IReadOnlyList<IHidBatteryProtocol> DefaultProtocols() =>
    [
        new LogitechHidppProtocol(),
        new RazerProtocol(),
        new SteelSeriesProtocol(),
        new CorsairProtocol(),
        new HyperXProtocol(),
        new SinoWealthAuroraProtocol(),
        new MonsGeekDongleProtocol(),
        new KeychronProtocol(),
        new RapooProtocol(),
        new AjazzProtocol(),
        new DarmosharkProtocol(),
        new MchoseProtocol(),
        new GetPowerProtocol(),
        new StandardHidBatteryProtocol(),
    ];

    public string Name => "USB receiver";

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var receivers = await Task.Run(HidEnumerator.EnumerateUsbReceivers, cancellationToken);
            _lastReceivers = receivers;

            var now = _time.GetUtcNow();
            var result = new List<Device>();

            foreach (var receiver in receivers)
            {
                var protocol = _protocols.FirstOrDefault(p => SafeCanHandle(p, receiver));
                if (protocol is null)
                {
                    LogUnsupportedOnce(receiver);
                    continue;
                }

                IReadOnlyList<HidBatteryReading> readings = [];
                try
                {
                    readings = await Task.Run(() => protocol.ReadAsync(receiver, cancellationToken), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CoreDiagnostics.Write($"[{Name}] {protocol.Name} failed on {receiver}: {ex.Message}");
                }

                var answered = new HashSet<string>();
                foreach (var reading in readings)
                {
                    if (reading.Percent is not { } percent)
                        continue;

                    var id = reading.SubId is null ? receiver.Id : $"{receiver.Id}:{reading.SubId}";
                    var name = reading.Name ?? receiver.DisplayName;
                    var kind = reading.Kind ?? DeviceKindClassifier.Resolve(receiver.InferKind(), name);
                    var device = new Device(id, name, kind, percent, now, $"{Name} · {protocol.Name}", reading.Charging);
                    _lastGood[id] = device;
                    answered.Add(id);
                    result.Add(device);
                }

                // Peripherals that did not answer may just be asleep: keep their last level for a while.
                foreach (var (id, last) in _lastGood)
                {
                    if (!answered.Contains(id) && BelongsTo(id, receiver) && now - last.LastUpdated < GracePeriod)
                        result.Add(last);
                }
            }

            foreach (var gone in _lastGood.Keys.Where(k => !receivers.Any(r => BelongsTo(k, r))).ToList())
                _lastGood.TryRemove(gone, out _);

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<string> DescribeDevices()
    {
        var lines = new List<string>();
        foreach (var receiver in _lastReceivers)
        {
            var protocol = _protocols.FirstOrDefault(p => SafeCanHandle(p, receiver));
            lines.Add($"{receiver} mfr='{receiver.Manufacturer}' product='{receiver.Product}' kind={receiver.InferKind()} " +
                      $"protocol={(protocol?.Name ?? "UNSUPPORTED")}");
            foreach (var c in receiver.Collections)
            {
                var caps = c.ValueCaps
                    .Where(v => v.UsagePage is 0x06 or 0x84 or 0x85 or >= 0xFF00)
                    .Select(v => $"{v.Type} {v.UsagePage:X4}/{v.Usage:X4} id={v.ReportId}");
                lines.Add($"    {c}{(caps.Any() ? " | " + string.Join(", ", caps) : string.Empty)}");
            }
        }
        return lines;
    }

    private void LogUnsupportedOnce(HidReceiver receiver)
    {
        if (!_loggedUnsupported.TryAdd(receiver.Id, 0))
            return;
        var vendorChannels = receiver.Collections.Count(c => c.UsagePage >= 0xFF00);
        CoreDiagnostics.Write(
            $"[{Name}] No battery protocol for {receiver} (mfr='{receiver.Manufacturer}', " +
            $"{receiver.Collections.Count} collections, {vendorChannels} vendor-defined). Nothing was sent to it.");
    }

    private static bool BelongsTo(string deviceId, HidReceiver receiver) =>
        deviceId == receiver.Id || deviceId.StartsWith(receiver.Id + ":", StringComparison.Ordinal);

    private static bool SafeCanHandle(IHidBatteryProtocol protocol, HidReceiver receiver)
    {
        try
        {
            return protocol.CanHandle(receiver);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private DeviceWatcher? CreateWatcher()
    {
        try
        {
            var selector = $"System.Devices.InterfaceClassGuid:=\"{{{HidNative.HidInterfaceClass}}}\" AND " +
                           "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
            var watcher = DeviceInformation.CreateWatcher(selector);
            watcher.Added += (_, _) => RaiseIfReady();
            watcher.Removed += (_, _) => RaiseIfReady();
            watcher.Updated += (_, _) => { };
            watcher.EnumerationCompleted += (_, _) => _watcherReady = true;
            watcher.Start();
            return watcher;
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[{Name}] HID arrival watcher unavailable: {ex.Message}");
            return null;
        }
    }

    private void RaiseIfReady()
    {
        if (_watcherReady)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        try
        {
            if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();
        }
        catch (Exception)
        {
            // Already stopping.
        }
    }
}
