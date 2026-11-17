using System.ComponentModel;
using System.Runtime.CompilerServices;
using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.App.ViewModels;

/// <summary>Un elemento del widget: icono del dispositivo + porcentaje.</summary>
public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private DeviceKind _kind;
    private int? _percent;
    private DateTimeOffset _lastUpdated;
    private bool _isShown = true;

    public DeviceViewModel(Device device)
    {
        Id = device.Id;
        Update(device);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public DeviceKind Kind { get => _kind; private set => Set(ref _kind, value); }

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

    /// <summary>False si el widget lo oculta por falta de espacio en la barra.</summary>
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

    public string PercentText => _percent is { } p ? $"{p}%" : "—";

    public BatteryLevel Level => BatteryLevelPalette.Classify(_percent);

    public string ToolTip => $"{Name}: {PercentText}\nÚltima lectura: {_lastUpdated.LocalDateTime:HH:mm:ss}";

    public void Update(Device device)
    {
        Name = device.Name;
        Kind = device.Kind;
        Percent = device.BatteryPercent;
        _lastUpdated = device.LastUpdated;
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
