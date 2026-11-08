namespace KeyboardUtils;

/// <summary>A run of neighbouring bars where the mistakes pile up (bar numbers as printed: the first bar is 1).</summary>
sealed record HardSpot(int FirstBar, int LastBar, double Mistakes)
{
    public string Bars => FirstBar == LastBar ? $"{FirstBar}" : $"{FirstBar}–{LastBar}";
}

/// <summary>
/// Where in a song the player keeps going wrong: learning mode notes the wrong notes per bar in every attempt
/// (<see cref="PracticeEntry.BarMistakes"/>), this adds the last few attempts up - recent ones counting more - and
/// picks the worst bars, joined with their neighbours, to practise in a loop.
/// </summary>
static class HardSpots
{
    /// <summary>The bar (1 = first) a moment of the song falls in - the same bars the sheet music shows.</summary>
    public static int BarOf(double seconds, ScoreTiming timing) =>
        (int)Math.Floor(timing.ToQuarters(seconds) / timing.BarQuarters + 1e-9) + 1;

    /// <summary>The start and end (seconds) of bars <paramref name="first"/> to <paramref name="last"/>, as a loop.</summary>
    public static (double Start, double End) Range(int first, int last, ScoreTiming timing) =>
        (timing.ToSeconds((first - 1) * timing.BarQuarters), timing.ToSeconds(last * timing.BarQuarters));

    /// <summary>Wrong notes per bar, from the start times of the notes played wrong.</summary>
    public static Dictionary<int, int> CountMistakes(IEnumerable<double> wrongNoteStarts, ScoreTiming timing)
    {
        var counts = new Dictionary<int, int>();
        foreach (double start in wrongNoteStarts)
        {
            int bar = BarOf(start, timing);
            counts[bar] = counts.GetValueOrDefault(bar) + 1;
        }
        return counts;
    }

    /// <summary>
    /// Mistakes per bar over the last <paramref name="attempts"/> attempts that noted them: the newest counts in
    /// full, each older one a fifth less.
    /// </summary>
    public static Dictionary<int, double> Weighted(IReadOnlyList<PracticeEntry> history, int attempts = 5)
    {
        var totals = new Dictionary<int, double>();
        var recent = history.Where(e => e.BarMistakes is not null).Reverse().Take(attempts).ToList();
        for (int i = 0; i < recent.Count; i++)
        {
            double weight = 1 - 0.2 * i;
            foreach (var (bar, count) in recent[i].BarMistakes!)
                totals[bar] = totals.GetValueOrDefault(bar) + weight * count;
        }
        return totals;
    }

    /// <summary>
    /// The worst spots, worst first: each starts at the bar with the most mistakes not yet taken, and grows over
    /// neighbouring bars that have mistakes too (the busier side first), up to <paramref name="maxLength"/> bars.
    /// </summary>
    public static IReadOnlyList<HardSpot> For(IReadOnlyList<PracticeEntry> history, int maxSpots = 3, int maxLength = 4)
    {
        var weighted = Weighted(history);
        var taken = new HashSet<int>();
        var spots = new List<HardSpot>();
        foreach (var (bar, _) in weighted.Where(b => b.Value > 0).OrderByDescending(b => b.Value).ThenBy(b => b.Key))
        {
            if (spots.Count >= maxSpots)
                break;
            if (taken.Contains(bar))
                continue;
            int first = bar, last = bar;
            while (last - first + 1 < maxLength)
            {
                double left = taken.Contains(first - 1) ? 0 : weighted.GetValueOrDefault(first - 1);
                double right = taken.Contains(last + 1) ? 0 : weighted.GetValueOrDefault(last + 1);
                if (left <= 0 && right <= 0)
                    break;
                if (right >= left)
                    last++;
                else
                    first--;
            }
            for (int b = first; b <= last; b++)
                taken.Add(b);
            spots.Add(new HardSpot(first, last, Enumerable.Range(first, last - first + 1).Sum(b => weighted.GetValueOrDefault(b))));
        }
        return spots.OrderByDescending(s => s.Mistakes).ToList();
    }
}
