namespace KeyboardUtils;

/// <summary>Favorites and tags for songs: menu items, on top of the recent-songs/songs-folder playlists.</summary>
sealed partial class MainForm
{
    /// <summary>The currently open song's own file, if it has one (null for an unsaved recording).</summary>
    string? CurrentSongPath => loadedSong is { } loaded && loaded.Score == currentScore ? loaded.Path : null;

    AntdUI.IContextMenuStripItem[] BuildLibraryMenuItems()
    {
        string? path = CurrentSongPath;
        var items = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(path != null && Library.IsFavorite(config, path)
                ? T("Odebrat z oblíbených", "Remove from favorites")
                : T("Přidat do oblíbených", "Add to favorites"))
            {
                Enabled = path != null,
                Tag = (Action)ToggleCurrentFavorite,
            },
            new AntdUI.ContextMenuStripItem(T("Štítky...", "Tags...")) { Enabled = path != null, Tag = (Action)EditCurrentTags },
        };

        var favorites = Library.FavoritePaths(config);
        var tags = Library.AllTags(config);
        if (favorites.Count > 0 || tags.Count > 0)
            items.Add(new AntdUI.ContextMenuStripItemDivider());
        if (favorites.Count > 0)
            items.Add(new AntdUI.ContextMenuStripItem(T("Oblíbené skladby", "Favorite songs")) { Sub = BuildPathListItems(favorites) });
        foreach (string tag in tags)
            items.Add(new AntdUI.ContextMenuStripItem(tag) { Sub = BuildPathListItems(Library.PathsTagged(config, tag)) });

        return items.ToArray();
    }

    AntdUI.IContextMenuStripItem[] BuildPathListItems(List<string> paths) =>
        paths.Select((path, i) => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(Path.GetFileNameWithoutExtension(path))
        {
            Tag = (Action)(() => OpenFromPaths(paths, i)),
        }).ToArray();

    void OpenFromPaths(List<string> paths, int index)
    {
        FinishCreateMode();
        playlist = paths;
        playlistIndex = index;
        LoadCurrentSong();
    }

    void ToggleCurrentFavorite()
    {
        if (CurrentSongPath is not { } path)
            return;
        Library.ToggleFavorite(config, path);
        SetStatus(Library.IsFavorite(config, path)
            ? T("Přidáno do oblíbených.", "Added to favorites.")
            : T("Odebráno z oblíbených.", "Removed from favorites."));
    }

    void EditCurrentTags()
    {
        if (CurrentSongPath is not { } path || currentScore is not { } score)
            return;
        using var dialog = new TagsForm(score.Title, Library.TagsOf(config, path));
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        Library.SetTags(config, path, dialog.Tags);
        SetStatus(dialog.Tags.Count > 0
            ? T("Štítky: ", "Tags: ") + string.Join(", ", dialog.Tags)
            : T("Štítky odebrány.", "Tags removed."));
    }
}
