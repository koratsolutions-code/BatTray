using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BatTray.App.Services;

/// <summary>
/// Lets Windows close BatTray cleanly. BatTray has no visible window, so installers (Windows Installer's Restart
/// Manager) and the end of the Windows session had nothing to ask: an update found the old version still running and
/// left its files for the next reboot. This hidden top-level window answers <c>WM_QUERYENDSESSION</c> /
/// <c>WM_ENDSESSION</c> / <c>WM_CLOSE</c> by closing BatTray, and the app registers with Restart Manager so an update
/// can start the new version again once the files are replaced.
/// </summary>
internal sealed class SessionCloseWindow : IDisposable
{
    private const int WM_CLOSE = 0x0010;
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION = 0x0016;

    // Restart only after an update, never after a crash, a hang or a reboot (start with Windows covers that).
    private const uint RESTART_NO_CRASH = 1, RESTART_NO_HANG = 2, RESTART_NO_REBOOT = 8;

    private readonly HwndSource _window;
    private readonly Action _close;
    private bool _closing;

    public SessionCloseWindow(Action close)
    {
        _close = close;
        // Top-level (not message-only) so Restart Manager finds it; never shown.
        _window = new HwndSource(new HwndSourceParameters("BatTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        _ = RegisterApplicationRestart(string.Empty, RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_QUERYENDSESSION:
                handled = true;
                return new IntPtr(1); // agree to close
            case WM_ENDSESSION when wParam != IntPtr.Zero:
            case WM_CLOSE:
                handled = true;
                if (!_closing)
                {
                    _closing = true;
                    Diagnostics.Log.Info(msg == WM_CLOSE ? "Asked to close (WM_CLOSE)." : "Windows session ending or installer closing BatTray.");
                    _close();
                }
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, uint flags);
}
