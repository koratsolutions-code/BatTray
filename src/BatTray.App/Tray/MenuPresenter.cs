using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Tray;

/// <summary>
/// Opens BatTray's context menus from the taskbar widget and the tray icon so they stay open.
/// </summary>
/// <remarks>
/// Clicks on the widget or the tray icon leave Explorer as the foreground application. Whenever the
/// foreground window changes, Windows sends WM_CANCELMODE to the window holding mouse capture, and a
/// WPF menu closes when it loses capture — so a menu opened first and activated afterwards (or left for
/// Explorer to re-activate the taskbar) flashes and disappears. The classic notification-area pattern
/// avoids that: first bring a hidden window of our own to the foreground (allowed, because our process
/// received the click), let the activation settle, and only then open the menu.
/// </remarks>
internal sealed class MenuPresenter : IDisposable
{
    private readonly HwndSource _anchor;

    public MenuPresenter()
    {
        // Hidden top-level tool window used only as the foreground owner while a menu is open.
        _anchor = new HwndSource(new HwndSourceParameters("BatTray.MenuAnchor")
        {
            Width = 0,
            Height = 0,
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        });
    }

    public void Show(ContextMenu menu)
    {
        if (menu.IsOpen)
            return;

        var activated = SetForegroundWindow(_anchor.Handle);

        // Open after the activation messages have been processed, so no WM_CANCELMODE reaches the menu.
        menu.Dispatcher.BeginInvoke(() =>
        {
            menu.PlacementTarget = null;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            Log.Info($"Menu shown (anchor activated={activated}).");
        }, DispatcherPriority.Input);
    }

    /// <summary>Logs when a menu closes (helps diagnose menus closing unexpectedly).</summary>
    public static void Track(ContextMenu menu) => menu.Closed += (_, _) => Log.Info("Menu closed.");

    public void Dispose() => _anchor.Dispose();
}
