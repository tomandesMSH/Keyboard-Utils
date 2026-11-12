namespace KeyboardUtils.Tests;

public class ChordTests
{
    static readonly Instrument Guitar = Instrument.ById("guitar");

    [Fact]
    public void Every_library_shape_sounds_exactly_its_chord()
    {
        foreach (var chord in GuitarChords.All)
        {
            var sounding = chord.Pitches(Guitar.Strings).Select(p => p % 12).ToHashSet();
            var tones = Chords.PitchClasses(chord.Root, chord.Kind);
            // Nothing outside the chord; every tone there - only a seventh chord may leave out its fifth (C7 x32310).
            var missing = tones.Except(sounding).ToList();
            bool fifthLeftOut = missing.Count == 1 && missing[0] == (chord.Root + 7) % 12 && tones.Count == 4;
            Assert.True(sounding.IsSubsetOf(tones) && (missing.Count == 0 || fifthLeftOut), $"{chord.Name}: {string.Join(",", sounding)}");
            // The fingers only where something is pressed, the barre on fret it names.
            Assert.All(Enumerable.Range(0, 6), s => Assert.Equal(chord.Frets[s] > 0, chord.Fingers[s] > 0));
            if (chord.Barre is { } barre)
                Assert.Contains(barre, chord.Frets);
        }
    }

    [Fact]
    public void Library_shapes_have_different_names() =>
        Assert.Equal(GuitarChords.All.Count, GuitarChords.All.Select(c => (c.Root, c.Kind)).Distinct().Count());

    [Fact]
    public void Czech_names()
    {
        // The tests run in Czech (the default language); switching it here could race other tests.
        Assert.Equal(AppLanguage.Czech, Lang.Current);
        Assert.Equal("H7", Chords.Name(11, ChordKind.Dominant7));
        Assert.Equal("B", Chords.Name(10, ChordKind.Major));
        Assert.Equal("Am", Chords.Name(9, ChordKind.Minor));
        Assert.Equal("F/C", Chords.Name(5, ChordKind.Major, 0));
        Assert.Equal("Dmaj7", Chords.Name(2, ChordKind.Major7));
        Assert.Equal("C", Chords.Name(0, ChordKind.Major, 0)); // the root as bass: no slash
    }

    [Theory]
    [InlineData(new[] { 48, 52, 55, 60 }, 0, (int)ChordKind.Major)]
    [InlineData(new[] { 45, 52, 57, 60, 64 }, 9, (int)ChordKind.Minor)]
    [InlineData(new[] { 43, 47, 50, 53 }, 7, (int)ChordKind.Dominant7)]
    [InlineData(new[] { 50, 57, 62, 65 }, 2, (int)ChordKind.Minor)]
    public void A_chord_is_recognised(int[] pitches, int root, int kindValue)
    {
        var kind = (ChordKind)kindValue;
        var weights = new double[12];
        foreach (int p in pitches)
            weights[p % 12] += 1;
        Assert.Equal((root, kind), ChordGuesser.Match(weights, pitches.Min()));
    }

    [Fact]
    public void A_lone_melody_gets_no_chord()
    {
        var weights = new double[12];
        weights[0] = 1;
        weights[2] = 1;
        Assert.Null(ChordGuesser.Match(weights, 60));
    }

    [Fact]
    public void Guessing_a_song_names_each_change_once()
    {
        // C (2 bars) - F - G7 - C as whole-bar chords in 4/4 at 120: a bar is 2 s.
        int[][] chords = [[48, 52, 55], [48, 52, 55], [41, 53, 57, 60], [43, 47, 50, 53], [48, 52, 55, 60]];
        var notes = chords.SelectMany((c, bar) => c.Select(p => new ScoreNote { Pitch = p, StartSeconds = bar * 2.0, DurationSeconds = 2.0 })).ToList();
        var score = new Score { Title = "t", Parts = [new ScorePart { Name = "p", Notes = notes }], Timing = ScoreTiming.Constant(120) };
        var guessed = ChordGuesser.Guess(score);
        Assert.Equal([(0, ChordKind.Major), (5, ChordKind.Major), (7, ChordKind.Dominant7), (0, ChordKind.Major)], guessed.Select(c => (c.Root, c.Kind)));
        Assert.Equal([0.0, 4.0, 6.0, 8.0], guessed.Select(c => Math.Round(c.StartSeconds, 3)));
    }

    [Fact]
    public void Musicxml_harmony_is_read_as_it_sounds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-harmony-{Guid.NewGuid():N}.musicxml");
        try
        {
            // A B♭ clarinet part (written a tone up): written D sounds C, written Gm7/D sounds Fm7/C.
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="UTF-8"?>
                <score-partwise version="4.0">
                  <part-list><score-part id="P1"><part-name>Clarinet</part-name></score-part></part-list>
                  <part id="P1">
                    <measure number="1">
                      <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>
                        <transpose><chromatic>-2</chromatic></transpose></attributes>
                      <sound tempo="60"/>
                      <harmony><root><root-step>D</root-step></root><kind>major</kind></harmony>
                      <note><pitch><step>D</step><octave>5</octave></pitch><duration>2</duration></note>
                      <harmony><root><root-step>G</root-step></root><kind>minor-seventh</kind><bass><bass-step>D</bass-step></bass></harmony>
                      <note><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration></note>
                    </measure>
                  </part>
                </score-partwise>
                """);
            var score = ScoreImporter.Import(path);
            Assert.Equal(2, score.Chords.Count);
            Assert.Equal((0, ChordKind.Major, (int?)null, 0.0), (score.Chords[0].Root, score.Chords[0].Kind, score.Chords[0].Bass, score.Chords[0].StartSeconds));
            Assert.Equal((5, ChordKind.Minor7, (int?)0), (score.Chords[1].Root, score.Chords[1].Kind, score.Chords[1].Bass));
            Assert.Equal(2.0, score.Chords[1].StartSeconds, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Musicxml_without_harmony_has_no_chords()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-noharmony-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="UTF-8"?>
                <score-partwise version="4.0">
                  <part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list>
                  <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
                    <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration></note></measure></part>
                </score-partwise>
                """);
            Assert.Empty(ScoreImporter.Import(path).Chords);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
