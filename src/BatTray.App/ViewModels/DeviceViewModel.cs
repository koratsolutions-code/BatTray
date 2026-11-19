using System.ComponentModel;
using System.Runtime.CompilerServices;
using BatTray.App.Icons;
using BatTray.App.Localization;
using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.App.ViewModels;

/// <summary>How the widget draws its items (from the settings).</summary>
/// <param name="PercentMode"><c>always</c>, <c>low</c> or <c>never</c>.</param>
/// <param name="PercentBelow">With <c>low</c>: the percentage shows below this level.</param>
/// <param name="Size"><c>small</c>, <c>normal</c> or <c>large</c>.</param>
public sealed record WidgetDisplay(string PercentMode, int PercentBelow, string Size)
{
    public static WidgetDisplay Default { get; } = new("always", 50, "normal");

    public bool ShowsPercent(int? percent) => PercentMode switch
    {
        "never" => false,
        "low" => percent is { } p && p < PercentBelow,
        _ => true,
    };

    public double SizeFactor => Size switch
    {
        "small" => 0.85,
        "large" => 1.2,
        _ => 1.0,
    };
}

/// <summary>One widget item: device icon + percentage.</summary>
public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private DeviceKind _kind;
    private int? _percent;
    private DateTimeOffset _lastUpdated;
    private string _source = string.Empty;
    private bool _isShown = true;
    private bool _isEstimated;
    private DeviceIcon? _icon;
    private string? _iconValue;
    private DeviceKind? _iconKind;
    private int _lowThreshold = BatteryLevelPalette.DefaultLowThreshold;
    private int _criticalThreshold = BatteryLevelPalette.DefaultCriticalThreshold;

    private WidgetDisplay _display = WidgetDisplay.Default;
    private bool _showPercent = true;

    public DeviceViewModel(Device device, int lowThreshold, int criticalThreshold, WidgetDisplay? display = null)
    {
        Id = device.Id;
        Update(device, lowThreshold, criticalThreshold, display);
    }

    /// <summary>Whether the percentage is drawn next to the icon (see <see cref="WidgetDisplay"/>).</summary>
    public bool ShowPercent { get => _showPercent; private set => Set(ref _showPercent, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public DeviceKind Kind { get => _kind; private set => Set(ref _kind, value); }

    /// <summary>The device's icon (built-in or uploaded by the user).</summary>
    public DeviceIcon? Icon { get => _icon; private set => Set(ref _icon, value); }

    public int? Percent
    {
        get => _percent;
        private set
        {
            if (Set(ref _percent, value))
            {
                OnPropertyChanged(nameof(PercentText));
                OnPropertyChanged(nameof(Level));
            }
        }
    }

    private bool _isCharging;

    public bool IsCharging { get => _isCharging; private set => Set(ref _isCharging, value); }

    /// <summary>False when the widget hides it for lack of room on the taskbar.</summary>
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

    /// <summary>True when the level is estimated from usage time; shown with a leading "~".</summary>
    public bool IsEstimated { get => _isEstimated; private set { if (Set(ref _isEstimated, value)) OnPropertyChanged(nameof(PercentText)); } }

    public string PercentText => _percent is { } p ? $"{(_isEstimated ? "~" : string.Empty)}{p}%" : "—";

    public BatteryLevel Level => BatteryLevelPalette.Classify(_percent, _lowThreshold, _criticalThreshold);

    public string ToolTip =>
        $"{Name}: {PercentText}{(IsCharging ? L.ChargingSuffix : string.Empty)}{(IsEstimated ? L.EstimatedSuffix : string.Empty)}\n{L.LastReading(_lastUpdated)}" +
        (string.IsNullOrEmpty(_source) ? string.Empty : $" ({_source})");

    public void Update(Device device, int lowThreshold, int criticalThreshold, WidgetDisplay? display = null)
    {
        _display = display ?? _display;
        if (lowThreshold != _lowThreshold || criticalThreshold != _criticalThreshold)
        {
            _lowThreshold = lowThreshold;
            _criticalThreshold = criticalThreshold;
            OnPropertyChanged(nameof(Level));
        }

        Name = device.Name;
        Kind = device.Kind;
        if (_icon is null || _iconValue != device.Icon || _iconKind != device.Kind)
        {
            _iconValue = device.Icon;
            _iconKind = device.Kind;
            Icon = DeviceIconProvider.Resolve(device.Kind, device.Icon);
        }
        Percent = device.BatteryPercent;
        IsCharging = device.Charging == true;
        IsEstimated = device.Estimated;
        _lastUpdated = device.LastUpdated;
        _source = device.Source;
        ShowPercent = _display.ShowsPercent(device.BatteryPercent);
        OnPropertyChanged(nameof(ToolTip));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
