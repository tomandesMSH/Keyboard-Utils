namespace KeyboardUtils.Tests;

public sealed class PracticeHistoryTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("KeyboardUtilsTests").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    static PracticeEntry Entry(double percent) =>
        new(DateTime.Now, percent, 1, 1, 0, 0, 10, true, LearningMode.WaitForNote, 100, ["Part"], null, null);

    [Fact]
    public void Keeps_the_latest_200_per_song()
    {
        var history = new PracticeHistory();
        for (int i = 0; i < 205; i++)
            history.Add("song", Entry(i));
        var entries = history.For("song");
        Assert.Equal(200, entries.Count);
        Assert.Equal(5, entries[0].Percent);
        Assert.Equal(204, entries[^1].Percent);
    }

    [Fact]
    public void Song_keys_ignore_case_also_after_loading()
    {
        string path = Path.Combine(folder, "history.json");
        var history = new PracticeHistory();
        history.Add(PracticeHistory.KeyFor(@"C:\Songs\Song.mid", "Song"), Entry(80));
        history.Save(path);

        var loaded = PracticeHistory.Load(path);
        var entries = loaded.For(PracticeHistory.KeyFor(@"c:\songs\SONG.MID", "Song"));
        Assert.Single(entries);
        Assert.Equal(80, entries[0].Percent);
        Assert.Equal(["Part"], entries[0].Parts);
    }

    [Fact]
    public void Songs_without_a_file_are_known_by_title()
    {
        Assert.Equal("title:Recording", PracticeHistory.KeyFor(null, "Recording"));
    }

    [Fact]
    public void A_broken_file_loads_empty_and_is_kept_aside()
    {
        string path = Path.Combine(folder, "history.json");
        File.WriteAllText(path, "{ not json");
        Assert.Empty(PracticeHistory.Load(path).Songs);
        Assert.True(File.Exists(path + ".broken"));
    }
}

public class MacroActionsTests
{
    [Fact]
    public void Every_action_has_its_own_description()
    {
        foreach (var action in Enum.GetValues<MacroAction>())
        {
            string text = new Binding { Action = action, PartName = "P", Pitch = 60 }.Describe();
            Assert.NotEqual(action.ToString(), text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void Opposites_pair_up_and_are_steps()
    {
        foreach (var action in Enum.GetValues<MacroAction>())
        {
            if (MacroActions.Opposite(action) is not { } opposite)
                continue;
            Assert.Equal(action, MacroActions.Opposite(opposite));
            Assert.True(MacroActions.IsStep(action), $"{action} has an opposite but isn't a step");
            Assert.True(MacroActions.IsStep(opposite));
            Assert.Equal(MacroActions.IsPerPart(action), MacroActions.IsPerPart(opposite));
        }
    }

    [Fact]
    public void The_action_picker_offers_every_action_once()
    {
        var offered = ActionPickerForm.Groups.SelectMany(g => g.Actions).ToList();
        Assert.Equal(offered.Count, offered.Distinct().Count());
        var expected = Enum.GetValues<MacroAction>().Where(a => !MacroActions.IsPerPart(a));
        Assert.Equal(expected.Order(), offered.Order());
    }
}

public class PlaybackLogicTests
{
    [Fact]
    public void Rewind_and_fast_forward_double_every_second_up_to_16x()
    {
        Assert.Equal(2, MainForm.ScrubRate(0), 6);
        Assert.Equal(4, MainForm.ScrubRate(1), 6);
        Assert.Equal(8, MainForm.ScrubRate(2), 6);
        Assert.Equal(16, MainForm.ScrubRate(3), 6);
        Assert.Equal(16, MainForm.ScrubRate(10), 6);
    }

    [Theory]
    [InlineData("2/4", 2, 1)]
    [InlineData("3/4", 3, 1)]
    [InlineData("4/4", 4, 1)]
    [InlineData("5/4", 5, 1)]
    [InlineData("6/8", 6, 3)]
    [InlineData("7/8", 4, 1)]
    public void Metronome_time_signatures(string meter, int clicksPerBar, int clicksPerBeat)
    {
        Assert.Equal((clicksPerBar, clicksPerBeat), StandaloneMetronome.Pattern(meter));
    }

    [Fact]
    public void Every_offered_time_signature_has_a_pattern_of_its_own()
    {
        Assert.All(StandaloneMetronome.Meters, m => Assert.Equal(m, new StandaloneMetronome(_ => { }) { Meter = m }.Meter));
        Assert.Equal("4/4", new StandaloneMetronome(_ => { }) { Meter = "nonsense" }.Meter);
    }

    [Fact]
    public void Tapping_every_half_second_is_120_bpm()
    {
        var tap = new TapTempo();
        Assert.Null(tap.Tap(10));
        Assert.Equal(120, tap.Tap(10.5));
        Assert.Equal(120, tap.Tap(11));
        Assert.Equal(120, tap.Tap(11.5));
    }

    [Fact]
    public void Tapping_averages_the_last_gaps()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        tap.Tap(0.5);
        Assert.Equal(109, tap.Tap(1.1)); // gaps 0.5 and 0.6: 0.55 s = 109 BPM
    }

    [Fact]
    public void A_long_pause_starts_a_new_tempo()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        tap.Tap(0.5);
        Assert.Null(tap.Tap(5));              // > 2 s later: first tap of a new series
        Assert.Equal(60, tap.Tap(6));
    }

    [Fact]
    public void Tapped_tempo_stays_in_the_metronome_range()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        Assert.Equal(StandaloneMetronome.MaxBpm, tap.Tap(0.05));
        tap = new TapTempo();
        tap.Tap(0);
        Assert.Equal(31, tap.Tap(1.95)); // just under the reset: 31 BPM, above the minimum of 20
    }
}

public class UpdateCheckerTests
{
    static readonly Version Current = new(0, 4, 1);

    [Theory]
    [InlineData("v0.4.2", "0.4.2")]
    [InlineData("v0.5.0", "0.5.0")]
    [InlineData("v1.0", "1.0")]
    public void A_newer_tag_is_an_update(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.NewerVersion(tag, Current));
    }

    [Theory]
    [InlineData("v0.4.1")]
    [InlineData("v0.4.0")]
    [InlineData("v0.3.9")]
    [InlineData("basic-pitch-module-v1")]
    [InlineData("0.9.0")]
    [InlineData("")]
    [InlineData(null)]
    public void Same_older_or_other_tags_are_not(string? tag)
    {
        Assert.Null(UpdateChecker.NewerVersion(tag, Current));
    }

    [Fact]
    public void The_assembly_version_and_the_tag_compare_alike()
    {
        // The build's version has four parts (0.4.1.0), tags three.
        Assert.Null(UpdateChecker.NewerVersion("v0.4.1", new Version(0, 4, 1, 0)));
    }
}

public class PackagingTests
{
    [Theory]
    [InlineData("KeyboardUtils.install-interception.exe")]
    [InlineData("KeyboardUtils.Interception-LGPL-3.0.txt")]
    [InlineData("KeyboardUtils.app.ico")]
    public void The_single_exe_carries_the_driver_installer_its_licence_and_the_icon(string resource)
    {
        // Downloaded as one .exe, the app has nothing next to it: the driver installer comes from inside it.
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0);
    }
}
