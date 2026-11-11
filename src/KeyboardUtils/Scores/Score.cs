namespace KeyboardUtils;

/// <summary>A single note, already resolved to absolute time - no tempo map needed to interpret it.</summary>
sealed class ScoreNote
{
    public int Pitch { get; init; } // MIDI note number, 0-127 (60 = middle C)
    public double StartSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public int Velocity { get; init; } = 100;
}

/// <summary>One track (MIDI) or part (MusicXML) - what "Advanced: mute instrument" toggles.</summary>
sealed class ScorePart
{
    public required string Name { get; set; }
    public List<ScoreNote> Notes { get; set; } = [];
    public bool Muted { get; set; }

    /// <summary>0-1, the part's playback volume (its slider in the parts panel).</summary>
    public double Volume { get; set; } = 1;

    /// <summary>Which staff of the grand staff the part is written on; Auto splits by pitch at middle C.</summary>
    public PartStaff Staff { get; set; }
}

/// <summary>
/// Where a part goes on the grand staff. Auto suits songs; a hand of an exercise is pinned, so a left hand climbing
/// above middle C stays on the bass staff (with ledger lines) instead of jumping to the treble.
/// </summary>
enum PartStaff { Auto, Treble, Bass }

/// <summary>
/// The common representation both importers (MIDI, MusicXML) produce and the piano roll / playback /
/// learning mode / create mode all consume. Times are absolute seconds, so nothing downstream needs a tempo map.
/// </summary>
sealed class Score
{
    public required string Title { get; set; }
    public List<ScorePart> Parts { get; set; } = [];

    /// <summary>Tempo and time signature from the file, for the sheet music view. Null when unknown (recordings).</summary>
    public ScoreTiming? Timing { get; set; }

    /// <summary>Key signature from the file (-7 = seven flats ... 7 = seven sharps); null = unknown, guessed from the notes.</summary>
    public int? KeyFifths { get; set; }

    /// <summary>The key is the minor one of <see cref="KeyFifths"/> (A minor for 0): its raised seventh is spelled as such.</summary>
    public bool KeyMinor { get; set; }

    public double DurationSeconds => Parts.SelectMany(p => p.Notes)
        .Select(n => n.StartSeconds + n.DurationSeconds)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>Resolves which part a binding refers to: by remembered name first (survives reordering/reimport), then by index.</summary>
    public int? ResolvePartIndex(Binding binding)
    {
        if (binding.PartName != null)
        {
            int byName = Parts.FindIndex(p => p.Name == binding.PartName);
            if (byName >= 0)
                return byName;
        }
        return binding.PartIndex is { } index && index >= 0 && index < Parts.Count ? index : null;
    }
}
