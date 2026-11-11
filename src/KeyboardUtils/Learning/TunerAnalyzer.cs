namespace KeyboardUtils;

/// <summary>
/// The tuner's ear: slides a 4096-sample window over the microphone's samples (low enough for a cello's C2) and finds
/// the pitch of it every 1024 samples (~23 ms), then smooths it with a median of the last five - bow noise and the attack make single
/// frames jump. Quiet frames are silence; after a few of them <see cref="Frequency"/> becomes null. No audio I/O.
/// </summary>
sealed class TunerAnalyzer
{
    public const int FrameSize = 4096;
    const int HopSize = 1024;
    const int MedianFrames = 5;
    const int SilentFramesToClear = 12; // ~0.3 s at 44.1 kHz

    readonly PitchDetector detector;
    readonly float[] window = new float[FrameSize];
    readonly Queue<double> recent = new();
    int filled, silentFrames;

    public TunerAnalyzer(int sampleRate) => detector = new PitchDetector(sampleRate, FrameSize);

    /// <summary>Frames quieter than this (dBFS) are silence.</summary>
    public double ThresholdDb { get; set; } = -45;

    /// <summary>The smoothed frequency heard, in Hz; null = nothing (clear) playing.</summary>
    public double? Frequency { get; private set; }

    /// <summary>Raised after each analysed frame (on the caller's thread).</summary>
    public event Action<double?>? Analysed;

    public void AddSamples(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            window[filled++] = sample;
            if (filled < FrameSize)
                continue;
            // A full window: analyse it, then slide it on by a hop.
            Analyse();
            Array.Copy(window, HopSize, window, 0, FrameSize - HopSize);
            filled = FrameSize - HopSize;
        }
    }

    void Analyse()
    {
        double level = NoteTracker.LevelDb(window);
        double? hz = level >= ThresholdDb ? detector.Detect(window, out _) : null;
        if (hz is { } f && f >= detector.MinFrequency)
        {
            silentFrames = 0;
            // A jump of more than a semitone and a half: a new note - start the median over.
            if (Frequency is { } current && Math.Abs(Tuning.Semitones(f) - Tuning.Semitones(current)) > 1.5)
                recent.Clear();
            recent.Enqueue(f);
            if (recent.Count > MedianFrames)
                recent.Dequeue();
            var sorted = recent.Order().ToList();
            Frequency = sorted[sorted.Count / 2];
        }
        else if (++silentFrames >= SilentFramesToClear)
        {
            recent.Clear();
            Frequency = null;
        }
        Analysed?.Invoke(Frequency);
    }
}
