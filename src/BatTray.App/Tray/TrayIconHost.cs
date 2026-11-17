using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatTray.App.Diagnostics;
using BatTray.Core.Models;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace BatTray.App.Tray;

/// <summary>
/// The BatTray icon in the notification area: always available entry point to the menu, even when the
/// taskbar widget is hidden for lack of room.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private bool? _lightTaskbar;

    public TrayIconHost(ContextMenu menu)
    {
        Menu = menu;
        // The menu is not handed to H.NotifyIcon: MenuPresenter opens it so it does not close immediately.
        _icon = new TaskbarIcon
        {
            ToolTipText = "BatTray",
            MenuActivation = PopupActivationMode.None,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => MenuRequested?.Invoke();
        _icon.TrayRightMouseUp += (_, _) => MenuRequested?.Invoke();
    }

    public ContextMenu Menu { get; }

    /// <summary>The user clicked the icon (left or right button).</summary>
    public event Action? MenuRequested;

    public void Show(bool lightTaskbar)
    {
        SetTheme(lightTaskbar);
        // false: do not enable Windows "Efficiency mode", which would lower the process priority.
        _icon.ForceCreate(enablesEfficiencyMode: false);
        Log.Info("Tray icon created.");
    }

    public void SetTheme(bool lightTaskbar)
    {
        if (_lightTaskbar == lightTaskbar)
            return;
        _lightTaskbar = lightTaskbar;

        var previous = _systemIcon;
        _systemIcon = ToSystemIcon(CreateIconImage(lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White));
        _icon.Icon = _systemIcon;
        DestroyIcon(previous);
    }

    private System.Drawing.Icon? _systemIcon;

    /// <summary>
    /// H.NotifyIcon only converts file/stream image sources, so the rendered bitmap is turned into a
    /// System.Drawing.Icon (32-bit with alpha) directly.
    /// </summary>
    private static System.Drawing.Icon ToSystemIcon(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        using var bitmap = new System.Drawing.Bitmap(source.PixelWidth, source.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);

        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DestroyIcon(System.Drawing.Icon? icon)
    {
        if (icon is null)
            return;
        NativeDestroyIcon(icon.Handle);
        icon.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool NativeDestroyIcon(IntPtr handle);

    public void SetDevices(IReadOnlyList<Device> devices)
    {
        var lines = devices.Count == 0
            ? ["No devices with battery data"]
            : devices.Select(d => $"{d.Name}: {(d.BatteryPercent is { } p ? $"{p}%" : "?")}");
        var text = "BatTray\n" + string.Join("\n", lines);
        // The notification area truncates tooltips at 127 characters.
        _icon.ToolTipText = text.Length > 127 ? text[..124] + "…" : text;
    }

    /// <summary>Battery outline with a green charge bar, drawn at 32×32 px.</summary>
    private static BitmapSource CreateIconImage(Color outline)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var pen = new Pen(new SolidColorBrush(outline), 2.4) { LineJoin = PenLineJoin.Round };
            dc.DrawRoundedRectangle(null, pen, new Rect(2.5, 9.5, 24, 14), 3, 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(outline), null, new Rect(27.5, 13.5, 3, 6), 1, 1);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)), null, new Rect(5.5, 12.5, 14, 8), 1.5, 1.5);
        }

        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        _icon.Dispose();
        DestroyIcon(_systemIcon);
        _systemIcon = null;
    }
}
