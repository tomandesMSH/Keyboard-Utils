namespace KeyboardUtils;

/// <summary>
/// The audio side of note detection, shared by every audio source (microphone, system sound): takes mono samples
/// as they arrive, slides a <see cref="FrameSize"/> window forward by <see cref="HopSize"/> samples and runs each
/// window through a <see cref="NoteTracker"/>. Single notes only - a chord has no single period, so it's either
/// reported as one of its notes or not at all.
/// </summary>
sealed class AudioNoteDetector
{
    /// <summary>~46 ms at 44.1 kHz. Long enough for a 43 Hz period twice over (lowest detectable: about F1).</summary>
    const int FrameSize = 2048;

    /// <summary>~12 ms between analyses: bounds the detection latency on top of the frame length.</summary>
    const int HopSize = 512;

    /// <summary>Half a frame to reach the window's centre plus the stable-pitch confirmation frames, roughly.</summary>
    public const double LatencySeconds = 0.07;

    readonly NoteTracker tracker;
    readonly float[] ring = new float[FrameSize];
    readonly float[] frame = new float[FrameSize];
    readonly Lock gate = new();
    int ringFill;
    int sinceLastFrame;

    public AudioNoteDetector(int sampleRate, double thresholdDb)
    {
        tracker = new NoteTracker(new PitchDetector(sampleRate, FrameSize)) { ThresholdDb = thresholdDb };
        tracker.NoteOn += note => NoteOn?.Invoke(note);
        tracker.NoteOff += note => NoteOff?.Invoke(note);
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

    /// <summary>Feeds samples (-1..1, mono). Called on the capture thread.</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            // Ring buffer: ring[ringFill % FrameSize] is the oldest sample once full.
            ring[ringFill % FrameSize] = sample;
            ringFill++;
            if (ringFill >= FrameSize && ++sinceLastFrame >= HopSize)
            {
                sinceLastFrame = 0;
                AnalyzeFrame();
            }
        }
        // Keep the counter from overflowing on long sessions while preserving its position in the ring.
        if (ringFill >= FrameSize * 2)
            ringFill = FrameSize + ringFill % FrameSize;
    }

    /// <summary>Ends a sounding note and forgets buffered audio, e.g. when capture stops.</summary>
    public void Reset()
    {
        // The capture thread may still be finishing its last buffer.
        lock (gate)
        {
            tracker.Release();
            ringFill = sinceLastFrame = 0;
        }
    }

    void AnalyzeFrame()
    {
        int oldest = ringFill % FrameSize;
        ring.AsSpan(oldest).CopyTo(frame);
        ring.AsSpan(0, oldest).CopyTo(frame.AsSpan(FrameSize - oldest));
        double level;
        lock (gate)
            level = tracker.Process(frame);
        LevelChanged?.Invoke(level);
    }
}
