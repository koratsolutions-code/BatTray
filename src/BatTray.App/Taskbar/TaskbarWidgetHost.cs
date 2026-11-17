using System.Windows;
using System.Windows.Interop;
using BatTray.App.Diagnostics;
using BatTray.App.ViewModels;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// Aloja la vista WPF del widget en una ventana Win32 (HwndSource) colocada sobre la barra de tareas,
/// justo a la izquierda de la zona de notificación.
/// </summary>
internal sealed class TaskbarWidgetHost : IDisposable
{
    /// <summary>Separación entre el widget y la zona de notificación (DIP).</summary>
    private const double GapDip = 4;

    private readonly AnchorMode _mode;
    private readonly FrameworkElement _view;
    private HwndSource? _source;
    private IntPtr _attachedTo;
    private TaskbarInfo? _info;
    private bool _fullscreen;
    private readonly WidgetViewModel _viewModel;
    private int? _taskButtonsRight;
    private int? _lastShownCount;

    public TaskbarWidgetHost(AnchorMode mode, FrameworkElement view, WidgetViewModel viewModel)
    {
        _mode = mode;
        _view = view;
        _viewModel = viewModel;
    }

    public AnchorMode Mode => _mode;

    /// <summary>Aplica una nueva geometría de la barra (se llama desde <see cref="TaskbarWatcher.Changed"/>).</summary>
    public void Update(TaskbarInfo info, bool fullscreen)
    {
        _info = info;
        _fullscreen = fullscreen;

        if (_source is null || _attachedTo != info.TaskbarHwnd || !IsWindow(_source.Handle))
            Attach(info);

        Reposition();
    }

    /// <summary>Recalcula tamaño y posición (p. ej. cuando cambia el contenido del widget).</summary>
    public void Reposition()
    {
        if (_source is null || _info is null)
            return;

        var info = _info;
        var scale = info.Scale;
        var heightPx = info.Taskbar.Height;

        var gapPx = (int)Math.Round(GapDip * scale);
        var rightPx = info.NotifyArea.Left - gapPx;

        // Nunca tapar los botones de apps: si no caben todos, se ocultan los dispositivos con más batería.
        var buttonsRight = _taskButtonsRight ?? (info.TaskButtons.IsEmpty ? (int?)null : info.TaskButtons.Right);
        var availablePx = buttonsRight is null ? int.MaxValue : rightPx - (buttonsRight.Value + gapPx);

        _viewModel.ShowAll();
        var widthPx = MeasureWidthPx(heightPx, scale);
        var fits = widthPx <= availablePx;
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
                ? $"Widget: {shown}/{_viewModel.Devices.Count} dispositivos visibles, {widthPx}px (disponible: {(buttonsRight is null ? "sin límite" : availablePx + "px")})."
                : $"Widget oculto: no hay espacio (disponible {availablePx}px).");
            _lastShownCount = state;
        }

        var leftPx = rightPx - widthPx;

        var hwnd = _source.Handle;
        if (_mode == AnchorMode.Child)
        {
            // Coordenadas relativas al área cliente de Shell_TrayWnd.
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
        // Aplica antes los cambios de visibilidad pendientes; si no, Measure devuelve el tamaño anterior.
        _view.UpdateLayout();
        _view.Measure(new Size(double.PositiveInfinity, heightPx / scale));
        return Math.Max(1, (int)Math.Ceiling(_view.DesiredSize.Width * scale));
    }

    /// <summary>Borde derecho real de los botones de apps (medido con UI Automation), o null si se desconoce.</summary>
    public void SetTaskButtonsRight(int? right)
    {
        _taskButtonsRight = right;
        Reposition();
    }

    /// <summary>En modo Overlay, vuelve a poner el widget encima de la barra.</summary>
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
            // Child: hija de la barra. Overlay: emergente cuyo propietario es la barra (queda siempre por encima de ella).
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
            Log.Info($"Widget creado en modo {_mode}: hwnd=0x{_source.Handle:X}");
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo crear el widget en modo {_mode}.", ex);
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
            // Si Explorer murió, la ventana hija ya fue destruida junto con su padre.
            Log.Info($"Liberando widget anterior: {ex.Message}");
        }

        _source = null;
        _attachedTo = IntPtr.Zero;
    }

    public void Dispose() => DestroySource();
}
