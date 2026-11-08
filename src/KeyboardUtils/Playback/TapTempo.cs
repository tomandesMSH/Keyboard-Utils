namespace KeyboardUtils;

/// <summary>
/// Tempo tapped in on a key: the average of the last few gaps between taps. A pause longer than
/// <see cref="ResetAfterSeconds"/> starts counting again, so tapping a new tempo later isn't mixed with the old one.
/// </summary>
sealed class TapTempo
{
    public const double ResetAfterSeconds = 2;
    const int MaxGaps = 4;

    readonly List<double> taps = [];

    /// <param name="nowSeconds">When this tap was, on any steadily running clock.</param>
    /// <returns>The tempo in BPM (clamped to the metronome's range), or null after the first tap of a series.</returns>
    public int? Tap(double nowSeconds)
    {
        if (taps.Count > 0 && (nowSeconds - taps[^1] > ResetAfterSeconds || nowSeconds <= taps[^1]))
            taps.Clear();
        taps.Add(nowSeconds);
        if (taps.Count > MaxGaps + 1)
            taps.RemoveAt(0);
        if (taps.Count < 2)
            return null;
        double gap = (taps[^1] - taps[0]) / (taps.Count - 1);
        return Math.Clamp((int)Math.Round(60 / gap), StandaloneMetronome.MinBpm, StandaloneMetronome.MaxBpm);
    }
}
