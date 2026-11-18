using System.Windows.Automation;
using System.Windows.Threading;
using BatTray.App.Diagnostics;

namespace BatTray.App.Taskbar;

/// <summary>
/// On Windows 11 the app buttons are drawn with XAML and <c>ReBarWindow32</c> does not reflect their real
/// extent. This probe uses UI Automation to get, for every taskbar, the right edge of its last button
/// (Start, Search, apps…). It runs on its own thread: UIA calls from the UI thread could deadlock,
/// because our widget is part of the taskbar's automation tree too.
/// </summary>
internal sealed class TaskButtonsProbe : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private static readonly Condition ButtonsCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleButton"));

    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private Dictionary<IntPtr, int?> _last = [];
    private bool _errorLogged;

    public TaskButtonsProbe(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "BatTray.TaskButtonsProbe" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>
    /// Right edge (screen px) of the buttons of each taskbar, keyed by taskbar window; null when it cannot
    /// be measured. Raised on the UI thread.
    /// </summary>
    public event Action<IReadOnlyDictionary<IntPtr, int?>>? Changed;

    public void Start() => _thread.Start();

    private void Run()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            var current = TaskbarLocator.FindTaskbarHandles().ToDictionary(h => h, Measure);
            if (!SameAs(current))
            {
                _last = current;
                _dispatcher.BeginInvoke(() => Changed?.Invoke(current));
            }

            token.WaitHandle.WaitOne(Interval);
        }
    }

    private bool SameAs(Dictionary<IntPtr, int?> current) =>
        current.Count == _last.Count && current.All(kv => _last.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private int? Measure(IntPtr taskbar)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            var buttons = root.FindAll(TreeScope.Descendants, ButtonsCondition);

            double right = double.NaN;
            foreach (AutomationElement button in buttons)
            {
                var rect = button.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && (double.IsNaN(right) || rect.Right > right))
                    right = rect.Right;
            }

            _errorLogged = false;
            return double.IsNaN(right) ? null : (int)Math.Ceiling(right);
        }
        catch (Exception ex)
        {
            if (!_errorLogged)
                Log.Error("Could not measure the taskbar buttons with UI Automation.", ex);
            _errorLogged = true;
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
