namespace KeyboardUtils;

/// <summary>A note as written on the staff: its pitch, plus the score note it came from (for highlighting).</summary>
sealed record SheetNote(int Pitch, ScoreNote Source, int PartIndex);

/// <summary>
/// What sounds during one stretch of a staff, in 16th notes: a chord (one or more notes) or a rest (none).
/// <paramref name="TiedFromPrevious"/>: the continuation of a note split at a bar line.
/// </summary>
sealed record Segment(int Start, int Length, IReadOnlyList<SheetNote> Notes, bool TiedFromPrevious = false)
{
    public bool IsRest => Notes.Count == 0;
}

/// <summary>A written note value: its length in 16ths and its MusicXML type name.</summary>
readonly record struct NoteValue(int Length, string Type, bool Dotted);

/// <summary>A piano grand staff: everything on a 16th-note grid, split into treble and bass, cut at bar lines.</summary>
sealed record GrandStaff(List<Segment> Treble, List<Segment> Bass, int Measures, int MeasureLength);

/// <summary>
/// Turns a score's notes (absolute seconds) into written music: snapped to 16th notes, from middle C up on the
/// treble staff and below it on the bass staff, one voice per staff. Shared by the MusicXML export and the sheet
/// music view, so what you see is what gets saved.
/// </summary>
static class SheetQuantizer
{
    /// <summary>Grid units per quarter note: 4 = 16th notes, the finest value written.</summary>
    public const int Divisions = 4;

    /// <summary>Notes from middle C up go to the treble staff, the rest to the bass staff.</summary>
    public const int SplitPitch = 60;

    /// <summary>Written durations, longest first: whole, dotted half, half, dotted quarter, quarter, dotted 8th, 8th, 16th.</summary>
    static readonly NoteValue[] NoteValues =
    [
        new(16, "whole", false), new(12, "half", true), new(8, "half", false), new(6, "quarter", true),
        new(4, "quarter", false), new(3, "eighth", true), new(2, "eighth", false), new(1, "16th", false),
    ];

    /// <param name="notes">The notes to write, with the index of the part each is from.</param>
    /// <param name="toUnits">Seconds to 16th notes since the start (the score's tempo).</param>
    /// <param name="measureLength">A bar in 16ths (16 = 4/4).</param>
    /// <param name="staffOf">A part's <see cref="ScorePart.Staff"/> by its index; null = every part split by pitch.</param>
    /// <param name="transpose">Added to each pitch: the written pitch of a transposing instrument
    /// (<see cref="Instrument.Transpose"/>). <see cref="SheetNote.Source"/> keeps the sounding note.</param>
    public static GrandStaff Quantize(IEnumerable<(ScoreNote Note, int Part)> notes, Func<double, double> toUnits, int measureLength,
        Func<int, PartStaff>? staffOf = null, int transpose = 0)
    {
        bool OnTreble(SheetNote note) => (staffOf?.Invoke(note.PartIndex) ?? PartStaff.Auto) switch
        {
            PartStaff.Treble => true,
            PartStaff.Bass => false,
            _ => note.Pitch >= SplitPitch,
        };
        var grid = notes
            .Select(n =>
            {
                int start = (int)Math.Round(toUnits(n.Note.StartSeconds));
                int end = Math.Max(start + 1, (int)Math.Round(toUnits(n.Note.StartSeconds + n.Note.DurationSeconds)));
                return (Note: new SheetNote(Math.Clamp(n.Note.Pitch + transpose, 0, 127), n.Note, n.Part), Start: Math.Max(0, start), End: Math.Max(1, end));
            })
            .ToList();

        int totalLength = grid.Count > 0 ? grid.Max(n => n.End) : measureLength;
        int measures = Math.Max(1, (totalLength + measureLength - 1) / measureLength);
        return new GrandStaff(
            BuildStaff(grid.Where(n => OnTreble(n.Note)), measures, measureLength),
            BuildStaff(grid.Where(n => !OnTreble(n.Note)), measures, measureLength),
            measures, measureLength);
    }

    /// <summary>
    /// Flattens one staff into back-to-back segments covering whole measures. One voice per staff keeps it
    /// simple: notes starting together form a chord, and each chord lasts until its shortest note ends or the
    /// next chord starts, whichever is first; gaps become rests.
    /// </summary>
    static List<Segment> BuildStaff(IEnumerable<(SheetNote Note, int Start, int End)> notes, int measures, int measureLength)
    {
        var segments = new List<Segment>();
        var onsets = notes.GroupBy(n => n.Start).OrderBy(g => g.Key).ToList();
        int time = 0;
        for (int i = 0; i < onsets.Count; i++)
        {
            int start = onsets[i].Key;
            if (start > time)
                segments.Add(new Segment(time, start - time, []));
            int end = onsets[i].Min(n => n.End);
            if (i + 1 < onsets.Count)
            {
                int next = onsets[i + 1].Key;
                // Played notes are let go a little early (MIDI files, recordings): a gap of a 16th, or of under a
                // quarter of the note, is articulation, not a written rest - hold the note until the next one.
                int gap = next - end;
                if (gap > 0 && (gap <= 1 || gap * 4 <= end - start))
                    end = next;
                end = Math.Min(end, next);
            }
            // The same key struck twice at once (two parts doubling a note) is written once.
            var chord = onsets[i].GroupBy(n => n.Note.Pitch).Select(g => g.First().Note).OrderBy(n => n.Pitch).ToList();
            segments.Add(new Segment(start, end - start, chord));
            time = end;
        }
        int total = measures * measureLength;
        if (time < total)
            segments.Add(new Segment(time, total - time, []));

        // Nothing may cross a bar line: split there (a note's halves get tied).
        var split = new List<Segment>();
        foreach (var segment in segments)
        {
            int start = segment.Start, remaining = segment.Length;
            while (remaining > 0)
            {
                int room = measureLength - start % measureLength;
                int length = Math.Min(room, remaining);
                split.Add(segment with { Start = start, Length = length, TiedFromPrevious = start != segment.Start && !segment.IsRest });
                start += length;
                remaining -= length;
            }
        }
        return split;
    }

    /// <summary>Lengths like 5 or 7 sixteenths have no single symbol: they're written as tied pieces (4 + 1, 6 + 1...).</summary>
    public static List<NoteValue> Decompose(int length)
    {
        var pieces = new List<NoteValue>();
        foreach (var value in NoteValues)
            while (length >= value.Length)
            {
                pieces.Add(value);
                length -= value.Length;
            }
        return pieces;
    }

    /// <summary>Segments of one measure, in order.</summary>
    public static IEnumerable<(Segment Segment, int Index)> InMeasure(List<Segment> staff, int measure, int measureLength)
    {
        int from = measure * measureLength, to = from + measureLength;
        for (int i = 0; i < staff.Count; i++)
            if (staff[i].Start >= from && staff[i].Start < to)
                yield return (staff[i], i);
    }
}
