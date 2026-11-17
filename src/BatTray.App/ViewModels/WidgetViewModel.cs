using System.Collections.ObjectModel;
using BatTray.Core.Models;

namespace BatTray.App.ViewModels;

/// <summary>Lista de dispositivos que muestra el widget. Usar solo desde el hilo de UI.</summary>
public sealed class WidgetViewModel
{
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    /// <summary>Vuelve a mostrar todos los dispositivos.</summary>
    public void ShowAll()
    {
        foreach (var device in Devices)
            device.IsShown = true;
    }

    /// <summary>
    /// Oculta el dispositivo visible con más batería (el menos urgente). Los de batería desconocida se ocultan primero.
    /// Devuelve false si ya no queda ninguno visible.
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

    /// <summary>Sincroniza la colección con la lista recibida. Devuelve true si cambió el número u orden de elementos.</summary>
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
