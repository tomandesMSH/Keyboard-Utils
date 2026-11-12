namespace KeyboardUtils;

enum ChordKind { Major, Minor, Dominant7, Major7, Minor7, Diminished, Augmented, Sus2, Sus4 }

/// <summary>A chord symbol: its root (pitch class 0 = C), kind, and a different bass note ("F/C"); at a moment in the song.</summary>
sealed record ChordSymbol(double StartSeconds, int Root, ChordKind Kind, int? Bass = null)
{
    public string Name => Chords.Name(Root, Kind, Bass);
}

/// <summary>Chord names and what's in each kind of chord.</summary>
static class Chords
{
    /// <summary>Roots as guitar charts write them: C♯ F♯ G♯ with sharps, E♭ A♭ B♭ with flats; Czech H and B.</summary>
    static readonly string[] CzechRoots = ["C", "C#", "D", "Es", "E", "F", "F#", "G", "As", "A", "B", "H"];
    static readonly string[] EnglishRoots = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    public static string RootName(int pitchClass)
    {
        int pc = ((pitchClass % 12) + 12) % 12;
        return T(CzechRoots[pc], EnglishRoots[pc]);
    }

    public static string Suffix(ChordKind kind) => kind switch
    {
        ChordKind.Minor => "m",
        ChordKind.Dominant7 => "7",
        ChordKind.Major7 => "maj7",
        ChordKind.Minor7 => "m7",
        ChordKind.Diminished => "dim",
        ChordKind.Augmented => "aug",
        ChordKind.Sus2 => "sus2",
        ChordKind.Sus4 => "sus4",
        _ => "",
    };

    /// <summary>"C", "Am", "G7", "Dmaj7", "F/C" (Czech: "H7", "B" for B♭).</summary>
    public static string Name(int root, ChordKind kind, int? bass = null) =>
        RootName(root) + Suffix(kind) + (bass is { } b && ((b - root) % 12 + 12) % 12 != 0 ? "/" + RootName(b) : "");

    /// <summary>Semitones above the root.</summary>
    public static int[] Intervals(ChordKind kind) => kind switch
    {
        ChordKind.Minor => [0, 3, 7],
        ChordKind.Dominant7 => [0, 4, 7, 10],
        ChordKind.Major7 => [0, 4, 7, 11],
        ChordKind.Minor7 => [0, 3, 7, 10],
        ChordKind.Diminished => [0, 3, 6],
        ChordKind.Augmented => [0, 4, 8],
        ChordKind.Sus2 => [0, 2, 7],
        ChordKind.Sus4 => [0, 5, 7],
        _ => [0, 4, 7],
    };

    /// <summary>The pitch classes of a chord.</summary>
    public static HashSet<int> PitchClasses(int root, ChordKind kind) => Intervals(kind).Select(i => (root + i) % 12).ToHashSet();
}

/// <summary>A guitar chord shape: frets per string from the low E (-1 = not played), the fingers (0 = open), a barre.</summary>
sealed record GuitarChord(int Root, ChordKind Kind, int[] Frets, int[] Fingers, int? Barre = null)
{
    public string Name => Chords.Name(Root, Kind);

    /// <summary>The sounding pitches, low to high, on a guitar in standard tuning.</summary>
    public IEnumerable<int> Pitches(int[] openStrings) =>
        Frets.Select((f, s) => f < 0 ? -1 : openStrings[s] + f).Where(p => p >= 0);
}

/// <summary>The chords a beginner learns first: open shapes, and F and Hm with a barre.</summary>
static class GuitarChords
{
    const int x = -1;

    public static readonly IReadOnlyList<GuitarChord> All =
    [
        new(0, ChordKind.Major, [x, 3, 2, 0, 1, 0], [0, 3, 2, 0, 1, 0]),          // C
        new(2, ChordKind.Major, [x, x, 0, 2, 3, 2], [0, 0, 0, 1, 3, 2]),          // D
        new(4, ChordKind.Major, [0, 2, 2, 1, 0, 0], [0, 2, 3, 1, 0, 0]),          // E
        new(5, ChordKind.Major, [1, 3, 3, 2, 1, 1], [1, 3, 4, 2, 1, 1], Barre: 1), // F
        new(7, ChordKind.Major, [3, 2, 0, 0, 0, 3], [2, 1, 0, 0, 0, 3]),          // G
        new(9, ChordKind.Major, [x, 0, 2, 2, 2, 0], [0, 0, 1, 2, 3, 0]),          // A
        new(2, ChordKind.Minor, [x, x, 0, 2, 3, 1], [0, 0, 0, 2, 3, 1]),          // Dm
        new(4, ChordKind.Minor, [0, 2, 2, 0, 0, 0], [0, 2, 3, 0, 0, 0]),          // Em
        new(9, ChordKind.Minor, [x, 0, 2, 2, 1, 0], [0, 0, 2, 3, 1, 0]),          // Am
        new(11, ChordKind.Minor, [x, 2, 4, 4, 3, 2], [0, 1, 3, 4, 2, 1], Barre: 2), // Hm
        new(0, ChordKind.Dominant7, [x, 3, 2, 3, 1, 0], [0, 3, 2, 4, 1, 0]),      // C7
        new(2, ChordKind.Dominant7, [x, x, 0, 2, 1, 2], [0, 0, 0, 2, 1, 3]),      // D7
        new(4, ChordKind.Dominant7, [0, 2, 0, 1, 0, 0], [0, 2, 0, 1, 0, 0]),      // E7
        new(7, ChordKind.Dominant7, [3, 2, 0, 0, 0, 1], [3, 2, 0, 0, 0, 1]),      // G7
        new(9, ChordKind.Dominant7, [x, 0, 2, 0, 2, 0], [0, 0, 2, 0, 3, 0]),      // A7
        new(11, ChordKind.Dominant7, [x, 2, 1, 2, 0, 2], [0, 2, 1, 3, 0, 4]),     // H7
        new(9, ChordKind.Minor7, [x, 0, 2, 0, 1, 0], [0, 0, 2, 0, 1, 0]),         // Am7
        new(4, ChordKind.Minor7, [0, 2, 0, 0, 0, 0], [0, 2, 0, 0, 0, 0]),         // Em7
        new(2, ChordKind.Minor7, [x, x, 0, 2, 1, 1], [0, 0, 0, 2, 1, 1]),         // Dm7
        new(0, ChordKind.Major7, [x, 3, 2, 0, 0, 0], [0, 3, 2, 0, 0, 0]),         // Cmaj7
        new(5, ChordKind.Major7, [x, x, 3, 2, 1, 0], [0, 0, 3, 2, 1, 0]),         // Fmaj7
        new(2, ChordKind.Sus4, [x, x, 0, 2, 3, 3], [0, 0, 0, 1, 2, 3]),           // Dsus4
        new(2, ChordKind.Sus2, [x, x, 0, 2, 3, 0], [0, 0, 0, 1, 3, 0]),           // Dsus2
        new(9, ChordKind.Sus4, [x, 0, 2, 2, 3, 0], [0, 0, 1, 2, 3, 0]),           // Asus4
    ];

    /// <summary>The shape for a chord, if the library has one (the bass of "F/C" is ignored).</summary>
    public static GuitarChord? For(int root, ChordKind kind) => All.FirstOrDefault(c => c.Root == root % 12 && c.Kind == kind);
}
