using System.Collections.ObjectModel;
using BatTray.Core.Models;

namespace BatTray.App.ViewModels;

/// <summary>Devices shown by the widget. Use from the UI thread only.</summary>
public sealed class WidgetViewModel
{
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

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
    public bool Apply(IReadOnlyList<Device> devices)
    {
        var structureChanged = false;
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
                Devices.Insert(i, new DeviceViewModel(device));
                structureChanged = true;
                continue;
            }

            existing.Update(device);
            var index = Devices.IndexOf(existing);
            if (index != i)
            {
                Devices.Move(index, i);
                structureChanged = true;
            }
        }

        return structureChanged;
    }
}
