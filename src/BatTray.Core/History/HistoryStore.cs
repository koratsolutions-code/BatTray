using System.Text.Json;

namespace BatTray.Core.History;

/// <summary>Loads and saves the battery history (default: %AppData%\KoratSolutions\BatTray\history.json).</summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public HistoryStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KoratSolutions", "BatTray", "history.json");
    }

    public string Path { get; }

    /// <summary>Returns the saved history, or an empty one when the file is missing or unreadable.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<HistoryPoint>> Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new Dictionary<string, IReadOnlyList<HistoryPoint>>();
            var data = JsonSerializer.Deserialize<Dictionary<string, List<HistoryPoint>>>(File.ReadAllText(Path), JsonOptions);
            return data?.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<HistoryPoint>)kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, IReadOnlyList<HistoryPoint>>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            CoreDiagnostics.Write($"History file unreadable ({ex.Message}); starting with an empty history.");
            return new Dictionary<string, IReadOnlyList<HistoryPoint>>();
        }
    }

    /// <summary>Writes the history atomically (temp file + replace).</summary>
    public void Save(IReadOnlyDictionary<string, IReadOnlyList<HistoryPoint>> history)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(history, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>Removes the history file.</summary>
    public void Delete()
    {
        if (File.Exists(Path))
            File.Delete(Path);
    }
}
