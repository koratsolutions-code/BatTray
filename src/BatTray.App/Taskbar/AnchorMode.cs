namespace BatTray.App.Taskbar;

/// <summary>Strategy used to place the widget on the taskbar.</summary>
internal enum AnchorMode
{
    /// <summary>A) Child window of Shell_TrayWnd: moves and hides together with the taskbar.</summary>
    Child,

    /// <summary>B) Popup owned by the taskbar, always on top, repositioned manually.</summary>
    Overlay,
}
