using System.Windows.Input;
using BatTray.App.Diagnostics;
using BatTray.App.ViewModels;
using BatTray.Core.Models;

namespace BatTray.App.Taskbar;

/// <summary>
/// One taskbar widget per taskbar (primary and secondary monitors). Each widget has its own view model,
/// because the devices that fit depend on the room available on that taskbar.
/// </summary>
internal sealed class WidgetSet : IDisposable
{
    private readonly AnchorMode _mode;
    private readonly bool _allTaskbars;
    private readonly Action _leftClick;
    private readonly Action _rightClick;
    private readonly Dictionary<IntPtr, Widget> _widgets = [];
    private IReadOnlyDictionary<IntPtr, int?> _buttonsRight = new Dictionary<IntPtr, int?>();
    private IReadOnlyList<Device> _devices = [];
    private int _low;
    private int _critical;
    private WidgetDisplay _display = WidgetDisplay.Default;

    public WidgetSet(AnchorMode mode, bool allTaskbars, Action leftClick, Action rightClick)
    {
        _mode = mode;
        _allTaskbars = allTaskbars;
        _leftClick = leftClick;
        _rightClick = rightClick;
    }

    /// <summary>Creates, updates or removes widgets to match the current taskbars.</summary>
    public void UpdateTaskbars(IReadOnlyList<TaskbarInfo> taskbars, bool fullscreen)
    {
        var wanted = _allTaskbars ? taskbars : taskbars.Where(t => t.IsPrimary).ToList();

        foreach (var gone in _widgets.Keys.Where(h => wanted.All(t => t.TaskbarHwnd != h)).ToList())
        {
            _widgets[gone].Dispose();
            _widgets.Remove(gone);
            Log.Info($"Widget removed from taskbar 0x{gone:X}.");
        }

        foreach (var taskbar in wanted)
        {
            if (!_widgets.TryGetValue(taskbar.TaskbarHwnd, out var widget))
            {
                widget = new Widget(_mode, _leftClick, _rightClick);
                widget.ViewModel.Apply(_devices, _low, _critical, _display);
                _widgets[taskbar.TaskbarHwnd] = widget;
            }

            if (_buttonsRight.TryGetValue(taskbar.TaskbarHwnd, out var right))
                widget.Host.SetTaskButtonsRight(right);
            widget.Host.Update(taskbar, fullscreen);
        }
    }

    public void SetTaskButtonsRight(IReadOnlyDictionary<IntPtr, int?> buttonsRight)
    {
        _buttonsRight = buttonsRight;
        foreach (var (hwnd, widget) in _widgets)
        {
            if (buttonsRight.TryGetValue(hwnd, out var right))
                widget.Host.SetTaskButtonsRight(right);
        }
    }

    public void SetDevices(IReadOnlyList<Device> visibleDevices, int lowThreshold, int criticalThreshold, WidgetDisplay display)
    {
        _devices = visibleDevices;
        _low = lowThreshold;
        _critical = criticalThreshold;
        _display = display;
        foreach (var widget in _widgets.Values)
            widget.ViewModel.Apply(visibleDevices, lowThreshold, criticalThreshold, display);
    }

    public void Reposition()
    {
        foreach (var widget in _widgets.Values)
            widget.Host.Reposition();
    }

    public void BringToFront()
    {
        foreach (var widget in _widgets.Values)
            widget.Host.BringToFront();
    }

    public void Dispose()
    {
        foreach (var widget in _widgets.Values)
            widget.Dispose();
        _widgets.Clear();
    }

    private sealed class Widget : IDisposable
    {
        public Widget(AnchorMode mode, Action leftClick, Action rightClick)
        {
            ViewModel = new WidgetViewModel();
            var view = new TaskbarWidgetView { DataContext = ViewModel };
            view.MouseLeftButtonUp += (_, e) => Handle(e, leftClick);
            view.MouseRightButtonUp += (_, e) => Handle(e, rightClick);
            Host = new TaskbarWidgetHost(mode, view, ViewModel);
        }

        public WidgetViewModel ViewModel { get; }
        public TaskbarWidgetHost Host { get; }

        private static void Handle(MouseButtonEventArgs e, Action action)
        {
            e.Handled = true;
            action();
        }

        public void Dispose() => Host.Dispose();
    }
}
