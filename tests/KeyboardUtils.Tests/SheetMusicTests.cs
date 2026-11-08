namespace KeyboardUtils.Tests;

public class SheetQuantizerTests
{
    // 60 BPM: a quarter per second, so one 16th = 0.25 s.
    static double ToUnits(double seconds) => seconds * 4;

    static ScoreNote Note(int pitch, double start, double duration) => new() { Pitch = pitch, StartSeconds = start, DurationSeconds = duration };

    static GrandStaff Quantize(params ScoreNote[] notes) =>
        SheetQuantizer.Quantize(notes.Select(n => (n, 0)), ToUnits, 16);

    [Fact]
    public void Notes_split_between_the_staves_at_middle_c()
    {
        var staff = Quantize(Note(60, 0, 1), Note(59, 0, 1));
        Assert.Contains(staff.Treble, s => s.Notes.Any(n => n.Pitch == 60));
        Assert.Contains(staff.Bass, s => s.Notes.Any(n => n.Pitch == 59));
        Assert.DoesNotContain(staff.Treble, s => s.Notes.Any(n => n.Pitch == 59));
    }

    [Fact]
    public void Times_snap_to_sixteenths_and_rests_fill_the_bar()
    {
        // Starts at 0.24 s (~1 sixteenth), lasts a quarter.
        var staff = Quantize(Note(64, 0.24, 1));
        var treble = staff.Treble;
        Assert.Equal(1, staff.Measures);
        Assert.True(treble[0].IsRest);
        Assert.Equal((0, 1), (treble[0].Start, treble[0].Length));
        Assert.Equal((1, 4), (treble[1].Start, treble[1].Length));
        Assert.Equal(16, treble.Sum(s => s.Length));
    }

    [Fact]
    public void A_note_let_go_a_sixteenth_early_is_held_to_the_next()
    {
        // A quarter played as a dotted eighth (released 1/16 early), then the next note.
        var staff = Quantize(Note(64, 0, 0.75), Note(65, 1, 1));
        Assert.Equal(4, staff.Treble[0].Length);
        Assert.False(staff.Treble[1].IsRest);
    }

    [Fact]
    public void A_note_across_the_bar_line_is_split_and_tied()
    {
        var staff = Quantize(Note(64, 3, 2)); // last quarter of bar 1 plus the first of bar 2
        var parts = staff.Treble.Where(s => !s.IsRest).ToList();
        Assert.Equal(2, parts.Count);
        Assert.False(parts[0].TiedFromPrevious);
        Assert.True(parts[1].TiedFromPrevious);
        Assert.Equal(16, parts[1].Start);
    }

    [Theory]
    [InlineData(16, new[] { 16 })]
    [InlineData(5, new[] { 4, 1 })]
    [InlineData(6, new[] { 6 })]
    [InlineData(7, new[] { 6, 1 })]
    public void Lengths_decompose_into_writable_values(int length, int[] expected)
    {
        Assert.Equal(expected, SheetQuantizer.Decompose(length).Select(v => v.Length));
    }
}

public class KeySignatureTests
{
    static IEnumerable<ScoreNote> Scale(params int[] pitches) =>
        pitches.Select((p, i) => new ScoreNote { Pitch = p, StartSeconds = i * 0.5, DurationSeconds = 0.5 });

    [Fact]
    public void Estimates_c_major() => Assert.Equal(0, KeySignature.Estimate(Scale(60, 62, 64, 65, 67, 69, 71, 72)));

    [Fact]
    public void Estimates_g_major() => Assert.Equal(1, KeySignature.Estimate(Scale(67, 69, 71, 72, 74, 76, 78, 79)));

    [Fact]
    public void Estimates_f_major() => Assert.Equal(-1, KeySignature.Estimate(Scale(65, 67, 69, 70, 72, 74, 76, 77)));

    [Fact]
    public void An_accidental_holds_until_the_end_of_the_bar()
    {
        var state = new AccidentalState(0);
        Assert.Equal(1, state.Next(61, false));     // C#: sharp
        Assert.Null(state.Next(61, false));         // again in the bar: nothing
        Assert.Equal(0, state.Next(60, false));     // C after it: natural
        state.NewBar();
        Assert.Null(state.Next(60, false));         // new bar, C natural by the key
        Assert.Equal(1, state.Next(61, false));
    }

    [Fact]
    public void Key_signature_notes_need_no_sign_and_a_tie_never_gets_one()
    {
        var state = new AccidentalState(1); // G major: F#
        Assert.Null(state.Next(66, false));
        Assert.Equal(0, state.Next(65, false)); // F natural
        state.NewBar();
        Assert.Null(state.Next(65, true));      // tied over: no sign even though it's off the key
    }
}
