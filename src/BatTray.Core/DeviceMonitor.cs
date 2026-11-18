using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>
/// Polls every provider, merges their results and reports changes. Providers can also request an
/// immediate refresh through <see cref="IBatteryProvider.Changed"/>.
/// <see cref="DevicesChanged"/> is raised on a thread-pool thread.
/// </summary>
public sealed class DeviceMonitor : IDisposable
{
    /// <summary>Delay applied after a provider signals a change, so bursts of events cause a single refresh.</summary>
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(500);

    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<Device> _devices = [];

    public DeviceMonitor(IEnumerable<IBatteryProvider> providers, TimeSpan interval)
    {
        _providers = providers.ToList();
        _interval = interval;
        foreach (var provider in _providers)
            provider.Changed += OnProviderChanged;
    }

    public event EventHandler<IReadOnlyList<Device>>? DevicesChanged;

    /// <summary>Raised when a provider fails; the remaining providers keep working.</summary>
    public event EventHandler<(IBatteryProvider Provider, Exception Error)>? ProviderFailed;

    public IReadOnlyList<Device> Devices => _devices;

    public void Start() => _ = RunAsync(_cts.Token);

    /// <summary>Asks the background loop to refresh as soon as possible.</summary>
    public void RequestRefresh()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var results = await Task.WhenAll(_providers.Select(p => QueryAsync(p, cancellationToken)));
            var merged = Merge(results.SelectMany(r => r));

            if (!merged.SequenceEqual(_devices))
            {
                _devices = merged;
                DevicesChanged?.Invoke(this, merged);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Merges devices reported by several providers: when two share the same <see cref="Device.Id"/>,
    /// the reading with a known percentage wins, then the most recent one. A known
    /// <see cref="DeviceKind"/> reported by any provider is kept even if the winning reading has none.
    /// Devices no provider could measure are dropped. Result is sorted by name.
    /// </summary>
    public static IReadOnlyList<Device> Merge(IEnumerable<Device> devices) =>
        MergeSameNamedControllers(MergeById(devices))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// A Bluetooth controller can be reported both by its Bluetooth address and by Windows.Gaming.Input
    /// under a different id; game controllers with the same name are treated as one device.
    /// </summary>
    private static IEnumerable<Device> MergeSameNamedControllers(IEnumerable<Device> devices) =>
        devices
            .GroupBy(d => d.Kind == DeviceKind.Gamepad ? "gamepad:" + d.Name.ToUpperInvariant() : "id:" + d.Id)
            .Select(g => g
                .OrderByDescending(d => d.Charging.HasValue)
                .ThenByDescending(d => d.LastUpdated)
                .First());

    private static IEnumerable<Device> MergeById(IEnumerable<Device> devices) =>
        devices
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(d => d.BatteryPercent.HasValue))
            .Select(g =>
            {
                var winner = g
                    .OrderByDescending(d => d.BatteryPercent.HasValue)
                    .ThenByDescending(d => d.LastUpdated)
                    .First();
                var knownKind = g.Select(d => d.Kind).FirstOrDefault(k => k != DeviceKind.Other);
                return winner.Kind == DeviceKind.Other && knownKind != DeviceKind.Other
                    ? winner with { Kind = knownKind }
                    : winner;
            });

    private void OnProviderChanged(object? sender, EventArgs e) => RequestRefresh();

    private async Task<IReadOnlyList<Device>> QueryAsync(IBatteryProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.GetDevicesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception ex)
        {
            ProviderFailed?.Invoke(this, (provider, ex));
            return [];
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshAsync(ct);

                var woken = await _wake.WaitAsync(_interval, ct);
                if (woken)
                {
                    await Task.Delay(ChangeDebounce, ct);
                    while (_wake.CurrentCount > 0)
                        _wake.Wait(0, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Changed -= OnProviderChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
