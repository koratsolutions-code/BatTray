using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>
/// Base for earbuds read over a vendor RFCOMM service. Only paired devices that Windows reports as connected
/// and that the subclass recognises are tried; the sessions run in the background so a poll waits at most a
/// few seconds, and a reading is kept for a while when a session fails (e.g. the vendor app holds the channel).
/// </summary>
public abstract class RfcommEarbudsProvider : IBatteryProvider
{
    /// <summary>A poll waits at most this long for the RFCOMM sessions.</summary>
    private static readonly TimeSpan MaxPollWait = TimeSpan.FromSeconds(3);

    /// <summary>The sessions are not repeated more often than this.</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(50);

    /// <summary>How long the last reading is kept when a session fails while the earbuds stay connected.</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(3);

    private readonly BluetoothConnectionWatcher _connections;
    private readonly BackgroundRefresh<IReadOnlyDictionary<ulong, Reading>> _refresh;
    private readonly TimeProvider _time;
    private volatile IReadOnlyList<PairedBluetoothDevice> _targets = [];

    protected RfcommEarbudsProvider(TimeProvider? timeProvider)
    {
        _time = timeProvider ?? TimeProvider.System;
        _connections = new BluetoothConnectionWatcher(includeClassic: true, includeLowEnergy: false);
        _connections.Changed += (_, _) =>
        {
            _refresh?.Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        _refresh = new BackgroundRefresh<IReadOnlyDictionary<ulong, Reading>>(
            ReadAllAsync, new Dictionary<ulong, Reading>(), MinInterval, _time);
        _refresh.Completed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public abstract string Name { get; }

    public event EventHandler? Changed;

    /// <summary>True when a paired device with this name may be one of this provider's earbuds.</summary>
    protected abstract bool IsCandidate(string name);

    /// <summary>Opens the vendor service and reads the levels; null when it could not.</summary>
    protected abstract Task<EarbudBattery?> ReadAsync(ulong address, CancellationToken cancellationToken);

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var connected = await _connections.GetConnectedAddressesAsync(cancellationToken);
        if (connected is null || connected.Count == 0)
        {
            _targets = [];
            return [];
        }

        var paired = await PairedBluetoothDevices.FindAsync(connected, cancellationToken);
        var targets = paired.Where(d => d.Connected && IsCandidate(d.Name)).ToList();
        _targets = targets;
        if (targets.Count == 0)
            return [];

        var readings = await _refresh.GetAsync(MaxPollWait, cancellationToken);
        var now = _time.GetUtcNow();
        var result = new List<Device>();
        foreach (var target in targets)
        {
            if (readings.TryGetValue(target.Address, out var reading) && now - reading.Time <= GracePeriod &&
                reading.Battery.ToDevice(target.DeviceId, target.Name, now, Name) is { } device)
            {
                result.Add(device);
            }
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<ulong, Reading>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var previous = _refresh.Last;
        var result = new Dictionary<ulong, Reading>();
        foreach (var target in _targets)
        {
            EarbudBattery? battery = null;
            try
            {
                battery = await ReadAsync(target.Address, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CoreDiagnostics.Write($"[{Name}] {target.Name}: {ex.Message}");
            }

            if (battery is not null)
                result[target.Address] = new Reading(battery, _time.GetUtcNow());
            else if (previous.TryGetValue(target.Address, out var old))
                result[target.Address] = old; // GetDevicesAsync drops it once the grace period is over
        }
        return result;
    }

    public void Dispose()
    {
        _refresh.Dispose();
        _connections.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Reading(EarbudBattery Battery, DateTimeOffset Time);
}
