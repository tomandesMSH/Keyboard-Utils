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

    /// <summary>Two notes on the treble staff: name the interval (second ... octave).</summary>
    Intervals,

    /// <summary>Alto clef (viola), C3-A4, no accidentals.</summary>
    Alto,

    /// <summary>Tenor clef (cello, bassoon, trombone up high), C3-A4, no accidentals.</summary>
    Tenor,

    /// <summary>The player's instrument (not piano): its clef, the lowest octave and a half as written, no accidentals.</summary>
    Instrument,

    /// <summary>The player's instrument: its whole range (up to three ledger lines), sharps and flats.</summary>
    InstrumentAdvanced,
}

/// <summary>
/// A note to read: its written pitch, how it's written (letter 0 = C ... 6 = B, alter -1/0/1) and on which clef. For
/// an interval question, <see cref="Upper"/> is the second note above it.
/// </summary>
sealed record QuizNote(int Pitch, int Letter, int Alter, Clef Clef, QuizNote? Upper = null)
{
    /// <summary>On a treble staff (the guitar's too).</summary>
    public bool Treble => Clef is Clef.Treble or Clef.Treble8;

    /// <summary>Line/space from the bottom line of its staff: 0 = bottom line, 8 = top line, negative = below.</summary>
    public int Step => Diatonic - KeyboardUtils.Instrument.BottomLine(Clef);

    /// <summary>octave * 7 + letter (C4 = 28).</summary>
    public int Diatonic => ((Pitch - Alter - NaturalPitch[Letter]) / 12 - 1) * 7 + Letter;

    static readonly int[] NaturalPitch = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The interval to <see cref="Upper"/> as a number: 2 = second ... 8 = octave; 0 without one.</summary>
    public int Interval => Upper == null ? 0 : Upper.Diatonic - Diatonic + 1;

    /// <summary>Its name: Czech (C, Cis, Es, H, B...) or English (C, C♯, E♭, B, B♭...), without the octave; an interval's name.</summary>
    public string Name => Upper == null ? NoteQuiz.Name(Letter, Alter) : NoteQuiz.IntervalName(Interval);

    /// <summary>Identifies the written note (clef, key, spelling) for the per-note mistake statistics.</summary>
    public string Key => $"{ClefKey}{Pitch}{(Alter > 0 ? "#" : Alter < 0 ? "b" : "")}";

    string ClefKey => Clef switch
    {
        Clef.Treble => "T",
        Clef.Bass => "B",
        Clef.Alto => "A",
        Clef.Tenor => "N",
        _ => "G",
    };
}

/// <summary>
/// The note-reading trainer: a note on a staff, the player plays it (or picks its name), 20 notes a round. Answers
/// from an instrument must be the exact key (so C♯ and D♭ are both right for that black key); answers by name only
/// need the letter. Notes the player keeps missing (<see cref="Weakness"/>) come up more often.
/// </summary>
sealed class NoteQuiz
{
    public const int RoundLength = 20;

    /// <summary>How much more often a note missed a lot comes up (its weight is 1 + misses, up to this).</summary>
    public const int MaxWeakness = 4;

    readonly Random random;
    readonly List<QuizNote> candidates;
    readonly List<double> answerSeconds = [];
    readonly List<QuizNote> missed = [];

    public NoteQuizLevel Level { get; }

    /// <summary>What the player plays: notes are shown as it reads them, a played note is compared as it sounds.</summary>
    public Instrument Instrument { get; }

    public QuizNote? Current { get; private set; }
    public int Done { get; private set; }
    public int Correct { get; private set; }
    public bool Finished => Done >= RoundLength;
    public double AverageSeconds => answerSeconds.Count == 0 ? 0 : answerSeconds.Average();

    /// <summary>Right answers in a row now, and the longest run this round.</summary>
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    /// <summary>The notes missed this round, in order.</summary>
    public IReadOnlyList<QuizNote> Missed => missed;

