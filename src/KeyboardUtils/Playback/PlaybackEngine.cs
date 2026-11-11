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

    /// <summary>A MIDI port (Windows' synth, a digital piano...) or the piano sound pack (<see cref="SoundFontOutputDevice"/>).</summary>
    IOutputDevice? outputDevice;
    Playback? playback;

    // Swapped wholesale so the NoteCallback (called on the playback thread) never sees a half-updated set.
    volatile IReadOnlySet<int> mutedChannels = new HashSet<int>();

    public event Action? Finished;

    public bool IsRunning => playback?.IsRunning ?? false;

    public double CurrentSeconds => playback?.GetCurrentTime<MetricTimeSpan>().TotalSeconds ?? 0;

    /// <summary>
    /// Null picks the system's default output (e.g. Microsoft GS Wavetable Synth); <see cref="PianoSoundModule.DeviceName"/>
    /// the piano sound pack (the default again while it isn't downloaded, or can't be played).
    /// </summary>
    public void SetOutputDevice(string? deviceName)
    {
        (outputDevice as IDisposable)?.Dispose();
        outputDevice = null;
        if (deviceName == PianoSoundModule.DeviceName && PianoSoundModule.IsInstalled)
        {
            try
            {
                outputDevice = new SoundFontOutputDevice(PianoSoundModule.SoundFontPath);
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NAudio.MmException or UnauthorizedAccessException)
            {
                // A broken download or no sound card: the default MIDI output instead.
            }
        }
        var all = OutputDevice.GetAll().ToList();
        var chosen = deviceName != null ? all.FirstOrDefault(d => d.Name == deviceName) : all.FirstOrDefault();
        outputDevice = chosen ?? all.FirstOrDefault();
    }

    public static IReadOnlyList<string> GetOutputDeviceNames() => OutputDevice.GetAll().Select(d => d.Name).ToList();

    /// <summary>
    /// General MIDI's drum channel (10, index 9) carries the metronome; the parts use the other 15, so none of them
    /// turns into drums.
    /// </summary>
    const int ClickChannel = 9;
    const int AccentClick = 76, Click = 77; // Hi / Low Wood Block

    static int ChannelOf(int partIndex) => partIndex % 15 >= ClickChannel ? partIndex % 15 + 1 : partIndex % 15;

    volatile bool metronome;

    /// <summary>Whether the click track is heard. It's always in the playback, so it stays exactly in time.</summary>
    public bool Metronome
    {
        get => metronome;
        set => metronome = value;
    }

    /// <param name="clickTiming">Where the metronome's beats fall: the score's own timing, or a set tempo for a recording.</param>
    public void Load(Score score, ScoreTiming clickTiming)
    {
        playback?.Dispose();
        playback = null;
        mutedChannels = new HashSet<int>();

        var notes = new List<Note>();
        for (int partIndex = 0; partIndex < score.Parts.Count; partIndex++)
        {
            var channel = (FourBitNumber)ChannelOf(partIndex);
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

        // The click track: a short wood block on every beat, the higher one on the first beat of each bar.
        double endQuarters = clickTiming.ToQuarters(score.DurationSeconds);
        long clickLength = TimeConverter.ConvertFrom(new MetricTimeSpan(TimeSpan.FromMilliseconds(60)), TempoMap);
        for (double q = 0; q <= endQuarters + 1e-6; q += clickTiming.ClickQuarters)
        {
            bool accent = Math.Abs(Math.IEEERemainder(q, clickTiming.BarQuarters)) < 1e-6;
            long time = TimeConverter.ConvertFrom(new MetricTimeSpan(TimeSpan.FromSeconds(clickTiming.ToSeconds(q))), TempoMap);
            notes.Add(new Note((SevenBitNumber)(accent ? AccentClick : Click), clickLength, time)
            {
                Channel = (FourBitNumber)ClickChannel,
                Velocity = (SevenBitNumber)(accent ? 120 : 90),
            });
        }

        playback = outputDevice != null
            ? new Playback(notes, TempoMap, outputDevice, new PlaybackSettings())
            : new Playback(notes, TempoMap, new PlaybackSettings());
        playback.NoteCallback = (data, _, _, _) =>
            data.Channel == ClickChannel ? (metronome ? data : NotePlaybackData.SkipNote)
            : mutedChannels.Contains(data.Channel) ? NotePlaybackData.SkipNote
            : data;
        // When jumping (seek), end notes left hanging and start the ones already sounding at the new spot.
        playback.TrackNotes = true;

        // The synth keeps channel volumes from whatever played before - set every part's own.
        for (int partIndex = 0; partIndex < score.Parts.Count; partIndex++)
            SetPartVolume(partIndex, score.Parts[partIndex].Volume);
        playback.Finished += (_, _) => Finished?.Invoke();
    }

    public void SetPartMuted(int partIndex, bool muted)
    {
        int channel = ChannelOf(partIndex);
        var next = new HashSet<int>(mutedChannels);
        if (muted)
            next.Add(channel);
        else
            next.Remove(channel);
        mutedChannels = next;
    }

    /// <summary>
    /// 0-1. Sent as Channel Volume (CC 7) on the part's channel, so it applies immediately - even to notes
    /// already sounding - unlike scaling note velocities, which would only affect notes started afterwards.
    /// </summary>
    public void SetPartVolume(int partIndex, double volume)
    {
        if (outputDevice is not { } device)
            return;
        var value = (SevenBitNumber)(int)Math.Round(Math.Clamp(volume, 0, 1) * 127);
        device.SendEvent(new ControlChangeEvent((SevenBitNumber)7, value) { Channel = (FourBitNumber)ChannelOf(partIndex) });
    }

    /// <summary>One metronome click right now (the count-in before playing). Safe to call from any thread.</summary>
    public void PlayClick(bool accent)
    {
        if (outputDevice is not { } device)
            return;
        var note = (SevenBitNumber)(accent ? AccentClick : Click);
        device.SendEvent(new NoteOnEvent(note, (SevenBitNumber)(accent ? 120 : 90)) { Channel = (FourBitNumber)ClickChannel });
        _ = Task.Delay(60).ContinueWith(_ => device.SendEvent(new NoteOffEvent(note, (SevenBitNumber)0) { Channel = (FourBitNumber)ClickChannel }));
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

    public void SeekTo(double seconds) =>
        playback?.MoveToTime(new MetricTimeSpan(TimeSpan.FromSeconds(Math.Max(0, seconds))));

    public void Play()
    {
        if (playback == null)
            return;
        // A playback that has never run starts from the beginning, whatever MoveToTime said before: keep the spot
        // that was moved to (a click in the sheet music, a loop start) and go back there once it's running.
        double from = CurrentSeconds;
        playback.Start();
        if (from > 0 && Math.Abs(CurrentSeconds - from) > 0.05)
            SeekTo(from);
    }
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
