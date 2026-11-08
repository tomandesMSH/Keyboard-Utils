namespace KeyboardUtils.Tests;

public class ScoreTimingTests
{
    [Fact]
    public void Constant_tempo_converts_both_ways()
    {
        var timing = ScoreTiming.Constant(120); // 2 quarters a second
        Assert.Equal(4, timing.ToQuarters(2), 6);
        Assert.Equal(2, timing.ToSeconds(4), 6);
    }

    [Fact]
    public void Tempo_change_converts_both_ways()
    {
        // 120 BPM for 2 s (4 quarters), then 60 BPM.
        var timing = new ScoreTiming([new TempoPoint(0, 0, 2), new TempoPoint(2, 4, 1)]);
        Assert.Equal(5, timing.ToQuarters(3), 6);
        Assert.Equal(3, timing.ToSeconds(5), 6);
        Assert.Equal(1, timing.QuartersPerSecondAt(2.5));
        Assert.Equal(2, timing.QuartersPerSecondAt(1));
        foreach (double seconds in new[] { 0.3, 1.9, 2, 2.7, 10 })
            Assert.Equal(seconds, timing.ToSeconds(timing.ToQuarters(seconds)), 6);
    }

    [Fact]
    public void First_tempo_point_after_zero_starts_at_zero()
    {
        var timing = new ScoreTiming([new TempoPoint(1, 2, 2)]);
        Assert.Equal(0, timing.Tempo[0].Seconds);
    }

    [Theory]
    [InlineData(4, 4, 4, 1.0, 4.0, 16)]
    [InlineData(3, 4, 3, 1.0, 3.0, 12)]
    [InlineData(2, 2, 2, 2.0, 4.0, 16)]
    [InlineData(6, 8, 2, 1.5, 3.0, 12)]   // compound: dotted quarters
    [InlineData(3, 8, 3, 0.5, 1.5, 6)]    // 3/8 is counted in eighths
    [InlineData(12, 8, 4, 1.5, 6.0, 24)]
    public void Metronome_clicks_follow_the_time_signature(int beats, int beatType, int clicksPerBar, double clickQuarters, double barQuarters, int sixteenths)
    {
        var timing = new ScoreTiming([new TempoPoint(0, 0, 2)], beats, beatType);
        Assert.Equal(clicksPerBar, timing.ClicksPerBar);
        Assert.Equal(clickQuarters, timing.ClickQuarters, 6);
        Assert.Equal(barQuarters, timing.BarQuarters, 6);
        Assert.Equal(sixteenths, timing.MeasureSixteenths);
    }

    [Theory]
    [InlineData(4, 32)]
    [InlineData(0, 4)]
    [InlineData(5, 3)]
    public void Unsupported_time_signature_falls_back_to_four_four(int beats, int beatType)
    {
        var timing = new ScoreTiming([new TempoPoint(0, 0, 2)], beats, beatType);
        Assert.Equal((4, 4), (timing.Beats, timing.BeatType));
    }
}
