using System.Runtime.InteropServices;
using BatTray.App.Interop;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Locates the primary taskbar and its regions. Everything that depends on Explorer's internal
/// structure (window class names) is isolated here.
/// </summary>
internal static class TaskbarLocator
{
    public const string TaskbarClass = "Shell_TrayWnd";
    public const string NotifyAreaClass = "TrayNotifyWnd";
    public const string TaskButtonsClass = "ReBarWindow32";

    /// <summary>Estimated notification-area width when TrayNotifyWnd cannot be found (DIP).</summary>
    private const int FallbackNotifyWidthDip = 300;

    public static TaskbarInfo? TryLocate()
    {
        var taskbar = FindWindow(TaskbarClass, null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect) || taskbarRect.IsEmpty)
            return null;

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0)
            dpi = 96;

        var estimated = false;
        var notify = FindWindowEx(taskbar, IntPtr.Zero, NotifyAreaClass, null);
        if (notify == IntPtr.Zero || !GetWindowRect(notify, out var notifyRect) || notifyRect.IsEmpty)
        {
            estimated = true;
            var width = (int)(FallbackNotifyWidthDip * dpi / 96.0);
            notifyRect = new RECT
            {
                Left = taskbarRect.Right - width,
                Top = taskbarRect.Top,
                Right = taskbarRect.Right,
                Bottom = taskbarRect.Bottom,
            };
        }

        var buttons = FindWindowEx(taskbar, IntPtr.Zero, TaskButtonsClass, null);
        if (buttons == IntPtr.Zero || !GetWindowRect(buttons, out var buttonsRect))
            buttonsRect = default;

        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var monitorRect = GetMonitorInfo(MonitorFromWindow(taskbar, MONITOR_DEFAULTTONEAREST), ref monitorInfo)
            ? monitorInfo.rcMonitor
            : taskbarRect;

        return new TaskbarInfo(taskbar, taskbarRect, notifyRect, buttonsRect, monitorRect, dpi, IsAutoHide(), estimated);
    }

    public static bool IsAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        var state = (uint)SHAppBarMessage(ABM_GETSTATE, ref data);
        return (state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>True when a full-screen app is running (D3D game, presentation, video…).</summary>
    public static bool IsFullscreenAppRunning()
    {
        if (SHQueryUserNotificationState(out var state) != 0)
            return false;
        return state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }
}
