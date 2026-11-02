using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeyboardUtils;

/// <summary>
/// Plays a <see cref="StemSet"/> by mixing all tracks together with NAudio. Muting a track just sets its
/// reader's Volume to 0 - the mix keeps running, so there's no click or resync when toggling mute live.
/// </summary>
sealed class StemPlaybackEngine : IDisposable
{
    List<AudioFileReader> readers = [];
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

        for (int i = 0; i < readers.Count; i++)
            readers[i].Volume = stemSet.Tracks[i].Muted ? 0f : 1f;

        var mixer = new MixingSampleProvider(readers.Cast<ISampleProvider>());
        output = new WaveOut();
        output.Init(mixer);
        output.PlaybackStopped += (_, _) => Finished?.Invoke();
    }

    public void SetTrackMuted(int index, bool muted)
    {
        if (index >= 0 && index < readers.Count)
            readers[index].Volume = muted ? 0f : 1f;
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
    }

    public void Dispose() => DisposeReaders();
}
