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
}

sealed class Binding
{
    public int Key { get; set; }
    public MacroAction Action { get; set; }

    /// <summary>Only used by <see cref="MacroAction.ToggleMutePart"/>: which part of the current song to mute.</summary>
    public int? PartIndex { get; set; }

    /// <summary>Remembered part name, since part order can differ between songs/imports.</summary>
    public string? PartName { get; set; }

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
        _ => Action.ToString(),
    };
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

    /// <summary>Null until the user picks one on first start.</summary>
    public AppLanguage? Language { get; set; }
    public List<Binding> Bindings { get; set; } = [];

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
