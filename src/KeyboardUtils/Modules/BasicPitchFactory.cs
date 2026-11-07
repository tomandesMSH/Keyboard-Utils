namespace KeyboardUtils;

/// <summary>
/// The ONLY place in Keyboard Utils allowed to reference BasicPitch types. Call only when
/// <see cref="BasicPitchModule.IsInstalled"/> is true (see the note on <see cref="BasicPitchModule"/>).
/// </summary>
static class BasicPitchFactory
{
    public const int SampleRate = 22050; // BasicPitch.BasicPitchTranscriber.SampleRate, repeated so callers needn't touch the type

    /// <summary>
    /// Intervals (semitones) of a note's 4th-6th and 8th harmonics: two octaves, two octaves+third, two octaves+fifth,
    /// three octaves. A weaker note at one of these above another sounding at the same time is most likely that
    /// note's overtone, not a played note. The octave and octave+fifth are left alone on purpose: those are played
    /// for real all the time (octave doublings), and the model reports them with similarly modest confidence.
    /// </summary>
    static readonly int[] HarmonicIntervals = [24, 28, 31, 36];

    /// <summary>Overtone "ghosts" came out at 0.34-0.39 confidence against 0.5-0.85 for the notes producing them.</summary>
    const double GhostAmplitudeRatio = 0.8;

    /// <summary>Notes (several at once) in mono audio at <see cref="SampleRate"/>.</summary>
    public static List<ScoreNote> Transcribe(float[] mono, IProgress<double>? progress, CancellationToken cancel)
    {
        using var transcriber = new BasicPitch.BasicPitchTranscriber(BasicPitchModule.ModelPath);
        var notes = transcriber.Transcribe(mono, progress, cancel);
        return notes
            .Where(n => !IsOvertoneGhost(n, notes))
            .Select(n => new ScoreNote
            {
                Pitch = n.Pitch,
                StartSeconds = n.StartSeconds,
                DurationSeconds = n.EndSeconds - n.StartSeconds,
                Velocity = Math.Clamp((int)Math.Round(127 * n.Amplitude), 1, 127),
            })
            .ToList();
    }

    static bool IsOvertoneGhost(BasicPitch.BasicPitchNote note, List<BasicPitch.BasicPitchNote> all) =>
        all.Any(other => HarmonicIntervals.Contains(note.Pitch - other.Pitch)
            && note.Amplitude < GhostAmplitudeRatio * other.Amplitude
            && Overlap(note, other) >= 0.5 * (note.EndSeconds - note.StartSeconds));

    static double Overlap(BasicPitch.BasicPitchNote a, BasicPitch.BasicPitchNote b) =>
        Math.Max(0, Math.Min(a.EndSeconds, b.EndSeconds) - Math.Max(a.StartSeconds, b.StartSeconds));
}
