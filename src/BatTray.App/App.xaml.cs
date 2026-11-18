using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatTray.App.Customize;
using BatTray.App.Diagnostics;
using BatTray.App.Icons;
using BatTray.App.Panel;
using BatTray.App.Services;
using BatTray.App.Taskbar;
using BatTray.App.Theme;
using BatTray.App.Tray;
using BatTray.App.Usage;
using BatTray.App.ViewModels;
using BatTray.Core;
using BatTray.Core.Estimation;
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
    private readonly EstimateStore _estimateStore = new();
    private static readonly TimeSpan EstimateSaveInterval = TimeSpan.FromMinutes(5);
    private AppSettings _settings = new();
    private IReadOnlyList<Device> _rawDevices = [];   // as reported by the providers
    private IReadOnlyList<Device> _devices = [];      // with the user's names and icons
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
    private BatteryEstimator? _estimator;
    private UsageActivityMonitor? _usage;
    private HidDongleBatteryProvider? _hidProvider;
    private DispatcherTimer? _estimateSaveTimer;

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
            DiagnosticsReport.OpenLogFolder, About.AboutWindow.ShowSingle, ConfigureEstimate, MarkCharged, StopEstimate,
            SetLevel, UseFixedLevel, CustomizeDevices, () => Shutdown());

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

        // Usage-based estimates for devices that do not report their battery.
        _estimator = new BatteryEstimator(_estimateStore.Load());
        if (!demo)
        {
            _usage = new UsageActivityMonitor(_estimator);
            _estimateSaveTimer = new DispatcherTimer { Interval = EstimateSaveInterval };
            _estimateSaveTimer.Tick += (_, _) => SaveEstimates(onlyIfDirty: true);
            _estimateSaveTimer.Start();
        }

        _providers = CreateProviders(demo, _estimator);
        _hidProvider = _providers.OfType<HidDongleBatteryProvider>().FirstOrDefault();
        _monitor = new DeviceMonitor(_providers, demo ? DemoPollInterval : PollInterval);
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.ProviderFailed += (_, f) => Log.Error($"Provider '{f.Provider.Name}' failed.", f.Error);

        RefreshUi();
        _watcher.Start();
        _buttonsProbe.Start();
        _monitor.Start();
    }

    private static IReadOnlyList<IBatteryProvider> CreateProviders(bool demo, BatteryEstimator? estimator)
    {
        if (demo)
            return [new FakeBatteryProvider(simulateDrain: true)];

        List<IBatteryProvider> providers =
        [
            new BleGattBatteryProvider(),
            new BluetoothPnpBatteryProvider(),
            new HidDongleBatteryProvider(),
            new GameControllerBatteryProvider(),
            new SonyControllerBatteryProvider(),
        ];
        if (estimator is not null)
            providers.Add(new EstimatedBatteryProvider(estimator));
        return providers;
    }

    private void OnDevicesChanged(object? sender, IReadOnlyList<Device> devices)
    {
        Log.Info($"Devices: {(devices.Count == 0 ? "none" : string.Join(", ", devices.Select(d => $"{d.Name} {d.BatteryPercent?.ToString() ?? "?"}% [{d.Source}]")))}");
        Dispatcher.BeginInvoke(() =>
        {
            _rawDevices = devices;
            _devices = devices.Select(_settings.Customize).ToList();
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
            Log.Info($"Low battery alert: {device.Name} at {device.BatteryPercent}%{(device.Estimated ? " (estimated)" : string.Empty)}.");
            _tray.Notify(
                device.Estimated ? $"{device.Name}: about {device.BatteryPercent}% battery (estimated)" : $"{device.Name}: {device.BatteryPercent}% battery",
                "Charge it or replace the batteries soon.");
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
            .Select(d => DetailPanelItem.From(d, _settings.LowThreshold, _settings.CriticalThreshold, _settings.IsHidden(d.Id), EstimateDetails(d)))
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
            DeviceMenu.Fill(menu, _devices, _settings, AutoStart.IsEnabled(), EstimateCandidates(), _menuActions);
    }

    // ---------- Usage-based estimates ----------

    /// <summary>Receivers that report no battery, plus every device already estimated.</summary>
    private IReadOnlyList<EstimateCandidate> EstimateCandidates()
    {
        if (_estimator is null)
            return [];
        var candidates = new Dictionary<string, EstimateCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var receiver in _hidProvider?.ReceiversWithoutProtocol ?? [])
        {
            var estimate = _estimator.Get(receiver.Id);
            candidates[receiver.Id] = new EstimateCandidate(receiver.Id,
                estimate?.Name ?? _usage?.FindAudioName(receiver.Id) ?? receiver.DisplayName, receiver.InferKind(), estimate);
        }
        foreach (var estimate in _estimator.Snapshot())
            candidates.TryAdd(estimate.DeviceId, new EstimateCandidate(estimate.DeviceId, estimate.Name, estimate.Kind, estimate));
        return candidates.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ConfigureEstimate(string deviceId)
    {
        if (_estimator is null || EstimateCandidates().FirstOrDefault(c => c.Id == deviceId) is not { } candidate)
            return;

        var existing = candidate.Estimate;
        var name = existing?.Name ?? _usage?.FindAudioName(deviceId) ?? candidate.Name;
        var hours = existing?.RatedHours ?? BatteryEstimator.DefaultRatedHours(candidate.Kind);
        var percent = existing is null ? 100 : BatteryEstimator.Percent(existing, DateTimeOffset.UtcNow);

        var dialog = new EstimateDialog(name, hours, percent);
        if (dialog.ShowDialog() != true)
            return;

        _estimator.Configure(deviceId, dialog.DeviceName, candidate.Kind, dialog.RatedHours, dialog.CurrentPercent, DateTimeOffset.UtcNow);
        Log.Info($"Usage estimate set for {dialog.DeviceName} ({deviceId}): {dialog.RatedHours} h, now {dialog.CurrentPercent}%.");
        SaveEstimates(onlyIfDirty: false);
    }

    /// <summary>
    /// Sets the level of a device without battery data (asks for it when <paramref name="percent"/> is null).
    /// A device with no entry yet gets a fixed level; an existing entry keeps its mode.
    /// </summary>
    private void SetLevel(string deviceId, int? percent)
    {
        if (_estimator is null || EstimateCandidates().FirstOrDefault(c => c.Id == deviceId) is not { } candidate)
            return;

        if (percent is null)
        {
            var current = candidate.Estimate is { } e ? BatteryEstimator.Percent(e, DateTimeOffset.UtcNow) : 100;
            var dialog = new LevelDialog(candidate.Estimate?.Name ?? candidate.Name, current);
            if (dialog.ShowDialog() != true)
                return;
            percent = dialog.Percent;
        }

        var now = DateTimeOffset.UtcNow;
        if (candidate.Estimate is null)
            _estimator.SetFixed(deviceId, _usage?.FindAudioName(deviceId) ?? candidate.Name, candidate.Kind, percent.Value, now);
        else
            _estimator.SetLevel(deviceId, percent.Value, now);
        Log.Info($"Level of {deviceId} set to {percent}%.");
        SaveEstimates(onlyIfDirty: false);
    }

    /// <summary>Switches a device to a fixed level, keeping its current value.</summary>
    private void UseFixedLevel(string deviceId)
    {
        if (_estimator?.Get(deviceId) is not { } e || e.IsFixed)
        {
            RefreshUi(); // re-checks the menu item when it was already fixed
            return;
        }
        _estimator.SetFixed(deviceId, e.Name, e.Kind, BatteryEstimator.Percent(e, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        Log.Info($"{deviceId} switched to a fixed level.");
        SaveEstimates(onlyIfDirty: false);
    }

    private void MarkCharged(string deviceId)
    {
        _estimator?.MarkCharged(deviceId, DateTimeOffset.UtcNow);
        Log.Info($"Usage estimate reset to 100% for {deviceId}.");
        SaveEstimates(onlyIfDirty: false);
    }

    private void StopEstimate(string deviceId)
    {
        _estimator?.Remove(deviceId);
        Log.Info($"Usage estimate removed for {deviceId}.");
        SaveEstimates(onlyIfDirty: false);
    }

    private string? EstimateDetails(Device device)
    {
        if (!device.Estimated || _estimator?.Get(device.Id) is not { } e)
            return null;
        var now = DateTimeOffset.UtcNow;
        var since = now - e.ChargedAt;
        var used = BatteryEstimator.UsedHoursAt(e, now);
        var setAgo = since.TotalDays >= 1 ? $"{(int)since.TotalDays} d" : since.TotalHours >= 1 ? $"{(int)since.TotalHours} h" : $"{(int)since.TotalMinutes} min";
        if (e.IsFixed)
            return $"Set manually · {setAgo} ago";
        var ago = since.TotalDays >= 1 ? $"{(int)since.TotalDays} d" : since.TotalHours >= 1 ? $"{(int)since.TotalHours} h" : $"{(int)since.TotalMinutes} min";
        return $"Estimated from usage · {(used < 1 ? $"{used * 60:0} min" : $"{used:0.#} h")} of {e.RatedHours:0.#} h used · set {ago} ago";
    }

    private void SaveEstimates(bool onlyIfDirty)
    {
        if (_estimator is null || (onlyIfDirty && !_estimator.IsDirty))
            return;
        try
        {
            _estimateStore.Save(_estimator.Snapshot(markSaved: true));
        }
        catch (Exception ex)
        {
            Log.Error($"Could not save estimates to {_estimateStore.Path}.", ex);
        }
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

    private void CustomizeDevices() =>
        CustomizeWindow.ShowSingle(_rawDevices, id => _settings.CustomizationFor(id), SaveCustomization);

    private void SaveCustomization(string deviceId, DeviceCustomization? customization)
    {
        _settings = _settings.WithCustomization(deviceId, customization);
        SaveSettings();
        DeviceIconProvider.DeleteUnused(_settings.Customizations.Values);
        Log.Info(customization is null || customization.IsEmpty
            ? $"Customization of {deviceId} reset."
            : $"Device {deviceId} customized (name: {customization.Name ?? "default"}, icon: {customization.Icon ?? "default"}).");
        _devices = _rawDevices.Select(_settings.Customize).ToList();
        RefreshUi();
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
        foreach (var provider in CreateProviders(demo, estimator: null))
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

        // Only close copies of this same executable: never an unrelated process that happens to be called "BatTray".
        var self = Environment.ProcessId;
        var ownPath = Environment.ProcessPath;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("BatTray").Where(p => p.Id != self))
        {
            try
            {
                if (ownPath is null || !string.Equals(process.MainModule?.FileName, ownPath, StringComparison.OrdinalIgnoreCase))
                    continue;
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
        _estimateSaveTimer?.Stop();
        _usage?.Dispose();
        SaveEstimates(onlyIfDirty: true);
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
