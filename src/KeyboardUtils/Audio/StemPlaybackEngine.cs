using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeyboardUtils;

/// <summary>
/// Plays a <see cref="StemSet"/> by mixing all tracks together with NAudio. Muting a track or changing its
/// volume just sets its reader's Volume - the mix keeps running, so there's no click or resync when changed live.
/// </summary>
sealed class StemPlaybackEngine : IDisposable
{
    List<AudioFileReader> readers = [];
    IReadOnlyList<StemTrack> tracks = [];
    HashSet<int> silenced = [];
    WaveOut? output;

    public event Action? Finished;

    public bool IsRunning => output?.PlaybackState == PlaybackState.Playing;
    public double CurrentSeconds => readers.Count > 0 ? readers[0].CurrentTime.TotalSeconds : 0;
    public double DurationSeconds => readers.Count > 0 ? readers.Max(r => r.TotalTime.TotalSeconds) : 0;

    /// <summary>Throws <see cref="InvalidDataException"/> if the tracks don't share a sample rate/channel count.</summary>
    public void Load(StemSet stemSet)
    {
        DisposeReaders();
        if (stemSet.Tracks.Count == 0)
            return;

        readers = stemSet.Tracks.Select(t => new AudioFileReader(t.FilePath)).ToList();
        var format = readers[0].WaveFormat;
        if (readers.Any(r => r.WaveFormat.SampleRate != format.SampleRate || r.WaveFormat.Channels != format.Channels))
        {
            DisposeReaders();
            throw new InvalidDataException(T("Všechny stopy musí mít stejnou vzorkovací frekvenci a počet kanálů.",
                "All stems must share the same sample rate and channel count."));
        }

        tracks = stemSet.Tracks;
        for (int i = 0; i < readers.Count; i++)
            ApplyVolume(i);

        var mixer = new MixingSampleProvider(readers.Cast<ISampleProvider>());
        output = new WaveOut();
        output.Init(mixer);
        output.PlaybackStopped += (_, _) => Finished?.Invoke();
    }

    /// <summary>Re-reads the track's <see cref="StemTrack.Muted"/> and <see cref="StemTrack.Volume"/> into the mix.</summary>
    public void ApplyVolume(int index)
    {
        if (index >= 0 && index < readers.Count && index < tracks.Count)
            readers[index].Volume = tracks[index].Muted || silenced.Contains(index) ? 0f : (float)Math.Clamp(tracks[index].Volume, 0, 1);
    }

    /// <summary>
    /// Mutes these tracks on top of their own mute setting, without changing it - learning mode silences the
    /// track the player is playing. An empty set restores normal playback.
    /// </summary>
    public void SetSilenced(IEnumerable<int> indices)
    {
        silenced = indices.ToHashSet();
        for (int i = 0; i < readers.Count; i++)
            ApplyVolume(i);
    }

    public void SeekTo(double seconds)
    {
        var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        foreach (var reader in readers)
            reader.CurrentTime = position < reader.TotalTime ? position : reader.TotalTime;
    }

    public void Play() => output?.Play();
    public void Pause() => output?.Pause();

    public void Stop()
    {
        output?.Stop();
        foreach (var reader in readers)
            reader.Position = 0;
    }

    void DisposeReaders()
    {
        output?.Dispose();
        output = null;
        foreach (var reader in readers)
            reader.Dispose();
        readers = [];
        tracks = [];
        silenced = [];
    }

    public void Dispose() => DisposeReaders();
}
