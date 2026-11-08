namespace KeyboardUtils.Tests;

public class HardSpotsTests
{
    static PracticeEntry Entry(Dictionary<int, int>? barMistakes) =>
        new(DateTime.Now, 80, 8, 10, 1, 1, 30, true, LearningMode.WaitForNote, 100, ["Pravá ruka"], null, null, barMistakes);

    [Fact]
    public void Bar_of_a_moment_in_four_four()
    {
        var timing = ScoreTiming.Constant(120); // a 4/4 bar = 2 s
        Assert.Equal(1, HardSpots.BarOf(0, timing));
        Assert.Equal(1, HardSpots.BarOf(1.99, timing));
        Assert.Equal(2, HardSpots.BarOf(2, timing));
        Assert.Equal(13, HardSpots.BarOf(24.5, timing));
    }

    [Fact]
    public void Bar_of_a_moment_in_three_four_with_a_tempo_change()
    {
        // 3/4; 120 BPM for 3 s (6 quarters = 2 bars), then 60 BPM (a bar = 3 s).
        var timing = new ScoreTiming([new TempoPoint(0, 0, 2), new TempoPoint(3, 6, 1)], beats: 3, beatType: 4);
        Assert.Equal(1, HardSpots.BarOf(1.4, timing));
        Assert.Equal(2, HardSpots.BarOf(1.5, timing));
        Assert.Equal(3, HardSpots.BarOf(3, timing));
        Assert.Equal(3, HardSpots.BarOf(5.9, timing));
        Assert.Equal(4, HardSpots.BarOf(6, timing));

        var (start, end) = HardSpots.Range(3, 4, timing);
        Assert.Equal(3, start, 6);
        Assert.Equal(9, end, 6);
    }

    [Fact]
    public void Counts_wrong_notes_per_bar()
    {
        var counts = HardSpots.CountMistakes([0.5, 1.0, 2.1, 9.0], ScoreTiming.Constant(120));
        Assert.Equal(new Dictionary<int, int> { [1] = 2, [2] = 1, [5] = 1 }, counts);
    }

    [Fact]
    public void Newer_attempts_weigh_more_and_old_entries_are_skipped()
    {
        var history = new List<PracticeEntry>
        {
            Entry(null), // from before 1.1.0
            Entry(new() { [3] = 5 }),
            Entry(new() { [3] = 1, [7] = 2 }),
            Entry(new()), // a clean attempt
        };
        var weighted = HardSpots.Weighted(history);
        // Newest (clean) 1.0, then 0.8, then 0.6.
        Assert.Equal(0.8 * 1 + 0.6 * 5, weighted[3], 6);
        Assert.Equal(0.8 * 2, weighted[7], 6);
    }

    [Fact]
    public void Only_the_last_attempts_count()
    {
        var history = Enumerable.Range(0, 6).Select(i => Entry(i == 0 ? new() { [1] = 10 } : new())).ToList();
        Assert.Empty(HardSpots.Weighted(history));
    }

    [Fact]
    public void Joins_neighbouring_bars_and_sorts_worst_first()
    {
        var history = new List<PracticeEntry>
        {
            Entry(new() { [12] = 4, [13] = 2, [27] = 3, [40] = 1 }),
        };
        var spots = HardSpots.For(history);
        Assert.Equal(3, spots.Count);
        Assert.Equal((12, 13), (spots[0].FirstBar, spots[0].LastBar));
        Assert.Equal("12–13", spots[0].Bars);
        Assert.Equal((27, 27), (spots[1].FirstBar, spots[1].LastBar));
        Assert.Equal("27", spots[1].Bars);
        Assert.Equal(40, spots[2].FirstBar);
    }

    [Fact]
    public void A_spot_is_at_most_four_bars()
    {
        var mistakes = Enumerable.Range(1, 10).ToDictionary(b => b, b => b == 5 ? 9 : 1);
        var spots = HardSpots.For([Entry(mistakes)], maxSpots: 1);
        var spot = Assert.Single(spots);
        Assert.Equal(4, spot.LastBar - spot.FirstBar + 1);
        Assert.InRange(5, spot.FirstBar, spot.LastBar);
    }

    [Fact]
    public void No_mistakes_no_spots()
    {
        Assert.Empty(HardSpots.For([Entry(null), Entry(new())]));
    }

    [Fact]
    public void History_keeps_bar_mistakes_and_reads_old_files()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-history-{Guid.NewGuid():N}.json");
        try
        {
            var history = new PracticeHistory();
            history.Add("song", Entry(new() { [2] = 3 }));
            history.Save(path);
            var loaded = PracticeHistory.Load(path).For("song");
            Assert.Equal(3, loaded.Single().BarMistakes![2]);

            // An entry written by 1.0.x, without BarMistakes.
            File.WriteAllText(path, """
                { "Songs": { "old": [ { "When": "2026-10-01T10:00:00", "Percent": 90, "Correct": 9, "Total": 10,
                  "Wrong": 1, "Missed": 0, "Seconds": 20, "Completed": true, "Mode": "InTempo", "SpeedPercent": 100,
                  "Parts": ["Obě ruce"], "LoopStart": null, "LoopEnd": null } ] } }
                """);
            var old = PracticeHistory.Load(path).For("old");
            Assert.Null(Assert.Single(old).BarMistakes);
            Assert.Empty(HardSpots.For(old));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
