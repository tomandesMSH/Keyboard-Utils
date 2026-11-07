using NAudio.Dsp;

namespace KeyboardUtils;

/// <summary>
/// Checks whether *known* notes are sounding - the easy half of polyphonic detection. Learning mode knows which
/// chord should be played, so instead of guessing every note in the sound (hard), it only asks "is each of these
/// pitches there?": for each expected pitch it sums the spectrum peaks at its first harmonics and compares that to
/// the spectrum's noise floor. Reports a note when it starts sounding or is struck again (a jump in its strength),
/// so a note still ringing from the previous chord doesn't count for the next one.
/// Not thread-safe: one instance per capture thread.
/// </summary>
sealed class ChordChecker
{
    /// <summary>~186 ms at 44.1 kHz: bins ~5 Hz apart, enough to tell neighbouring semitones apart from about C2 up.</summary>
    public const int FrameSize = 8192;
    const int Log2FrameSize = 13;

    const int Harmonics = 5;

    /// <summary>How far above the spectrum's typical level a pitch's harmonics must stand to count as sounding.</summary>
    const double PresenceDb = 14;

    /// <summary>A sounding note's fundamental is at most this far below the loudest peak (chord notes are similar in level).</summary>
    const double RangeBelowLoudestDb = 25;

    /// <summary>A jump this big over the recent minimum is a new strike of a note already sounding.</summary>
    const double RestrikeDb = 6;

    /// <summary>Frames a pitch must be present in a row before it counts (one frame can be an attack transient).</summary>
    const int ConfirmFrames = 2;

    /// <summary>History length for the restrike comparison (~100 ms at a 512-sample hop).</summary>
    const int HistoryFrames = 8;

    /// <summary>
    /// After a note starts, its strength keeps rising for a whole window (~16 frames at a 512-sample hop) as the
    /// attack fills it - that rise must not read as more strikes.
    /// </summary>
    const int RefractoryFrames = FrameSize / 512;

    /// <summary>
    /// A real tone is a narrow peak: it must beat the spectrum a quarter-tone to either side by this much. The broad
    /// splash of a key's attack (energy everywhere for a moment) fails this, which keeps it from faking notes.
    /// </summary>
    const double ContrastDb = 6;

    sealed class PitchState
    {
        public bool Sounding;
        public int PresentFrames;
        public int FramesSinceOn = int.MaxValue / 2;
        public readonly Queue<double> History = new();
    }

    readonly int sampleRate;
    readonly Complex[] fft = new Complex[FrameSize];
    readonly float[] window = Enumerable.Range(0, FrameSize).Select(i => (float)FastFourierTransform.HannWindow(i, FrameSize)).ToArray();
    readonly double[] magnitude = new double[FrameSize / 2];
    readonly double[] sorted = new double[FrameSize / 2];
    readonly Dictionary<int, PitchState> states = [];

    public ChordChecker(int sampleRate) => this.sampleRate = sampleRate;

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>
    /// Analyses one frame for the given pitches. Pitches no longer expected are released; newly expected ones
    /// start from their current state without an event (a note already ringing has to be struck again).
    /// <paramref name="audible"/> false (below the silence threshold) means nothing sounds, whatever the spectrum.
    /// </summary>
    public void Process(ReadOnlySpan<float> frame, IReadOnlyCollection<int> expected, bool audible)
    {
        foreach (int gone in states.Keys.Where(p => !expected.Contains(p)).ToList())
        {
            if (states[gone].Sounding)
                NoteOff?.Invoke(gone);
            states.Remove(gone);
        }
        if (expected.Count == 0)
            return;

        double floor = 1, needed = double.MaxValue;
        if (audible)
        {
            Spectrum(frame);
            floor = NoiseFloor();
            // The fundamental must stand out on its own (a coinciding harmonic of another note isn't enough), both
            // above the noise and not far below the loudest thing in the spectrum.
            needed = Math.Max(floor * DbToRatio(PresenceDb), magnitude.Max() * DbToRatio(-RangeBelowLoudestDb));
        }
        foreach (int pitch in expected)
        {
            double fundamental = audible ? Fundamental(pitch) : 0;
            bool present = audible && fundamental >= needed
                && HarmonicMean(pitch) >= floor * DbToRatio(PresenceDb)
                && Contrast(pitch) >= DbToRatio(ContrastDb);
            // Absolute level, not relative to the floor: the floor itself jumps with every strike.
            double strengthDb = 20 * Math.Log10(fundamental + 1e-12);
            if (!states.TryGetValue(pitch, out var state))
            {
                states[pitch] = state = new PitchState { Sounding = present, PresentFrames = present ? ConfirmFrames : 0 };
                state.History.Enqueue(strengthDb);
                continue;
            }

            state.FramesSinceOn++;
            bool restruck = state.Sounding && state.FramesSinceOn > RefractoryFrames
                && state.History.Count > 0 && strengthDb - state.History.Min() >= RestrikeDb;
            state.PresentFrames = present ? state.PresentFrames + 1 : 0;
            if (state.Sounding && (!present || restruck))
            {
                state.Sounding = false;
                NoteOff?.Invoke(pitch);
                // A restrike counts as a new note straight away; the attack is already confirmed by the jump.
                if (restruck && present)
                    Start(pitch, state);
            }
            else if (!state.Sounding && state.PresentFrames >= ConfirmFrames)
                Start(pitch, state);

            state.History.Enqueue(strengthDb);
            if (state.History.Count > HistoryFrames)
                state.History.Dequeue();
        }
    }

