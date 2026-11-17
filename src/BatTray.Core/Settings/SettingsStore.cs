using System.Text.Json;

namespace BatTray.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON (default: %AppData%\KoratSolutions\BatTray\settings.json).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? DefaultPath;
    }

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KoratSolutions", "BatTray", "settings.json");

    public string Path { get; }

    /// <summary>Returns the saved settings, or defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            CoreDiagnostics.Write($"Settings file unreadable ({ex.Message}); using defaults.");
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings atomically (temp file + replace) so a crash never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