    /// <summary>
    /// Mistakes per note (<see cref="QuizNote.Key"/>) carried over from earlier rounds: a miss adds one (up to
    /// <see cref="MaxWeakness"/>), a right answer takes one off. Kept by the caller (in the config).
    /// </summary>
    public Dictionary<string, int> Weakness { get; }

    public NoteQuiz(NoteQuizLevel level, Random? random = null, Dictionary<string, int>? weakness = null, Instrument? instrument = null)
    {
        Level = level;
        Instrument = instrument ?? Instrument.Piano;
        this.random = random ?? new Random();
        Weakness = weakness ?? [];
        candidates = Candidates(level, Instrument);
    }

    /// <summary>The levels for an instrument: its own two first (not for piano), then the clef levels.</summary>
    public static NoteQuizLevel[] LevelsFor(Instrument instrument) => instrument.IsPiano
        ? [NoteQuizLevel.Treble, NoteQuizLevel.Bass, NoteQuizLevel.Both, NoteQuizLevel.Advanced, NoteQuizLevel.Intervals, NoteQuizLevel.Alto, NoteQuizLevel.Tenor]
        : [NoteQuizLevel.Instrument, NoteQuizLevel.InstrumentAdvanced, NoteQuizLevel.Treble, NoteQuizLevel.Bass, NoteQuizLevel.Alto, NoteQuizLevel.Tenor, NoteQuizLevel.Intervals];

    /// <summary>Lowest and highest staff step an instrument level asks for: three ledger lines either side (and the note just below them, a clarinet's low E).</summary>
    const int LowestStep = -7, HighestStep = 14;

    /// <summary>Every note the level can ask for (for intervals: the lower notes). Pitches are as written.</summary>
    public static List<QuizNote> Candidates(NoteQuizLevel level, Instrument? instrument = null)
    {
        instrument ??= Instrument.Piano;
        var notes = new List<QuizNote>();
        void Range(int from, int to, Clef treble, bool accidentals)
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
                Range(60, 79, Clef.Treble, false);
                break;
            case NoteQuizLevel.Bass:
                Range(41, 60, Clef.Bass, false);
                break;
            case NoteQuizLevel.Both:
                Range(60, 79, Clef.Treble, false);
                Range(41, 60, Clef.Bass, false);
                break;
            case NoteQuizLevel.Intervals:
                Range(60, 72, Clef.Treble, false); // up to an octave above: C6 at most
                break;
            case NoteQuizLevel.Alto:
                Range(48, 69, Clef.Alto, false);
                break;
            case NoteQuizLevel.Tenor:
                Range(48, 69, Clef.Tenor, false);
                break;
            case NoteQuizLevel.Instrument:
            case NoteQuizLevel.InstrumentAdvanced:
                // As the player reads it: a clarinet's lowest note is written E3 though it sounds D3.
                int low = instrument.Written(instrument.Low), high = instrument.Written(instrument.High);
                bool advanced = level == NoteQuizLevel.InstrumentAdvanced;
                Range(low, advanced ? high : Math.Min(high, low + 19), instrument.Clef, advanced);
                notes.RemoveAll(n => n.Step is < LowestStep or > HighestStep);
                break;
            default:
                Range(57, 84, Clef.Treble, true);
                Range(36, 64, Clef.Bass, true);
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

    /// <summary>
    /// Picks the next note (never the same one twice in a row; missed ones more often); null once the round is over.
    /// </summary>
    public QuizNote? Next()
    {
        if (Finished)
            return Current = null;
        QuizNote next;
        do
            next = Level == NoteQuizLevel.Intervals ? RandomInterval() : Weighted();
        while (candidates.Count > 1 && next == Current);
        return Current = next;
    }

    QuizNote Weighted()
    {
        var weights = candidates.Select(c => 1 + Math.Min(MaxWeakness, Weakness.GetValueOrDefault(c.Key))).ToList();
        int pick = random.Next(weights.Sum());
        for (int i = 0; i < candidates.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0)
                return candidates[i];
        }
        return candidates[^1];
    }

