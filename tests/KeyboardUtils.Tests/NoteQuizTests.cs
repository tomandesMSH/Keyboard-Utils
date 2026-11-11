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

    [Fact]
    public void Streaks_and_what_was_missed()
    {
        var quiz = new NoteQuiz(NoteQuizLevel.Treble, new Random(5));
        for (int i = 0; i < 4; i++)
            quiz.AnswerPitch(quiz.Next()!.Pitch, 1);
        Assert.Equal(4, quiz.Streak);
        var miss = quiz.Next()!;
        quiz.AnswerPitch(miss.Pitch + 1, 1);
        Assert.Equal(0, quiz.Streak);
        quiz.Next();
        quiz.TimeUp(3);
        Assert.Equal(4, quiz.BestStreak);
        Assert.Equal(2, quiz.Missed.Count);
        Assert.Equal(miss, quiz.Missed[0]);
    }

    [Fact]
    public void Missed_notes_come_up_more_often_and_right_answers_forgive_them()
    {
        var weakness = new Dictionary<string, int>();
        var quiz = new NoteQuiz(NoteQuizLevel.Treble, new Random(7), weakness);
        var note = quiz.Next()!;
        quiz.AnswerLetter((note.Letter + 1) % 7, 1);
        Assert.Equal(1, weakness[note.Key]);
        for (int i = 0; i < 10; i++)
            weakness[note.Key] = Math.Min(NoteQuiz.MaxWeakness, weakness[note.Key] + 1);
        Assert.Equal(NoteQuiz.MaxWeakness, weakness[note.Key]);

        // With weight 5 against 1 for each of the other 11 notes, it comes up far more than 1 in 12.
        int hits = 0;
        var sampler = new NoteQuiz(NoteQuizLevel.Treble, new Random(11), weakness);
        for (int i = 0; i < 2000; i++)
            if (sampler.Next() == note)
                hits++;
            else if (sampler.Current is { } other)
                _ = other;
        Assert.InRange(hits, 2000 / 12 + 150, 2000);

        // Right answers take it back off.
        var forgiving = new NoteQuiz(NoteQuizLevel.Treble, new Random(13), weakness);
        while (forgiving.Next() is { } n && weakness.ContainsKey(note.Key) && !forgiving.Finished)
            forgiving.AnswerPitch(n.Pitch, 1);
        Assert.True(!weakness.ContainsKey(note.Key) || weakness[note.Key] < NoteQuiz.MaxWeakness);
    }

    [Fact]
    public void Intervals_two_white_notes_a_second_to_an_octave_apart()
    {
        var quiz = new NoteQuiz(NoteQuizLevel.Intervals, new Random(3));
        for (int i = 0; i < NoteQuiz.RoundLength; i++)
        {
            var item = quiz.Next()!;
            Assert.NotNull(item.Upper);
            Assert.InRange(item.Interval, 2, 8);
            Assert.True(item.Upper!.Pitch > item.Pitch);
            Assert.Equal(0, item.Upper.Alter);
            Assert.False(quiz.AnswerPitch(item.Pitch, 1)); // an instrument doesn't answer intervals...
            Assert.Equal(i, quiz.Done);                    // ...and doesn't count
            Assert.True(quiz.AnswerInterval(item.Interval, 1));
        }
        Assert.Equal(NoteQuiz.RoundLength, quiz.Correct);
        Assert.Equal("Tercie", new QuizNote(60, 0, 0, Clef.Treble, new QuizNote(64, 2, 0, Clef.Treble)).Name);
        Assert.Equal(8, new QuizNote(60, 0, 0, Clef.Treble, new QuizNote(72, 0, 0, Clef.Treble)).Interval);
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
