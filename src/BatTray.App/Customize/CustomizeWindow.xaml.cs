using InvalidDataException = System.IO.InvalidDataException;
using IOException = System.IO.IOException;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatTray.App.Icons;
using BatTray.App.Localization;
using BatTray.App.Theme;
using BatTray.Core.Models;
using BatTray.Core.Settings;
using Microsoft.Win32;

namespace BatTray.App.Customize;

/// <summary>Rename devices and pick their icon from the built-in set or an uploaded image.</summary>
public partial class CustomizeWindow : Window
{
    private static CustomizeWindow? _open;

    private readonly Func<string, DeviceCustomization?> _get;
    private readonly Action<string, DeviceCustomization?> _save;
    private readonly List<Entry> _entries;
    private Entry? _current;
    private string? _pendingIcon;
    private bool _loading;

    private readonly Action<IReadOnlyList<string>> _saveOrder;
    private readonly Func<int?> _generalAlert;
    private Point _dragStart;

    private CustomizeWindow(IReadOnlyList<Device> devices, Func<string, DeviceCustomization?> get, Action<string, DeviceCustomization?> save,
        Action<IReadOnlyList<string>> saveOrder, Func<int?> generalAlert)
    {
        InitializeComponent();
        ThemeWatcher.ApplyTitleBar(this);
        _get = get;
        _save = save;
        _saveOrder = saveOrder;
        _generalAlert = generalAlert;
        _entries = devices.Select(d => new Entry(d.Id, d.Name, d.Kind)).ToList();
        foreach (var entry in _entries)
            entry.Refresh(_get(entry.Id));
        DeviceList.ItemsSource = _entries;
        Closed += (_, _) => _open = null;
        Loaded += (_, _) => DeviceList.SelectedIndex = _entries.Count > 0 ? 0 : -1;
    }

    /// <summary>Shows the window (or brings the open one to the front).</summary>
    /// <param name="devices">Devices with their own names and kinds (before customization), in the user's order.</param>
    /// <param name="saveOrder">Saves the new order of the device ids.</param>
    /// <param name="generalAlert">The general alert level, or null when notifications are off.</param>
    public static void ShowSingle(IReadOnlyList<Device> devices, Func<string, DeviceCustomization?> get, Action<string, DeviceCustomization?> save,
        Action<IReadOnlyList<string>> saveOrder, Func<int?> generalAlert)
    {
        _open ??= new CustomizeWindow(devices, get, save, saveOrder, generalAlert);
        _open.Show();
        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        _current = DeviceList.SelectedItem as Entry;
        Editor.IsEnabled = SaveButton.IsEnabled = ResetButton.IsEnabled = _current is not null;
        UpdateMoveButtons();
        if (_current is null)
            return;

        var customization = _get(_current.Id);
        _loading = true;
        NameBox.Text = customization?.Name ?? _current.OriginalName;
        OriginalName.Text = L.OriginalName(_current.OriginalName);
        _pendingIcon = customization?.Icon;
        BuildIconTiles();
        AlertGeneral.Content = _generalAlert() is { } general ? L.AlertGeneral(general) : L.AlertGeneralOff;
        AlertSlider.Value = customization?.AlertAt ?? _generalAlert() ?? 15;
        ShowAlertValue();
        (customization?.NoAlerts == true ? AlertNever : customization?.AlertAt is not null ? AlertCustom : AlertGeneral).IsChecked = true;
        _loading = false;
        HideError();
    }

    /// <summary>"Default", the built-in set and, when there is one, the uploaded image.</summary>
    private void BuildIconTiles()
    {
        if (_current is null)
            return;
        IconPanel.Children.Clear();

        AddTile(null, L.DefaultIcon(L.IconLabel(DeviceIconProvider.DefaultKey(_current.Kind))), DeviceIconProvider.Resolve(_current.Kind, null), isDefault: true);
        foreach (var (key, _) in DeviceIconProvider.BuiltIn)
            AddTile(key, L.IconLabel(key), DeviceIconProvider.Resolve(_current.Kind, key));
        if (_pendingIcon?.StartsWith(DeviceCustomization.FilePrefix, StringComparison.Ordinal) == true)
            AddTile(_pendingIcon, L.YourImage, DeviceIconProvider.Resolve(_current.Kind, _pendingIcon));
    }

    private void AddTile(string? value, string label, DeviceIcon icon, bool isDefault = false)
    {
        var content = CreateIconElement(icon, 20);
        if (isDefault && content is FrameworkElement fe)
        {
            // Mark the "default" tile with a small dot under the icon.
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(fe);
            var dot = new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 3, 0, 0) };
            dot.SetResourceReference(Shape.FillProperty, "App.Accent");
            stack.Children.Add(dot);
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
        var dialog = new OpenFileDialog { Title = L.ChooseImage, Filter = DeviceIconProvider.FileFilter, CheckFileExists = true };
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
            ShowError(L.ErrorImageSave(ex.Message));
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var name = NameBox.Text.Trim();
        var customization = new DeviceCustomization(
            name.Length == 0 || name == _current.OriginalName ? null : name,
            _pendingIcon,
            AlertCustom.IsChecked == true ? (int)AlertSlider.Value : null,
            AlertNever.IsChecked == true);
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

    // ---------- Alerts ----------

    private void OnAlertSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AlertValue is null || AlertCustom is null)
            return; // still being built
        ShowAlertValue();
        if (!_loading)
            AlertCustom.IsChecked = true;
    }

    private void ShowAlertValue() => AlertValue.Text = $"{AlertSlider.Value:0}%";

    // ---------- Order ----------

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(DeviceList.SelectedIndex, DeviceList.SelectedIndex - 1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(DeviceList.SelectedIndex, DeviceList.SelectedIndex + 1);

    private void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _entries.Count || to >= _entries.Count || from == to)
            return;
        var entry = _entries[from];
        _entries.RemoveAt(from);
        _entries.Insert(to, entry);
        DeviceList.Items.Refresh();
        DeviceList.SelectedIndex = to;
        _saveOrder(_entries.Select(x => x.Id).ToList());
        UpdateMoveButtons();
    }

    private void UpdateMoveButtons()
    {
        if (UpButton is null || DownButton is null)
            return;
        UpButton.IsEnabled = DeviceList.SelectedIndex > 0;
        DownButton.IsEnabled = DeviceList.SelectedIndex >= 0 && DeviceList.SelectedIndex < _entries.Count - 1;
    }

    private void OnListMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _dragStart = e.GetPosition(DeviceList);

    private void OnListMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || DeviceList.SelectedItem is not Entry entry)
            return;
        var delta = e.GetPosition(DeviceList) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        DragDrop.DoDragDrop(DeviceList, entry, DragDropEffects.Move);
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Entry)) is not Entry dragged)
            return;
        var target = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(DeviceList, source) as ListBoxItem
            : null;
        var to = target?.DataContext is Entry over ? _entries.IndexOf(over) : _entries.Count - 1;
        Move(_entries.IndexOf(dragged), to);
    }

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
            Child = ThemedPath(icon.Geometry),
        };
    }

    private static Path ThemedPath(Geometry? geometry)
    {
        var path = new Path
        {
                Data = geometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, "App.Text");
        return path;
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
