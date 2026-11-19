using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using BatTray.App.Localization;
using BatTray.App.Services;
using BatTray.App.Theme;
using BatTray.Core.Settings;
using BatTray.Core.Updates;

namespace BatTray.App.Settings;

/// <summary>What the settings window needs from the app. Every change is applied at once.</summary>
internal sealed record SettingsContext(
    Func<AppSettings> Get,
    Action<Func<AppSettings, AppSettings>> Update,
    Func<bool> IsStartWithWindows,
    Action<bool> SetStartWithWindows,
    Func<Task<ReleaseInfo?>> CheckForUpdate,
    Action<ReleaseInfo> OpenUpdate,
    Action SaveDiagnostics,
    Action OpenLogFolder,
    Action ClearHistory,
    Action CustomizeDevices,
    Func<bool> HotkeyWorks,
    string CurrentVersion);

/// <summary>Battery levels and notifications, taskbar look, language, theme, shortcut and updates.</summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private readonly SettingsContext _context;
    private bool _loading = true;
    private bool _capturing;

    private SettingsWindow(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
        ThemeWatcher.ApplyTitleBar(this);
        ApplyTexts();
        Load();
        Closed += (_, _) => _open = null;
    }

    internal static void ShowSingle(SettingsContext context)
    {
        _open ??= new SettingsWindow(context);
        _open.Show();
        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private void ApplyTexts()
    {
        Title = L.SettingsTitle;
        BatteryTab.Header = L.TabBattery;
        TaskbarTab.Header = L.TabTaskbar;
        GeneralTab.Header = L.TabGeneral;

        LevelsTitle.Text = L.Levels;
        LowLabel.Text = L.LowLevel;
        CriticalLabel.Text = L.CriticalLevel;
        AlertsTitle.Text = L.Alerts;
        AlertsBox.Content = L.LowBatteryAlerts;
        FullChargeBox.Content = L.FullChargeAlerts;
        PerDeviceHint.Text = L.PerDeviceAlertsHint;
        HistoryTitle.Text = L.History;
        HistoryBox.Content = L.KeepHistory;
        HistoryHint.Text = L.KeepHistoryHint;
        ClearHistoryButton.Content = L.ClearHistory;

        PercentTitle.Text = L.PercentOnTaskbar;
        PercentAlways.Content = L.PercentAlways;
        PercentLow.Content = L.PercentLow;
        PercentNever.Content = L.PercentNever;
        PercentNeverHint.Text = L.PercentNeverHint;
        SizeTitle.Text = L.ItemSize;
        SizeSmall.Content = L.SizeSmall;
        SizeNormal.Content = L.SizeNormal;
        SizeLarge.Content = L.SizeLarge;
        SizeHint.Text = L.SizeHint;
        DevicesTitle.Text = L.Devices;
        CustomizeButton.Content = L.OrderAndCustomize;
        AllTaskbarsBox.Content = L.ShowOnAllTaskbars;
        RestartHint.Text = L.RestartToApply;

        LanguageLabel.Text = L.Language;
        LangAuto.Content = L.FollowWindows;
        ThemeLabel.Text = L.Theme;
        ThemeAuto.Content = L.FollowWindows;
        ThemeLight.Content = L.Light;
        ThemeDark.Content = L.Dark;
        StartBox.Content = L.StartWithWindows;
        HotkeyBox.Content = L.Hotkey;
        HotkeyHint.Text = L.HotkeyHint;
        UpdatesBox.Content = L.CheckForUpdates;
        UpdatesHint.Text = L.CheckForUpdatesHint;
        CheckNowButton.Content = L.CheckNow;
        DiagnosticsButton.Content = L.SaveDiagnostics;
        LogsButton.Content = L.OpenLogFolder;
        CloseButton.Content = L.Close;
    }

    private void Load()
    {
        _loading = true;
        var s = _context.Get();
        LowSlider.Value = s.LowThreshold;
        CriticalSlider.Value = s.CriticalThreshold;
        ShowValues();
        AlertsBox.IsChecked = s.LowBatteryAlerts;
        FullChargeBox.IsChecked = s.FullChargeAlerts;
        HistoryBox.IsChecked = s.BatteryHistory;
        ClearHistoryButton.IsEnabled = s.BatteryHistory;

        (s.WidgetPercent switch { "low" => PercentLow, "never" => PercentNever, _ => PercentAlways }).IsChecked = true;
        PercentBelowSlider.Value = s.PercentBelow;
        PercentBelowValue.Text = $"{s.PercentBelow}%";
        (s.WidgetSize switch { "small" => SizeSmall, "large" => SizeLarge, _ => SizeNormal }).IsChecked = true;
        AllTaskbarsBox.IsChecked = s.ShowOnAllTaskbars;

        (s.Language switch { "en" => LangEn, "es" => LangEs, _ => LangAuto }).IsChecked = true;
        (s.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeAuto }).IsChecked = true;
        StartBox.IsChecked = _context.IsStartWithWindows();
        HotkeyBox.IsChecked = s.HotkeyEnabled;
        HotkeyText.Text = HotkeyGesture.Parse(s.Hotkey)?.ToString() ?? AppSettings.DefaultHotkey;
        HotkeyText.IsEnabled = s.HotkeyEnabled;
        ShowHotkeyState();
        UpdatesBox.IsChecked = s.CheckForUpdates;
        _loading = false;
    }

    // ---------- Battery ----------

    private void ShowValues()
    {
        LowValue.Text = $"{LowSlider.Value:0}%";
        CriticalValue.Text = $"{CriticalSlider.Value:0}%";
    }

    private void OnLowChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the window is still being built (Minimum coerces Value).
        if (_loading)
            return;
        // Critical must stay below low.
        if (CriticalSlider.Value >= LowSlider.Value)
            CriticalSlider.Value = Math.Max(CriticalSlider.Minimum, LowSlider.Value - 5);
        ShowValues();
        SaveThresholds();
    }

    private void OnCriticalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;
        if (LowSlider.Value <= CriticalSlider.Value)
            LowSlider.Value = Math.Min(LowSlider.Maximum, CriticalSlider.Value + 5);
        ShowValues();
        SaveThresholds();
    }

    private void SaveThresholds()
    {
        var low = (int)LowSlider.Value;
        var critical = (int)CriticalSlider.Value;
        _context.Update(s => s with { LowThreshold = low, CriticalThreshold = critical });
    }

    private void OnAlertsClick(object sender, RoutedEventArgs e) =>
        _context.Update(s => s with { LowBatteryAlerts = AlertsBox.IsChecked == true });

    private void OnFullChargeClick(object sender, RoutedEventArgs e) =>
        _context.Update(s => s with { FullChargeAlerts = FullChargeBox.IsChecked == true });

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        var on = HistoryBox.IsChecked == true;
        _context.Update(s => s with { BatteryHistory = on });
        ClearHistoryButton.IsEnabled = on;
        HistoryStatus.Text = on ? string.Empty : L.HistoryCleared;
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        _context.ClearHistory();
        HistoryStatus.Text = L.HistoryCleared;
    }

    // ---------- Taskbar ----------

    private void OnPercentChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode })
            return;
        _context.Update(s => s with { WidgetPercent = mode });
    }

    private void OnPercentBelowChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || PercentBelowValue is null)
            return;
        var below = (int)PercentBelowSlider.Value;
        PercentBelowValue.Text = $"{below}%";
        PercentLow.IsChecked = true; // also saves the mode
        _context.Update(s => s with { PercentBelow = below, WidgetPercent = "low" });
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string size })
            return;
        _context.Update(s => s with { WidgetSize = size });
    }

    private void OnCustomize(object sender, RoutedEventArgs e) => _context.CustomizeDevices();

    private void OnAllTaskbarsClick(object sender, RoutedEventArgs e) =>
        _context.Update(s => s with { ShowOnAllTaskbars = AllTaskbarsBox.IsChecked == true });

    // ---------- General ----------

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string language })
            return;
        _context.Update(s => s with { Language = language });
        L.Apply(language);
        ApplyTexts(); // this window follows the new language at once
        ShowHotkeyState();
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string theme })
            return;
        _context.Update(s => s with { Theme = theme });
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _context.SetStartWithWindows(StartBox.IsChecked == true);

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        var on = HotkeyBox.IsChecked == true;
        HotkeyText.IsEnabled = on;
        _context.Update(s => s with { HotkeyEnabled = on });
        ShowHotkeyState();
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _capturing = true;
        HotkeyText.Text = L.HotkeyPress;
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        _capturing = false;
        HotkeyText.Text = HotkeyGesture.Parse(_context.Get().Hotkey)?.ToString() ?? AppSettings.DefaultHotkey;
    }

    /// <summary>Records the pressed combination as the new shortcut.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
            return; // keep keyboard navigation
        e.Handled = true;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            CloseButton.Focus();
            return;
        }
        if (HotkeyGesture.IsModifierKey(key))
            return;

        var modifiers = Keyboard.Modifiers;
        if (!HotkeyGesture.IsValid(modifiers, key))
        {
            HotkeyError.Text = L.HotkeyNeedsModifier;
            HotkeyError.Visibility = Visibility.Visible;
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        _context.Update(s => s with { Hotkey = gesture.ToString(), HotkeyEnabled = true });
        HotkeyBox.IsChecked = true;
        _capturing = false;
        HotkeyText.Text = gesture.ToString();
        CloseButton.Focus();
        ShowHotkeyState();
    }

    private void ShowHotkeyState()
    {
        var failed = HotkeyBox.IsChecked == true && !_context.HotkeyWorks();
        HotkeyError.Text = failed ? L.HotkeyInUse : string.Empty;
        HotkeyError.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUpdatesClick(object sender, RoutedEventArgs e) =>
        _context.Update(s => s with { CheckForUpdates = UpdatesBox.IsChecked == true });

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        UpdateStatus.Inlines.Clear();
        UpdateStatus.Text = L.Checking;
        try
        {
            var release = await _context.CheckForUpdate();
            UpdateStatus.Inlines.Clear();
            if (release is null)
            {
                UpdateStatus.Text = L.UpToDate(_context.CurrentVersion);
                return;
            }
            UpdateStatus.Inlines.Add(new Run(L.NewVersion(release.Version.ToString(3)) + " "));
            var link = new Hyperlink(new Run(L.Download));
            link.Click += (_, _) => _context.OpenUpdate(release);
            UpdateStatus.Inlines.Add(link);
        }
        catch (Exception)
        {
            UpdateStatus.Inlines.Clear();
            UpdateStatus.Text = L.CheckFailed;
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void OnDiagnostics(object sender, RoutedEventArgs e) => _context.SaveDiagnostics();

    private void OnLogs(object sender, RoutedEventArgs e) => _context.OpenLogFolder();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
