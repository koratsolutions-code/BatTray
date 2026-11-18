using System.Windows;
using System.Windows.Interop;
using BatTray.App.Diagnostics;
using BatTray.App.ViewModels;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Hosts the WPF widget view in a Win32 window (HwndSource) placed on the taskbar,
/// right next to the left edge of the notification area.
/// </summary>
internal sealed class TaskbarWidgetHost : IDisposable
{
    /// <summary>Gap between the widget and its neighbours (DIP).</summary>
    private const double GapDip = 4;

    /// <summary>Two rows of devices need at least this taskbar height (Windows 11 default: 48).</summary>
    private const double MinTwoRowHeightDip = 40;

    private readonly AnchorMode _mode;
    private readonly FrameworkElement _view;
    private readonly WidgetViewModel _viewModel;
    private HwndSource? _source;
    private IntPtr _attachedTo;
    private TaskbarInfo? _info;
    private bool _fullscreen;
    private int? _taskButtonsRight;
    private int? _lastShownCount;

    public TaskbarWidgetHost(AnchorMode mode, FrameworkElement view, WidgetViewModel viewModel)
    {
        _mode = mode;
        _view = view;
        _viewModel = viewModel;
    }

    public AnchorMode Mode => _mode;

    /// <summary>Applies a new taskbar geometry (called from <see cref="TaskbarWatcher.Changed"/>).</summary>
    public void Update(TaskbarInfo info, bool fullscreen)
    {
        _info = info;
        _fullscreen = fullscreen;

        if (_source is null || _attachedTo != info.TaskbarHwnd || !IsWindow(_source.Handle))
            Attach(info);

        Reposition();
    }

    /// <summary>Recomputes size and position (e.g. when the widget content changes).</summary>
    public void Reposition()
    {
        if (_source is null || _info is null)
            return;

        var info = _info;
        var scale = info.Scale;
        var heightPx = info.Taskbar.Height;
        var gapPx = (int)Math.Round(GapDip * scale);
        var rightPx = info.NotifyArea.Left - gapPx;

        // Never cover the app buttons. When not every device fits, draw them smaller, then in two rows
        // (when the taskbar is tall enough), and only then hide the ones with the most battery.
        var buttonsRight = _taskButtonsRight ?? (info.TaskButtons.IsEmpty ? (int?)null : info.TaskButtons.Right);
        var availablePx = buttonsRight is null ? int.MaxValue : rightPx - (buttonsRight.Value + gapPx);
        var heightDip = heightPx / scale;
        var maxDensity = heightDip >= MinTwoRowHeightDip ? WidgetViewModel.MaxDensity : WidgetViewModel.FirstTwoRowDensity - 1;
        _viewModel.RowHeight = Math.Floor(heightDip / 2) - 1;

        _viewModel.ShowAll();
        var widthPx = 0;
        var fits = false;
        for (var density = 0; density <= maxDensity && !fits; density++)
        {
            _viewModel.Density = density;
            widthPx = MeasureWidthPx(heightPx, scale);
            fits = widthPx <= availablePx;
        }
        while (!fits && _viewModel.HideLeastUrgent())
        {
            widthPx = MeasureWidthPx(heightPx, scale);
            fits = widthPx <= availablePx;
        }

        var shown = _viewModel.Devices.Count(d => d.IsShown);
        var state = fits ? shown : -1;
        if (state != _lastShownCount)
        {
            Log.Info(fits
                ? $"Widget: {shown}/{_viewModel.Devices.Count} devices shown, density {_viewModel.Density}, {widthPx}px (available: {(buttonsRight is null ? "unbounded" : availablePx + "px")})."
                : $"Widget hidden: not enough room (available {availablePx}px).");
            _lastShownCount = state;
        }

        var leftPx = rightPx - widthPx;
        var hwnd = _source.Handle;
        if (_mode == AnchorMode.Child)
        {
            // Coordinates relative to the Shell_TrayWnd client area.
            SetWindowPos(hwnd, HWND_TOP, leftPx - info.Taskbar.Left, 0, widthPx, heightPx,
                SWP_NOACTIVATE | (fits ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        }
        else
        {
            var visible = fits
                          && !_fullscreen
                          && IsWindowVisible(info.TaskbarHwnd)
                          && info.VisibleHeight >= info.Taskbar.Height / 2;
            SetWindowPos(hwnd, HWND_TOPMOST, leftPx, info.Taskbar.Top, widthPx, heightPx,
                SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        }
    }

    private int MeasureWidthPx(int heightPx, double scale)
    {
        // Apply pending visibility changes first; otherwise Measure returns the previous size.
        _view.UpdateLayout();
        _view.Measure(new Size(double.PositiveInfinity, heightPx / scale));
        return Math.Max(1, (int)Math.Ceiling(_view.DesiredSize.Width * scale));
    }

    /// <summary>Real right edge of the app buttons (measured with UI Automation), or null when unknown.</summary>
    public void SetTaskButtonsRight(int? right)
    {
        _taskButtonsRight = right;
        Reposition();
    }

    /// <summary>In Overlay mode, puts the widget back on top of the taskbar.</summary>
    public void BringToFront()
    {
        if (_mode == AnchorMode.Overlay && _source is not null)
            SetWindowPos(_source.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Attach(TaskbarInfo info)
    {
        DestroySource();

        var parameters = new HwndSourceParameters("BatTray.TaskbarWidget")
        {
            // Child: child of the taskbar. Overlay: popup owned by the taskbar (always stays above it).
            ParentWindow = info.TaskbarHwnd,
            WindowStyle = _mode == AnchorMode.Child
                ? WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS
                : WS_POPUP | WS_VISIBLE,
            ExtendedWindowStyle = _mode == AnchorMode.Child
                ? 0
                : WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            UsesPerPixelTransparency = true,
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = info.Taskbar.Height,
        };

        try
        {
            _source = new HwndSource(parameters)
            {
                RootVisual = _view,
                SizeToContent = SizeToContent.Manual,
            };
            _attachedTo = info.TaskbarHwnd;
            Log.Info($"Widget created in {_mode} mode: hwnd=0x{_source.Handle:X}");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create the widget in {_mode} mode.", ex);
            _source = null;
            _attachedTo = IntPtr.Zero;
        }
    }

    private void DestroySource()
    {
        if (_source is null)
            return;

        try
        {
            _source.RootVisual = null;
            _source.Dispose();
        }
        catch (Exception ex)
        {
            // If Explorer died, the child window was already destroyed together with its parent.
            Log.Info($"Releasing previous widget: {ex.Message}");
        }

        _source = null;
        _attachedTo = IntPtr.Zero;
    }

    public void Dispose() => DestroySource();
}
