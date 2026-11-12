namespace KeyboardUtils;

/// <summary>Where a note is played on a string instrument: the string (0 = the lowest), the fret or the place on
/// it (semitones above the open string), and the finger - 0 for an open string, null where a beginner's first
/// position doesn't reach.</summary>
sealed record StringSpot(int String, int Semitones, int? Finger, string Label);

/// <summary>
/// Beginner fingerings for the fingerboard view: the guitar in its lowest frets, violin, viola and cello in first
/// position, preferring open strings. One place per note - the one a pupil learns first.
/// </summary>
static class Fingering
{
    /// <summary>Guitar frets shown and played.</summary>
    public const int GuitarFrets = 12;

    /// <summary>Violin / viola, first position: semitones above the open string → finger (low 1, 1, low 2, 2, 3, high 3, 4).</summary>
    static readonly int[] UpperStringFingers = [0, 1, 1, 2, 2, 3, 3, 4];

    /// <summary>Cello, first position: semitones → finger (1 back, 1, 2, 3, 4, 4 extended); the fifth is the next string.</summary>
    static readonly int[] CelloFingers = [0, 1, 1, 2, 3, 4, 4];

    /// <summary>The spot for <paramref name="pitch"/> (sounding) on <paramref name="instrument"/>, or null when it
    /// isn't a string instrument or the note is out of its reach.</summary>
    public static StringSpot? For(Instrument instrument, int pitch)
    {
        var strings = instrument.Strings;
        if (strings.Length == 0 || pitch < strings[0])
            return null;
        return instrument.Id == "guitar" ? Guitar(strings, pitch) : Bowed(strings, pitch, instrument.Id == "cello");
    }

    /// <summary>The lowest fret wins (open strings, first position); on a tie the thicker string.</summary>
    static StringSpot? Guitar(int[] strings, int pitch)
    {
        StringSpot? best = null;
        for (int s = 0; s < strings.Length; s++)
        {
            int fret = pitch - strings[s];
            if (fret < 0 || fret > GuitarFrets)
                continue;
            if (best == null || fret < best.Semitones)
                best = new StringSpot(s, fret, null, fret.ToString());
        }
        return best;
    }

    /// <summary>
    /// The highest string at or below the note, first position; a note a fifth up is the next open string, except
    /// on the top string (4th finger). Above first position on the top string: the place, without a finger.
    /// </summary>
    static StringSpot? Bowed(int[] strings, int pitch, bool cello)
    {
        var fingers = cello ? CelloFingers : UpperStringFingers;
        int s = strings.Length - 1;
        while (s > 0 && strings[s] > pitch)
            s--;
        int semitones = pitch - strings[s];
        if (semitones < fingers.Length)
        {
            int finger = fingers[semitones];
            return new StringSpot(s, semitones, finger, finger == 0 ? "0" : finger.ToString());
        }
        // Only the top string goes on up: higher positions, no first-position finger. Two octaves are plenty.
        return s == strings.Length - 1 && semitones <= 24 ? new StringSpot(s, semitones, null, "") : null;
    }
}
