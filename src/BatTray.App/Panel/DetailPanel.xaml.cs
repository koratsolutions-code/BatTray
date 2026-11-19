using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using BatTray.App.Interop;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Panel;

/// <summary>
/// Flyout listing every device with its level bar, charging state and last reading. Opens above the
/// taskbar next to the click and closes as soon as it loses focus, like Windows' own flyouts.
/// </summary>
public partial class DetailPanel : Window
{
    /// <summary>Distance between the panel and the taskbar (DIP, includes the shadow margin).</summary>
    private const double TaskbarGap = 0;

    private bool _closing;
    private DateTime _hiddenAt = DateTime.MinValue;

    public DetailPanel()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                _hiddenAt = DateTime.UtcNow;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Hide();
        };
    }

    public void SetItems(IReadOnlyList<DetailPanelItem> items)
    {
        Items.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows the panel above the taskbar, horizontally centred on the cursor and kept on screen.</summary>
    internal void ShowNearCursor(RECT taskbar, RECT monitor)
    {
        GetCursorPos(out var cursor);
        ShowAt(taskbar, monitor, cursor.X);
    }

    /// <summary>Shows the panel above the taskbar, horizontally centred on <paramref name="anchorX"/> (or closes it when open).</summary>
    internal void ShowAt(RECT taskbar, RECT monitor, int anchorX)
    {
        // Clicking the widget while the panel is open first deactivates (hides) it: treat that click as "close".
        if (IsVisible || DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(400))
        {
            Hide();
            return;
        }

        // Render off-screen first to know the size in physical pixels, then move it into place.
        Left = -10000;
        Top = -10000;
        Show();
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);

        var x = Math.Clamp(anchorX - width / 2, monitor.Left, monitor.Right - width);
        var y = taskbar.Top - height - (int)(TaskbarGap * scale);

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);
        Activate();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Keep a single instance alive for the app lifetime; only really close on shutdown.
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void CloseForShutdown()
    {
        _closing = true;
        Close();
    }
}

/// <summary>Draws the history segments (x, y in 0–1) into the given width and height.</summary>
public sealed class HistoryGeometryConverter : IMultiValueConverter
{
    public static HistoryGeometryConverter Instance { get; } = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [IReadOnlyList<IReadOnlyList<(double X, double Y)>> segments, double width, double height] ||
            double.IsNaN(width) || width <= 0 || height <= 0)
            return null;

        const double pad = 1; // keeps the stroke inside the box at 0 % and 100 %
        var geometry = new System.Windows.Media.StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var segment in segments)
            {
                if (segment.Count == 0)
                    continue;
                Point P((double X, double Y) p) => new(p.X * width, pad + (1 - p.Y) * (height - 2 * pad));
                context.BeginFigure(P(segment[0]), isFilled: false, isClosed: false);
                if (segment.Count == 1)
                    context.LineTo(new Point(P(segment[0]).X + 1.5, P(segment[0]).Y), isStroked: true, isSmoothJoin: false);
                for (var i = 1; i < segment.Count; i++)
                    context.LineTo(P(segment[i]), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Multiplies a 0–1 fraction by an available width.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public static FractionWidthConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double fraction, double width] && !double.IsNaN(width)
            ? Math.Max(0, Math.Min(1, fraction)) * width
            : 0d;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
