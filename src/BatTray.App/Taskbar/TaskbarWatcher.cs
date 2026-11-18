using System.Windows.Interop;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Watches every taskbar (primary and secondary monitors): position/size changes (WinEvent), Explorer
/// restarts (TaskbarCreated message), monitors being added or removed, foreground changes and a
/// once-per-second fallback poll. Must be created and used on the UI thread.
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
    private IReadOnlyList<TaskbarInfo> _last = [];
    private HashSet<IntPtr> _watchedWindows = [];
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

    /// <summary>The set of taskbars, their geometry or the full-screen state changed.</summary>
    public event Action<IReadOnlyList<TaskbarInfo>, bool>? Changed;

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
        var all = TaskbarLocator.TryLocateAll();
        if (all.Count == 0)
        {
            if (_last.Count > 0)
                Log.Info("Taskbar not found (is Explorer restarting?).");
            _last = [];
            _watchedWindows = [];
            return;
        }

        EnsureLocationHook(all[0].TaskbarHwnd);

        var fullscreen = TaskbarLocator.IsFullscreenAppRunning();
        if (!force && all.SequenceEqual(_last) && fullscreen == _lastFullscreen)
            return;

        foreach (var info in all.Where(i => _last.All(l => l.TaskbarHwnd != i.TaskbarHwnd)))
        {
            Log.Info($"Taskbar located ({(info.IsPrimary ? "primary" : "secondary")}): hwnd=0x{info.TaskbarHwnd:X} " +
                     $"rect={info.Taskbar} notify={info.NotifyArea}{(info.NotifyAreaEstimated ? " (estimated)" : string.Empty)} " +
                     $"buttons={info.TaskButtons} dpi={info.Dpi} autohide={info.AutoHide}");
        }

        _last = all;
        _watchedWindows = all.SelectMany(TaskbarParts).ToHashSet();
        _lastFullscreen = fullscreen;
        Changed?.Invoke(all, fullscreen);
    }

    private static IEnumerable<IntPtr> TaskbarParts(TaskbarInfo info)
    {
        yield return info.TaskbarHwnd;
        var notify = FindWindowEx(info.TaskbarHwnd, IntPtr.Zero, TaskbarLocator.NotifyAreaClass, null);
        if (notify != IntPtr.Zero)
            yield return notify;
        var buttons = FindWindowEx(info.TaskbarHwnd, IntPtr.Zero, TaskbarLocator.TaskButtonsClass, null);
        if (buttons != IntPtr.Zero)
            yield return buttons;
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
        // Explorer moves many windows (File Explorer ones too): only react to the taskbars'.
        if (idObject == OBJID_WINDOW && _watchedWindows.Contains(hwnd))
            ScheduleCheck();
    }

    private void OnForegroundChange(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        ForegroundChanged?.Invoke();
        ScheduleCheck(); // the full-screen state may have changed
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
            _last = [];
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
