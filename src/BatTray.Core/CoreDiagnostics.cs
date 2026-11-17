namespace BatTray.Core;

/// <summary>
/// Lightweight logging hook so Core stays free of logging dependencies.
/// The app sets <see cref="Sink"/> at startup.
/// </summary>
public static class CoreDiagnostics
{
    public static Action<string>? Sink { get; set; }

    internal static void Write(string message) => Sink?.Invoke(message);
}
