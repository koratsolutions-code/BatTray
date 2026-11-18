using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using BatTray.App.Panel;
using BatTray.App.Services;
using BatTray.App.Taskbar;
using BatTray.App.Theme;
using BatTray.App.Tray;
using BatTray.App.ViewModels;
using BatTray.Core;
using BatTray.Core.Models;
using BatTray.Core.Providers;
using BatTray.Core.Settings;

namespace BatTray.App;

/// <summary>
/// Entry point. Command-line options:
///   --mode child|overlay   taskbar anchoring strategy (default: child)
///   --demo                 simulated devices instead of real readings
///   --list-devices         log one reading of every provider and exit (diagnostics)
///   --uninstall-cleanup    used by the uninstaller: disable start with Windows, close BatTray, exit
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\KoratSolutions.BatTray";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DemoPollInterval = TimeSpan.FromSeconds(5);

    private readonly SettingsStore _settingsStore = new();
    private readonly BatteryAlertTracker _alerts = new();
    private AppSettings _settings = new();
    private IReadOnlyList<Device> _devices = [];
    private IReadOnlyList<IBatteryProvider> _providers = [];

    private Mutex? _singleInstance;
    private ThemeWatcher? _theme;
    private TaskbarWatcher? _watcher;
    private WidgetSet? _widgets;
    private TaskButtonsProbe? _buttonsProbe;
    private DeviceMonitor? _monitor;
    private TrayIconHost? _tray;
    private ContextMenu? _widgetMenu;
    private MenuPresenter? _menuPresenter;
    private DetailPanel? _panel;
    private DeviceMenuActions? _menuActions;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        CoreDiagnostics.Sink = Log.Info;

        var demo = HasFlag(e.Args, "--demo");

        if (HasFlag(e.Args, "--uninstall-cleanup"))
        {
            UninstallCleanup();
            Shutdown();
            return;
        }

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

        _settings = _settingsStore.Load();
        AutoStart.RefreshPathIfEnabled();
        _theme = new ThemeWatcher(Resources);

        _menuActions = new DeviceMenuActions(SetDeviceHidden, SetStartWithWindows, SetLowBatteryAlerts, SaveDiagnostics,
            DiagnosticsReport.OpenLogFolder, About.AboutWindow.ShowSingle, () => Shutdown());

        _menuPresenter = new MenuPresenter();

        // Taskbar widgets (one per taskbar): left click opens the detail panel, right click the shared menu.
        _panel = new DetailPanel();
        _widgetMenu = new ContextMenu();
        _widgets = new WidgetSet(mode, _settings.ShowOnAllTaskbars, ShowPanel, () => ShowMenu(_widgetMenu));

        // Tray icon: always reachable, even when the widget has no room on the taskbar.
        _tray = new TrayIconHost(new ContextMenu());
        _tray.MenuRequested += () => ShowMenu(_tray.Menu);
        _tray.PanelRequested += ShowPanel;
        MenuPresenter.Track(_widgetMenu);
        MenuPresenter.Track(_tray.Menu);
        _tray.Show(_theme.IsLight);

        _watcher = new TaskbarWatcher();
        _watcher.Changed += _widgets.UpdateTaskbars;
        _watcher.ForegroundChanged += _widgets.BringToFront;
        _theme.ThemeChanged += () =>
        {
            _widgets.Reposition();
            _tray.SetTheme(_theme.IsLight);
        };

        _buttonsProbe = new TaskButtonsProbe(Dispatcher);
        _buttonsProbe.Changed += _widgets.SetTaskButtonsRight;

        _providers = CreateProviders(demo);
        _monitor = new DeviceMonitor(_providers, demo ? DemoPollInterval : PollInterval);
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.ProviderFailed += (_, f) => Log.Error($"Provider '{f.Provider.Name}' failed.", f.Error);

        RefreshUi();
        _watcher.Start();
        _buttonsProbe.Start();
        _monitor.Start();
    }

    private static IReadOnlyList<IBatteryProvider> CreateProviders(bool demo) =>
        demo
            ? [new FakeBatteryProvider(simulateDrain: true)]
            :
            [
                new BleGattBatteryProvider(),
                new BluetoothPnpBatteryProvider(),
                new HidDongleBatteryProvider(),
                new GameControllerBatteryProvider(),
                new SonyControllerBatteryProvider(),
            ];

    private void OnDevicesChanged(object? sender, IReadOnlyList<Device> devices)
    {
        Log.Info($"Devices: {(devices.Count == 0 ? "none" : string.Join(", ", devices.Select(d => $"{d.Name} {d.BatteryPercent?.ToString() ?? "?"}% [{d.Source}]")))}");
        Dispatcher.BeginInvoke(() =>
        {
            _devices = devices;
            RefreshUi();
            AnnounceLowBatteries();
        });
    }

    private void AnnounceLowBatteries()
    {
        var due = _alerts.Evaluate(_devices, _settings.CriticalThreshold, _settings.LowThreshold);
        if (!_settings.LowBatteryAlerts || _tray is null)
            return;

        foreach (var device in due)
        {
            Log.Info($"Low battery alert: {device.Name} at {device.BatteryPercent}%.");
            _tray.Notify($"{device.Name}: {device.BatteryPercent}% battery", "Charge it or replace the batteries soon.");
        }
    }

    private void SetLowBatteryAlerts(bool enabled)
    {
        _settings = _settings with { LowBatteryAlerts = enabled };
        SaveSettings();
        Log.Info($"Low battery alerts {(enabled ? "enabled" : "disabled")}.");
        RefreshUi();
    }

    /// <summary>Pushes the current devices and settings to the widget, the tray icon and both menus. UI thread only.</summary>
    private void RefreshUi()
    {
        var visible = _devices.Where(d => !_settings.IsHidden(d.Id)).ToList();
        _widgets?.SetDevices(visible, _settings.LowThreshold, _settings.CriticalThreshold);

        _tray?.SetDevices(_devices);

        // Menus are rebuilt when they open; refresh closed ones so their size is right on the next open.
        foreach (var menu in new[] { _tray?.Menu, _widgetMenu })
        {
            if (menu is { IsOpen: false })
                FillMenu(menu);
        }

        // Text width may change (e.g. 100% → 99%): recompute the size once layout has run.
        Dispatcher.BeginInvoke(() => _widgets?.Reposition(), DispatcherPriority.Loaded);
    }

    private void ShowPanel()
    {
        if (_panel is null || TaskbarLocator.TryLocateUnderCursor() is not { } taskbar)
            return;

        _panel.SetItems(_devices
            .Select(d => DetailPanelItem.From(d, _settings.LowThreshold, _settings.CriticalThreshold, _settings.IsHidden(d.Id)))
            .ToList());
        _panel.ShowNearCursor(taskbar.Taskbar, taskbar.Monitor);
    }

    private void ShowMenu(ContextMenu menu)
    {
        if (menu.IsOpen || _menuPresenter is null)
            return;
        FillMenu(menu);
        _menuPresenter.Show(menu);
    }

    private void FillMenu(ContextMenu menu)
    {
        if (_menuActions is not null)
            DeviceMenu.Fill(menu, _devices, _settings, AutoStart.IsEnabled(), _menuActions);
    }

    private void SetDeviceHidden(string deviceId, bool hidden)
    {
        _settings = _settings.WithHidden(deviceId, hidden);
        SaveSettings();
        Log.Info($"Device {deviceId} {(hidden ? "hidden from" : "shown on")} the taskbar.");
        RefreshUi();
    }

    private void SetStartWithWindows(bool enabled)
    {
        AutoStart.SetEnabled(enabled);
        RefreshUi();
    }

    private void SaveDiagnostics()
    {
        try
        {
            DiagnosticsReport.RevealInExplorer(DiagnosticsReport.Write(_providers, _devices));
        }
        catch (Exception ex)
        {
            Log.Error("Could not write the diagnostics report.", ex);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not save settings to {_settingsStore.Path}.", ex);
        }
    }

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

    /// <summary>Run by the uninstaller: removes the start-with-Windows entry and closes running instances.</summary>
    private static void UninstallCleanup()
    {
        Log.Info("--uninstall-cleanup: removing start-with-Windows entry and closing BatTray.");
        AutoStart.SetEnabled(false);

        var self = Environment.ProcessId;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("BatTray").Where(p => p.Id != self))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not close BatTray process {process.Id}.", ex);
            }
        }
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
        _widgets?.Dispose();
        _tray?.Dispose();
        _menuPresenter?.Dispose();
        _panel?.CloseForShutdown();
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
