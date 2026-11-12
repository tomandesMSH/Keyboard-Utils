namespace KeyboardUtils.Tests;

/// <summary>Note names (the tests run in Czech, the default language; the naming is passed in, not switched globally).</summary>
public class NoteNameTests
{
    [Theory]
    [InlineData(12, "C2")]   // subcontra
    [InlineData(23, "H2")]
    [InlineData(24, "C1")]   // contra
    [InlineData(33, "A1")]
    [InlineData(36, "C")]    // great octave
    [InlineData(47, "H")]
    [InlineData(48, "c")]    // small octave
    [InlineData(57, "a")]
    [InlineData(60, "c1")]   // middle C
    [InlineData(61, "cis1")]
    [InlineData(63, "dis1")]
    [InlineData(69, "a1")]
    [InlineData(70, "b1")]   // B♭ is "b" in Czech
    [InlineData(71, "h1")]
    [InlineData(72, "c2")]
    [InlineData(78, "fis2")]
    [InlineData(84, "c3")]
    [InlineData(96, "c4")]
    [InlineData(108, "c5")]
    [InlineData(39, "Dis")]
    [InlineData(30, "Fis1")]
    public void School_names(int midi, string name) =>
        Assert.Equal(name, NoteNames.Name(midi, NoteNaming.School));

    [Theory]
    [InlineData(60, "C4")]
    [InlineData(69, "A4")]
    [InlineData(59, "H3")]
    [InlineData(58, "B3")]
    [InlineData(61, "C♯4")]
    [InlineData(21, "A0")]
    public void Scientific_names(int midi, string name) =>
        Assert.Equal(name, NoteNames.Name(midi, NoteNaming.Scientific));

    [Theory]
    [InlineData(0, "C")]
    [InlineData(6, "F♯")]
    [InlineData(10, "B")]
    [InlineData(11, "H")]
    [InlineData(64, "E")]  // any octave: just the letter
    public void Letters_without_octave(int pitch, string letter) =>
        Assert.Equal(letter, NoteNames.Letter(pitch));
}
