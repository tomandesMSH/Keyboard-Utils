namespace KeyboardUtils;

/// <summary>
/// Chord symbols for a song whose file has none: per half bar (a whole bar in 3/4), the pitch classes sounding,
/// weighted by how long, matched against major, minor and seventh chords. Only clear matches are named - a melody
/// on its own gets nothing - and a symbol is written only where the chord changes.
/// </summary>
static class ChordGuesser
{
    static readonly ChordKind[] Kinds = [ChordKind.Major, ChordKind.Minor, ChordKind.Dominant7, ChordKind.Minor7, ChordKind.Major7, ChordKind.Diminished];

    /// <summary>How much of what sounds must be in the chord.</summary>
    const double Coverage = 0.8;

    /// <summary>Each chord tone must make up at least this share (a seventh chord's seventh too).</summary>
    const double ToneShare = 0.07;

    public static List<ChordSymbol> Guess(Score score)
    {
        var result = new List<ChordSymbol>();
        var timing = score.Timing ?? ScoreTiming.Constant(120);
        double window = timing.Beats == 3 || timing.BarQuarters <= 2 ? timing.BarQuarters : timing.BarQuarters / 2;
        var notes = score.Parts.SelectMany(p => p.Notes).ToList();
        if (notes.Count == 0)
            return result;
        double endQuarters = timing.ToQuarters(notes.Max(n => n.StartSeconds + n.DurationSeconds));
        ChordSymbol? last = null;
        for (double q = 0; q < endQuarters; q += window)
        {
            double from = timing.ToSeconds(q), to = timing.ToSeconds(q + window);
            var weights = new double[12];
            int? bass = null;
            foreach (var n in notes)
            {
                double overlap = Math.Min(to, n.StartSeconds + n.DurationSeconds) - Math.Max(from, n.StartSeconds);
                if (overlap <= 0)
                    continue;
                weights[n.Pitch % 12] += overlap;
                if (bass == null || n.Pitch < bass)
                    bass = n.Pitch;
            }
            var chord = Match(weights, bass);
            if (chord == null)
                continue;
            if (last != null && last.Root == chord.Value.Root && last.Kind == chord.Value.Kind)
                continue;
            last = new ChordSymbol(from, chord.Value.Root, chord.Value.Kind);
            result.Add(last);
        }
        return result;
    }

    /// <summary>The chord these pitch-class weights spell, if one clearly does.</summary>
    public static (int Root, ChordKind Kind)? Match(double[] weights, int? bass)
    {
        double total = weights.Sum();
        if (total <= 0 || weights.Count(w => w > total * 0.02) < 3)
            return null; // one or two notes: a melody, an interval - no chord to name
        (int, ChordKind)? best = null;
        double bestScore = double.MinValue;
        for (int root = 0; root < 12; root++)
            foreach (var kind in Kinds)
            {
                var tones = Chords.PitchClasses(root, kind);
                if (tones.Any(t => weights[t] < total * ToneShare))
                    continue;
                double inside = tones.Sum(t => weights[t]);
                if (inside < total * Coverage)
                    continue;
                // More of the sound explained is better; a simpler chord wins a tie; the bass on the root helps.
                double score = inside / total - 0.04 * tones.Count + (bass is { } b && b % 12 == root ? 0.05 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (root, kind);
                }
            }
        return best;
    }
}
