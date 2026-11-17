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
/// Punto de entrada. Argumentos de línea de comandos (prototipo):
///   --mode child|overlay   estrategia de anclaje a la barra (por defecto: child)
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\KoratSolutions.BatTray";

    private Mutex? _singleInstance;
    private ThemeWatcher? _theme;
    private TaskbarWatcher? _watcher;
    private TaskbarWidgetHost? _host;
    private TaskButtonsProbe? _buttonsProbe;
    private DeviceMonitor? _monitor;
    private readonly WidgetViewModel _viewModel = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            Log.Info("Ya hay otra instancia de BatTray en ejecución; se cierra esta.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Excepción no controlada.", args.ExceptionObject as Exception);

        var mode = ParseMode(e.Args);
        Log.Info($"Inicio de BatTray {typeof(App).Assembly.GetName().Version} (modo {mode}).");

        _theme = new ThemeWatcher(Resources);

        var view = new TaskbarWidgetView { DataContext = _viewModel };
        _host = new TaskbarWidgetHost(mode, view, _viewModel);

        _watcher = new TaskbarWatcher();
        _watcher.Changed += _host.Update;
        _watcher.ForegroundChanged += _host.BringToFront;
        _theme.ThemeChanged += _host.Reposition;

        _buttonsProbe = new TaskButtonsProbe(Dispatcher);
        _buttonsProbe.Changed += _host.SetTaskButtonsRight;

        // Prototipo: datos simulados que bajan 1 % cada 5 s para ver el widget actualizarse.
        _monitor = new DeviceMonitor([new FakeBatteryProvider(simulateDrain: true)], TimeSpan.FromSeconds(5));
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.ProviderFailed += (_, f) => Log.Error($"Proveedor '{f.Provider.Name}' falló.", f.Error);

        _watcher.Start();
        _buttonsProbe.Start();
        _monitor.Start();
    }

    private void OnDevicesChanged(object? sender, IReadOnlyList<Device> devices) =>
        Dispatcher.BeginInvoke(() =>
        {
            _viewModel.Apply(devices);
            // El ancho del texto puede cambiar (p. ej. 100% → 99%): recalcular tamaño tras el layout.
            Dispatcher.BeginInvoke(() => _host?.Reposition(), DispatcherPriority.Loaded);
        });

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
        Log.Error("Excepción no controlada en la UI.", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        _buttonsProbe?.Dispose();
        _watcher?.Dispose();
        _host?.Dispose();
        _theme?.Dispose();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        Log.Info("BatTray cerrado.");
        base.OnExit(e);
    }
}
