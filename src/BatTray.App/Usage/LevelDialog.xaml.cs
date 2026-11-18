using System.Globalization;
using System.Windows;

namespace BatTray.App.Usage;

/// <summary>Asks for a battery level (0–100 %).</summary>
public partial class LevelDialog : Window
{
    public LevelDialog(string deviceName, int currentPercent)
    {
        InitializeComponent();
        TitleText.Text = deviceName;
        PercentBox.Text = currentPercent.ToString(CultureInfo.CurrentCulture);
        Loaded += (_, _) =>
        {
            Activate();
            PercentBox.Focus();
            PercentBox.SelectAll();
        };
    }

    public int Percent { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PercentBox.Text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.CurrentCulture, out var percent) ||
            percent is < 0 or > 100)
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Percent = percent;
        DialogResult = true;
    }
}
