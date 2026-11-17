using System.Windows;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using BatTray.App.Taskbar;
using BatTray.App.Theme;
using BatTray.App.ViewModels;
using BatTray.Core;
using BatTray.Core.Models;
using BatTray.Core.Providers;

namespace BatTray.App;

/// <summary>
/// Entry point. Command-line options:
///   --mode child|overlay   taskbar anchoring strategy (default: child)
///   --demo                 simulated devices instead of real Bluetooth readings
///   --list-devices         log one reading of every provider and exit (diagnostics)
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\KoratSolutions.BatTray";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DemoPollInterval = TimeSpan.FromSeconds(5);

    private readonly WidgetViewModel _viewModel = new();
    private Mutex? _singleInstance;
    private ThemeWatcher? _theme;
    private TaskbarWatcher? _watcher;
    private TaskbarWidgetHost? _host;
    private TaskButtonsProbe? _buttonsProbe;
    private DeviceMonitor? _monitor;
    private IReadOnlyList<IBatteryProvider> _providers = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        CoreDiagnostics.Sink = Log.Info;

        var demo = HasFlag(e.Args, "--demo");

        if (HasFlag(e.Args, "--list-devices"))
        {
            await ListDevicesAsync(demo);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            Log.Info("Another BatTray instance is already running; exiting.");
            Shutdown();
            return;
        }

        var mode = ParseMode(e.Args);
        Log.Info($"BatTray {typeof(App).Assembly.GetName().Version} starting (mode {mode}{(demo ? ", demo" : string.Empty)}).");

        _theme = new ThemeWatcher(Resources);

        var view = new TaskbarWidgetView { DataContext = _viewModel };
        _host = new TaskbarWidgetHost(mode, view, _viewModel);

        _watcher = new TaskbarWatcher();
        _watcher.Changed += _host.Update;
        _watcher.ForegroundChanged += _host.BringToFront;
        _theme.ThemeChanged += _host.Reposition;

        _buttonsProbe = new TaskButtonsProbe(Dispatcher);
        _buttonsProbe.Changed += _host.SetTaskButtonsRight;

        _providers = CreateProviders(demo);
        _monitor = new DeviceMonitor(_providers, demo ? DemoPollInterval : PollInterval);
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.ProviderFailed += (_, f) => Log.Error($"Provider '{f.Provider.Name}' failed.", f.Error);

        _watcher.Start();
        _buttonsProbe.Start();
        _monitor.Start();
    }

    private static IReadOnlyList<IBatteryProvider> CreateProviders(bool demo) =>
        demo
            ? [new FakeBatteryProvider(simulateDrain: true)]
            : [new BleGattBatteryProvider(), new BluetoothPnpBatteryProvider(), new HidDongleBatteryProvider()];

    private static async Task ListDevicesAsync(bool demo)
    {
        Log.Info("--list-devices: querying every provider once.");
        foreach (var provider in CreateProviders(demo))
        {
            using (provider)
            {
                try
                {
                    var devices = await provider.GetDevicesAsync();
                    Log.Info($"[{provider.Name}] {devices.Count} device(s) with battery data.");
                    foreach (var d in devices)
                        Log.Info($"  {d.Name} | {d.Kind} | {d.BatteryPercent?.ToString() ?? "?"}% | {d.Id}");
                    if (provider is IDiagnosticsSource diagnostics)
                    {
                        foreach (var line in diagnostics.DescribeDevices())
                            Log.Info($"  · {line}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[{provider.Name}] failed.", ex);
                }
            }
        }
    }

    private void OnDevicesChanged(object? sender, IReadOnlyList<Device> devices)
    {
        Log.Info($"Devices: {(devices.Count == 0 ? "none" : string.Join(", ", devices.Select(d => $"{d.Name} {d.BatteryPercent?.ToString() ?? "?"}% [{d.Source}]")))}");
        Dispatcher.BeginInvoke(() =>
        {
            _viewModel.Apply(devices);
            // Text width may change (e.g. 100% → 99%): recompute the size once layout has run.
            Dispatcher.BeginInvoke(() => _host?.Reposition(), DispatcherPriority.Loaded);
        });
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static AnchorMode ParseMode(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--mode", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length &&
            Enum.TryParse<AnchorMode>(args[index + 1], ignoreCase: true, out var mode))
        {
            return mode;
        }
        return AnchorMode.Child;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception on the UI thread.", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        foreach (var provider in _providers)
            provider.Dispose();
        _buttonsProbe?.Dispose();
        _watcher?.Dispose();
        _host?.Dispose();
        _theme?.Dispose();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        Log.Info("BatTray closed.");
        base.OnExit(e);
    }
}
