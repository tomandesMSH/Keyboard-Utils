namespace KeyboardUtils;

/// <summary>Favorite flag and/or tags for one song file, kept only while it has at least one of either.</summary>
sealed class LibraryEntry
{
    public required string Path { get; set; }
    public bool Favorite { get; set; }
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Favorites and tags, keyed by full file path - on top of (not instead of) the existing recent-songs list and
/// songs folder. Entries that are neither favorited nor tagged are dropped so the list doesn't grow forever.
/// </summary>
static class Library
{
    static LibraryEntry? Find(AppConfig config, string path)
    {
        string full = Path.GetFullPath(path);
        return config.Library.FirstOrDefault(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFavorite(AppConfig config, string path) => Find(config, path)?.Favorite ?? false;

    public static List<string> TagsOf(AppConfig config, string path) => Find(config, path)?.Tags ?? [];

    public static void ToggleFavorite(AppConfig config, string path)
    {
        string full = Path.GetFullPath(path);
        var entry = Find(config, full);
        if (entry == null)
            config.Library.Add(entry = new LibraryEntry { Path = full });
        entry.Favorite = !entry.Favorite;
        Prune(config, entry);
        config.Save();
    }

    public static void SetTags(AppConfig config, string path, List<string> tags)
    {
        string full = Path.GetFullPath(path);
        var entry = Find(config, full);
        if (entry == null)
        {
            if (tags.Count == 0)
                return;
            config.Library.Add(entry = new LibraryEntry { Path = full });
        }
        entry.Tags = tags;
        Prune(config, entry);
        config.Save();
    }

    /// <summary>Every tag in use, alphabetically, for the submenu list.</summary>
    public static List<string> AllTags(AppConfig config) =>
        config.Library.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Favorited songs that still exist on disk.</summary>
    public static List<string> FavoritePaths(AppConfig config) =>
        config.Library.Where(e => e.Favorite).Select(e => e.Path).Where(File.Exists).ToList();

    /// <summary>Songs tagged with <paramref name="tag"/> that still exist on disk.</summary>
    public static List<string> PathsTagged(AppConfig config, string tag) =>
        config.Library.Where(e => e.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).Select(e => e.Path).Where(File.Exists).ToList();

    /// <summary>Drops an entry once it has nothing left worth remembering.</summary>
    static void Prune(AppConfig config, LibraryEntry entry)
    {
        if (!entry.Favorite && entry.Tags.Count == 0)
            config.Library.Remove(entry);
    }
}
