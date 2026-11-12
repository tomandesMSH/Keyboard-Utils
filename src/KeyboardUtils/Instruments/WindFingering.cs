namespace KeyboardUtils;

/// <summary>One hole, key or valve: open, closed, or half (a pinched thumb, one of a double hole's two little holes).</summary>
enum KeyState { Open, Closed, Half }

enum KeyKind { Hole, Thumb, DoubleHole, Valve }

/// <summary>The recorder's two fingering systems: baroque (English, forked F) and German (simple F).</summary>
enum RecorderSystem { Baroque, German }

/// <summary>A hole / valve on the drawing, at (X, Y) in 0..1 of the chart's box.</summary>
sealed record WindKey(string Label, KeyKind Kind, float X, float Y);

/// <summary>How a wind instrument looks on the chart: its holes / valves.</summary>
sealed record WindChart(string InstrumentId, IReadOnlyList<WindKey> Keys);

/// <summary>A note's fingering: one state per key of the chart, and a hint ("zakryj spodek" ...).</summary>
sealed record WindFingering(int WrittenPitch, IReadOnlyList<KeyState> Keys, string? Hint);

/// <summary>
/// Fingering charts of the wind instruments, by the note as written. So far the soprano recorder (baroque and German
/// fingering) and the B♭ trumpet; flute, clarinet and saxophone come when there are charts to copy from.
/// </summary>
static class WindFingerings
{
    static readonly WindChart Recorder = new("recorder",
    [
        new("0", KeyKind.Thumb, 0.24f, 0.17f),
        new("1", KeyKind.Hole, 0.5f, 0.13f),
        new("2", KeyKind.Hole, 0.5f, 0.24f),
        new("3", KeyKind.Hole, 0.5f, 0.35f),
        new("4", KeyKind.Hole, 0.5f, 0.52f),
        new("5", KeyKind.Hole, 0.5f, 0.63f),
        new("6", KeyKind.DoubleHole, 0.5f, 0.74f),
        new("7", KeyKind.DoubleHole, 0.5f, 0.85f),
    ]);

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

    const int RecorderLowest = 60, TrumpetLowest = 54;

    /// <summary>The chart for an instrument that has one (recorder, trumpet); null otherwise.</summary>
    public static WindChart? ChartFor(Instrument instrument) => instrument.Id switch
    {
        "recorder" => Recorder,
        "trumpet" => Trumpet,
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
            default:
                return null;
        }
    }

    /// <summary>The written range a chart covers (for tests and the "out of range" text).</summary>
    public static (int Low, int High)? WrittenRange(Instrument instrument) => instrument.Id switch
    {
        "recorder" => (RecorderLowest, RecorderLowest + RecorderBaroque.Length - 1),
        "trumpet" => (TrumpetLowest, TrumpetLowest + TrumpetValves.Length - 1),
        _ => null,
    };
}
