namespace KeyboardUtils;

/// <summary>
/// Key signatures as a number of fifths: 0 = C major / A minor, 1 = one sharp (G major), -1 = one flat (F major)...
/// Decides how each key on the piano is written (A♯ or B♭?) and guesses the key of music whose file doesn't say.
/// </summary>
static class KeySignature
{
    /// <summary>Pitch class of each letter without accidentals: C D E F G A B.</summary>
    static readonly int[] NaturalPitch = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The order sharps (F C G D A E B) and flats (B E A D G C F) are added in, as letters (C = 0).</summary>
    public static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];
    public static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];

    /// <summary>What the key does to each letter: +1 sharp, -1 flat, 0 nothing.</summary>
    public static int[] LetterAlters(int fifths)
    {
        var alters = new int[7];
        for (int i = 0; i < Math.Min(7, Math.Abs(fifths)); i++)
        {
            if (fifths > 0)
                alters[SharpOrder[i]] = 1;
            else
                alters[FlatOrder[i]] = -1;
        }
        return alters;
    }

    /// <summary>
    /// How each pitch class (0 = C ... 11 = B) is written in the key: the key's own notes as the key spells them,
    /// other white keys as naturals, the remaining black keys as sharps (sharp keys and C) or flats (flat keys).
    /// In a minor key the raised seventh (the leading note of harmonic minor) is written as the seventh degree
    /// raised - C♯ in D minor, E♯ in F♯ minor - when that needs no double sharp.
    /// </summary>
    public static (int Letter, int Alter)[] Spelling(int fifths, bool minor)
    {
        var spelling = Spelling(fifths);
        if (!minor)
            return spelling;
        int majorTonic = Mod12(7 * fifths);
        int tonicLetter = (spelling[majorTonic].Letter + 5) % 7;   // the relative minor: a sixth up
        int leadingLetter = (tonicLetter + 6) % 7;
        int leadingPitch = Mod12(majorTonic + 8);                   // minor tonic - 1
        int alter = leadingPitch - NaturalPitch[leadingLetter];
        alter = alter > 6 ? alter - 12 : alter < -6 ? alter + 12 : alter;
        if (alter is >= -1 and <= 1)
            spelling[leadingPitch] = (leadingLetter, alter);
        return spelling;
    }

    /// <summary>
    /// The key a whole tone, a sixth... away: moving by <paramref name="semitones"/> adds 7 × semitones fifths (mod
    /// 12). Kept within six sharps or flats (C♯ major comes out as D♭ major).
    /// </summary>
    public static int Transposed(int fifths, int semitones)
    {
        int shift = Mod12(7 * semitones);
        int result = fifths + (shift > 6 ? shift - 12 : shift);
        return result > 6 ? result - 12 : result < -6 ? result + 12 : result;
    }

    /// <summary>Pitch class of the key's tonic: major, or its relative minor.</summary>
    public static int Tonic(int fifths, bool minor) => Mod12(7 * fifths + (minor ? 9 : 0));

    /// <summary>
    /// How each pitch class (0 = C ... 11 = B) is written in the key: the key's own notes as the key spells them,
    /// other white keys as naturals, the remaining black keys as sharps (sharp keys and C) or flats (flat keys).
    /// </summary>
    public static (int Letter, int Alter)[] Spelling(int fifths)
    {
        var alters = LetterAlters(fifths);
        var spelling = new (int Letter, int Alter)?[12];
        for (int letter = 0; letter < 7; letter++)
            spelling[Mod12(NaturalPitch[letter] + alters[letter])] = (letter, alters[letter]);
        for (int letter = 0; letter < 7; letter++)
            spelling[NaturalPitch[letter]] ??= (letter, 0);
        for (int pitch = 0; pitch < 12; pitch++)
        {
            if (spelling[pitch] != null)
                continue;
            // A black key: its white neighbour below plus a sharp, or above minus a flat.
            spelling[pitch] = fifths >= 0
                ? (Array.IndexOf(NaturalPitch, Mod12(pitch - 1)), 1)
                : (Array.IndexOf(NaturalPitch, Mod12(pitch + 1)), -1);
        }
        return spelling.Select(s => s!.Value).ToArray();
    }

    /// <summary>The written line/space of a note: octave * 7 + letter (C4 = 28), with B♯ and C♭ in the right octave.</summary>
    public static int Diatonic(int pitch, (int Letter, int Alter) spelled)
    {
        int octave = (pitch - spelled.Alter - NaturalPitch[spelled.Letter]) / 12 - 1;
        return octave * 7 + spelled.Letter;
    }

    /// <summary>
    /// The major key (or its relative minor) that leaves the fewest notes needing accidentals, weighted by how long
    /// they sound. Ties go to the key with fewer sharps or flats, so music that fits C stays in C.
    /// </summary>
    public static int Estimate(IEnumerable<ScoreNote> notes)
    {
        var weight = new double[12];
        foreach (var note in notes)
            weight[Mod12(note.Pitch)] += Math.Min(note.DurationSeconds, 2) + 0.1;
        double total = weight.Sum();
        if (total <= 0)
            return 0;

        int best = 0;
        double bestCost = double.MaxValue;
        for (int fifths = -6; fifths <= 6; fifths++)
        {
            var alters = LetterAlters(fifths);
            var inKey = Enumerable.Range(0, 7).Select(l => Mod12(NaturalPitch[l] + alters[l])).ToHashSet();
            double cost = Enumerable.Range(0, 12).Where(p => !inKey.Contains(p)).Sum(p => weight[p])
                + total * 0.01 * Math.Abs(fifths);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = fifths;
            }
        }
        return best;
    }

    /// <summary>The score's key: from its file, otherwise guessed from all its notes.</summary>
    public static int Of(Score score) => score.KeyFifths ?? Estimate(score.Parts.SelectMany(p => p.Notes));

    /// <summary>A minor key's spelling only when the key is known to be minor (guessed keys are taken as major).</summary>
    public static bool IsMinor(Score score) => score.KeyFifths != null && score.KeyMinor;

    /// <summary>Letter names, for MusicXML's &lt;step&gt;.</summary>
    public static readonly string[] LetterNames = ["C", "D", "E", "F", "G", "A", "B"];

    static int Mod12(int value) => ((value % 12) + 12) % 12;
}

/// <summary>
/// Which accidentals are in force on one staff: the key signature everywhere, plus signs written earlier in the
/// current bar on their own line. Decides which notes need a sharp, flat or natural in front of them - the same
/// for the sheet music view and the MusicXML export.
/// </summary>
sealed class AccidentalState(int fifths, bool minor = false)
{
    readonly (int Letter, int Alter)[] spelling = KeySignature.Spelling(fifths, minor);
    readonly int[] keyAlters = KeySignature.LetterAlters(fifths);
    readonly Dictionary<int, int> inBar = [];

    public void NewBar() => inBar.Clear();

    /// <summary>
    /// The sign to write before <paramref name="pitch"/> (-1 flat, 0 natural, 1 sharp), or null when the key or the
    /// bar already makes it right. A tied continuation never gets one.
    /// </summary>
    public int? Next(int pitch, bool tiedFromPrevious)
    {
        var spelled = spelling[pitch % 12];
        int line = KeySignature.Diatonic(pitch, spelled);
        int current = inBar.TryGetValue(line, out int alter) ? alter : keyAlters[spelled.Letter];
        inBar[line] = spelled.Alter;
        return !tiedFromPrevious && spelled.Alter != current ? spelled.Alter : null;
    }
}
