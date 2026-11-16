using System.Text.Json;

namespace KeyboardUtils;

/// <summary>
/// Where the user was when an update restarted the app: the open song (and the playlist it came from), the
/// position in it and the loop. Written by the old version just before it hands over (<see cref="SelfUpdater"/>),
/// read once - and deleted - by the new one, so the update doesn't lose the song (a score opened from Muzio
/// included: it's a downloaded file like any other).
/// </summary>
sealed record ResumeState
{
    public List<string> Playlist { get; init; } = [];
    public int PlaylistIndex { get; init; }
    public double Seconds { get; init; }
    public double? LoopStart { get; init; }
    public double? LoopEnd { get; init; }
    public bool LoopOn { get; init; }
    public DateTime SavedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>A left-over file (an update that never finished) isn't applied to some later, unrelated start.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "resume.json");

    /// <summary>The song to open: the playlist entry, if the file is still there.</summary>
    public string? SongPath =>
        PlaylistIndex >= 0 && PlaylistIndex < Playlist.Count && File.Exists(Playlist[PlaylistIndex]) ? Playlist[PlaylistIndex] : null;

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The update still goes ahead; the new version just starts without the song.
        }
    }

    /// <summary>The saved state (deleting the file), or null when there's none, it's too old or it doesn't point to a song.</summary>
    public static ResumeState? Take(string? path = null, DateTime? nowUtc = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;
        ResumeState? state;
        try
        {
            state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            state = null;
        }
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (state == null || (nowUtc ?? DateTime.UtcNow) - state.SavedUtc > MaxAge || state.SongPath == null)
            return null;
        return state;
    }
}