    /// <summary>A white-key lower note and a white key a second to an octave above it.</summary>
    QuizNote RandomInterval()
    {
        var lower = candidates[random.Next(candidates.Count)];
        int steps = random.Next(1, 8);                      // 1 = second ... 7 = octave
        int diatonic = lower.Diatonic + steps;
        int letter = diatonic % 7, octave = diatonic / 7; // Diatonic counts C4 as octave 4 (MIDI octave + 1)
        var upper = new QuizNote((octave + 1) * 12 + WhitePitch[letter], letter, 0, Clef.Treble);
        return lower with { Upper = upper };
    }

    /// <summary>
    /// A note played on the instrument (as it sounds): right when it's the very note written (any spelling of it) -
    /// for a transposing instrument, what that written note sounds like. Intervals aren't answered by playing - a
    /// note then doesn't count at all.
    /// </summary>
    public bool AnswerPitch(int pitch, double seconds) =>
        Current is { Upper: null } note && Record(Instrument.Sounding(note.Pitch) == pitch, seconds);

    /// <summary>A name picked (letter 0 = C ... 6 = B/H): right when it's the written letter.</summary>
    public bool AnswerLetter(int letter, double seconds) => Record(Current is { Upper: null } note && note.Letter == letter, seconds);

    /// <summary>An interval picked: 2 = second ... 8 = octave.</summary>
    public bool AnswerInterval(int number, double seconds) => Record(Current is { Upper: not null } note && note.Interval == number, seconds);

    /// <summary>The time for the note ran out: a miss.</summary>
    public void TimeUp(double seconds) => Record(false, seconds);

    bool Record(bool right, double seconds)
    {
        if (Current is not { } note || Finished)
            return false;
        Done++;
        answerSeconds.Add(seconds);
        if (right)
        {
            Correct++;
            BestStreak = Math.Max(BestStreak, ++Streak);
        }
        else
        {
            Streak = 0;
            missed.Add(note);
        }
        // The note's standing for the next rounds (single notes only - intervals are random pairs).
        if (note.Upper == null)
        {
            int misses = Weakness.GetValueOrDefault(note.Key) + (right ? -1 : 1);
            if (misses <= 0)
                Weakness.Remove(note.Key);
            else
                Weakness[note.Key] = Math.Min(MaxWeakness, misses);
        }
        return right;
    }

    /// <summary>The notes missed most this round (names, worst first), for the summary.</summary>
    public List<string> MostMissed(int count = 3) =>
        // Grouped by what's shown: different octaves of one interval (or note) count as the same mistake.
        missed.Select(n => n.Upper == null && Level is NoteQuizLevel.Both or NoteQuizLevel.Advanced
                ? $"{n.Name} ({(n.Treble ? T("houslový", "treble") : T("basový", "bass"))})"
                : n.Name)
            .GroupBy(name => name).OrderByDescending(g => g.Count())
            .Select(g => g.Key).Take(count)
            .ToList();

    /// <summary>Letter buttons, in the reader's language: C D E F G A H (Czech) or C D E F G A B.</summary>
    public static string LetterName(int letter) => letter == 6 ? T("H", "B") : KeySignature.LetterNames[letter];

    /// <summary>2 = second ... 8 = octave, in the reader's language.</summary>
    public static string IntervalName(int number) => number switch
    {
        2 => T("Sekunda", "Second"),
        3 => T("Tercie", "Third"),
        4 => T("Kvarta", "Fourth"),
        5 => T("Kvinta", "Fifth"),
        6 => T("Sexta", "Sixth"),
        7 => T("Septima", "Seventh"),
        8 => T("Oktáva", "Octave"),
        _ => "?",
    };

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
