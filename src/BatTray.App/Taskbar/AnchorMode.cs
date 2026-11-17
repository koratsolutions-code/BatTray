namespace BatTray.App.Taskbar;

/// <summary>Estrategia para colocar el widget sobre la barra de tareas.</summary>
internal enum AnchorMode
{
    /// <summary>A) Ventana hija de Shell_TrayWnd: se mueve y se oculta junto con la barra.</summary>
    Child,

    /// <summary>B) Ventana emergente propiedad de la barra, siempre encima, reposicionada a mano.</summary>
    Overlay,
}
