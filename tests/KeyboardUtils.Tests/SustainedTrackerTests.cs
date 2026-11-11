namespace KeyboardUtils.Tests;

public class SustainedTrackerTests
{
    const int Rate = 44100, Frame = 2048, Hop = 512;

    /// <summary>
    /// A bowed / blown line: one continuous tone (no gaps, no attacks) through <paramref name="notes"/>, each held
    /// <paramref name="seconds"/>, with vibrato of ±<paramref name="vibratoCents"/> at 5.5 Hz.
    /// </summary>
    static float[] Legato(int[] notes, double seconds, double vibratoCents)
    {
        int length = (int)(notes.Length * seconds * Rate);
        var samples = new float[length];
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            double t = (double)i / Rate;
            int note = notes[Math.Min(notes.Length - 1, (int)(t / seconds))];
            double cents = vibratoCents * Math.Sin(2 * Math.PI * 5.5 * t);
            double hz = 440 * Math.Pow(2, (note - 69 + cents / 100) / 12);
            phase += 2 * Math.PI * hz / Rate;
            // A rich-ish tone: fundamental plus two overtones, like a string.
            samples[i] = (float)(0.25 * Math.Sin(phase) + 0.12 * Math.Sin(2 * phase) + 0.06 * Math.Sin(3 * phase));
        }
        return samples;
    }

    static List<int> Track(float[] samples, Instrument instrument)
    {
        var tracker = new NoteTracker(new PitchDetector(Rate, Frame))
        {
            Sustained = instrument.Sustained,
            Lowest = instrument.Low,
            Highest = instrument.High,
        };
        var heard = new List<int>();
        tracker.NoteOn += heard.Add;
        for (int start = 0; start + Frame <= samples.Length; start += Hop)
            tracker.Process(samples.AsSpan(start, Frame));
        return heard;
    }

    [Fact]
    public void Vibrato_stays_one_note()
    {
        var heard = Track(Legato([69], 2, vibratoCents: 40), Instrument.ById("violin"));
        Assert.Equal([69], heard);
    }

    [Fact]
    public void Legato_steps_are_separate_notes()
    {
        var heard = Track(Legato([67, 69, 71, 72], 0.5, vibratoCents: 20), Instrument.ById("violin"));
        Assert.Equal([67, 69, 71, 72], heard);
    }

    [Fact]
    public void Notes_out_of_the_instruments_range_are_ignored()
    {
        // A flute can't play A2: whatever the detector makes of it, the tracker reports nothing.
        var heard = Track(Legato([45], 1, vibratoCents: 0), Instrument.ById("flute"));
        Assert.Empty(heard);
    }
}
