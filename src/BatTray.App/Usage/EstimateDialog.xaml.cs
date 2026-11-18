using System.Globalization;
using System.Windows;

namespace BatTray.App.Usage;

/// <summary>Asks for the name, battery life and current charge of a device to estimate.</summary>
public partial class EstimateDialog : Window
{
    public EstimateDialog(string name, double ratedHours, int currentPercent)
    {
        InitializeComponent();
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
            ShowError("Enter a name.");
            return;
        }
        if (!double.TryParse(HoursBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours) || hours is < 0.5 or > 10000)
        {
            ShowError("Battery life must be a number of hours between 0.5 and 10000.");
            return;
        }
        if (!int.TryParse(PercentBox.Text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.CurrentCulture, out var percent) || percent is < 0 or > 100)
        {
            ShowError("Current charge must be a whole number from 0 to 100.");
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