    void Start(int pitch, PitchState state)
    {
        state.Sounding = true;
        state.FramesSinceOn = 0;
        // Compare later restrikes against this note's own level, not the silence before it.
        state.History.Clear();
        NoteOn?.Invoke(pitch);
    }

    /// <summary>Forgets everything (and ends sounding notes), e.g. when learning stops.</summary>
    public void Reset()
    {
        foreach (var (pitch, state) in states)
            if (state.Sounding)
                NoteOff?.Invoke(pitch);
        states.Clear();
    }

    void Spectrum(ReadOnlySpan<float> frame)
    {
        for (int i = 0; i < FrameSize; i++)
        {
            fft[i].X = frame[i] * window[i];
            fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, Log2FrameSize, fft);
        for (int i = 0; i < magnitude.Length; i++)
            magnitude[i] = Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
    }

    /// <summary>The median magnitude over the piano's range: the level of "nothing in particular".</summary>
    double NoiseFloor()
    {
        int low = Bin(27.5), high = Math.Min(magnitude.Length - 1, Bin(5000));
        int count = high - low;
        Array.Copy(magnitude, low, sorted, 0, count);
        Array.Sort(sorted, 0, count);
        return Math.Max(sorted[count / 2], 1e-9);
    }

    /// <summary>
    /// The pitch's own peak. Below C3 a piano's fundamental is weak (the string's 2nd partial dominates), so there
    /// the stronger of the first two partials counts.
    /// </summary>
    double Fundamental(int pitch)
    {
        double first = Peak(pitch, 1);
        return pitch < 48 ? Math.Max(first, Peak(pitch, 2)) : first;
    }

    /// <summary>Mean peak over the first harmonics - a real note has a series of them, not one stray peak.</summary>
    double HarmonicMean(int pitch)
    {
        double sum = 0;
        int counted = 0;
        for (int k = 1; k <= Harmonics; k++)
        {
            if (Frequency(pitch) * k >= sampleRate / 2.0 * 0.95)
                break;
            sum += Peak(pitch, k);
            counted++;
        }
        return counted > 0 ? sum / counted : 0;
    }

    /// <summary>
    /// Strongest bin near the k-th harmonic. The window is ±25 cents for the fundamental (a semitone away stays
    /// outside the Hann main lobe) and widens for higher partials, which run sharp on piano strings.
    /// </summary>
    double Peak(int pitch, int k)
    {
        double f = Frequency(pitch) * k;
        double cents = 25 + 10 * (k - 1);
        int from = Bin(f * Math.Pow(2, -cents / 1200)), to = Bin(f * Math.Pow(2, cents / 1200));
        double peak = 0;
        for (int b = Math.Max(1, from); b <= Math.Min(magnitude.Length - 1, to); b++)
            peak = Math.Max(peak, magnitude[b]);
        return peak;
    }

    /// <summary>
    /// How much the pitch's peak beats the spectrum a quarter-tone either side (as a ratio). Measured on the lowest
    /// harmonic where a quarter-tone spans at least ~1.5 bins - for low notes the fundamental is too crowded.
    /// </summary>
    double Contrast(int pitch)
    {
        double binWidth = (double)sampleRate / FrameSize;
        double quarterTone = Math.Pow(2, 50 / 1200.0) - 1; // ≈ 2.9 % of the frequency
        int k = Math.Clamp((int)Math.Ceiling(1.5 * binWidth / (quarterTone * Frequency(pitch))), 1, Harmonics);
        double f = Frequency(pitch) * k;
        double side = Math.Max(magnitude[Math.Min(magnitude.Length - 1, Bin(f / (1 + quarterTone)))],
                               magnitude[Math.Min(magnitude.Length - 1, Bin(f * (1 + quarterTone)))]);
        return Peak(pitch, k) / Math.Max(side, 1e-12);
    }

    static double Frequency(int pitch) => 440 * Math.Pow(2, (pitch - 69) / 12.0);

    static double DbToRatio(double db) => Math.Pow(10, db / 20);

    int Bin(double frequency) => (int)Math.Round(frequency * FrameSize / sampleRate);
}
