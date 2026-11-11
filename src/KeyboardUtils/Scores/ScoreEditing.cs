namespace KeyboardUtils;

/// <summary>
/// Clones a <see cref="ScoreNote"/> with one property changed - it's init-only, so editing it always means
/// building a replacement to swap into its <see cref="ScorePart"/>'s note list.
/// </summary>
static class ScoreEditing
{
    public static ScoreNote Nudge(ScoreNote note, double deltaSeconds) => new()
    {
        Pitch = note.Pitch,
        StartSeconds = Math.Max(0, note.StartSeconds + deltaSeconds),
        DurationSeconds = note.DurationSeconds,
        Velocity = note.Velocity,
    };

    public static ScoreNote Repitch(ScoreNote note, int deltaSemitones) => new()
    {
        Pitch = Math.Clamp(note.Pitch + deltaSemitones, 0, 127),
        StartSeconds = note.StartSeconds,
        DurationSeconds = note.DurationSeconds,
        Velocity = note.Velocity,
    };

    public static ScoreNote Retime(ScoreNote note, double startSeconds, double durationSeconds) => new()
    {
        Pitch = note.Pitch,
        StartSeconds = Math.Max(0, startSeconds),
        DurationSeconds = Math.Max(0.02, durationSeconds),
        Velocity = note.Velocity,
    };
}
