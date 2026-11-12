using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardUtils;

/// <summary>
/// Everything a macro keyboard key can trigger. Saved by name, so new ones can go anywhere - but keep the old names.
/// </summary>
enum MacroAction
{
    PlayPause,
    Stop,
    NextTrack,
    PreviousTrack,
    ToggleMutePart,
    ToggleLearningMode,
    ToggleCreateMode,
    PlayNote,
    SeekBack,
    SeekForward,
    SpeedDown,
    SpeedUp,
    SpeedReset,
    ToggleLoop,
    LoopStart,
    LoopEnd,
    ToggleMetronome,
    ToggleKeyLabels,
    SwitchView,
    ToggleListening,
    OpenSong,
    SeekBackFine,
    SeekForwardFine,
    ZoomIn,
    ZoomOut,
    KeyboardLarger,
    KeyboardSmaller,
    PartVolumeUp,
    PartVolumeDown,
    VolumeUp,
    VolumeDown,
    SongsFromFolder,
    RecentSongs,
    SaveRecording,
    LearningModeNext,
    LearningPartsNext,
    LearningSpeedNext,
    NoteInputNext,
    ShowWindow,
    MinimizeWindow,
    ToggleDarkMode,
    KeyEnter,
    KeyEscape,
    KeyTab,
    KeyShiftTab,
    KeyUp,
    KeyDown,
    KeyLeft,
    KeyRight,
    KeySpace,
    ShowPracticeHistory,
    MetronomeTap,
    MetronomeMeterNext,
    ToggleStandaloneMetronome,
    PracticeHardSpot,
    NoteTrainer,
    Exercise,
    Tuner,
}

/// <summary>How the actions behave when a key repeats fast - a knob (rotary encoder) sends a burst of one key.</summary>
static class MacroActions
{
    /// <summary>
    /// Actions that move by a step: a burst of presses is done as one move by that many steps. Everything else
    /// (on/off switches above all) runs once per burst, so a knob doesn't flip it back and forth.
    /// </summary>
    public static bool IsStep(MacroAction action) => action is
        MacroAction.SeekBack or MacroAction.SeekForward or MacroAction.SeekBackFine or MacroAction.SeekForwardFine or
        MacroAction.SpeedDown or MacroAction.SpeedUp or MacroAction.ZoomIn or MacroAction.ZoomOut or
        MacroAction.KeyboardLarger or MacroAction.KeyboardSmaller or MacroAction.PartVolumeUp or MacroAction.PartVolumeDown or
        MacroAction.VolumeUp or MacroAction.VolumeDown or MacroAction.NextTrack or MacroAction.PreviousTrack or
        MacroAction.KeyTab or MacroAction.KeyShiftTab or MacroAction.KeyUp or MacroAction.KeyDown or
        MacroAction.KeyLeft or MacroAction.KeyRight;

    /// <summary>The other direction of a step action (the other way of turning a knob); null if it has none.</summary>
    public static MacroAction? Opposite(MacroAction action) => action switch
    {
        MacroAction.SeekBack => MacroAction.SeekForward,
        MacroAction.SeekForward => MacroAction.SeekBack,
        MacroAction.SeekBackFine => MacroAction.SeekForwardFine,
        MacroAction.SeekForwardFine => MacroAction.SeekBackFine,
        MacroAction.SpeedDown => MacroAction.SpeedUp,
        MacroAction.SpeedUp => MacroAction.SpeedDown,
        MacroAction.ZoomIn => MacroAction.ZoomOut,
        MacroAction.ZoomOut => MacroAction.ZoomIn,
        MacroAction.KeyboardLarger => MacroAction.KeyboardSmaller,
        MacroAction.KeyboardSmaller => MacroAction.KeyboardLarger,
        MacroAction.PartVolumeUp => MacroAction.PartVolumeDown,
        MacroAction.PartVolumeDown => MacroAction.PartVolumeUp,
        MacroAction.VolumeUp => MacroAction.VolumeDown,
        MacroAction.VolumeDown => MacroAction.VolumeUp,
        MacroAction.NextTrack => MacroAction.PreviousTrack,
        MacroAction.PreviousTrack => MacroAction.NextTrack,
        MacroAction.KeyTab => MacroAction.KeyShiftTab,
        MacroAction.KeyShiftTab => MacroAction.KeyTab,
        MacroAction.KeyUp => MacroAction.KeyDown,
        MacroAction.KeyDown => MacroAction.KeyUp,
        MacroAction.KeyLeft => MacroAction.KeyRight,
        MacroAction.KeyRight => MacroAction.KeyLeft,
        _ => null,
    };

