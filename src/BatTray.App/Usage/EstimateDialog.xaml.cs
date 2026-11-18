using System.Globalization;
using System.Windows;
using BatTray.App.Localization;
using BatTray.App.Theme;

namespace BatTray.App.Usage;

/// <summary>Asks for the name, battery life and current charge of a device to estimate.</summary>
public partial class EstimateDialog : Window
{
    public EstimateDialog(string name, double ratedHours, int currentPercent)
    {
        InitializeComponent();
        ThemeWatcher.ApplyTitleBar(this);
        NameBox.Text = name;
        HoursBox.Text = ratedHours.ToString("0.#", CultureInfo.CurrentCulture);
        PercentBox.Text = currentPercent.ToString(CultureInfo.CurrentCulture);
        Loaded += (_, _) =>
        {
            Activate();
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string DeviceName { get; private set; } = string.Empty;

    public double RatedHours { get; private set; }

    public int CurrentPercent { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError(L.ErrorName);
            return;
        }
        if (!double.TryParse(HoursBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours) || hours is < 0.5 or > 10000)
        {
            ShowError(L.ErrorHours);
            return;
        }
        if (!int.TryParse(PercentBox.Text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.CurrentCulture, out var percent) || percent is < 0 or > 100)
        {
            ShowError(L.ErrorPercent);
            return;
        }

        DeviceName = name.Length > 60 ? name[..60] : name;
        RatedHours = hours;
        CurrentPercent = percent;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
