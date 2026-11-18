using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatTray.Core.Estimation;

/// <summary>Loads and saves battery estimates (default: %AppData%\KoratSolutions\BatTray\estimates.json).</summary>
public sealed class EstimateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public EstimateStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KoratSolutions", "BatTray", "estimates.json");
    }

    public string Path { get; }

    /// <summary>Returns the saved estimates, or none when the file is missing or unreadable.</summary>
    public IReadOnlyList<BatteryEstimate> Load()
    {
        try
        {
            if (!File.Exists(Path))
                return [];
            return JsonSerializer.Deserialize<List<BatteryEstimate>>(File.ReadAllText(Path), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            CoreDiagnostics.Write($"Estimates file unreadable ({ex.Message}); starting without estimates.");
            return [];
        }
    }

    /// <summary>Writes the estimates atomically (temp file + replace).</summary>
    public void Save(IReadOnlyList<BatteryEstimate> estimates)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(estimates, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
