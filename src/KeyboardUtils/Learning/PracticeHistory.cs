using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardUtils;

/// <summary>One learning session (or one pass of a loop) and how it went.</summary>
sealed record PracticeEntry(
    DateTime When,
    double Percent,
    int Correct,
    int Total,
    int Wrong,
    int Missed,
    double Seconds,
    bool Completed,
    LearningMode Mode,
    int SpeedPercent,
    string[] Parts,
    double? LoopStart,
    double? LoopEnd);

/// <summary>
/// Results of learning mode, per song - in history.json next to config.json (kept apart, it only grows). A song is
/// known by its file's full path; a song without a file (a recording, stems) by its title.
/// </summary>
sealed class PracticeHistory
{
    const int MaxPerSong = 200;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(AppConfig.FilePath)!, "history.json");

    public Dictionary<string, List<PracticeEntry>> Songs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string KeyFor(string? filePath, string title) =>
        filePath != null ? Path.GetFullPath(filePath) : "title:" + title;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<PracticeEntry> For(string key) => Songs.TryGetValue(key, out var entries) ? entries : [];

    public void Add(string key, PracticeEntry entry)
    {
        if (!Songs.TryGetValue(key, out var entries))
            Songs[key] = entries = [];
        entries.Add(entry);
        if (entries.Count > MaxPerSong)
            entries.RemoveRange(0, entries.Count - MaxPerSong);
    }

    public static PracticeHistory Load() => Load(FilePath);

    /// <param name="path">Where the history is; the app uses <see cref="FilePath"/>, the tests a temporary file.</param>
    public static PracticeHistory Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<PracticeHistory>(File.ReadAllText(path), JsonOptions) is { } history)
            {
                // The deserialized dictionary compares keys exactly; paths differ only in case on Windows.
                history.Songs = new Dictionary<string, List<PracticeEntry>>(history.Songs, StringComparer.OrdinalIgnoreCase);
                return history;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            try { File.Copy(path, path + ".broken", overwrite: true); } catch (IOException) { }
        }
        return new PracticeHistory();
    }

    public void Save() => Save(FilePath);

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing one entry is better than losing the session: the next save tries again.
        }
    }
}
