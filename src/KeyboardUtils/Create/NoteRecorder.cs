using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// Create mode: writes down the notes the player plays (from any <see cref="INoteSource"/>) with their times.
/// Driven from the UI thread. <see cref="Snapshot"/> gives a live view for the piano roll, <see cref="Finish"/>
/// the final score, shifted so the first note starts at 0.
/// </summary>
sealed class NoteRecorder
{
    /// <summary>Shorter than this is a glitch (or the mic catching an attack), not a note anyone meant to play.</summary>
    const double MinNoteSeconds = 0.05;

    sealed record Recorded(int Pitch, double Start, double End);

    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<Recorded> notes = [];
    readonly Dictionary<int, double> open = [];
    readonly double latencySeconds;

    /// <param name="latencySeconds">The source's detection delay; notes are back-dated by it.</param>
    public NoteRecorder(double latencySeconds) => this.latencySeconds = latencySeconds;

    public double CurrentSeconds => clock.Elapsed.TotalSeconds;
    public int NoteCount => notes.Count + open.Count;

    public void OnNoteOn(int pitch)
    {
        // The same key again before its note-off: the old note ends here.
        OnNoteOff(pitch);
        open[pitch] = Math.Max(0, CurrentSeconds - latencySeconds);
    }

    public void OnNoteOff(int pitch)
    {
        if (!open.Remove(pitch, out double start))
            return;
        double end = Math.Max(start, CurrentSeconds - latencySeconds);
        if (end - start >= MinNoteSeconds)
            notes.Add(new Recorded(pitch, start, end));
    }

    /// <summary>Everything so far, with still-held notes running up to now. Times are raw recording time.</summary>
    public Score Snapshot(string title)
    {
        double now = CurrentSeconds;
        var part = new ScorePart { Name = T("Nahrávka", "Recording") };
        part.Notes.AddRange(notes.Select(n => new ScoreNote { Pitch = n.Pitch, StartSeconds = n.Start, DurationSeconds = n.End - n.Start }));
        part.Notes.AddRange(open.Select(o => new ScoreNote { Pitch = o.Key, StartSeconds = o.Value, DurationSeconds = Math.Max(0, now - o.Value) }));
        return new Score { Title = title, Parts = [part] };
    }

    /// <summary>Closes held notes and returns the recording with the silence before the first note cut off.</summary>
    public Score Finish(string title)
    {
        foreach (int pitch in open.Keys.ToList())
            OnNoteOff(pitch);
        clock.Stop();

        double offset = notes.Count > 0 ? notes.Min(n => n.Start) : 0;
        var part = new ScorePart { Name = T("Nahrávka", "Recording") };
        part.Notes.AddRange(notes
            .OrderBy(n => n.Start)
            .Select(n => new ScoreNote { Pitch = n.Pitch, StartSeconds = n.Start - offset, DurationSeconds = n.End - n.Start }));
        return new Score { Title = title, Parts = [part] };
    }
}
