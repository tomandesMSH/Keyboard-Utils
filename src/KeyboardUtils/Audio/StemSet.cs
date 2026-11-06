namespace KeyboardUtils;

/// <summary>One separately-exported audio track (e.g. a FL Studio mixer track, or one file from a stem pack).</summary>
sealed class StemTrack
{
    public required string Name { get; set; }
    public required string FilePath { get; set; }
    public bool Muted { get; set; }

    /// <summary>0-1, the track's playback volume (its slider in the parts panel).</summary>
    public double Volume { get; set; } = 1;
}

/// <summary>
/// A song as a set of synced audio stems rather than symbolic notes - there's no piano roll for this (no note
/// data to show), but the per-track mute works the same way as it does for MIDI/MusicXML parts.
/// </summary>
sealed class StemSet
{
    public required string Title { get; set; }
    public List<StemTrack> Tracks { get; set; } = [];

    /// <summary>Resolves which track a binding refers to: by remembered name first, then by index.</summary>
    public int? ResolvePartIndex(Binding binding)
    {
        if (binding.PartName != null)
        {
            int byName = Tracks.FindIndex(t => t.Name == binding.PartName);
            if (byName >= 0)
                return byName;
        }
        return binding.PartIndex is { } index && index >= 0 && index < Tracks.Count ? index : null;
    }
}
