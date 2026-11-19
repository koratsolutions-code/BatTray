using System.Collections.ObjectModel;
using System.ComponentModel;
using BatTray.Core.Models;

namespace BatTray.App.ViewModels;

/// <summary>Devices shown by the widget and how compactly they are drawn. Use from the UI thread only.</summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    /// <summary>Densest layout available: 0–2 single row (normal, compact, small), 3–4 two rows.</summary>
    public const int MaxDensity = 4;
    public const int FirstTwoRowDensity = 3;

    private int _density;
    private double _rowHeight = 20;

    private WidgetDisplay _display = WidgetDisplay.Default;

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public WidgetDisplay Display => _display;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 0 = normal, 1 = compact, 2 = small (one row); 3 = two rows, 4 = two rows small.
    /// The widget picks the first density at which every device fits.
    /// </summary>
    public int Density
    {
        get => _density;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxDensity);
            if (clamped == _density)
                return;
            _density = clamped;
            Raise(nameof(Density));
            Raise(nameof(TwoRows));
            Raise(nameof(IconScale));
            Raise(nameof(FontSize));
            Raise(nameof(ItemMargin));
            Raise(nameof(TextMargin));
        }
    }

    public bool TwoRows => _density >= FirstTwoRowDensity;

    /// <summary>Height of each row in two-row mode (half the taskbar).</summary>
    public double RowHeight
    {
        get => _rowHeight;
        set
        {
            if (Math.Abs(value - _rowHeight) < 0.1)
                return;
            _rowHeight = value;
            Raise(nameof(RowHeight));
        }
    }

    /// <summary>Scale of the 16 × 16 icon.</summary>
    public double IconScale => _display.SizeFactor * _density switch
    {
        0 => 1.0,
        1 => 0.875,
        2 => 0.75,
        3 => 0.75,
        _ => 0.6875,
    };

    public double FontSize => _display.SizeFactor * _density switch
    {
        0 => 12,
        1 => 11,
        2 => 10,
        3 => 10.5,
        _ => 9.5,
    };

    public System.Windows.Thickness ItemMargin => _density switch
    {
        0 => new(5, 0, 5, 0),
        1 => new(3, 0, 3, 0),
        2 => new(2, 0, 2, 0),
        3 => new(3, 0, 3, 0),
        _ => new(2, 0, 2, 0),
    };

    public System.Windows.Thickness TextMargin => _density switch
    {
        0 => new(5, 0, 0, 0),
        1 => new(3, 0, 0, 0),
        _ => new(2, 0, 0, 0),
    };

    /// <summary>Shows every device again.</summary>
    public void ShowAll()
    {
        foreach (var device in Devices)
            device.IsShown = true;
    }

    /// <summary>
    /// Hides the visible device with the most battery (the least urgent one). Devices with unknown
    /// battery are hidden first. Returns false when none is left visible.
    /// </summary>
    public bool HideLeastUrgent()
    {
        var candidate = Devices
            .Where(d => d.IsShown)
            .OrderByDescending(d => d.Percent ?? int.MaxValue)
            .FirstOrDefault();

        if (candidate is null)
            return false;

        candidate.IsShown = false;
        return Devices.Any(d => d.IsShown);
    }

    /// <summary>Synchronises the collection with the given list. Returns true when items were added, removed or reordered.</summary>
    public bool Apply(IReadOnlyList<Device> devices, int lowThreshold, int criticalThreshold, WidgetDisplay? display = null)
    {
        var structureChanged = false;
        if (display is not null && display != _display)
        {
            _display = display;
            structureChanged = true; // sizes change: the host must re-measure
            Raise(nameof(IconScale));
            Raise(nameof(FontSize));
        }
        var ids = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Devices[i].Id))
            {
                Devices.RemoveAt(i);
                structureChanged = true;
            }
        }

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var existing = Devices.FirstOrDefault(d => string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Devices.Insert(i, new DeviceViewModel(device, lowThreshold, criticalThreshold, _display));
                structureChanged = true;
                continue;
            }

            existing.Update(device, lowThreshold, criticalThreshold, _display);
            var index = Devices.IndexOf(existing);
            if (index != i)
            {
                Devices.Move(index, i);
                structureChanged = true;
            }
        }

        return structureChanged;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
