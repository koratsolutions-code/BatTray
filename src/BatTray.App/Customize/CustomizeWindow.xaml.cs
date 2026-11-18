using InvalidDataException = System.IO.InvalidDataException;
using IOException = System.IO.IOException;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatTray.App.Icons;
using BatTray.Core.Models;
using BatTray.Core.Settings;
using Microsoft.Win32;

namespace BatTray.App.Customize;

/// <summary>Rename devices and pick their icon from the built-in set or an uploaded image.</summary>
public partial class CustomizeWindow : Window
{
    private static readonly Brush IconBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x28, 0x4C));
    private static CustomizeWindow? _open;

    private readonly Func<string, DeviceCustomization?> _get;
    private readonly Action<string, DeviceCustomization?> _save;
    private readonly List<Entry> _entries;
    private Entry? _current;
    private string? _pendingIcon;
    private bool _loading;

    private CustomizeWindow(IReadOnlyList<Device> devices, Func<string, DeviceCustomization?> get, Action<string, DeviceCustomization?> save)
    {
        InitializeComponent();
        _get = get;
        _save = save;
        _entries = devices.Select(d => new Entry(d.Id, d.Name, d.Kind)).ToList();
        foreach (var entry in _entries)
            entry.Refresh(_get(entry.Id));
        DeviceList.ItemsSource = _entries;
        Closed += (_, _) => _open = null;
        Loaded += (_, _) => DeviceList.SelectedIndex = _entries.Count > 0 ? 0 : -1;
    }

    /// <summary>Shows the window (or brings the open one to the front).</summary>
    /// <param name="devices">Devices with their own names and kinds (before customization).</param>
    public static void ShowSingle(IReadOnlyList<Device> devices, Func<string, DeviceCustomization?> get, Action<string, DeviceCustomization?> save)
    {
        _open ??= new CustomizeWindow(devices, get, save);
        _open.Show();
        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        _current = DeviceList.SelectedItem as Entry;
        Editor.IsEnabled = SaveButton.IsEnabled = ResetButton.IsEnabled = _current is not null;
        if (_current is null)
            return;

        var customization = _get(_current.Id);
        _loading = true;
        NameBox.Text = customization?.Name ?? _current.OriginalName;
        OriginalName.Text = $"Original name: {_current.OriginalName}";
        _pendingIcon = customization?.Icon;
        BuildIconTiles();
        _loading = false;
        HideError();
    }

    /// <summary>"Default", the built-in set and, when there is one, the uploaded image.</summary>
    private void BuildIconTiles()
    {
        if (_current is null)
            return;
        IconPanel.Children.Clear();

        AddTile(null, $"Default ({DeviceIconProvider.DefaultKey(_current.Kind)})", DeviceIconProvider.Resolve(_current.Kind, null), isDefault: true);
        foreach (var (key, label) in DeviceIconProvider.BuiltIn)
            AddTile(key, label, DeviceIconProvider.Resolve(_current.Kind, key));
        if (_pendingIcon?.StartsWith(DeviceCustomization.FilePrefix, StringComparison.Ordinal) == true)
            AddTile(_pendingIcon, "Your image", DeviceIconProvider.Resolve(_current.Kind, _pendingIcon));
    }

    private void AddTile(string? value, string label, DeviceIcon icon, bool isDefault = false)
    {
        var content = CreateIconElement(icon, 20);
        if (isDefault && content is FrameworkElement fe)
        {
            // Mark the "default" tile with a small dot under the icon.
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(fe);
            stack.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD0)), Margin = new Thickness(0, 3, 0, 0) });
            content = stack;
        }

        var tile = new RadioButton
        {
            Style = (Style)FindResource("IconTile"),
            GroupName = "Icons",
            Content = content,
            ToolTip = label,
            Tag = value,
            IsChecked = value == _pendingIcon,
        };
        AutomationProperties.SetName(tile, label);
        tile.Checked += (_, _) =>
        {
            if (!_loading)
                _pendingIcon = (string?)tile.Tag;
        };
        IconPanel.Children.Add(tile);
    }

    private void OnUpload(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var dialog = new OpenFileDialog { Title = "Choose an icon image", Filter = DeviceIconProvider.FileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _pendingIcon = DeviceIconProvider.Import(dialog.FileName);
            _loading = true;
            BuildIconTiles();
            _loading = false;
            HideError();
        }
        catch (InvalidDataException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the image: {ex.Message}");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var name = NameBox.Text.Trim();
        var customization = new DeviceCustomization(
            name.Length == 0 || name == _current.OriginalName ? null : name,
            _pendingIcon);
        _save(_current.Id, customization);
        _current.Refresh(_get(_current.Id));
        DeviceList.Items.Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        _save(_current.Id, null);
        _current.Refresh(null);
        DeviceList.Items.Refresh();
        OnDeviceSelected(this, null!);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    internal static UIElement CreateIconElement(DeviceIcon icon, double size)
    {
        if (icon.Image is { } image)
            return new Image { Source = image, Width = size, Height = size };
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Path
            {
                Data = icon.Geometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
                Stroke = IconBrush,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            },
        };
    }

    /// <summary>One device in the list.</summary>
    private sealed class Entry(string id, string originalName, DeviceKind kind)
    {
        public string Id { get; } = id;
        public string OriginalName { get; } = originalName;
        public DeviceKind Kind { get; } = kind;
        public string DisplayName { get; private set; } = originalName;
        public UIElement? IconElement { get; private set; }

        public void Refresh(DeviceCustomization? customization)
        {
            DisplayName = customization?.Name ?? OriginalName;
            IconElement = CreateIconElement(DeviceIconProvider.Resolve(Kind, customization?.Icon), 16);
        }
    }
}
