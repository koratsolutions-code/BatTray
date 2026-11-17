using System.IO;

namespace BatTray.App.Diagnostics;

/// <summary>Minimal file logger writing to %LocalAppData%\KoratSolutions\BatTray\logs.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KoratSolutions", "BatTray", "logs");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"battray-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch
        {
            // Logging must never bring the app down.
        }
    }
}
