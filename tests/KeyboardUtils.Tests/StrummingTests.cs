namespace KeyboardUtils.Tests;

public class StrummingTests
{
    // Six guitar strings, the lowest (index 0) at the bottom of the screen.
    static readonly float[] Ys = [250, 210, 170, 130, 90, 50];

    [Fact]
    public void A_downstroke_crosses_from_the_top_string_down()
    {
        // Moving down the screen (growing y) over all the strings: high E first, low E last.
        Assert.Equal([5, 4, 3, 2, 1, 0], Strumming.CrossedStrings(20, 280, Ys));
        // Moving up: low E first.
        Assert.Equal([0, 1, 2, 3, 4, 5], Strumming.CrossedStrings(280, 20, Ys));
    }

    [Fact]
    public void Only_the_strings_passed_since_the_last_move()
    {
        Assert.Equal([3, 2], Strumming.CrossedStrings(100, 170, Ys));     // 130 and 170 (included), not 90
        Assert.Empty(Strumming.CrossedStrings(100, 120, Ys));             // between two strings
        Assert.Empty(Strumming.CrossedStrings(170, 200, Ys));             // starting on a string doesn't sound it again
    }

    [Fact]
    public void Faster_is_louder_within_limits()
    {
        Assert.Equal(50, Strumming.Velocity(0));
        Assert.True(Strumming.Velocity(1) > Strumming.Velocity(0.3));
        Assert.Equal(120, Strumming.Velocity(50));
    }

    [Fact]
    public void A_c_major_chord_sounds_its_notes()
    {
        var guitar = Instrument.ById("guitar").Strings;
        // x32010: A string 3rd fret, D 2nd, G open, B 1st, high E open (the low E isn't strummed).
        var stopped = new Dictionary<int, int> { [1] = 3, [2] = 2, [4] = 1 };
        Assert.Equal([48, 52, 55, 60, 64], Enumerable.Range(1, 5).Select(s => Strumming.Pitch(guitar, s, stopped)));
        Assert.Equal(40, Strumming.Pitch(guitar, 0, stopped)); // the low E open
    }
}
