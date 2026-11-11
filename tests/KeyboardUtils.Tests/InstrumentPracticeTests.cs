namespace KeyboardUtils.Tests;

public class InstrumentPracticeTests
{
    static Instrument Get(string id) => Instrument.ById(id);

    static List<int> Pitches(Score score) =>
        score.Parts.SelectMany(p => p.Notes).OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch).Select(n => n.Pitch).ToList();

    [Fact]
    public void A_violin_scale_starts_on_its_lowest_tonic_and_stays_in_range()
    {
        var violin = Get("violin");
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: 1, Octaves: 2, Hands: ExerciseHands.Both), violin);
        var pitches = Pitches(score);
        Assert.Single(score.Parts);
        Assert.Equal("Housle", score.Parts[0].Name);
        Assert.Equal(55, pitches[0]);                 // G3, the open G string
        Assert.Equal(79, pitches.Max());              // two octaves up
        Assert.All(pitches, p => Assert.InRange(p, violin.Low, violin.High));
        Assert.DoesNotContain("ruce", score.Title);
    }

    [Theory]
    [InlineData("flute", 0, 60)]
    [InlineData("cello", 0, 36)]
    [InlineData("recorder", 0, 72)]
    [InlineData("clarinet", 1, 53)]   // written G major = concert F major: F3, written G3
    [InlineData("altosax", 0, 51)]    // written C major = concert E♭ major
    public void Each_instrument_starts_where_it_can(string id, int writtenFifths, int firstPitch)
    {
        var instrument = Get(id);
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: writtenFifths), instrument);
        Assert.Equal(firstPitch, Pitches(score)[0]);
        Assert.Equal(instrument.ConcertFifths(writtenFifths), score.KeyFifths);
        Assert.Equal(writtenFifths, instrument.WrittenFifths(score.KeyFifths!.Value));
    }

    [Fact]
    public void Two_octaves_that_dont_fit_become_one()
    {
        var flute = Get("flute");
        var spec = new ExerciseSpec(ExerciseKind.Thirds, Fifths: 5, Octaves: 2);
        Assert.Equal(1, ExerciseGenerator.PlaceOnInstrument(spec, flute).Octaves);
        var score = ExerciseGenerator.Generate(spec, flute);
        Assert.All(Pitches(score), p => Assert.InRange(p, flute.Low, flute.High));
        Assert.Contains("1 oktáva", score.Title);
    }

    [Fact]
    public void Only_single_line_exercises_for_instruments()
    {
        Assert.DoesNotContain(ExerciseKind.Cadence, ExerciseGenerator.KindsFor(Get("violin")));
        Assert.DoesNotContain(ExerciseKind.Hanon, ExerciseGenerator.KindsFor(Get("violin")));
        Assert.Contains(ExerciseKind.Hanon, ExerciseGenerator.KindsFor(Instrument.Piano));
        // A chord exercise asked of a violin comes out as a scale.
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Cadence), Get("violin"));
        Assert.StartsWith("Stupnice", score.Title);
    }

    [Fact]
    public void Scale_in_thirds()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Thirds, Fifths: 0));
        var pitches = Pitches(score);
        Assert.Equal([60, 64, 62, 65, 64, 67], pitches.Take(6));
        Assert.Equal([71, 74], pitches.Skip(12).Take(2));             // up to 7 9
        Assert.Equal([72, 69, 71, 67], pitches.Skip(14).Take(4));     // then down from the top tonic: 8 6, 7 5
        Assert.Equal([64, 60], pitches.TakeLast(2));                  // ... 3 1
        Assert.Equal(26, pitches.Count);
    }

    [Fact]
    public void Instrument_note_reading_uses_its_clef_and_written_range()
    {
        var clarinet = Get("clarinet");
        var notes = NoteQuiz.Candidates(NoteQuizLevel.Instrument, clarinet);
        Assert.All(notes, n => { Assert.Equal(Clef.Treble, n.Clef); Assert.Equal(0, n.Alter); });
        Assert.Equal(52, notes.Min(n => n.Pitch)); // written E3 (sounds D3)
        Assert.InRange(notes.Max(n => n.Pitch), 69, 71);

        var viola = NoteQuiz.Candidates(NoteQuizLevel.Instrument, Get("viola"));
        Assert.All(viola, n => Assert.Equal(Clef.Alto, n.Clef));
        Assert.Contains(viola, n => n.Pitch == 60 && n.Step == 4); // middle C on the alto staff's middle line

        var advanced = NoteQuiz.Candidates(NoteQuizLevel.InstrumentAdvanced, Get("violin"));
        Assert.Contains(advanced, n => n.Alter != 0);
        Assert.All(advanced, n => Assert.InRange(n.Step, -7, 14));
    }

    [Fact]
    public void A_played_note_counts_as_it_sounds()
    {
        var quiz = new NoteQuiz(NoteQuizLevel.Instrument, new Random(2), instrument: Get("clarinet"));
        var note = quiz.Next()!;
        Assert.True(quiz.AnswerPitch(note.Pitch - 2, 1)); // written D, sounds C
        var next = quiz.Next()!;
        Assert.False(quiz.AnswerPitch(next.Pitch, 1));    // the written pitch itself is a tone too high
    }

    [Fact]
    public void Levels_for_an_instrument_start_with_its_own()
    {
        Assert.Equal(NoteQuizLevel.Instrument, NoteQuiz.LevelsFor(Get("cello"))[0]);
        Assert.DoesNotContain(NoteQuizLevel.Instrument, NoteQuiz.LevelsFor(Instrument.Piano));
        Assert.All(NoteQuiz.Candidates(NoteQuizLevel.Tenor), n => Assert.Equal(Clef.Tenor, n.Clef));
    }
}
