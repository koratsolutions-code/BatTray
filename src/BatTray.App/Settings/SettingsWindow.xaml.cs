using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BatTray.App.Localization;
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
    string CurrentVersion);

/// <summary>Battery thresholds, alerts, language, theme, start with Windows and update checks.</summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private readonly SettingsContext _context;
    private bool _loading = true;

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
        BatteryTitle.Text = L.Battery;
        LowLabel.Text = L.LowLevel;
        CriticalLabel.Text = L.CriticalLevel;
        AlertsBox.Content = L.LowBatteryAlerts;
        AppearanceTitle.Text = L.Appearance;
        LanguageLabel.Text = L.Language;
        LangAuto.Content = L.FollowWindows;
        ThemeLabel.Text = L.Theme;
        ThemeAuto.Content = L.FollowWindows;
        ThemeLight.Content = L.Light;
        ThemeDark.Content = L.Dark;
        GeneralTitle.Text = L.General;
        StartBox.Content = L.StartWithWindows;
        AllTaskbarsBox.Content = L.ShowOnAllTaskbars;
        RestartHint.Text = L.RestartToApply;
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
        (s.Language switch { "en" => LangEn, "es" => LangEs, _ => LangAuto }).IsChecked = true;
        (s.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeAuto }).IsChecked = true;
        StartBox.IsChecked = _context.IsStartWithWindows();
        AllTaskbarsBox.IsChecked = s.ShowOnAllTaskbars;
        UpdatesBox.IsChecked = s.CheckForUpdates;
        _loading = false;
    }

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

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string language })
            return;
        _context.Update(s => s with { Language = language });
        L.Apply(language);
        ApplyTexts(); // this window follows the new language at once
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string theme })
            return;
        _context.Update(s => s with { Theme = theme });
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _context.SetStartWithWindows(StartBox.IsChecked == true);

    private void OnAllTaskbarsClick(object sender, RoutedEventArgs e) =>
        _context.Update(s => s with { ShowOnAllTaskbars = AllTaskbarsBox.IsChecked == true });

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
