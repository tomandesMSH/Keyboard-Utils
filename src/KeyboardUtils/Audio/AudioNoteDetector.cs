namespace KeyboardUtils;

/// <summary>
/// The audio side of note detection, shared by every audio source (microphone, system sound): takes mono samples
/// as they arrive, slides a <see cref="FrameSize"/> window forward by <see cref="HopSize"/> samples and runs each
/// window through a <see cref="NoteTracker"/> (one note at a time). When told which notes are expected
/// (<see cref="SetExpectedNotes"/>, learning mode), a <see cref="ChordChecker"/> additionally reports each of them
/// that sounds - that's how the notes of a chord are heard.
/// </summary>
sealed class AudioNoteDetector
{
    /// <summary>~46 ms at 44.1 kHz. Long enough for a 43 Hz period twice over (lowest detectable: about F1).</summary>
    const int FrameSize = 2048;

    /// <summary>~12 ms between analyses: bounds the detection latency on top of the frame length.</summary>
    const int HopSize = 512;

    /// <summary>Half a frame to reach the window's centre plus the stable-pitch confirmation frames, roughly.</summary>
    public const double LatencySeconds = 0.07;

    /// <summary>Which detector reported a sounding note; a note is on while either still reports it.</summary>
    const int FromTracker = 1, FromChords = 2;

    readonly NoteTracker tracker;
    readonly ChordChecker chords;
    readonly float[] ring = new float[ChordChecker.FrameSize];
    readonly float[] frame = new float[FrameSize];
    readonly float[] chordFrame = new float[ChordChecker.FrameSize];
    readonly Dictionary<int, int> soundingFrom = [];
    readonly Lock gate = new();
    int ringFill;
    int sinceLastFrame;
    volatile int[] expected = [];

    public AudioNoteDetector(int sampleRate, double thresholdDb)
    {
        tracker = new NoteTracker(new PitchDetector(sampleRate, FrameSize)) { ThresholdDb = thresholdDb };
        // While a chord is expected, the one-note tracker only guesses (often an octave off) - the chord checker
        // answers instead. With a single expected note it keeps reporting, so a wrong key still shows up.
        tracker.NoteOn += note => { if (expected.Length < 2) Report(note, FromTracker, on: true); };
        tracker.NoteOff += note => Report(note, FromTracker, on: false);
        chords = new ChordChecker(sampleRate);
        chords.NoteOn += note => Report(note, FromChords, on: true);
        chords.NoteOff += note => Report(note, FromChords, on: false);
    }

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>Level of each analysed frame in dBFS (0 = full scale), ~86 times a second. For meters/calibration.</summary>
    public event Action<double>? LevelChanged;

    public double ThresholdDb
    {
        get => tracker.ThresholdDb;
        set => tracker.ThresholdDb = value;
    }

    /// <summary>
    /// The notes the player should be playing now (learning mode); empty turns chord checking off. Callable from
    /// any thread - the capture thread picks up the new set at its next frame.
    /// </summary>
    public void SetExpectedNotes(IEnumerable<int> notes) => expected = notes.Distinct().ToArray();

    /// <summary>Feeds samples (-1..1, mono). Called on the capture thread.</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            // Ring buffer: ring[ringFill % length] is the oldest sample once full. It's sized for the chord
            // checker's longer window; the pitch tracker reads just the newest FrameSize samples of it.
            ring[ringFill % ring.Length] = sample;
            ringFill++;
            if (ringFill >= FrameSize && ++sinceLastFrame >= HopSize)
            {
                sinceLastFrame = 0;
                AnalyzeFrame();
            }
        }
        // Keep the counter from overflowing on long sessions while preserving its position in the ring.
        if (ringFill >= ring.Length * 2)
            ringFill = ring.Length + ringFill % ring.Length;
    }

    /// <summary>Ends sounding notes and forgets buffered audio, e.g. when capture stops.</summary>
    public void Reset()
    {
        // The capture thread may still be finishing its last buffer.
        lock (gate)
        {
            tracker.Release();
            chords.Reset();
            ringFill = sinceLastFrame = 0;
        }
    }

    void AnalyzeFrame()
    {
        CopyNewest(frame);
        double level;
        int[] want = expected;
        lock (gate)
        {
            level = tracker.Process(frame);
            if (want.Length > 0 && ringFill >= ChordChecker.FrameSize)
            {
                CopyNewest(chordFrame);
                // Below the silence threshold nothing counts as sounding - room noise has peaks too.
                chords.Process(chordFrame, want, audible: level >= tracker.ThresholdDb);
            }
            else
                chords.Process(chordFrame, [], audible: false);
        }
        LevelChanged?.Invoke(level);
    }

    /// <summary>The newest target.Length samples, oldest first.</summary>
    void CopyNewest(float[] target)
    {
        int length = ring.Length, count = target.Length;
        int start = ((ringFill - count) % length + length) % length;
        int firstPart = Math.Min(count, length - start);
        ring.AsSpan(start, firstPart).CopyTo(target);
        ring.AsSpan(0, count - firstPart).CopyTo(target.AsSpan(firstPart));
    }

    /// <summary>Merges both detectors: one NoteOn when the first starts reporting a note, NoteOff when the last stops.</summary>
    void Report(int note, int source, bool on)
    {
        soundingFrom.TryGetValue(note, out int from);
        int now = on ? from | source : from & ~source;
        if (now == 0)
            soundingFrom.Remove(note);
        else
            soundingFrom[note] = now;
        if (from == 0 && now != 0)
            NoteOn?.Invoke(note);
        else if (from != 0 && now == 0)
            NoteOff?.Invoke(note);
    }
}
