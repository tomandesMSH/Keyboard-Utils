namespace KeyboardUtils;

enum ExerciseKind
{
    /// <summary>Up and down; minor keys use harmonic minor (raised seventh).</summary>
    Scale,

    /// <summary>The tonic triad broken, up and down.</summary>
    Arpeggio,

    /// <summary>I - IV - V - I: close-position chords in the right hand, the roots in the left.</summary>
    Cadence,
}

enum ExerciseHands { Right, Left, Both }

/// <summary>What to generate: kind, key (fifths + major/minor), range, hands, tempo and note value.</summary>
sealed record ExerciseSpec(
    ExerciseKind Kind = ExerciseKind.Scale,
    int Fifths = 0,
    bool Minor = false,
    int Octaves = 1,
    ExerciseHands Hands = ExerciseHands.Right,
    int Bpm = 80,
    bool Eighths = false);

/// <summary>A key offered for exercises, with its Czech and English name.</summary>
sealed record ExerciseKey(int Fifths, bool Minor, string Czech, string English)
{
    public string Name => T($"{Czech} {(Minor ? "moll" : "dur")}", $"{English} {(Minor ? "minor" : "major")}");
}

/// <summary>
/// Scales, arpeggios and cadences in any key as an ordinary <see cref="Score"/> - with a tempo, 4/4 bars and the key
/// signature - so learning mode, loops, speeding up, the metronome and "Save as" all work on them as on any song.
/// </summary>
static class ExerciseGenerator
{
    /// <summary>Major keys from 5 flats to 6 sharps, minor ones from 5 flats to 4 sharps (no double sharps in harmonic minor).</summary>
    public static readonly ExerciseKey[] Keys =
    [
        new(0, false, "C", "C"), new(1, false, "G", "G"), new(2, false, "D", "D"), new(3, false, "A", "A"),
        new(4, false, "E", "E"), new(5, false, "H", "B"), new(6, false, "Fis", "F♯"), new(-1, false, "F", "F"),
        new(-2, false, "B", "B♭"), new(-3, false, "Es", "E♭"), new(-4, false, "As", "A♭"), new(-5, false, "Des", "D♭"),
        new(0, true, "a", "A"), new(1, true, "e", "E"), new(2, true, "h", "B"), new(3, true, "fis", "F♯"),
        new(4, true, "cis", "C♯"), new(-1, true, "d", "D"), new(-2, true, "g", "G"), new(-3, true, "c", "C"),
        new(-4, true, "f", "F"), new(-5, true, "b", "B♭"),
    ];

    public static ExerciseKey KeyOf(ExerciseSpec spec) =>
        Keys.FirstOrDefault(k => k.Fifths == spec.Fifths && k.Minor == spec.Minor) ?? Keys[0];

    static readonly int[] MajorSteps = [0, 2, 4, 5, 7, 9, 11];
    static readonly int[] HarmonicMinorSteps = [0, 2, 3, 5, 7, 8, 11];

    public static Score Generate(ExerciseSpec spec)
    {
        var key = KeyOf(spec);
        int octaves = Math.Clamp(spec.Octaves, 1, 2);
        int tonic = KeySignature.Tonic(key.Fifths, key.Minor);
        // Right hand from the tonic above middle C; the left hand as many octaves lower as it spans, so each hand
        // stays (mostly) on its own staff.
        int rightBase = 60 + tonic;
        int leftBase = rightBase - 12 * (spec.Kind == ExerciseKind.Cadence ? 1 : octaves);
        double quarters = spec.Eighths && spec.Kind != ExerciseKind.Cadence ? 0.5 : 1;
        var timing = new ScoreTiming([new TempoPoint(0, 0, Math.Clamp(spec.Bpm, 20, 300) / 60.0)], 4, 4);

        var score = new Score { Title = Title(spec, key, octaves), Timing = timing, KeyFifths = key.Fifths, KeyMinor = key.Minor };
        if (spec.Hands != ExerciseHands.Left)
            score.Parts.Add(new ScorePart { Name = T("Pravá ruka", "Right hand"), Notes = Part(spec.Kind, key.Minor, rightBase, octaves, quarters, timing, right: true) });
        if (spec.Hands != ExerciseHands.Right)
            score.Parts.Add(new ScorePart { Name = T("Levá ruka", "Left hand"), Notes = Part(spec.Kind, key.Minor, leftBase, octaves, quarters, timing, right: false) });
        return score;
    }

