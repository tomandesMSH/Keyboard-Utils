namespace KeyboardUtils;

/// <summary>
/// Speeding a loop up as it gets better - the usual way to bring a hard passage up to tempo: after a pass played
/// well enough, the next one goes a little faster, up to full speed.
/// </summary>
static class SpeedRamp
{
    /// <summary>A pass at least this good (in %) earns a faster next one.</summary>
    public const double GoodPassPercent = 90;

    /// <summary>How much faster (in percentage points of the song's tempo).</summary>
    public const int StepPercent = 5;

    public const int FullSpeedPercent = 100;

    /// <summary>The speed for the next pass after one played at <paramref name="speedPercent"/> scoring <paramref name="passPercent"/>.</summary>
    public static int Next(int speedPercent, double passPercent) =>
        passPercent >= GoodPassPercent && speedPercent < FullSpeedPercent
            ? Math.Min(FullSpeedPercent, speedPercent + StepPercent)
            : speedPercent;
}