    /// <summary>Actions on one part of the song: offered once per part, the binding remembers which.</summary>
    public static bool IsPerPart(MacroAction action) =>
        action is MacroAction.ToggleMutePart or MacroAction.PartVolumeUp or MacroAction.PartVolumeDown;
}

sealed class Binding
{
    public int Key { get; set; }
    public MacroAction Action { get; set; }

    /// <summary>Only used by the per-part actions (<see cref="MacroActions.IsPerPart"/>): which part of the current song.</summary>
    public int? PartIndex { get; set; }

    /// <summary>Remembered part name, since part order can differ between songs/imports.</summary>
    public string? PartName { get; set; }

    /// <summary>Only used by <see cref="MacroAction.PlayNote"/>: the MIDI pitch (0-127) to play.</summary>
    public int? Pitch { get; set; }

    public string Describe() => Action switch
    {
        MacroAction.PlayPause => T("Přehrát / pauza", "Play / pause"),
        MacroAction.Stop => T("Zastavit", "Stop"),
        MacroAction.NextTrack => T("Další skladba", "Next track"),
        MacroAction.PreviousTrack => T("Předchozí skladba", "Previous track"),
        MacroAction.ToggleMutePart => PartName != null
            ? T($"Ztlumit/zapnout: {PartName}", $"Mute/unmute: {PartName}")
            : T("Ztlumit/zapnout část...", "Mute/unmute part..."),
        MacroAction.PartVolumeUp => T($"Hlasitost +10 %: {PartName}", $"Volume +10 %: {PartName}"),
        MacroAction.PartVolumeDown => T($"Hlasitost −10 %: {PartName}", $"Volume −10 %: {PartName}"),
        MacroAction.ToggleLearningMode => T("Režim učení spustit / ukončit", "Learning mode start / end"),
        MacroAction.ToggleCreateMode => T("Zapisovat noty spustit / ukončit", "Write notes start / end"),
        MacroAction.PlayNote => Pitch is { } pitch
            ? T($"Zahrát {NoteNames.Describe(pitch)}", $"Play {NoteNames.Describe(pitch)}")
            : T("Zahrát klávesu...", "Play note..."),
        MacroAction.SeekBack => T("O 5 s zpět", "Back 5 s"),
        MacroAction.SeekForward => T("O 5 s dopředu", "Forward 5 s"),
        MacroAction.SeekBackFine => T("O 1 s zpět", "Back 1 s"),
        MacroAction.SeekForwardFine => T("O 1 s dopředu", "Forward 1 s"),
        MacroAction.SpeedDown => T("Pomalejší přehrávání (−5 %)", "Slower playback (−5 %)"),
        MacroAction.SpeedUp => T("Rychlejší přehrávání (+5 %)", "Faster playback (+5 %)"),
        MacroAction.SpeedReset => T("Rychlost zpět na 100 %", "Speed back to 100 %"),
        MacroAction.ToggleLoop => T("Smyčka zapnout / vypnout", "Loop on / off"),
        MacroAction.LoopStart => T("Začátek smyčky = aktuální místo", "Loop start = current position"),
        MacroAction.LoopEnd => T("Konec smyčky = aktuální místo", "Loop end = current position"),
        MacroAction.ToggleMetronome => T("Metronom zapnout / vypnout", "Metronome on / off"),
        MacroAction.ToggleKeyLabels => T("Popisky kláves zapnout / vypnout", "Key labels on / off"),
        MacroAction.SwitchView => T("Přepnout zobrazení (klávesy / noty / obojí)", "Switch the view (keyboard / sheet / both)"),
        MacroAction.ZoomIn => T("Noty větší", "Sheet music larger"),
        MacroAction.ZoomOut => T("Noty menší", "Sheet music smaller"),
        MacroAction.KeyboardLarger => T("Klávesnice větší", "Keyboard larger"),
        MacroAction.KeyboardSmaller => T("Klávesnice menší", "Keyboard smaller"),
        MacroAction.VolumeUp => T("Hlasitost všech částí +10 %", "Volume of all parts +10 %"),
        MacroAction.VolumeDown => T("Hlasitost všech částí −10 %", "Volume of all parts −10 %"),
        MacroAction.ToggleListening => T("Poslouchat zapnout / vypnout", "Listen on / off"),
        MacroAction.OpenSong => T("Otevřít skladbu (dialog)", "Open song (dialog)"),
        MacroAction.SongsFromFolder => T("Moje složka se skladbami", "My songs folder"),
        MacroAction.RecentSongs => T("Nedávné skladby", "Recent songs"),
        MacroAction.SaveRecording => T("Uložit nahrávku (MIDI, bez dialogu)", "Save the recording (MIDI, no dialog)"),
        MacroAction.LearningModeNext => T("Učení: čekat na notu / v tempu", "Learning: wait for note / in tempo"),
        MacroAction.LearningPartsNext => T("Učení: co hraju (všechno / jednotlivé části)", "Learning: what I play (all / each part)"),
        MacroAction.LearningSpeedNext => T("Učení: rychlost (25 / 50 / 75 / 100 %)", "Learning: speed (25 / 50 / 75 / 100 %)"),
        MacroAction.NoteInputNext => T("Vstup not: další (mikrofon, zvuk z počítače, MIDI...)", "Note input: next (microphone, computer sound, MIDI...)"),
        MacroAction.ShowWindow => T("Okno appky do popředí", "Bring the app window to the front"),
        MacroAction.MinimizeWindow => T("Minimalizovat okno", "Minimize the window"),
        MacroAction.ToggleDarkMode => T("Tmavý / světlý režim", "Dark / light mode"),
        MacroAction.KeyEnter => T("Potvrdit (Enter)", "Confirm (Enter)"),
        MacroAction.KeyEscape => T("Zrušit / zavřít (Esc)", "Cancel / close (Esc)"),
        MacroAction.KeyTab => T("Další volba (Tab)", "Next option (Tab)"),
        MacroAction.KeyShiftTab => T("Předchozí volba (Shift + Tab)", "Previous option (Shift + Tab)"),
        MacroAction.KeyUp => T("Šipka nahoru", "Arrow up"),
        MacroAction.KeyDown => T("Šipka dolů", "Arrow down"),
        MacroAction.KeyLeft => T("Šipka doleva", "Arrow left"),
        MacroAction.KeyRight => T("Šipka doprava", "Arrow right"),
        MacroAction.KeySpace => T("Zaškrtnout / stisknout (mezerník)", "Check / press (Space)"),
        MacroAction.ShowPracticeHistory => T("Historie cvičení této skladby", "Practice history of this song"),
        MacroAction.MetronomeTap => T("Metronom: naťukat tempo", "Metronome: tap the tempo"),
        MacroAction.MetronomeMeterNext => T("Metronom: další takt (2/4, 3/4, 4/4, 5/4, 6/8)", "Metronome: next time signature (2/4, 3/4, 4/4, 5/4, 6/8)"),
        MacroAction.ToggleStandaloneMetronome => T("Metronom bez podkladu zapnout / vypnout", "Metronome without a song on / off"),
        MacroAction.PracticeHardSpot => T("Procvičit těžké místo (smyčka + učení)", "Practise the hard spot (loop + learning)"),
        MacroAction.NoteTrainer => T("Trénink čtení not", "Note-reading trainer"),
        MacroAction.Exercise => T("Stupnice a rozcvičky", "Scales and warm-ups"),
        MacroAction.Tuner => T("Ladička", "Tuner"),
        _ => Action.ToString(),
    };
}

