namespace KeyboardUtils.Tests;

public class LibraryTests
{
    static AppConfig Config() => new();

    [Fact]
    public void Favoriting_adds_and_toggling_again_removes()
    {
        var config = Config();
        string path = Path.GetFullPath("song.mid");
        Assert.False(Library.IsFavorite(config, path));

        Library.ToggleFavorite(config, path);
        Assert.True(Library.IsFavorite(config, path));
        Assert.Single(config.Library);

        Library.ToggleFavorite(config, path);
        Assert.False(Library.IsFavorite(config, path));
        Assert.Empty(config.Library); // pruned: neither favorited nor tagged
    }

    [Fact]
    public void Tags_are_pruned_when_cleared_and_not_favorited()
    {
        var config = Config();
        string path = Path.GetFullPath("song.mid");
        Library.SetTags(config, path, ["jazz", "Hard"]);
        Assert.Single(config.Library);

        Library.SetTags(config, path, []);
        Assert.Empty(config.Library);
    }

    [Fact]
    public void Favorite_and_tagged_entry_survives_until_both_are_cleared()
    {
        var config = Config();
        string path = Path.GetFullPath("song.mid");
        Library.ToggleFavorite(config, path);
        Library.SetTags(config, path, ["jazz"]);

        Library.SetTags(config, path, []); // still favorited
        Assert.Single(config.Library);

        Library.ToggleFavorite(config, path); // now neither
        Assert.Empty(config.Library);
    }

    [Fact]
    public void Path_matching_is_case_insensitive()
    {
        var config = Config();
        string path = Path.GetFullPath("Song.mid");
        Library.ToggleFavorite(config, path);
        Assert.True(Library.IsFavorite(config, path.ToUpperInvariant()));
    }

    [Fact]
    public void All_tags_are_distinct_and_sorted()
    {
        var config = Config();
        Library.SetTags(config, Path.GetFullPath("a.mid"), ["jazz", "Hard"]);
        Library.SetTags(config, Path.GetFullPath("b.mid"), ["hard", "slow"]);

        Assert.Equal(["Hard", "jazz", "slow"], Library.AllTags(config));
    }
}
