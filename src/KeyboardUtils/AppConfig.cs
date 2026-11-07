using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardUtils;

/// <summary>
/// Everything a macro keyboard key can trigger. More actions (mode switches, part mutes, ...) are added
/// as the corresponding feature lands; for now they are recognized but have no effect yet.
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
}

sealed class Binding
{
    public int Key { get; set; }
    public MacroAction Action { get; set; }

    /// <summary>Only used by <see cref="MacroAction.ToggleMutePart"/>: which part of the current song to mute.</summary>
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
        MacroAction.ToggleLearningMode => T("Přepnout režim učení", "Toggle learning mode"),
        MacroAction.ToggleCreateMode => T("Přepnout create mode", "Toggle create mode"),
        MacroAction.PlayNote => Pitch is { } pitch
            ? T($"Zahrát {NoteNames.Describe(pitch)}", $"Play {NoteNames.Describe(pitch)}")
            : T("Zahrát klávesu...", "Play note..."),
        _ => Action.ToString(),
    };
}

/// <summary>MIDI pitch (0-127) &lt;-&gt; scientific pitch notation (e.g. 60 -&gt; "C4").</summary>
static class NoteNames
{
    static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static string Describe(int pitch) => $"{Names[((pitch % 12) + 12) % 12]}{pitch / 12 - 1}";
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

    /// <summary>"No" to downloading the chord detection add-on: don't offer it again on every conversion.</summary>
    public bool ChordAddonDeclined { get; set; }

    /// <summary>Piano roll key width in px - the "keyboard size" slider.</summary>
    public int KeyboardKeyWidth { get; set; } = PianoRollControl.DefaultKeyWidth;

    /// <summary>Tempo the recording is written in when saved - MusicXML snaps notes to 16ths at this speed.</summary>
    public int CreateTempoBpm { get; set; } = 100;

    /// <summary>Names of the parts last practiced; matched by name against the next song (all parts if none match).</summary>
    public List<string> LearningPartNames { get; set; } = [];

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
