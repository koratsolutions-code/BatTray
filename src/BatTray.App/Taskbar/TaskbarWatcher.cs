using System.Windows.Interop;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Watches the taskbar: position/size changes (WinEvent), Explorer restarts (TaskbarCreated message),
/// foreground changes and a once-per-second fallback poll. Must be created and used on the UI thread.
/// </summary>
internal sealed class TaskbarWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly uint _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _poll;
    private readonly WinEventDelegate _locationProc;
    private readonly WinEventDelegate _foregroundProc;

    private HwndSource? _messageWindow;
    private IntPtr _locationHook;
    private IntPtr _foregroundHook;
    private uint _hookedPid;
    private TaskbarInfo? _last;
    private bool _lastFullscreen;

    public TaskbarWatcher()
    {
        // Delegates are kept in fields so the GC does not collect them while the hooks are active.
        _locationProc = OnLocationChange;
        _foregroundProc = OnForegroundChange;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebounceDelay };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Check(force: false); };
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += (_, _) => Check(force: false);
    }

    /// <summary>The taskbar geometry or the full-screen state changed.</summary>
    public event Action<TaskbarInfo, bool>? Changed;

    /// <summary>Another window came to the foreground (used to re-assert Overlay mode).</summary>
    public event Action? ForegroundChanged;

    public void Start()
    {
        // Hidden top-level window: receives the "TaskbarCreated" broadcast when Explorer restarts.
        _messageWindow = new HwndSource(new HwndSourceParameters("BatTray.Messages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _messageWindow.AddHook(WndProc);

        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        _poll.Start();
        Check(force: true);
    }

    /// <summary>Forces a notification with the current state.</summary>
    public void Refresh() => Check(force: true);

    private void Check(bool force)
    {
        var info = TaskbarLocator.TryLocate();
        if (info is null)
        {
            if (_last is not null)
                Log.Info("Taskbar not found (is Explorer restarting?).");
            _last = null;
            return;
        }

        EnsureLocationHook(info.TaskbarHwnd);

        var fullscreen = TaskbarLocator.IsFullscreenAppRunning();
        if (!force && info == _last && fullscreen == _lastFullscreen)
            return;

        if (_last is null || info.TaskbarHwnd != _last.TaskbarHwnd)
        {
            Log.Info($"Taskbar located: hwnd=0x{info.TaskbarHwnd:X} rect={info.Taskbar} notify={info.NotifyArea}" +
                     $"{(info.NotifyAreaEstimated ? " (estimated)" : string.Empty)} buttons={info.TaskButtons}" +
                     $" dpi={info.Dpi} autohide={info.AutoHide}");
        }

        _last = info;
        _lastFullscreen = fullscreen;
        Changed?.Invoke(info, fullscreen);
    }

    private void EnsureLocationHook(IntPtr taskbar)
    {
        GetWindowThreadProcessId(taskbar, out var pid);
        if (pid == _hookedPid && _locationHook != IntPtr.Zero)
            return;

        if (_locationHook != IntPtr.Zero)
            UnhookWinEvent(_locationHook);

        _hookedPid = pid;
        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
            _locationProc, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnLocationChange(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || _last is null)
            return;

        // Explorer moves many windows (File Explorer ones too): only react to the taskbar's.
        if (hwnd == _last.TaskbarHwnd || IsTaskbarPart(hwnd))
            ScheduleCheck();
    }

    private void OnForegroundChange(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        ForegroundChanged?.Invoke();
        ScheduleCheck(); // the full-screen state may have changed
    }

    private bool IsTaskbarPart(IntPtr hwnd)
    {
        var tb = _last!.TaskbarHwnd;
        return hwnd == FindWindowEx(tb, IntPtr.Zero, TaskbarLocator.NotifyAreaClass, null)
            || hwnd == FindWindowEx(tb, IntPtr.Zero, TaskbarLocator.TaskButtonsClass, null);
    }

    private void ScheduleCheck()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            Log.Info("TaskbarCreated received: Explorer restarted.");
            _last = null;
            Check(force: true);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _poll.Stop();
        _debounce.Stop();
        if (_locationHook != IntPtr.Zero)
            UnhookWinEvent(_locationHook);
        if (_foregroundHook != IntPtr.Zero)
            UnhookWinEvent(_foregroundHook);
        _locationHook = _foregroundHook = IntPtr.Zero;
        _messageWindow?.RemoveHook(WndProc);
        _messageWindow?.Dispose();
        _messageWindow = null;
    }
}
