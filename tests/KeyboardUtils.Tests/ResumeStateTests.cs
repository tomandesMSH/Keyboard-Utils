namespace KeyboardUtils.Tests;

public class ResumeStateTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("ku-resume").FullName;
    string StateFile => Path.Combine(folder, "resume.json");

    string Song(string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "");
        return path;
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void SavedStateComesBackOnceWithSongPositionAndLoop()
    {
        string a = Song("a.mid"), b = Song("b.musicxml");
        new ResumeState { Playlist = [a, b], PlaylistIndex = 1, Seconds = 42.5, LoopStart = 10, LoopEnd = 20, LoopOn = true }.Save(StateFile);

        var state = ResumeState.Take(StateFile);

        Assert.NotNull(state);
        Assert.Equal([a, b], state.Playlist);
        Assert.Equal(b, state.SongPath);
        Assert.Equal(42.5, state.Seconds);
        Assert.Equal((10.0, 20.0, true), (state.LoopStart!.Value, state.LoopEnd!.Value, state.LoopOn));
        // Read once: a later start doesn't jump back to it.
        Assert.False(File.Exists(StateFile));
        Assert.Null(ResumeState.Take(StateFile));
    }

    [Fact]
    public void NothingSavedMeansNothingToResume() => Assert.Null(ResumeState.Take(StateFile));

    [Fact]
    public void AnOldFileIsIgnored()
    {
        new ResumeState { Playlist = [Song("a.mid")], SavedUtc = DateTime.UtcNow.AddHours(-1) }.Save(StateFile);
        Assert.Null(ResumeState.Take(StateFile));
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void ASongThatIsGoneIsIgnored()
    {
        new ResumeState { Playlist = [Path.Combine(folder, "deleted.mid")] }.Save(StateFile);
        Assert.Null(ResumeState.Take(StateFile));
    }

    [Fact]
    public void ABrokenFileIsIgnoredAndRemoved()
    {
        File.WriteAllText(StateFile, "{ not json");
        Assert.Null(ResumeState.Take(StateFile));
        Assert.False(File.Exists(StateFile));
    }
}
