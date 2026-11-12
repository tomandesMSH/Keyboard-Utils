namespace KeyboardUtils;

/// <summary>A staff's clef. Treble8 is the guitar's: a treble clef with an 8 below, sounding an octave lower.</summary>
enum Clef { Treble, Treble8, Alto, Tenor, Bass }

/// <summary>
/// The instrument the player practises: piano (the grand staff, as always) or one of the single-staff instruments.
/// Notes in a score are always the pitch that sounds; <see cref="Transpose"/> turns them into what the player reads -
/// a B♭ clarinet's part is written a whole tone higher than it sounds, an alto sax's a major sixth, a guitar's an
/// octave (its treble clef with the 8 below).
/// </summary>
/// <param name="Transpose">Written minus sounding, in semitones.</param>
/// <param name="Low">The lowest sounding note (MIDI).</param>
/// <param name="High">The highest sounding note (MIDI).</param>
/// <param name="GmProgram">General MIDI instrument for playback (0 = Acoustic Grand Piano).</param>
/// <param name="Sustained">Bowed or blown: notes change without a new attack (legato), and may have vibrato.</param>
/// <param name="Strings">The open strings, low to high (sounding), for the tuner; empty for winds and piano.</param>
sealed record Instrument(string Id, string Czech, string English, Clef Clef, int Transpose, int Low, int High,
    int GmProgram, bool Sustained, int[] Strings)
{
    public string Name => T(Czech, English);

    public bool IsPiano => Id == PianoId;

    public const string PianoId = "piano";

    public static readonly Instrument Piano = new(PianoId, "Piano", "Piano", Clef.Treble, 0, 21, 108, 0, false, []);

    /// <summary>Every instrument offered, piano first. Double bass is missing on purpose: its low notes are below what
    /// the microphone recognises (about F1).</summary>
    public static readonly IReadOnlyList<Instrument> All =
    [
        Piano,
        new("guitar", "Kytara", "Guitar", Clef.Treble8, 12, 40, 83, 24, false, [40, 45, 50, 55, 59, 64]),
        new("violin", "Housle", "Violin", Clef.Treble, 0, 55, 100, 40, true, [55, 62, 69, 76]),
        new("viola", "Viola", "Viola", Clef.Alto, 0, 48, 88, 41, true, [48, 55, 62, 69]),
        new("cello", "Violoncello", "Cello", Clef.Bass, 0, 36, 81, 42, true, [36, 43, 50, 57]),
        new("flute", "Příčná flétna", "Flute", Clef.Treble, 0, 60, 96, 73, true, []),
        new("recorder", "Zobcová flétna (sopránová)", "Recorder (soprano)", Clef.Treble, -12, 72, 98, 74, true, []),
        new("clarinet", "Klarinet in B", "Clarinet in B♭", Clef.Treble, 2, 50, 94, 71, true, []),
        new("trumpet", "Trubka in B", "Trumpet in B♭", Clef.Treble, 2, 52, 82, 56, true, []),
        new("altosax", "Altový saxofon in Es", "Alto saxophone in E♭", Clef.Treble, 9, 49, 80, 65, true, []),
    ];

    /// <summary>The instrument with this id; piano for null or one this version doesn't know.</summary>
    public static Instrument ById(string? id) => All.FirstOrDefault(i => i.Id == id) ?? Piano;

    /// <summary>A sounding pitch as the player reads it.</summary>
    public int Written(int sounding) => sounding + Transpose;

    /// <summary>A written pitch as it sounds.</summary>
    public int Sounding(int written) => written - Transpose;

    /// <summary>
    /// The key signature the player reads for music in <paramref name="concertFifths"/>: up a whole tone is two
    /// sharps more (concert F major → G major on a B♭ clarinet), up a major sixth three. Kept within six sharps or
    /// flats (C♯ major is written D♭ major).
    /// </summary>
    public int WrittenFifths(int concertFifths) => Transpose % 12 == 0 ? concertFifths : KeySignature.Transposed(concertFifths, Transpose);

    /// <summary>The concert key for music the player reads in <paramref name="writtenFifths"/>.</summary>
    public int ConcertFifths(int writtenFifths) => Transpose % 12 == 0 ? writtenFifths : KeySignature.Transposed(writtenFifths, -Transpose);

    /// <summary>The bottom line of a staff in this clef, as a diatonic step (octave × 7 + letter, C4 = 28).</summary>
    public static int BottomLine(Clef clef) => clef switch
    {
        Clef.Treble or Clef.Treble8 => 30, // E4 (as written - a guitar sounds E3)
        Clef.Alto => 24,                   // F3
        Clef.Tenor => 22,                  // D3
        Clef.Bass => 18,                   // G2
        _ => 30,
    };
}
