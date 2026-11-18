using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatTray.Core.Models;
using BatTray.Core.Settings;

namespace BatTray.App.Tray;

/// <summary>Actions offered by the BatTray menu (tray icon and widget right-click).</summary>
internal sealed record DeviceMenuActions(
    Action<string, bool> SetDeviceHidden,
    Action<bool> SetStartWithWindows,
    Action<bool> SetLowBatteryAlerts,
    Action SaveDiagnostics,
    Action OpenLogFolder,
    Action ShowAbout,
    Action Exit);

/// <summary>Builds the shared BatTray context menu.</summary>
internal static class DeviceMenu
{

    public static void Fill(
        ContextMenu menu,
        IReadOnlyList<Device> devices,
        AppSettings settings,
        bool startWithWindows,
        DeviceMenuActions actions)
    {
        menu.Items.Clear();

        menu.Items.Add(new MenuItem { Header = "BatTray — Your devices. One glance.", IsEnabled = false });
        menu.Items.Add(new Separator());

        if (devices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No devices with battery data", IsEnabled = false });
        }
        else
        {
            foreach (var device in devices)
            {
                var id = device.Id;
                // The check box uses the icon column, so the device icon goes inside the header.
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                if (CreateIcon(device.Kind) is { } icon)
                    header.Children.Add(icon);
                header.Children.Add(new TextBlock
                {
                    Text = $"{device.Name}  —  {(device.BatteryPercent is { } p ? $"{p}%" : "?")}{(device.Charging == true ? "  ⚡ charging" : string.Empty)}",
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                var item = new MenuItem
                {
                    Header = header,
                    ToolTip = $"Show on the taskbar\n{device.Source}",
                    IsCheckable = true,
                    IsChecked = !settings.IsHidden(id),
                    StaysOpenOnClick = true,
                };
                item.Click += (_, _) => actions.SetDeviceHidden(id, !item.IsChecked);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());

        var autoStart = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = startWithWindows };
        autoStart.Click += (_, _) => actions.SetStartWithWindows(autoStart.IsChecked);
        menu.Items.Add(autoStart);

        var alerts = new MenuItem
        {
            Header = $"Low battery alerts (below {settings.CriticalThreshold}%)",
            IsCheckable = true,
            IsChecked = settings.LowBatteryAlerts,
        };
        alerts.Click += (_, _) => actions.SetLowBatteryAlerts(alerts.IsChecked);
        menu.Items.Add(alerts);

        var diagnostics = new MenuItem { Header = "Save diagnostics report…", ToolTip = "Lists every device found, including unsupported receivers" };
        diagnostics.Click += (_, _) => actions.SaveDiagnostics();
        menu.Items.Add(diagnostics);

        var logs = new MenuItem { Header = "Open log folder" };
        logs.Click += (_, _) => actions.OpenLogFolder();
        menu.Items.Add(logs);

        var about = new MenuItem { Header = "About BatTray" };
        about.Click += (_, _) => actions.ShowAbout();
        menu.Items.Add(about);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit BatTray" };
        exit.Click += (_, _) => actions.Exit();
        menu.Items.Add(exit);
    }

    private static UIElement? CreateIcon(DeviceKind kind)
    {
        var key = kind switch
        {
            DeviceKind.Mouse => "Icon.Mouse",
            DeviceKind.Keyboard => "Icon.Keyboard",
            DeviceKind.Headset => "Icon.Headset",
            DeviceKind.Gamepad => "Icon.Gamepad",
            _ => "Icon.Other",
        };

        if (Application.Current?.TryFindResource(key) is not Geometry geometry)
            return null;

        return new Path
        {
            Data = geometry,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            Stroke = SystemColors.MenuTextBrush,
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }
}
