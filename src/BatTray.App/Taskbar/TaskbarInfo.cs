using BatTray.App.Interop;

namespace BatTray.App.Taskbar;

/// <summary>Instantánea de la geometría de la barra de tareas principal (píxeles físicos de pantalla).</summary>
internal sealed record TaskbarInfo(
    IntPtr TaskbarHwnd,
    RECT Taskbar,
    RECT NotifyArea,
    RECT TaskButtons,
    RECT Monitor,
    uint Dpi,
    bool AutoHide,
    bool NotifyAreaEstimated)
{
    public double Scale => Dpi / 96.0;

    /// <summary>Altura de la barra que está realmente dentro del monitor (≈0 si el autoocultar la escondió).</summary>
    public int VisibleHeight =>
        Math.Max(0, Math.Min(Taskbar.Bottom, Monitor.Bottom) - Math.Max(Taskbar.Top, Monitor.Top));
}
