using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace KeyboardUtils;

/// <summary>
/// Plays a <see cref="Score"/> by synthesizing it into MIDI notes (one channel per part) and driving a DryWetMidi
/// <see cref="Playback"/>. Absolute times in the score are converted to ticks via a fixed default tempo map -
/// the original tempo markings are already baked into each note's seconds, so this is just a unit conversion.
/// </summary>
sealed class PlaybackEngine : IDisposable
{
    static readonly TempoMap TempoMap = TempoMap.Default;

    OutputDevice? outputDevice;
    Playback? playback;

    // Swapped wholesale so the NoteCallback (called on the playback thread) never sees a half-updated set.
    volatile IReadOnlySet<int> mutedChannels = new HashSet<int>();

    public event Action? Finished;

    public bool IsRunning => playback?.IsRunning ?? false;

    public double CurrentSeconds => playback?.GetCurrentTime<MetricTimeSpan>().TotalSeconds ?? 0;

    /// <summary>Null picks the system's default output (e.g. Microsoft GS Wavetable Synth).</summary>
    public void SetOutputDevice(string? deviceName)
    {
        outputDevice?.Dispose();
        outputDevice = null;
        var all = OutputDevice.GetAll().ToList();
        var chosen = deviceName != null ? all.FirstOrDefault(d => d.Name == deviceName) : all.FirstOrDefault();
        outputDevice = chosen ?? all.FirstOrDefault();
    }

    public static IReadOnlyList<string> GetOutputDeviceNames() => OutputDevice.GetAll().Select(d => d.Name).ToList();

    public void Load(Score score)
    {
        playback?.Dispose();
        playback = null;
        mutedChannels = new HashSet<int>();

        var notes = new List<Note>();
        for (int partIndex = 0; partIndex < score.Parts.Count; partIndex++)
        {
            var channel = (FourBitNumber)(partIndex % 16);
            foreach (var note in score.Parts[partIndex].Notes)
            {
                long time = TimeConverter.ConvertFrom(new MetricTimeSpan(TimeSpan.FromSeconds(note.StartSeconds)), TempoMap);
                long length = TimeConverter.ConvertFrom(new MetricTimeSpan(TimeSpan.FromSeconds(note.DurationSeconds)), TempoMap);
                notes.Add(new Note((SevenBitNumber)Math.Clamp(note.Pitch, 0, 127), Math.Max(1, length), time)
                {
                    Channel = channel,
                    Velocity = (SevenBitNumber)Math.Clamp(note.Velocity, 1, 127),
                });
            }
        }

        playback = outputDevice != null
            ? new Playback(notes, TempoMap, outputDevice, new PlaybackSettings())
            : new Playback(notes, TempoMap, new PlaybackSettings());
        playback.NoteCallback = (data, _, _, _) => mutedChannels.Contains(data.Channel) ? NotePlaybackData.SkipNote : data;
        playback.Finished += (_, _) => Finished?.Invoke();
    }

    public void SetPartMuted(int partIndex, bool muted)
    {
        int channel = partIndex % 16;
        var next = new HashSet<int>(mutedChannels);
        if (muted)
            next.Add(channel);
        else
            next.Remove(channel);
        mutedChannels = next;
    }

    /// <summary>Fires a single note at the output device, independent of score playback. Fire-and-forget.</summary>
    public void PlayNote(int pitch, int durationMs = 400)
    {
        if (outputDevice is not { } device)
            return;
        var noteNumber = (SevenBitNumber)Math.Clamp(pitch, 0, 127);
        device.SendEvent(new NoteOnEvent(noteNumber, (SevenBitNumber)100));
        _ = Task.Delay(durationMs).ContinueWith(_ => device.SendEvent(new NoteOffEvent(noteNumber, (SevenBitNumber)0)));
    }

    /// <summary>1 = as written, 0.5 = half speed. <see cref="CurrentSeconds"/> stays in score time either way.</summary>
    public double Speed
    {
        get => playback?.Speed ?? 1;
        set { if (playback != null) playback.Speed = value; }
    }

    public void Play() => playback?.Start();
    public void Pause() => playback?.Stop();

    public void Stop()
    {
        playback?.Stop();
        playback?.MoveToStart();
    }

    public void Dispose()
    {
        playback?.Dispose();
        outputDevice?.Dispose();
    }
}
