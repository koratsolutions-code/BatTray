using BatTray.App.Interop;

namespace BatTray.App.Taskbar;

/// <summary>Snapshot of the primary taskbar geometry (physical screen pixels).</summary>
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

    /// <summary>Height of the taskbar actually inside the monitor (≈0 when auto-hide has hidden it).</summary>
    public int VisibleHeight =>
        Math.Max(0, Math.Min(Taskbar.Bottom, Monitor.Bottom) - Math.Max(Taskbar.Top, Monitor.Top));
}
