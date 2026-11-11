namespace KeyboardUtils.Tests;

public class TunerTests
{
    [Fact]
    public void Cents_from_the_nearest_note()
    {
        var (note, cents) = Tuning.Nearest(441);
        Assert.Equal(69, note);
        Assert.Equal(3.9, cents, 1);
        Assert.Equal(0, Tuning.Nearest(442, a4: 442).Cents, 6);
        Assert.Equal(-100, Tuning.CentsFrom(440, 70), 6);
        Assert.Equal(261.63, Tuning.Frequency(60), 2);
    }

    [Fact]
    public void Finds_the_string_being_tuned_even_when_far_off()
    {
        var guitar = Instrument.ById("guitar");
        Assert.Equal(1, Tuning.NearestString(110 * 0.95, guitar.Strings)); // A string, almost a semitone flat
        Assert.Equal(5, Tuning.NearestString(330, guitar.Strings));        // high E
        Assert.Null(Tuning.NearestString(1000, guitar.Strings));           // nowhere near a string
        Assert.Null(Tuning.NearestString(440, []));
    }

    [Fact]
    public void Note_names_are_czech_by_default()
    {
        Assert.Equal("A4", Tuning.Name(69));
        Assert.Equal("H3", Tuning.Name(59));
        Assert.Equal("B3", Tuning.Name(58));
        Assert.Equal("C♯4", Tuning.Name(61));
    }

    static float[] Sine(double hz, double seconds, double amplitude = 0.3, int rate = 44100) =>
        Enumerable.Range(0, (int)(seconds * rate)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    [Theory]
    [InlineData(441)]
    [InlineData(65.41)] // a cello's C string
    [InlineData(1318.5)]
    public void Hears_a_steady_tone_precisely(double hz)
    {
        var analyzer = new TunerAnalyzer(44100);
        analyzer.AddSamples(Sine(hz, 1.5));
        Assert.NotNull(analyzer.Frequency);
        Assert.InRange(Tuning.CentsFrom(analyzer.Frequency!.Value, (int)Math.Round(Tuning.Semitones(hz))) - Tuning.CentsFrom(hz, (int)Math.Round(Tuning.Semitones(hz))), -2, 2);

        analyzer.AddSamples(new float[44100 / 2]); // half a second of silence
        Assert.Null(analyzer.Frequency);
    }
}
