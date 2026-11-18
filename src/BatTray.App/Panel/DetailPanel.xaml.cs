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

        GetCursorPos(out var cursor);
        var x = Math.Clamp(cursor.X - width / 2, monitor.Left, monitor.Right - width);
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
