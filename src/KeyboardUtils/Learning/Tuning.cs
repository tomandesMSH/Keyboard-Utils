namespace KeyboardUtils;

/// <summary>The tuner's arithmetic: notes, cents and the nearest string, against a reference A4 (usually 440 Hz).</summary>
static class Tuning
{
    /// <summary>The pitch in semitones (MIDI numbers, fractional): 69 = A4 exactly.</summary>
    public static double Semitones(double hz, double a4 = 440) => 69 + 12 * Math.Log2(hz / a4);

    /// <summary>The frequency of a note.</summary>
    public static double Frequency(int midi, double a4 = 440) => a4 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>The nearest note and how far off it is, in cents (-50..50; positive = sharp, too high).</summary>
    public static (int Note, double Cents) Nearest(double hz, double a4 = 440)
    {
        double semitones = Semitones(hz, a4);
        int note = (int)Math.Round(semitones);
        return (note, (semitones - note) * 100);
    }

    /// <summary>
    /// The string being tuned: the nearest one within a fourth (5 semitones), so a string tuned far too low still
    /// counts as itself; null when none is that close.
    /// </summary>
    public static int? NearestString(double hz, IReadOnlyList<int> strings, double a4 = 440)
    {
        if (strings.Count == 0)
            return null;
        double semitones = Semitones(hz, a4);
        int best = 0;
        for (int i = 1; i < strings.Count; i++)
            if (Math.Abs(strings[i] - semitones) < Math.Abs(strings[best] - semitones))
                best = i;
        return Math.Abs(strings[best] - semitones) <= 5 ? best : null;
    }

    /// <summary>Cents between the sound and a target note (any distance, not just ±50).</summary>
    public static double CentsFrom(double hz, int target, double a4 = 440) => (Semitones(hz, a4) - target) * 100;

    /// <summary>Close enough to call it in tune.</summary>
    public const double InTuneCents = 5;

    /// <summary>A note's name with its octave, as chosen in Settings: a1 / A4, h / H3 (Czech) or B3 (English).</summary>
    public static string Name(int midi) => NoteNames.Name(midi);
}
