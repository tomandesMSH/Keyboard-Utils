using System.Numerics;

namespace KeyboardUtils;

/// <summary>
/// Monophonic pitch detection with the YIN algorithm (de Cheveigné &amp; Kawahara, 2002). Plain FFT peak picking
/// is unreliable for piano - the fundamental is often weaker than its overtones - while YIN looks for the period
/// the waveform repeats at, which survives that. One instance per frame size; not thread-safe (reuses buffers).
/// </summary>
sealed class PitchDetector
{
    /// <summary>Lower = stricter. 0.10-0.20 is the usual range; above it noise starts getting a "pitch".</summary>
    const float Threshold = 0.15f;

    readonly int sampleRate;
    readonly int window;
    readonly float[] difference;
    readonly int minPeriod;

    /// <param name="frameSize">Samples per call. The longest detectable period is half of it.</param>
    /// <param name="maxFrequency">Upper bound of interest; also limits octave-too-high errors.</param>
    public PitchDetector(int sampleRate, int frameSize, double maxFrequency = 4400)
    {
        this.sampleRate = sampleRate;
        window = frameSize / 2;
        difference = new float[window];
        minPeriod = Math.Max(2, (int)(sampleRate / maxFrequency));
    }

    /// <summary>Lowest frequency this frame size can see.</summary>
    public double MinFrequency => (double)sampleRate / (window - 1);

    /// <summary>
    /// Returns the fundamental frequency in Hz, or null if the frame isn't clearly pitched (noise, silence, a
    /// chord with no common period). <paramref name="confidence"/> is 0-1, higher = cleaner periodicity.
    /// </summary>
    public double? Detect(ReadOnlySpan<float> frame, out double confidence)
    {
        confidence = 0;
        if (frame.Length < window * 2)
            throw new ArgumentException($"Frame must have at least {window * 2} samples.", nameof(frame));

        // Step 1+2: difference function d(tau) = sum (x[j] - x[j+tau])^2.
        var head = frame[..window];
        for (int tau = 1; tau < window; tau++)
            difference[tau] = SquaredDistance(head, frame.Slice(tau, window));

        // Step 3: cumulative mean normalized difference, in place. Dips toward 0 at the period and its multiples.
        difference[0] = 1;
        float runningSum = 0;
        for (int tau = 1; tau < window; tau++)
        {
            runningSum += difference[tau];
            difference[tau] = runningSum > 0 ? difference[tau] * tau / runningSum : 1;
        }

        // Step 4: the first dip under the threshold (not the global minimum - that's often 2x the period).
        int period = -1;
        for (int tau = minPeriod; tau < window - 1; tau++)
        {
            if (difference[tau] >= Threshold)
                continue;
            while (tau + 1 < window && difference[tau + 1] < difference[tau])
                tau++;
            period = tau;
            break;
        }
        if (period < 0)
            return null;

        // Step 5: parabolic interpolation between neighbours for sub-sample accuracy (matters for high notes,
        // where one sample of period is a big chunk of a semitone).
        double refined = period;
        if (period > 1 && period < window - 1)
        {
            float s0 = difference[period - 1], s1 = difference[period], s2 = difference[period + 1];
            float denominator = s0 + s2 - 2 * s1;
            if (Math.Abs(denominator) > 1e-9f)
                refined = period + (s0 - s2) / (2 * denominator);
        }

        confidence = Math.Clamp(1 - difference[period], 0, 1);
        return sampleRate / refined;
    }

    static float SquaredDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int i = 0;
        var sum = Vector<float>.Zero;
        for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
        {
            var delta = new Vector<float>(a[i..]) - new Vector<float>(b[i..]);
            sum += delta * delta;
        }
        float total = Vector.Sum(sum);
        for (; i < a.Length; i++)
        {
            float delta = a[i] - b[i];
            total += delta * delta;
        }
        return total;
    }

    /// <summary>Nearest MIDI note number (A4 = 440 Hz = 69).</summary>
    public static int ToMidi(double frequency) => (int)Math.Round(69 + 12 * Math.Log2(frequency / 440.0));
}
