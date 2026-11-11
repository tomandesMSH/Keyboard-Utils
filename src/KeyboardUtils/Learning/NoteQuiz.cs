namespace KeyboardUtils;

enum NoteQuizLevel
{
    /// <summary>Treble clef, C4-G5, no accidentals.</summary>
    Treble,

    /// <summary>Bass clef, F2-C4, no accidentals.</summary>
    Bass,

    /// <summary>Both of the above, mixed.</summary>
    Both,

    /// <summary>Both clefs with ledger lines further out, sharps and flats.</summary>
    Advanced,
}

/// <summary>A note to read: its pitch, how it's written (letter 0 = C ... 6 = B, alter -1/0/1) and on which clef.</summary>
sealed record QuizNote(int Pitch, int Letter, int Alter, bool Treble)
{
    /// <summary>Line/space from the bottom line of its staff: 0 = bottom line, 8 = top line, negative = below.</summary>
    public int Step => (Pitch - Alter - NaturalPitch[Letter]) / 12 * 7 - 7 + Letter - (Treble ? TrebleBottom : BassBottom);

    static readonly int[] NaturalPitch = [0, 2, 4, 5, 7, 9, 11];
    const int TrebleBottom = 4 * 7 + 2; // E4
    const int BassBottom = 2 * 7 + 4;   // G2

    /// <summary>Its name: Czech (C, Cis, Es, H, B...) or English (C, C♯, E♭, B, B♭...), without the octave.</summary>
    public string Name => NoteQuiz.Name(Letter, Alter);
}

/// <summary>
/// The note-reading trainer: a note on a staff, the player plays it (or picks its name), 20 notes a round. Answers
/// from an instrument must be the exact key (so C♯ and D♭ are both right for that black key); answers by name only
/// need the letter.
/// </summary>
sealed class NoteQuiz
{
    public const int RoundLength = 20;

    readonly Random random;
    readonly List<QuizNote> candidates;
    readonly List<double> answerSeconds = [];

    public NoteQuizLevel Level { get; }
    public QuizNote? Current { get; private set; }
    public int Done { get; private set; }
    public int Correct { get; private set; }
    public bool Finished => Done >= RoundLength;
    public double AverageSeconds => answerSeconds.Count == 0 ? 0 : answerSeconds.Average();

    public NoteQuiz(NoteQuizLevel level, Random? random = null)
    {
        Level = level;
        this.random = random ?? new Random();
        candidates = Candidates(level);
    }

    /// <summary>Every note the level can ask for.</summary>
    public static List<QuizNote> Candidates(NoteQuizLevel level)
    {
        var notes = new List<QuizNote>();
        void Range(int from, int to, bool treble, bool accidentals)
        {
            for (int pitch = from; pitch <= to; pitch++)
            {
                int pc = pitch % 12;
                if (WhiteLetter(pc) is { } letter)
                    notes.Add(new QuizNote(pitch, letter, 0, treble));
                else if (accidentals)
                {
                    // A black key, both ways: as the sharp of the white key below, as the flat of the one above.
                    notes.Add(new QuizNote(pitch, WhiteLetter(pc - 1)!.Value, 1, treble));
                    notes.Add(new QuizNote(pitch, WhiteLetter(pc + 1)!.Value, -1, treble));
                }
            }
        }
        switch (level)
        {
            case NoteQuizLevel.Treble:
                Range(60, 79, true, false);
                break;
            case NoteQuizLevel.Bass:
                Range(41, 60, false, false);
                break;
            case NoteQuizLevel.Both:
                Range(60, 79, true, false);
                Range(41, 60, false, false);
                break;
            default:
                Range(57, 84, true, true);
                Range(36, 64, false, true);
                break;
        }
        return notes;
    }

    static readonly int[] WhitePitch = [0, 2, 4, 5, 7, 9, 11];

    static int? WhiteLetter(int pitchClass)
    {
        int index = Array.IndexOf(WhitePitch, ((pitchClass % 12) + 12) % 12);
        return index < 0 ? null : index;
    }

    /// <summary>Picks the next note (never the same one twice in a row); null once the round is over.</summary>
    public QuizNote? Next()
    {
        if (Finished)
            return Current = null;
        QuizNote next;
        do
            next = candidates[random.Next(candidates.Count)];
        while (candidates.Count > 1 && next == Current);
        return Current = next;
    }

    /// <summary>A key played on the instrument: right when it's the very key written (any spelling of it).</summary>
    public bool AnswerPitch(int pitch, double seconds) => Record(Current is { } note && note.Pitch == pitch, seconds);

    /// <summary>A name picked (letter 0 = C ... 6 = B/H): right when it's the written letter.</summary>
    public bool AnswerLetter(int letter, double seconds) => Record(Current is { } note && note.Letter == letter, seconds);

    bool Record(bool right, double seconds)
    {
        if (Current == null || Finished)
            return false;
        Done++;
        if (right)
            Correct++;
        answerSeconds.Add(seconds);
        return right;
    }

    /// <summary>Letter buttons, in the reader's language: C D E F G A H (Czech) or C D E F G A B.</summary>
    public static string LetterName(int letter) => letter == 6 ? T("H", "B") : KeySignature.LetterNames[letter];

    /// <summary>
    /// A note's name. Czech: Cis, Dis..., flats Des, Es, Ges, As, and B for B♭ (H flat). English: C♯, D♭, B♭...
    /// </summary>
    public static string Name(int letter, int alter)
    {
        if (Lang.Current == AppLanguage.English)
            return KeySignature.LetterNames[letter] + (alter > 0 ? "♯" : alter < 0 ? "♭" : "");
        string czech = LetterName(letter);
        return alter switch
        {
            > 0 => czech + "is",
            < 0 => letter switch
            {
                6 => "B",                 // H flat
                2 => "Es",                // E flat
                5 => "As",                // A flat
                _ => czech + "es",        // Des, Ges, Ces, Fes
            },
            _ => czech,
        };
    }
}
