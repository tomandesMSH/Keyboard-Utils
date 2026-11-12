namespace KeyboardUtils;

/// <summary>
/// Where each note of a guitar song is played - the string and fret of its TAB number, and of its dot on the
/// fingerboard. Notes starting together go on different strings; the hand stays where it is if it can, low on the
/// neck otherwise; a fret written in the file (MusicXML &lt;technical&gt;) is kept.
/// </summary>
static class Tablature
{
    /// <summary>Notes closer than this start together (a chord).</summary>
    const double ChordSeconds = 0.03;

    /// <summary>The widest stretch of the hand, in frets, before a shape counts as unplayable.</summary>
    const int Stretch = 4;

    public static Dictionary<ScoreNote, StringSpot> Assign(IEnumerable<ScoreNote> notes, int[] openStrings)
    {
        var result = new Dictionary<ScoreNote, StringSpot>(ReferenceEqualityComparer.Instance);
        if (openStrings.Length == 0)
            return result;
        double position = 0; // where the hand is: the average fretted fret, 0 = first position
        var sorted = notes.OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch).ToList();
        for (int i = 0; i < sorted.Count;)
        {
            int end = i + 1;
            while (end < sorted.Count && sorted[end].StartSeconds - sorted[i].StartSeconds < ChordSeconds)
                end++;
            // More notes at once than strings (a piano chord): the highest ones, the rest have no TAB number.
            var chord = sorted.GetRange(i, end - i).OrderByDescending(n => n.Pitch).Take(openStrings.Length).ToList();
            i = end;

            var spots = Place(chord, openStrings, position);
            foreach (var (note, spot) in spots)
                result[note] = spot;
            var fretted = spots.Values.Where(s => s.Semitones > 0).ToList();
            if (fretted.Count > 0)
                position = fretted.Average(s => s.Semitones);
        }
        return result;
    }

    /// <summary>The best strings for notes sounding together, from where the hand is.</summary>
    static Dictionary<ScoreNote, StringSpot> Place(List<ScoreNote> chord, int[] open, double position)
    {
        // Each note's possible strings (lowest fret first); a note out of the guitar's reach gets none.
        var options = chord.Select(n => Candidates(n, open).ToList()).ToList();
        int[]? best = null;
        double bestCost = double.MaxValue;
        var choice = new int[chord.Count];
        var used = new bool[open.Length];

        void Search(int k)
        {
            if (k == chord.Count)
            {
                double cost = Cost(chord, choice, open, position);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = (int[])choice.Clone();
                }
                return;
            }
            bool any = false;
            foreach (int s in options[k])
            {
                if (used[s])
                    continue;
                any = true;
                used[s] = true;
                choice[k] = s;
                Search(k + 1);
                used[s] = false;
            }
            if (!any)
            {
                // No free string: this note isn't placed (more notes than strings, or out of range).
                choice[k] = -1;
                Search(k + 1);
            }
        }
        Search(0);

        var placed = new Dictionary<ScoreNote, StringSpot>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < chord.Count && best != null; k++)
            if (best[k] >= 0)
            {
                int fret = chord[k].Pitch - open[best[k]];
                placed[chord[k]] = new StringSpot(best[k], fret, null, fret.ToString());
            }
        return placed;
    }

    static IEnumerable<int> Candidates(ScoreNote note, int[] open)
    {
        // Written in the file: that string only (MusicXML counts strings from the highest, 1).
        if (note.TabString is { } written)
        {
            int s = open.Length - written;
            if (s >= 0 && s < open.Length && note.Pitch - open[s] is >= 0 and <= Fingering.GuitarFrets)
            {
                yield return s;
                yield break;
            }
        }
        for (int s = 0; s < open.Length; s++)
            if (note.Pitch - open[s] is >= 0 and <= Fingering.GuitarFrets)
                yield return s;
    }

    /// <summary>Low is cheap, moving the hand costs, a stretch beyond four frets is nearly impossible, unplaced notes worst.</summary>
    static double Cost(List<ScoreNote> chord, int[] choice, int[] open, double position)
    {
        double cost = 0;
        var frets = new List<int>();
        for (int k = 0; k < chord.Count; k++)
        {
            if (choice[k] < 0)
            {
                cost += 1000;
                continue;
            }
            int fret = chord[k].Pitch - open[choice[k]];
            cost += 0.25 * fret;
            if (fret > 0)
                frets.Add(fret);
        }
        if (frets.Count > 0)
        {
            int span = frets.Max() - frets.Min();
            cost += span > Stretch ? 100 * (span - Stretch) : 0.3 * span;
            cost += Math.Abs(frets.Average() - Math.Max(position, 1)) * 0.8;
        }
        return cost;
    }
}
