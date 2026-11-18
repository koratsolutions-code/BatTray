using System.Runtime.InteropServices;
using BatTray.App.Interop;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Locates the taskbars (primary and one per extra monitor) and their regions. Everything that depends on
/// Explorer's internal structure (window class names) is isolated here.
/// </summary>
internal static class TaskbarLocator
{
    public const string TaskbarClass = "Shell_TrayWnd";
    public const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    public const string NotifyAreaClass = "TrayNotifyWnd";
    public const string TaskButtonsClass = "ReBarWindow32";

    /// <summary>Estimated notification-area width on the primary taskbar when TrayNotifyWnd is missing (DIP).</summary>
    private const int FallbackNotifyWidthDip = 300;

    /// <summary>
    /// Secondary taskbars have no notification area; Windows 11 shows the clock (and the "show desktop"
    /// sliver) at the right edge. Estimated width of that region (DIP).
    /// </summary>
    private const int SecondaryClockWidthDip = 130;

    /// <summary>Handles of every taskbar window: the primary first, then the secondary ones.</summary>
    public static IReadOnlyList<IntPtr> FindTaskbarHandles()
    {
        var handles = new List<IntPtr>();
        var primary = FindWindow(TaskbarClass, null);
        if (primary != IntPtr.Zero)
            handles.Add(primary);

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, SecondaryTaskbarClass, null)) != IntPtr.Zero)
            handles.Add(secondary);

        return handles;
    }

    /// <summary>All taskbars currently present (primary first).</summary>
    public static IReadOnlyList<TaskbarInfo> TryLocateAll() =>
        FindTaskbarHandles()
            .Select((hwnd, i) => TryLocate(hwnd, isPrimary: i == 0 && IsClass(hwnd, TaskbarClass)))
            .OfType<TaskbarInfo>()
            .ToList();

    /// <summary>The primary taskbar.</summary>
    public static TaskbarInfo? TryLocate()
    {
        var primary = FindWindow(TaskbarClass, null);
        return primary == IntPtr.Zero ? null : TryLocate(primary, isPrimary: true);
    }

    /// <summary>The taskbar on the monitor that contains the cursor (falls back to the primary one).</summary>
    public static TaskbarInfo? TryLocateUnderCursor()
    {
        var all = TryLocateAll();
        if (GetCursorPos(out var cursor))
        {
            var match = all.FirstOrDefault(t =>
                cursor.X >= t.Monitor.Left && cursor.X < t.Monitor.Right &&
                cursor.Y >= t.Monitor.Top && cursor.Y < t.Monitor.Bottom);
            if (match is not null)
                return match;
        }
        return all.FirstOrDefault();
    }

    private static TaskbarInfo? TryLocate(IntPtr taskbar, bool isPrimary)
    {
        if (!GetWindowRect(taskbar, out var taskbarRect) || taskbarRect.IsEmpty)
            return null;

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0)
            dpi = 96;

        var estimated = false;
        var notify = isPrimary ? FindWindowEx(taskbar, IntPtr.Zero, NotifyAreaClass, null) : IntPtr.Zero;
        if (notify == IntPtr.Zero || !GetWindowRect(notify, out var notifyRect) || notifyRect.IsEmpty)
        {
            estimated = true;
            var width = (int)((isPrimary ? FallbackNotifyWidthDip : SecondaryClockWidthDip) * dpi / 96.0);
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

        return new TaskbarInfo(taskbar, taskbarRect, notifyRect, buttonsRect, monitorRect, dpi, IsAutoHide(), estimated, isPrimary);
    }

    private static bool IsClass(IntPtr hwnd, string className)
    {
        var buffer = new System.Text.StringBuilder(64);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 && buffer.ToString() == className;
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
