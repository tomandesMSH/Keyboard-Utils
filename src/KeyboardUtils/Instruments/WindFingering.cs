namespace KeyboardUtils;

/// <summary>One hole, key or valve: open, closed, or half (a pinched thumb, one of a double hole's two little holes).</summary>
enum KeyState { Open, Closed, Half }

/// <summary>A tone hole, the thumb's, a recorder's double hole, a trumpet valve, a flute key (Key: on the tube; Lever: a pinky / thumb lever beside it).</summary>
enum KeyKind { Hole, Thumb, DoubleHole, Valve, Key, Lever }

/// <summary>The recorder's two fingering systems: baroque (English, forked F) and German (simple F).</summary>
enum RecorderSystem { Baroque, German }

/// <summary>
/// A hole / key / valve: its id, how it's played, where it is (X, Y: 0..1 along / across the instrument's drawing),
/// and the finger that plays it ("L1" = left index, "P4" = right little finger; Czech "P" = pravá).
/// </summary>
sealed record WindKey(string Label, KeyKind Kind, float X, float Y, string Finger = "");

/// <summary>How a wind instrument looks on the chart: its holes / valves.</summary>
sealed record WindChart(string InstrumentId, IReadOnlyList<WindKey> Keys);

/// <summary>A note's fingering: one state per key of the chart, and a hint ("zakryj spodek" ...).</summary>
sealed record WindFingering(int WrittenPitch, IReadOnlyList<KeyState> Keys, string? Hint);

/// <summary>
/// Fingering charts of the wind instruments, by the note as written: the soprano recorder (baroque and German
/// fingering), the flute and the B♭ trumpet. Clarinet and saxophone come when there are charts to copy from.
/// </summary>
static class WindFingerings
{
    static readonly WindChart Recorder = new("recorder",
    [
        // Y: along the recorder from the mouthpiece (0) to the bell (1); the thumb hole is at the back.
        new("0", KeyKind.Thumb, 0.5f, 0.33f, "L0"),
        new("1", KeyKind.Hole, 0.5f, 0.33f, "L1"),
        new("2", KeyKind.Hole, 0.5f, 0.40f, "L2"),
        new("3", KeyKind.Hole, 0.5f, 0.47f, "L3"),
        new("4", KeyKind.Hole, 0.5f, 0.57f, "P1"),
        new("5", KeyKind.Hole, 0.5f, 0.64f, "P2"),
        new("6", KeyKind.DoubleHole, 0.5f, 0.71f, "P3"),
        new("7", KeyKind.DoubleHole, 0.53f, 0.83f, "P4"),
    ]);

    // X: along the flute from the lip plate (0) to the foot (1); Y: 0 on the tube, 1 below it (levers, the thumb's key).
    static readonly WindChart Flute = new("flute",
    [
        new("T", KeyKind.Thumb, 0.37f, 1f, "L0"),
        new("L1", KeyKind.Key, 0.37f, 0f, "L1"),
        new("L2", KeyKind.Key, 0.45f, 0f, "L2"),
        new("L3", KeyKind.Key, 0.52f, 0f, "L3"),
        new("G#", KeyKind.Lever, 0.565f, 1f, "L4"),
        new("R1", KeyKind.Key, 0.63f, 0f, "P1"),
        new("R2", KeyKind.Key, 0.70f, 0f, "P2"),
        new("R3", KeyKind.Key, 0.77f, 0f, "P3"),
        new("Eb", KeyKind.Lever, 0.82f, 1f, "P4"),
        new("C#", KeyKind.Lever, 0.905f, 1f, "P4"),
        new("C", KeyKind.Lever, 0.95f, 1f, "P4"),
    ]);

    /// <summary>
    /// Flute (Boehm), written = sounding, c1 (60) to a3 (93): the keys pressed. The basic fingerings of the Woodwind
    /// Fingering Guide (wfg.woodwind.org, flute basic chart); the notes above a3 need trill keys and are left out.
    /// </summary>
    static readonly string[] FluteKeys =
    [
        "T L1 L2 L3 R1 R2 R3 C", "T L1 L2 L3 R1 R2 R3 C#", "T L1 L2 L3 R1 R2 R3", "T L1 L2 L3 R1 R2 R3 Eb", // c1 .. dis1
        "T L1 L2 L3 R1 R2 Eb", "T L1 L2 L3 R1 Eb", "T L1 L2 L3 R3 Eb", "T L1 L2 L3 Eb",                     // e1 .. g1
        "T L1 L2 L3 G# Eb", "T L1 L2 Eb", "T L1 R1 Eb", "T L1 Eb",                                          // gis1 .. h1
        "L1 Eb", "Eb", "T L2 L3 R1 R2 R3", "T L2 L3 R1 R2 R3 Eb",                                           // c2 .. dis2
        "T L1 L2 L3 R1 R2 Eb", "T L1 L2 L3 R1 Eb", "T L1 L2 L3 R3 Eb", "T L1 L2 L3 Eb",                     // e2 .. g2 (overblown)
        "T L1 L2 L3 G# Eb", "T L1 L2 Eb", "T L1 R1 Eb", "T L1 Eb",                                          // gis2 .. h2
        "L1 Eb", "Eb", "T L2 L3 Eb", "T L1 L2 L3 G# R1 R2 R3 Eb",                                           // c3 .. dis3
        "T L1 L2 R1 R2 Eb", "T L1 L3 R1 Eb", "T L1 L3 R3 Eb", "L1 L2 L3 Eb",                                // e3 .. g3
        "L2 L3 G# Eb", "T L2 R1 Eb",                                                                        // gis3, a3
    ];

