using System.Windows;
using System.Windows.Media;
using BatTray.App.Diagnostics;
using Microsoft.Win32;

namespace BatTray.App.Theme;

/// <summary>
/// Follows the taskbar theme (SystemUsesLightTheme) and publishes the widget brushes as application
/// resources: WidgetForegroundBrush, Level.Good/Low/Critical/Unknown.
/// </summary>
internal sealed class ThemeWatcher : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ResourceDictionary _resources;
    private bool? _isLight;

    public ThemeWatcher(ResourceDictionary resources)
    {
        _resources = resources;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLight => _isLight ?? false;

    /// <summary>Raised when switching between light and dark.</summary>
    public event Action? ThemeChanged;

    public static bool ReadTaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        // When the value is missing, Windows 11 uses a dark taskbar.
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.BeginInvoke(Apply);
    }

    private void Apply()
    {
        var light = ReadTaskbarUsesLightTheme();
        if (light == _isLight)
            return;

        _isLight = light;
        Log.Info($"Taskbar theme: {(light ? "light" : "dark")}");

        // Windows 11 Fluent semantic colours for each background.
        Set("WidgetForegroundBrush", light ? "#1A1A1A" : "#FFFFFF");
        Set("Level.Good", light ? "#0F7B0F" : "#6CCB5F");
        Set("Level.Low", light ? "#9D5D00" : "#FCE100");
        Set("Level.Critical", light ? "#C42B1C" : "#FF99A4");
        Set("Level.Unknown", light ? "#6E6E6E" : "#9E9E9E");

        // Detail panel (Windows 11 flyout colours).
        Set("Panel.Background", light ? "#F9F9F9" : "#2C2C2C");
        Set("Panel.Border", light ? "#E0E0E0" : "#404040");
        Set("Panel.Track", light ? "#E6E6E6" : "#454545");
        Set("Panel.Secondary", light ? "#5F5F5F" : "#ABABAB");

        ThemeChanged?.Invoke();
    }

    private void Set(string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        _resources[key] = brush;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