/// <summary>What the song area shows: the falling-notes keyboard, sheet music, or both side by side.</summary>
enum SongView
{
    Keyboard,
    Sheet,
    SheetAndKeyboard,
}

/// <summary>How notes are named: the Czech school's c1, fis2, C, H1 or the scientific C4, F♯5.</summary>
enum NoteNaming { School, Scientific }

/// <summary>A MIDI pitch's name, the way chosen in Settings (<see cref="Naming"/>): school or scientific.</summary>
static class NoteNames
{
    /// <summary>Set at start from the settings (the app's default is school names); scientific until then.</summary>
    public static NoteNaming Naming { get; set; } = NoteNaming.Scientific;

    static readonly string[] Czech = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "B", "H"];
    static readonly string[] English = ["C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];
    static readonly string[] CzechSchool = ["c", "cis", "d", "dis", "e", "f", "fis", "g", "gis", "a", "b", "h"];
    static readonly string[] EnglishSchool = ["c", "c♯", "d", "e♭", "e", "f", "f♯", "g", "a♭", "a", "b♭", "b"];

    static int PitchClass(int midi) => ((midi % 12) + 12) % 12;

    public static string Name(int midi) => Name(midi, Naming);

    /// <summary>
    /// School: c1 = middle C (60), c2, c3...; c = the small octave (48), C = the great octave (36), C1 = contra (24),
    /// C2 = subcontra (12). Scientific: C4 = middle C. Czech names say H for B and B for B♭.
    /// </summary>
    public static string Name(int midi, NoteNaming naming)
    {
        int pc = PitchClass(midi), octave = midi / 12 - 1;
        if (naming == NoteNaming.Scientific)
            return T(Czech[pc], English[pc]) + octave;
        string name = T(CzechSchool[pc], EnglishSchool[pc]);
        return octave switch
        {
            >= 4 => name + (octave - 3),                                     // c1, c2, ...
            3 => name,                                                       // the small octave: c
            2 => char.ToUpperInvariant(name[0]) + name[1..],                 // the great octave: C
            _ => char.ToUpperInvariant(name[0]) + name[1..] + (2 - octave),  // contra C1, subcontra C2
        };
    }

    /// <summary>Just the letter (with ♯ / ♭), upper case: string names, key labels. Czech H for B.</summary>
    public static string Letter(int pitchClass) => T(Czech[PitchClass(pitchClass)], English[PitchClass(pitchClass)]);

    /// <summary>The name as used before the setting existed (kept for the callers' wording).</summary>
    public static string Describe(int pitch) => Name(pitch);
}

