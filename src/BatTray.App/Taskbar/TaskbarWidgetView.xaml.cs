using System.Windows;
using System.Windows.Controls;

namespace BatTray.App.Taskbar;

public partial class TaskbarWidgetView : UserControl
{
    public TaskbarWidgetView()
    {
        InitializeComponent();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
