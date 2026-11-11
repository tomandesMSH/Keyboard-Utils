namespace KeyboardUtils.Tests;

public class NoteQuizTests
{
    [Fact]
    public void Levels_ask_for_their_range()
    {
        var treble = NoteQuiz.Candidates(NoteQuizLevel.Treble);
        Assert.All(treble, n => { Assert.True(n.Treble); Assert.Equal(0, n.Alter); Assert.InRange(n.Pitch, 60, 79); });
        Assert.Equal(12, treble.Count); // C4 ... G5, white keys only

        var bass = NoteQuiz.Candidates(NoteQuizLevel.Bass);
        Assert.All(bass, n => { Assert.False(n.Treble); Assert.InRange(n.Pitch, 41, 60); });

        var advanced = NoteQuiz.Candidates(NoteQuizLevel.Advanced);
        Assert.Contains(advanced, n => n.Alter == 1);
        Assert.Contains(advanced, n => n.Alter == -1);
    }

    [Theory]
    [InlineData(64, 0, true, 0)]   // E4: bottom line of the treble staff
    [InlineData(60, 0, true, -2)]  // C4: the ledger line below
    [InlineData(77, 0, true, 8)]   // F5: top line
    [InlineData(43, 0, false, 0)]  // G2: bottom line of the bass staff
    [InlineData(57, 0, false, 8)]  // A3: top line of the bass staff
    [InlineData(60, 0, false, 10)] // C4: the ledger line above the bass staff
    [InlineData(61, 1, true, -2)]  // C♯4 sits where C4 does
    [InlineData(61, -1, true, -1)] // D♭4 where D4 does
    public void Staff_positions(int pitch, int alter, bool treble, int step)
    {
        var spelled = NoteQuiz.Candidates(NoteQuizLevel.Advanced).Concat(NoteQuiz.Candidates(NoteQuizLevel.Both))
            .First(n => n.Pitch == pitch && n.Alter == alter && n.Treble == treble);
        Assert.Equal(step, spelled.Step);
    }

    [Fact]
    public void Never_the_same_note_twice_in_a_row_and_twenty_a_round()
    {
        var quiz = new NoteQuiz(NoteQuizLevel.Treble, new Random(1));
        QuizNote? previous = null;
        int asked = 0;
        while (quiz.Next() is { } note)
        {
            Assert.NotEqual(previous, note);
            previous = note;
            quiz.AnswerPitch(note.Pitch, 1.5);
            asked++;
        }
        Assert.Equal(NoteQuiz.RoundLength, asked);
        Assert.True(quiz.Finished);
        Assert.Equal(20, quiz.Correct);
        Assert.Equal(1.5, quiz.AverageSeconds, 6);
    }

    [Fact]
    public void Answers_by_key_need_the_exact_key_answers_by_name_the_letter()
    {
        var quiz = new NoteQuiz(NoteQuizLevel.Advanced, new Random(3));
        var note = quiz.Next()!;
        Assert.False(quiz.AnswerPitch(note.Pitch + 12, 1)); // right name, wrong octave
        note = quiz.Next()!;
        Assert.True(quiz.AnswerPitch(note.Pitch, 1));       // the key itself, however it's spelled
        note = quiz.Next()!;
        Assert.True(quiz.AnswerLetter(note.Letter, 1));
        note = quiz.Next()!;
        Assert.False(quiz.AnswerLetter((note.Letter + 1) % 7, 1));
        Assert.Equal(4, quiz.Done);
        Assert.Equal(2, quiz.Correct);
    }

    [Theory]
    [InlineData(0, 1, "Cis")]
    [InlineData(1, -1, "Des")]
    [InlineData(2, -1, "Es")]
    [InlineData(5, -1, "As")]
    [InlineData(6, -1, "B")]
    [InlineData(6, 0, "H")]
    [InlineData(3, 1, "Fis")]
    public void Czech_note_names(int letter, int alter, string name)
    {
        Assert.Equal(name, NoteQuiz.Name(letter, alter));
    }
}
