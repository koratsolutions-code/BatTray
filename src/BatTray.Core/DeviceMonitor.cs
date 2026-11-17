using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>
/// Consulta periódicamente todos los proveedores, combina sus resultados y notifica los cambios.
/// El evento <see cref="DevicesChanged"/> se dispara desde un hilo del pool.
/// </summary>
public sealed class DeviceMonitor : IDisposable
{
    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private IReadOnlyList<Device> _devices = [];

    public DeviceMonitor(IEnumerable<IBatteryProvider> providers, TimeSpan interval)
    {
        _providers = providers.ToList();
        _interval = interval;
    }

    public event EventHandler<IReadOnlyList<Device>>? DevicesChanged;

    /// <summary>Se dispara cuando un proveedor falla; el resto sigue funcionando.</summary>
    public event EventHandler<(IBatteryProvider Provider, Exception Error)>? ProviderFailed;

    public IReadOnlyList<Device> Devices => _devices;

    public void Start() => _ = RunAsync(_cts.Token);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(_providers.Select(p => QueryAsync(p, cancellationToken)));
        var merged = Merge(results.SelectMany(r => r));

        if (!merged.SequenceEqual(_devices))
        {
            _devices = merged;
            DevicesChanged?.Invoke(this, merged);
        }
    }

    /// <summary>
    /// Combina dispositivos de varios proveedores: si dos reportan el mismo <see cref="Device.Id"/>,
    /// gana la lectura con porcentaje conocido más reciente. Resultado ordenado por nombre.
    /// </summary>
    public static IReadOnlyList<Device> Merge(IEnumerable<Device> devices) =>
        devices
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(d => d.BatteryPercent.HasValue)
                .ThenByDescending(d => d.LastUpdated)
                .First())
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                await RefreshAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
