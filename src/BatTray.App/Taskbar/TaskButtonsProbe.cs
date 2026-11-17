using System.Windows.Automation;
using System.Windows.Threading;
using BatTray.App.Diagnostics;
using static BatTray.App.Interop.NativeMethods;

namespace BatTray.App.Taskbar;

/// <summary>
/// En Windows 11 los botones de apps se dibujan con XAML y <c>ReBarWindow32</c> no refleja su extensión real.
/// Este sondeo usa UI Automation para obtener el borde derecho del último botón (Inicio, Buscar, apps…).
/// Corre en su propio hilo: las llamadas UIA desde el hilo de UI podrían bloquearse, porque nuestro widget
/// también forma parte del árbol de la barra.
/// </summary>
internal sealed class TaskButtonsProbe : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private static readonly Condition ButtonsCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleButton"));

    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private int? _lastRight;
    private bool _errorLogged;

    public TaskButtonsProbe(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "BatTray.TaskButtonsProbe" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Borde derecho (px de pantalla) de los botones de la barra; null si no se pudo medir. Se dispara en el hilo de UI.</summary>
    public event Action<int?>? Changed;

    public void Start() => _thread.Start();

    private void Run()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            var right = Measure();
            if (right != _lastRight)
            {
                _lastRight = right;
                _dispatcher.BeginInvoke(() => Changed?.Invoke(right));
            }

            token.WaitHandle.WaitOne(Interval);
        }
    }

    private int? Measure()
    {
        try
        {
            var taskbar = FindWindow(TaskbarLocator.TaskbarClass, null);
            if (taskbar == IntPtr.Zero)
                return null;

            var root = AutomationElement.FromHandle(taskbar);
            var buttons = root.FindAll(TreeScope.Descendants, ButtonsCondition);

            double right = double.NaN;
            foreach (AutomationElement button in buttons)
            {
                var rect = button.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && (double.IsNaN(right) || rect.Right > right))
                    right = rect.Right;
            }

            _errorLogged = false;
            return double.IsNaN(right) ? null : (int)Math.Ceiling(right);
        }
        catch (Exception ex)
        {
            if (!_errorLogged)
                Log.Error("No se pudieron medir los botones de la barra con UI Automation.", ex);
            _errorLogged = true;
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