    static List<ScoreNote> Part(ExerciseKind kind, bool minor, int basePitch, int octaves, double quarters, ScoreTiming timing, bool right)
    {
        if (kind == ExerciseKind.Cadence)
            return Cadence(minor, basePitch, timing, right);
        var steps = kind == ExerciseKind.Arpeggio ? (minor ? [0, 3, 7] : [0, 4, 7]) : minor ? HarmonicMinorSteps : MajorSteps;
        var up = new List<int>();
        for (int octave = 0; octave < octaves; octave++)
            up.AddRange(steps.Select(s => basePitch + 12 * octave + s));
        up.Add(basePitch + 12 * octaves);
        var pitches = up.Concat(Enumerable.Reverse(up).Skip(1)).ToList();
        return Line(pitches, quarters, timing);
    }

    /// <summary>One note after another, each <paramref name="quarters"/> long; the last held to the end of its bar.</summary>
    static List<ScoreNote> Line(List<int> pitches, double quarters, ScoreTiming timing)
    {
        var notes = new List<ScoreNote>();
        for (int i = 0; i < pitches.Count; i++)
        {
            double start = i * quarters;
            double end = i == pitches.Count - 1 ? BarEnd(start, timing) : start + quarters;
            notes.Add(Note(pitches[i], start, end, timing));
        }
        return notes;
    }

    /// <summary>I - IV - V - I, each chord a half note (two bars): close position in the right hand, roots in the left.</summary>
    static List<ScoreNote> Cadence(bool minor, int basePitch, ScoreTiming timing, bool right)
    {
        int third = minor ? 3 : 4, sixth = minor ? 8 : 9;
        int[][] chords = right
            ? [[0, third, 7], [0, 5, sixth], [-1, 2, 7], [0, third, 7]]   // I, IV 6/4, V 6, I - the hand barely moves
            : [[0], [5], [-5], [0]];                                      // roots: tonic, subdominant, dominant below, tonic
        var notes = new List<ScoreNote>();
        for (int c = 0; c < chords.Length; c++)
        {
            double start = c * 2;
            notes.AddRange(chords[c].Select(step => Note(basePitch + step, start, start + 2, timing)));
        }
        return notes;
    }

    static double BarEnd(double startQuarters, ScoreTiming timing)
    {
        double bar = timing.BarQuarters;
        double end = Math.Ceiling((startQuarters + 1e-9) / bar) * bar;
        // Held to the end of its bar - a whole bar when it starts on a bar line, never less than a quarter.
        return end - startQuarters < 1 ? end + bar : end;
    }

    static ScoreNote Note(int pitch, double startQuarters, double endQuarters, ScoreTiming timing)
    {
        double start = timing.ToSeconds(startQuarters);
        // A hair shorter than the full value, so repeated notes are separate presses in learning mode.
        double end = timing.ToSeconds(endQuarters) - 0.02;
        return new ScoreNote { Pitch = pitch, StartSeconds = start, DurationSeconds = Math.Max(0.05, end - start) };
    }

    static string Title(ExerciseSpec spec, ExerciseKey key, int octaves)
    {
        string what = spec.Kind switch
        {
            ExerciseKind.Arpeggio => T($"Rozložený akord {key.Name}", $"Arpeggio, {key.Name}"),
            ExerciseKind.Cadence => T($"Kadence T–S–D–T {key.Name}", $"Cadence I–IV–V–I, {key.Name}"),
            _ => key.Minor ? T($"Stupnice {key.Name} harmonická", $"Scale, {key.Name} harmonic") : T($"Stupnice {key.Name}", $"Scale, {key.Name}"),
        };
        string range = spec.Kind == ExerciseKind.Cadence ? "" : octaves == 1 ? T(", 1 oktáva", ", 1 octave") : T($", {octaves} oktávy", $", {octaves} octaves");
        string hands = spec.Hands switch
        {
            ExerciseHands.Left => T(", levá ruka", ", left hand"),
            ExerciseHands.Both => T(", obě ruce", ", both hands"),
            _ => T(", pravá ruka", ", right hand"),
        };
        return what + range + hands;
    }
}
