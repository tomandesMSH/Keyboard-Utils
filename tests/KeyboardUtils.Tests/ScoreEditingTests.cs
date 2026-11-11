namespace KeyboardUtils.Tests;

public class ScoreEditingTests
{
    static ScoreNote Note() => new() { Pitch = 60, StartSeconds = 2, DurationSeconds = 0.5, Velocity = 90 };

    [Fact]
    public void Nudge_moves_start_time_and_keeps_everything_else()
    {
        var moved = ScoreEditing.Nudge(Note(), 1.5);
        Assert.Equal(3.5, moved.StartSeconds, 6);
        Assert.Equal(60, moved.Pitch);
        Assert.Equal(0.5, moved.DurationSeconds, 6);
        Assert.Equal(90, moved.Velocity);
    }

    [Fact]
    public void Nudge_does_not_go_below_zero()
    {
        var moved = ScoreEditing.Nudge(Note(), -10);
        Assert.Equal(0, moved.StartSeconds, 6);
    }

    [Fact]
    public void Repitch_shifts_by_semitones_and_clamps_to_midi_range()
    {
        Assert.Equal(63, ScoreEditing.Repitch(Note(), 3).Pitch);
        Assert.Equal(0, ScoreEditing.Repitch(Note(), -1000).Pitch);
        Assert.Equal(127, ScoreEditing.Repitch(Note(), 1000).Pitch);
    }

    [Fact]
    public void Retime_sets_start_and_duration_with_a_minimum_length()
    {
        var retimed = ScoreEditing.Retime(Note(), 5, 0);
        Assert.Equal(5, retimed.StartSeconds, 6);
        Assert.True(retimed.DurationSeconds > 0);
    }
}
