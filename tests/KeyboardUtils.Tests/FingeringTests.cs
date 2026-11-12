namespace KeyboardUtils.Tests;

public class FingeringTests
{
    static StringSpot? Spot(string instrument, int pitch) => Fingering.For(Instrument.ById(instrument), pitch);

    [Theory]
    [InlineData(40, 0, 0)]   // E2: the low E string, open
    [InlineData(43, 0, 3)]   // G2: low E, 3rd fret
    [InlineData(45, 1, 0)]   // A2: the A string open (not the low E's 5th fret)
    [InlineData(55, 3, 0)]   // G3: G string open
    [InlineData(60, 4, 1)]   // C4: B string, 1st fret
    [InlineData(64, 5, 0)]   // E4: the high E string open
    [InlineData(76, 5, 12)]  // E5: high E, 12th fret
    public void Guitar_plays_low_on_the_neck(int pitch, int stringIndex, int fret)
    {
        var spot = Spot("guitar", pitch)!;
        Assert.Equal(stringIndex, spot.String);
        Assert.Equal(fret, spot.Semitones);
        Assert.Equal(fret.ToString(), spot.Label);
    }

    [Theory]
    [InlineData(55, 0, 0)]   // G3: G string open
    [InlineData(69, 2, 0)]   // A4: A string open
    [InlineData(71, 2, 1)]   // B4: A string, 1st finger
    [InlineData(72, 2, 2)]   // C5: low 2nd finger
    [InlineData(74, 2, 3)]   // D5: A string, 3rd finger (not the E string)
    [InlineData(76, 3, 0)]   // E5: E string open (not the A string's 4th finger)
    [InlineData(83, 3, 4)]   // B5: E string, 4th finger
    public void Violin_in_first_position(int pitch, int stringIndex, int finger)
    {
        var spot = Spot("violin", pitch)!;
        Assert.Equal(stringIndex, spot.String);
        Assert.Equal(finger, spot.Finger);
    }

    [Fact]
    public void Cello_and_the_limits()
    {
        Assert.Equal((0, 0), (Spot("cello", 36)!.String, Spot("cello", 36)!.Finger!.Value)); // C2 open
        Assert.Equal(1, Spot("cello", 38)!.Finger);                                         // D2: 1st finger
        Assert.Equal(4, Spot("cello", 41)!.Finger);                                         // F2: 4th finger
        Assert.Equal(1, Spot("cello", 43)!.String);                                         // G2: the G string open
        Assert.Null(Spot("violin", 54));                                                    // below the G string
        Assert.Null(Spot("violin", 77 + 24));                                               // far too high
        Assert.Null(Spot("violin", 90)!.Finger);                                            // high on the E string: no first-position finger
        Assert.Null(Spot("piano", 60));                                                     // no strings to show
        Assert.Null(Spot("flute", 72));
    }
}
