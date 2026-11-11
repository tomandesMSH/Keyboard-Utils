namespace KeyboardUtils;

/// <summary>The kinds of exercise. Saved in the config by name, so new ones go anywhere; the dialog lists them in <see cref="ExerciseGenerator.Kinds"/> order.</summary>
enum ExerciseKind
{
    /// <summary>Up and down; minor keys in the chosen <see cref="MinorForm"/>.</summary>
    Scale,

    /// <summary>The tonic triad broken, up and down.</summary>
    Arpeggio,

    /// <summary>I - IV - V - I: close-position chords in the right hand, the roots in the left.</summary>
    Cadence,

    /// <summary>Every semitone from the tonic, up and down.</summary>
    Chromatic,

    /// <summary>The tonic triad as a chord in root position and its inversions, up the octave and back.</summary>
    Inversions,

    /// <summary>Hanon's exercise No. 1 (C E F G A G F E, a step higher each bar) in the key, up and back down.</summary>
    Hanon,
}

enum ExerciseHands { Right, Left, Both }

/// <summary>Which minor scale: harmonic (raised 7th), melodic (raised 6th and 7th going up, natural going down) or natural.</summary>
enum MinorForm { Harmonic, Melodic, Natural }

/// <summary>What to generate: kind, key (fifths + major/minor), range, hands, tempo and note value.</summary>
sealed record ExerciseSpec(
    ExerciseKind Kind = ExerciseKind.Scale,
    int Fifths = 0,
    bool Minor = false,
    int Octaves = 1,
    ExerciseHands Hands = ExerciseHands.Right,
    int Bpm = 80,
    bool Eighths = false,
    MinorForm MinorForm = MinorForm.Harmonic);

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

    /// <summary>The kinds in the order the dialog offers them.</summary>
    public static readonly ExerciseKind[] Kinds =
        [ExerciseKind.Scale, ExerciseKind.Chromatic, ExerciseKind.Arpeggio, ExerciseKind.Inversions, ExerciseKind.Cadence, ExerciseKind.Hanon];

    static readonly int[] MajorSteps = [0, 2, 4, 5, 7, 9, 11];
    static readonly int[] HarmonicMinorSteps = [0, 2, 3, 5, 7, 8, 11];
    static readonly int[] MelodicMinorSteps = [0, 2, 3, 5, 7, 9, 11];
    static readonly int[] NaturalMinorSteps = [0, 2, 3, 5, 7, 8, 10];

    /// <summary>Kinds with chords in fixed half notes: one octave, no eighths.</summary>
    public static bool IsChordal(ExerciseKind kind) => kind is ExerciseKind.Cadence or ExerciseKind.Inversions;

    public static Score Generate(ExerciseSpec spec)
    {
        var key = KeyOf(spec);
        int octaves = Math.Clamp(spec.Octaves, 1, 2);
        int tonic = KeySignature.Tonic(key.Fifths, key.Minor);
        // Right hand from the tonic above middle C. The left hand goes down by octaves until its highest note is
        // at most G4 (a few ledger lines over the bass staff) - but no lower than C1 - so it reads on its own staff; each hand is also pinned to
        // its staff, so it never jumps to the other one.
        int rightBase = 60 + tonic;
        int reach = spec.Kind switch
        {
            ExerciseKind.Cadence => 7,            // the roots: up to the dominant
            ExerciseKind.Inversions => 19,        // second inversion an octave up: an octave and a fifth
            ExerciseKind.Hanon => 12 * octaves + 9, // a sixth past the top tonic
            _ => 12 * octaves,
        };
        int leftBase = rightBase - 12;
        while (leftBase + reach > 67 && leftBase - 12 >= 24)
            leftBase -= 12;
        double quarters = spec.Eighths && !IsChordal(spec.Kind) ? 0.5 : 1;
        var timing = new ScoreTiming([new TempoPoint(0, 0, Math.Clamp(spec.Bpm, 20, 300) / 60.0)], 4, 4);

        var score = new Score { Title = Title(spec, key, octaves), Timing = timing, KeyFifths = key.Fifths, KeyMinor = key.Minor };
        if (spec.Hands != ExerciseHands.Left)
            score.Parts.Add(new ScorePart { Name = T("Pravá ruka", "Right hand"), Staff = PartStaff.Treble, Notes = Part(spec, key.Minor, rightBase, octaves, quarters, timing, right: true) });
        if (spec.Hands != ExerciseHands.Right)
            score.Parts.Add(new ScorePart { Name = T("Levá ruka", "Left hand"), Staff = PartStaff.Bass, Notes = Part(spec, key.Minor, leftBase, octaves, quarters, timing, right: false) });
        return score;
    }

    static List<ScoreNote> Part(ExerciseSpec spec, bool minor, int basePitch, int octaves, double quarters, ScoreTiming timing, bool right)
    {
        switch (spec.Kind)
        {
            case ExerciseKind.Cadence:
                return Cadence(minor, basePitch, timing, right);
            case ExerciseKind.Inversions:
                return Inversions(minor, basePitch, timing);
            case ExerciseKind.Hanon:
                return Line(Hanon(minor ? NaturalMinorSteps : MajorSteps, basePitch, octaves), quarters, timing);
        }
        if (spec.Kind == ExerciseKind.Chromatic)
        {
            var chromaticUp = Enumerable.Range(0, 12 * octaves + 1).Select(i => basePitch + i).ToList();
            return Line(chromaticUp.Concat(Enumerable.Reverse(chromaticUp).Skip(1)).ToList(), quarters, timing);
        }
        if (spec.Kind == ExerciseKind.Arpeggio)
            return Line(UpAndDown(minor ? [0, 3, 7] : [0, 4, 7], null, basePitch, octaves), quarters, timing);
        // Scales: melodic minor goes up melodic and comes down natural.
        var (up, down) = !minor ? (MajorSteps, MajorSteps) : spec.MinorForm switch
        {
            MinorForm.Melodic => (MelodicMinorSteps, NaturalMinorSteps),
            MinorForm.Natural => (NaturalMinorSteps, NaturalMinorSteps),
            _ => (HarmonicMinorSteps, HarmonicMinorSteps),
        };
        return Line(UpAndDown(up, down, basePitch, octaves), quarters, timing);
    }

    /// <summary>The steps of each octave going up to the top tonic, then (with <paramref name="downSteps"/>, else the same) back down.</summary>
    static List<int> UpAndDown(int[] upSteps, int[]? downSteps, int basePitch, int octaves)
    {
        List<int> Run(int[] steps)
        {
            var run = new List<int>();
            for (int octave = 0; octave < octaves; octave++)
                run.AddRange(steps.Select(s => basePitch + 12 * octave + s));
            run.Add(basePitch + 12 * octaves);
            return run;
        }
        var up = Run(upSteps);
        var down = Enumerable.Reverse(Run(downSteps ?? upSteps)).Skip(1);
        return up.Concat(down).ToList();
    }

    /// <summary>
    /// Hanon No. 1 on the key's scale: degrees 1 3 4 5 6 5 4 3, a step higher each bar up through the octaves, then
    /// mirrored back down (5 3 2 1 7 1 2 3, a step lower each bar), ending on the tonic.
    /// </summary>
    static List<int> Hanon(int[] steps, int basePitch, int octaves)
    {
        int Pitch(int degree) => basePitch + 12 * (int)Math.Floor(degree / 7.0) + steps[((degree % 7) + 7) % 7];
        int[] upPattern = [0, 2, 3, 4, 5, 4, 3, 2];
        int[] downPattern = [0, -2, -3, -4, -5, -4, -3, -2];
        int bars = 7 * octaves;
        var degrees = new List<int>();
        for (int shift = 0; shift < bars; shift++)
            degrees.AddRange(upPattern.Select(d => d + shift));
        int top = bars + 4;
        for (int shift = 0; shift < bars; shift++)
            degrees.AddRange(downPattern.Select(d => top + d - shift));
        degrees.Add(0);
        return degrees.Select(Pitch).ToList();
    }

    /// <summary>The tonic triad in root position, first and second inversion and up the octave, then back - half notes.</summary>
    static List<ScoreNote> Inversions(bool minor, int basePitch, ScoreTiming timing)
    {
        int third = minor ? 3 : 4;
        int[][] shapes = [[0, third, 7], [third, 7, 12], [7, 12, 12 + third], [12, 12 + third, 19]];
        var chords = shapes.Concat(shapes.Reverse().Skip(1)).ToList();
        var notes = new List<ScoreNote>();
        for (int c = 0; c < chords.Count; c++)
        {
            double start = c * 2;
            double end = c == chords.Count - 1 ? BarEnd(start, timing) : start + 2;
            notes.AddRange(chords[c].Select(step => Note(basePitch + step, start, end, timing)));
        }
        return notes;
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
        string form = spec.MinorForm switch
        {
            MinorForm.Melodic => T(" melodická", " melodic"),
            MinorForm.Natural => T(" přirozená", " natural"),
            _ => T(" harmonická", " harmonic"),
        };
        string what = spec.Kind switch
        {
            ExerciseKind.Arpeggio => T($"Rozložený akord {key.Name}", $"Arpeggio, {key.Name}"),
            ExerciseKind.Cadence => T($"Kadence T–S–D–T {key.Name}", $"Cadence I–IV–V–I, {key.Name}"),
            ExerciseKind.Chromatic => T($"Chromatická stupnice od {key.Name}", $"Chromatic scale from {key.Name}"),
            ExerciseKind.Inversions => T($"Obraty akordu {key.Name}", $"Inversions, {key.Name}"),
            ExerciseKind.Hanon => T($"Hanon č. 1 {key.Name}", $"Hanon No. 1, {key.Name}"),
            _ => key.Minor ? T($"Stupnice {key.Name}", $"Scale, {key.Name}") + form : T($"Stupnice {key.Name}", $"Scale, {key.Name}"),
        };
        string range = IsChordal(spec.Kind) ? "" : octaves == 1 ? T(", 1 oktáva", ", 1 octave") : T($", {octaves} oktávy", $", {octaves} octaves");
        string hands = spec.Hands switch
        {
            ExerciseHands.Left => T(", levá ruka", ", left hand"),
            ExerciseHands.Both => T(", obě ruce", ", both hands"),
            _ => T(", pravá ruka", ", right hand"),
        };
        return what + range + hands;
    }
}
