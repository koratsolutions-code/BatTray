using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatTray.App.Diagnostics;
using Microsoft.Win32;

namespace BatTray.App.Theme;

/// <summary>
/// Publishes two sets of brushes as application resources:
/// <list type="bullet">
///   <item>widget brushes (WidgetForegroundBrush, Level.*) that follow the <b>taskbar</b> theme
///         (SystemUsesLightTheme), so the widget is always readable on it;</item>
///   <item>app brushes (App.*, Panel.*) for menus, the detail panel and windows, which follow the
///         <b>apps</b> theme (AppsUseLightTheme) or the user's choice in Settings.</item>
/// </list>
/// </summary>
internal sealed class ThemeWatcher : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ResourceDictionary _resources;
    private bool? _taskbarLight;
    private bool? _appLight;
    private string _mode = "auto";

    public ThemeWatcher(ResourceDictionary resources, string mode)
    {
        _resources = resources;
        _mode = mode;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>True when the taskbar is light (widget colours).</summary>
    public bool IsLight => _taskbarLight ?? false;

    /// <summary>True when menus and windows use the light theme.</summary>
    public bool IsAppLight => _appLight ?? true;

    /// <summary>Raised when the taskbar or the app theme changes.</summary>
    public event Action? ThemeChanged;

    /// <summary>Current instance, so windows can follow the theme (title bar colour).</summary>
    public static ThemeWatcher? Current { get; private set; }

    /// <summary>Changes the app theme: <c>auto</c>, <c>light</c> or <c>dark</c>.</summary>
    public void SetMode(string mode)
    {
        _mode = mode;
        Apply();
    }

    public static bool ReadTaskbarUsesLightTheme() => ReadFlag("SystemUsesLightTheme", fallback: false);

    public static bool ReadAppsUseLightTheme() => ReadFlag("AppsUseLightTheme", fallback: true);

    private static bool ReadFlag(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.BeginInvoke(Apply);
    }

    private void Apply()
    {
        Current = this;
        var taskbarLight = ReadTaskbarUsesLightTheme();
        var appLight = _mode switch
        {
            "light" => true,
            "dark" => false,
            _ => ReadAppsUseLightTheme(),
        };
        if (taskbarLight == _taskbarLight && appLight == _appLight)
            return;

        if (taskbarLight != _taskbarLight)
        {
            _taskbarLight = taskbarLight;
            Log.Info($"Taskbar theme: {(taskbarLight ? "light" : "dark")}");
            // Windows 11 Fluent semantic colours for each taskbar background.
            Set("WidgetForegroundBrush", taskbarLight ? "#1A1A1A" : "#FFFFFF");
            Set("Level.Good", taskbarLight ? "#0F7B0F" : "#6CCB5F");
            Set("Level.Low", taskbarLight ? "#9D5D00" : "#FCE100");
            Set("Level.Critical", taskbarLight ? "#C42B1C" : "#FF99A4");
            Set("Level.Unknown", taskbarLight ? "#6E6E6E" : "#9E9E9E");
        }

        if (appLight != _appLight)
        {
            _appLight = appLight;
            Log.Info($"App theme: {(appLight ? "light" : "dark")} (mode {_mode})");
            ApplyAppBrushes(appLight);
            foreach (Window window in Application.Current?.Windows ?? new WindowCollection())
                ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke();
    }

    private void ApplyAppBrushes(bool light)
    {
        // Menus and windows (Korat palette on light, Windows 11 dark surfaces on dark).
        Set("App.Background", light ? "#FFFFFF" : "#202020");
        Set("App.Surface", light ? "#F5F8FC" : "#2B2B2B");
        Set("App.Border", light ? "#E3E9F2" : "#3D3D3D");
        Set("App.Text", light ? "#16284C" : "#F2F2F2");
        Set("App.TextSecondary", light ? "#3A4A66" : "#D0D0D0");
        Set("App.Muted", light ? "#5A6B85" : "#A6A6A6");
        Set("App.Accent", light ? "#2E6FD0" : "#7DADFF");
        Set("App.AccentText", light ? "#FFFFFF" : "#0B1A33");
        Set("App.AccentSoft", light ? "#E8F0FC" : "#1E3354");
        Set("App.Hover", light ? "#EEF3FB" : "#383838");
        Set("App.Pressed", light ? "#DDE7F7" : "#454545");
        Set("App.InputBackground", light ? "#FFFFFF" : "#2B2B2B");
        Set("App.InputBorder", light ? "#C9D3E1" : "#5A5A5A");
        Set("App.Danger", light ? "#C42B1C" : "#FF99A4");

        // Menus: the classic Windows (Aero2) menu colours, and a dark version of the same design.
        Set("Menu.Background", light ? "#F0F0F0" : "#2B2B2B");
        Set("Menu.Border", light ? "#999999" : "#555555");
        Set("Menu.Text", light ? "#000000" : "#F2F2F2");
        Set("Menu.Disabled", light ? "#6D6D6D" : "#8C8C8C");
        Set("Menu.Separator", light ? "#D7D7D7" : "#454545");
        Set("Menu.HighlightBackground", light ? "#3D26A0DA" : "#4D26A0DA");
        Set("Menu.HighlightBorder", light ? "#FF26A0DA" : "#FF2F8FD0");
        Set("Menu.CheckBackground", light ? "#3D26A0DA" : "#4D26A0DA");
        Set("Menu.CheckBorder", light ? "#FF26A0DA" : "#FF2F8FD0");

        // Detail panel (Windows 11 flyout colours).
        Set("Panel.Background", light ? "#F9F9F9" : "#2C2C2C");
        Set("Panel.Border", light ? "#E0E0E0" : "#404040");
        Set("Panel.Track", light ? "#E6E6E6" : "#454545");
        Set("Panel.Secondary", light ? "#5F5F5F" : "#ABABAB");
        Set("Panel.Text", light ? "#1A1A1A" : "#FFFFFF");
        Set("Panel.Level.Good", light ? "#0F7B0F" : "#6CCB5F");
        Set("Panel.Level.Low", light ? "#9D5D00" : "#FCE100");
        Set("Panel.Level.Critical", light ? "#C42B1C" : "#FF99A4");
        Set("Panel.Level.Unknown", light ? "#6E6E6E" : "#9E9E9E");

        // Korat logo: navy on light backgrounds, white wordmark on dark ones.
        _resources["App.KoratLogo"] = LoadImage(light ? "KoratLogo.png" : "KoratLogo-dark.png");
    }

    private static ImageSource? LoadImage(string asset)
    {
        try
        {
            var image = new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}"));
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load {asset}.", ex);
            return null;
        }
    }

    private void Set(string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        _resources[key] = brush;
    }

    /// <summary>Gives a window a dark or light title bar matching the app theme.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var dark = !(Current?.IsAppLight ?? true);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyTitleBar(window);
        }
    }

    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
