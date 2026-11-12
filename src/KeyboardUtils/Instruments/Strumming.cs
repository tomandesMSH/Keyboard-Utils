namespace KeyboardUtils;

/// <summary>The arithmetic of playing the on-screen fingerboard: which strings a strum crosses, how hard, what they sound.</summary>
static class Strumming
{
    /// <summary>
    /// The strings whose line lies between <paramref name="fromY"/> (excluded) and <paramref name="toY"/> (included),
    /// in the order a hand moving from one to the other crosses them.
    /// </summary>
    public static List<int> CrossedStrings(float fromY, float toY, IReadOnlyList<float> stringYs)
    {
        var crossed = new List<int>();
        for (int s = 0; s < stringYs.Count; s++)
        {
            float y = stringYs[s];
            bool between = fromY < toY ? y > fromY && y <= toY : y < fromY && y >= toY;
            if (between)
                crossed.Add(s);
        }
        // Moving down the screen crosses the lines top to bottom, i.e. by growing y.
        return fromY < toY
            ? crossed.OrderBy(s => stringYs[s]).ToList()
            : crossed.OrderByDescending(s => stringYs[s]).ToList();
    }

    /// <summary>How hard a strum is from how fast the hand moves (px per ms): 50 (gentle) to 120 (hard).</summary>
    public static int Velocity(double pixelsPerMs) => (int)Math.Round(Math.Clamp(50 + pixelsPerMs * 35, 50, 120));

    /// <summary>What a string sounds: open, or stopped <paramref name="place"/> semitones higher.</summary>
    public static int Pitch(IReadOnlyList<int> openStrings, int stringIndex, IReadOnlyDictionary<int, int> stopped) =>
        openStrings[stringIndex] + stopped.GetValueOrDefault(stringIndex);
}
