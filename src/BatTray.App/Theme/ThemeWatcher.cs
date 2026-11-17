using System.Windows;
using System.Windows.Media;
using BatTray.App.Diagnostics;
using Microsoft.Win32;

namespace BatTray.App.Theme;

/// <summary>
/// Sigue el tema de la barra de tareas (SystemUsesLightTheme) y publica los pinceles del widget
/// como recursos de aplicación: WidgetForegroundBrush, Level.Good/Low/Critical/Unknown.
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

    /// <summary>Se dispara cuando cambia entre claro y oscuro.</summary>
    public event Action? ThemeChanged;

    public static bool ReadTaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        // Si el valor no existe, Windows 11 usa la barra oscura.
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
        Log.Info($"Tema de la barra: {(light ? "claro" : "oscuro")}");

        // Colores semánticos de Fluent (Windows 11) para cada fondo.
        Set("WidgetForegroundBrush", light ? "#1A1A1A" : "#FFFFFF");
        Set("Level.Good", light ? "#0F7B0F" : "#6CCB5F");
        Set("Level.Low", light ? "#9D5D00" : "#FCE100");
        Set("Level.Critical", light ? "#C42B1C" : "#FF99A4");
        Set("Level.Unknown", light ? "#6E6E6E" : "#9E9E9E");

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
