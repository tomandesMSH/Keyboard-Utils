namespace KeyboardUtils;

/// <summary>From this moment on, the music moves at <paramref name="QuartersPerSecond"/>.</summary>
readonly record struct TempoPoint(double Seconds, double Quarters, double QuartersPerSecond);

/// <summary>
/// How a score's seconds map to beats and bars - what sheet music needs and the flat note list doesn't. Known for
/// MIDI and MusicXML files; recordings and converted audio get a constant tempo instead.
/// </summary>
sealed class ScoreTiming
{
    /// <summary>Sorted by time, the first at 0 s.</summary>
    public IReadOnlyList<TempoPoint> Tempo { get; }

    /// <summary>Time signature, e.g. 3/4: <see cref="Beats"/> = 3, <see cref="BeatType"/> = 4.</summary>
    public int Beats { get; }
    public int BeatType { get; }

    public ScoreTiming(IEnumerable<TempoPoint> tempo, int beats = 4, int beatType = 4)
    {
        var points = tempo.Where(p => p.QuartersPerSecond > 0).OrderBy(p => p.Seconds).ToList();
        if (points.Count == 0 || points[0].Seconds > 0)
            points.Insert(0, new TempoPoint(0, 0, points.Count > 0 ? points[0].QuartersPerSecond : 2));
        Tempo = points;
        // Anything a 16th-note grid can't hold (e.g. x/32) falls back to 4/4.
        bool valid = beats is > 0 and <= 32 && beatType is 1 or 2 or 4 or 8 or 16;
        Beats = valid ? beats : 4;
        BeatType = valid ? beatType : 4;
    }

    public static ScoreTiming Constant(double bpm) => new([new TempoPoint(0, 0, bpm / 60.0)]);

    /// <summary>Length of one bar in 16th notes.</summary>
    public int MeasureSixteenths => Beats * 16 / BeatType;

    public double ToQuarters(double seconds)
    {
        var point = Tempo[0];
        foreach (var p in Tempo)
        {
            if (p.Seconds > seconds)
                break;
            point = p;
        }
        return point.Quarters + (seconds - point.Seconds) * point.QuartersPerSecond;
    }

    public double ToSeconds(double quarters)
    {
        var point = Tempo[0];
        foreach (var p in Tempo)
        {
            if (p.Quarters > quarters)
                break;
            point = p;
        }
        return point.Seconds + (quarters - point.Quarters) / point.QuartersPerSecond;
    }
}