    static readonly WindChart Trumpet = new("trumpet",
    [
        new("1", KeyKind.Valve, 0.25f, 0.5f),
        new("2", KeyKind.Valve, 0.5f, 0.5f),
        new("3", KeyKind.Valve, 0.75f, 0.5f),
    ]);

    /// <summary>
    /// Soprano recorder, written c1 (60) up: thumb, holes 1-7; 1 = closed, 0 = open, 2 = half (pinched thumb, or one
    /// of the double hole's two). Main fingerings from blockfloetengriffe.de (Baroque "Basic fingerings", German "All
    /// fingerings", first of each) and the Dolmetsch text chart (baroque second octave).
    /// </summary>
    static readonly string[] RecorderBaroque =
    [
        "11111111", "11111112", "11111110", "11111120", "11111100", "11111011", "11110110", "11110000", // c1 .. g1
        "11101120", "11100000", "11011000", "11000000",                                                 // gis1 .. h1
        "10100000", "01100000", "00100000", "00111110", "21111100", "21111010", "21110100", "21110000", // c2 .. g2
        "21101000", "21100000", "21100110", "21101100",                                                 // gis2 .. h2
        "21001100", "21011010", "21011010",                                                             // c3, cis3, d3
    ];

    static readonly string[] RecorderGerman =
    [
        "11111111", "11111112", "11111110", "11111120", "11111100", "11111000", "11110111", "11110000",
        "11101100", "11100000", "11010110", "11000000",
        "10100000", "01100000", "00100000", "00111110", "21111100", "21111000", "21110100", "21110000",
        "21110111", "21100000", "21101110", "21101100",
        "21001100", "21011010", "21011012",
    ];

    /// <summary>B♭ trumpet, written f#3 (54) to c6 (84): the valves pressed (the lip finds the partial).</summary>
    static readonly string[] TrumpetValves =
    [
        "123", "13", "23", "12", "1", "2",                          // fis3 .. h3
        "", "123", "13", "23", "12", "1", "2",                      // c4 .. fis4
        "", "23", "12", "1", "2",                                   // g4 .. h4
        "", "12", "1", "2", "", "1", "2",                           // c5 .. fis5
        "", "23", "12", "1", "2", "",                               // g5 .. c6
    ];

    const int RecorderLowest = 60, TrumpetLowest = 54, FluteLowest = 60;

    /// <summary>The chart for an instrument that has one (recorder, trumpet); null otherwise.</summary>
    public static WindChart? ChartFor(Instrument instrument) => instrument.Id switch
    {
        "recorder" => Recorder,
        "trumpet" => Trumpet,
        "flute" => Flute,
        _ => null,
    };

    /// <summary>The fingering of a sounding pitch on the instrument; null outside its chart.</summary>
    public static WindFingering? For(Instrument instrument, int soundingPitch, RecorderSystem system = RecorderSystem.Baroque)
    {
        int written = instrument.Written(soundingPitch);
        switch (instrument.Id)
        {
            case "recorder":
            {
                var table = system == RecorderSystem.German ? RecorderGerman : RecorderBaroque;
                int i = written - RecorderLowest;
                if (i < 0 || i >= table.Length)
                    return null;
                // c#3 needs the bell closed too (against the knee).
                string? hint = written == 85 ? T("zakryj spodní otvor (o koleno)", "close the bell (against your knee)") : null;
                return new WindFingering(written, table[i].Select(c => c switch { '1' => KeyState.Closed, '2' => KeyState.Half, _ => KeyState.Open }).ToArray(), hint);
            }
            case "trumpet":
            {
                int i = written - TrumpetLowest;
                if (i < 0 || i >= TrumpetValves.Length)
                    return null;
                string valves = TrumpetValves[i];
                return new WindFingering(written, Trumpet.Keys.Select(k => valves.Contains(k.Label) ? KeyState.Closed : KeyState.Open).ToArray(),
                    valves.Length == 0 ? T("bez ventilů", "open") : null);
            }
            case "flute":
            {
                int i = written - FluteLowest;
                if (i < 0 || i >= FluteKeys.Length)
                    return null;
                var pressed = FluteKeys[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                // The same fingering an octave apart (e1 / e2): the breath decides.
                string? hint = i is >= 16 and <= 25 ? T("silněji (přefouknout)", "blow harder (overblow)") : null;
                return new WindFingering(written, Flute.Keys.Select(k => pressed.Contains(k.Label) ? KeyState.Closed : KeyState.Open).ToArray(), hint);
            }
            default:
                return null;
        }
    }

    /// <summary>The written range a chart covers (for tests and the "out of range" text).</summary>
    public static (int Low, int High)? WrittenRange(Instrument instrument) => instrument.Id switch
    {
        "recorder" => (RecorderLowest, RecorderLowest + RecorderBaroque.Length - 1),
        "trumpet" => (TrumpetLowest, TrumpetLowest + TrumpetValves.Length - 1),
        "flute" => (FluteLowest, FluteLowest + FluteKeys.Length - 1),
        _ => null,
    };

    /// <summary>
    /// The notes (sounding, low to high) a fingering plays - one on the recorder, several an octave or a partial
    /// apart on the flute and trumpet, where the breath picks; empty when it's no fingering of the chart.
    /// </summary>
    public static List<int> NotesFor(Instrument instrument, IReadOnlyList<KeyState> keys, RecorderSystem system = RecorderSystem.Baroque)
    {
        var notes = new List<int>();
        if (WrittenRange(instrument) is not var (low, high))
            return notes;
        for (int written = low; written <= high; written++)
        {
            int sounding = instrument.Sounding(written);
            if (For(instrument, sounding, system) is { } fingering && fingering.Keys.SequenceEqual(keys))
                notes.Add(sounding);
        }
        return notes;
    }
}