sealed class AppConfig
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "config.json");

    public string? MacroKeyboardHardwareId { get; set; }
    public int MacroKeyboardDevice { get; set; }
    public bool MacroEnabled { get; set; } = true;

    /// <summary>Null = system default MIDI output.</summary>
    public string? MidiOutputDevice { get; set; }

    /// <summary>The player's instrument (<see cref="KeyboardUtils.Instrument.Id"/>): its clef, transposition, range and sound.</summary>
    public string Instrument { get; set; } = KeyboardUtils.Instrument.PianoId;

    /// <summary>The instruments I play, one tab each (ids); null in configs from before 1.7.0 = just <see cref="Instrument"/>.</summary>
    public List<string>? MyInstruments { get; set; }

    /// <summary>The tuner's reference: A4 in Hz.</summary>
    public double TunerA4 { get; set; } = 440;

    /// <summary>Where played notes are heard from (learning mode, create mode, the "Listen" button).</summary>
    public NoteInputKind NoteInput { get; set; } = NoteInputKind.Microphone;

    /// <summary>Null = system default recording device.</summary>
    public string? MicrophoneDevice { get; set; }

    /// <summary>Microphone frames quieter than this (dBFS) count as silence. Set in the calibration dialog.</summary>
    public double MicrophoneThresholdDb { get; set; } = -45;

    /// <summary>
    /// Like <see cref="MicrophoneThresholdDb"/> for the computer's own sound. Digital audio has no room noise, so it
    /// can sit lower than a microphone's; there's no calibration dialog for it yet.
    /// </summary>
    public double SystemAudioThresholdDb { get; set; } = -50;

    /// <summary>Null = first available MIDI input.</summary>
    public string? MidiInputDevice { get; set; }

    /// <summary>Last learning mode settings - reused as-is when learning is started from a macro key.</summary>
    public LearningMode LearningMode { get; set; } = LearningMode.WaitForNote;
    public int LearningSpeedPercent { get; set; } = 100;
    public bool LearningAccompaniment { get; set; } = true;

    /// <summary>With an audio input (microphone, computer sound): require every note of a chord, checked in the spectrum.</summary>
    public bool LearningCheckChords { get; set; } = true;

    /// <summary>Learning a loop: each pass played well enough makes the next one a bit faster (<see cref="SpeedRamp"/>).</summary>
    public bool LearningRampUp { get; set; }

    /// <summary>Minutes of learning a day to aim for (<see cref="PracticeGoal"/>); 0 = no goal, nothing shown.</summary>
    public int DailyGoalMinutes { get; set; } = 20;

    /// <summary>The last scale / warm-up made (<see cref="ExerciseGenerator"/>), offered again next time.</summary>
    public ExerciseSpec Exercise { get; set; } = new();

    /// <summary>Note-reading trainer: the level picked last and the best round (correct notes of 20) per level.</summary>
    public NoteQuizLevel NoteQuizLevel { get; set; } = NoteQuizLevel.Treble;
    public Dictionary<NoteQuizLevel, int> NoteQuizBest { get; set; } = [];

    /// <summary>Seconds to answer each note; 0 = no limit.</summary>
    public int NoteQuizSeconds { get; set; }

    /// <summary>Note-reading trainer: played notes (microphone / MIDI) count too. Off by default: just clicking.</summary>
    public bool NoteQuizPlay { get; set; }

    /// <summary>Mistakes per written note (<see cref="QuizNote.Key"/>): those come up more often (<see cref="NoteQuiz.Weakness"/>).</summary>
    public Dictionary<string, int> NoteQuizWeakness { get; set; } = [];

    /// <summary>Learning mode records the player (microphone: sound, MIDI / on-screen keys: notes) to listen back.</summary>
    public bool LearningRecordTake { get; set; }

    /// <summary>"No" to downloading the chord detection add-on: don't offer it again on every conversion.</summary>
    public bool ChordAddonDeclined { get; set; }

    /// <summary>Piano roll key width in px - the "keyboard size" slider.</summary>
    public int KeyboardKeyWidth { get; set; } = PianoRollControl.DefaultKeyWidth;

    /// <summary>Note names on every white key (C, D, E...), not just on the Cs.</summary>
    public bool AllKeyLabels { get; set; }

    /// <summary>Clicks on every beat during playback and learning in tempo, with a bar of count-in.</summary>
    public bool Metronome { get; set; }

    /// <summary>The metronome on its own: Play runs just the clicks, at <see cref="MetronomeBpm"/>.</summary>
    public bool MetronomeStandalone { get; set; }

    public int MetronomeBpm { get; set; } = 100;

    /// <summary>Time signature of the metronome on its own, one of <see cref="StandaloneMetronome.Meters"/>.</summary>
    public string MetronomeMeter { get; set; } = "4/4";

    /// <summary>The metronome's options (without a song, tempo, detection, time signature) unfolded under it.</summary>
    public bool MetronomeOptionsShown { get; set; } = true;

    /// <summary>Ask GitHub for a newer release at start (at most once a day).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public DateTime? LastUpdateCheck { get; set; }

    public SongView SongView { get; set; } = SongView.Keyboard;

    /// <summary>Share of the width the keyboard takes when shown next to the sheet music (0-1).</summary>
    public double SplitRatio { get; set; } = 0.47;

    public bool DarkMode { get; set; }

    /// <summary>What a macro key or a shortcut did, shown big over the song for a moment.</summary>
    public bool BigNotices { get; set; } = true;

    /// <summary>The Muzio menu in the menu bar (it can be hidden for those who don't use Muzio).</summary>
    public bool ShowMuzio { get; set; } = true;

    /// <summary>The recorder's fingering chart: baroque (most recorders and schools) or German.</summary>
    public RecorderSystem RecorderFingering { get; set; }

    /// <summary>Guitar: the TAB (string and fret numbers) under the sheet music.</summary>
    public bool GuitarTab { get; set; } = true;

    /// <summary>Chord symbols above the notes (the file's; guessed ones for the guitar).</summary>
    public bool ChordSymbols { get; set; } = true;

    /// <summary>Note names: the Czech school's (c1, fis2, C, H1) or scientific (C4, F♯5).</summary>
    public NoteNaming NoteNaming { get; set; } = NoteNaming.School;

    /// <summary>The name on the teacher's report (remembered from last time).</summary>
    public string? StudentName { get; set; }

    /// <summary>Tempo the recording is written in when saved - MusicXML snaps notes to 16ths at this speed.</summary>
    public int CreateTempoBpm { get; set; } = 100;

    /// <summary>Names of the parts last practiced; matched by name against the next song (all parts if none match).</summary>
    public List<string> LearningPartNames { get; set; } = [];

    /// <summary>Its songs are a playlist browsed with next / previous song - no file dialog needed.</summary>
    public string? SongsFolder { get; set; }

    /// <summary>Last opened song files, the newest first.</summary>
    public List<string> RecentSongs { get; set; } = [];

    public const int MaxRecentSongs = 10;

    /// <summary>Favorites and tags for songs, on top of <see cref="RecentSongs"/> and <see cref="SongsFolder"/>.</summary>
    public List<LibraryEntry> Library { get; set; } = [];

    /// <summary>Null until the user picks one on first start.</summary>
    public AppLanguage? Language { get; set; }
    public List<Binding> Bindings { get; set; } = [];

    public INoteSource CreateNoteSource() => NoteInput switch
    {
        NoteInputKind.Midi => new MidiNoteSource(MidiInputDevice),
        NoteInputKind.ScreenKeyboard => new ScreenKeyboardNoteSource(),
        NoteInputKind.SystemAudio => new SystemAudioNoteSource(SystemAudioThresholdDb),
        _ => new MicrophoneNoteSource(MicrophoneDevice, MicrophoneThresholdDb),
    };

    [JsonIgnore]
    public MacroTarget? MacroTarget => MacroKeyboardHardwareId == null
        ? null
        : new MacroTarget(MacroKeyboardHardwareId, MacroKeyboardDevice);

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            File.Copy(FilePath, FilePath + ".broken", overwrite: true);
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Writes just the bindings; the keyboard and other settings are specific to this PC.</summary>
    public void ExportBindings(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new BindingsFile { Bindings = Bindings }, JsonOptions));

    /// <summary>Reads bindings from an export or a whole config.json. Throws JsonException or IOException.</summary>
    public static List<Binding> ImportBindings(string path)
    {
        var file = JsonSerializer.Deserialize<BindingsFile>(File.ReadAllText(path), JsonOptions);
        if (file?.Bindings is not { } bindings)
            throw new JsonException("No bindings found.");
        // One binding per key; the last one wins, like when adding keys in the app.
        return bindings.GroupBy(b => b.Key).Select(g => g.Last()).ToList();
    }

    sealed class BindingsFile
    {
        public List<Binding>? Bindings { get; set; }
    }
}
