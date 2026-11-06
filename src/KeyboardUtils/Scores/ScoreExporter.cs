using System.Xml.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace KeyboardUtils;

/// <summary>
/// Saves a <see cref="Score"/> as MIDI (exact timing) or MusicXML (sheet music for MuseScore). Scores only know
/// seconds, so both need a tempo: MIDI just records it, MusicXML snaps every note to a 16th-note grid at it.
/// </summary>
static class ScoreExporter
{
    public static string FileFilter =>
        "MIDI (*.mid)|*.mid|MusicXML (*.musicxml)|*.musicxml";

    /// <summary>Picks the format by extension. Throws IOException/UnauthorizedAccessException on write errors.</summary>
    public static void Save(Score score, string path, int bpm)
    {
        if (Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase))
            SaveMidi(score, path, bpm);
        else
            SaveMusicXml(score, path, bpm);
    }

    // --- MIDI ------------------------------------------------------------------------------------

    public static void SaveMidi(Score score, string path, int bpm)
    {
        var tempoMap = TempoMap.Create(new TicksPerQuarterNoteTimeDivision(480), Tempo.FromBeatsPerMinute(bpm));
        long Ticks(double seconds) => TimeConverter.ConvertFrom(new MetricTimeSpan(TimeSpan.FromSeconds(Math.Max(0, seconds))), tempoMap);

        var chunks = new List<TrackChunk>();
        for (int i = 0; i < score.Parts.Count; i++)
        {
            var part = score.Parts[i];
            var chunk = new TrackChunk(new SequenceTrackNameEvent(part.Name));
            // Channel 10 (index 9) is drums in General MIDI - skip it so every part stays a pitched instrument.
            var channel = (FourBitNumber)(i % 15 >= 9 ? i % 15 + 1 : i % 15);
            using (var manager = chunk.ManageNotes())
            {
                foreach (var note in part.Notes)
                {
                    long start = Ticks(note.StartSeconds);
                    manager.Objects.Add(new Note((SevenBitNumber)Math.Clamp(note.Pitch, 0, 127), Math.Max(1, Ticks(note.StartSeconds + note.DurationSeconds) - start), start)
                    {
                        Channel = channel,
                        Velocity = (SevenBitNumber)Math.Clamp(note.Velocity, 1, 127),
                    });
                }
            }
            chunks.Add(chunk);
        }

        var file = new MidiFile(chunks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        file.ReplaceTempoMap(tempoMap);
        file.Write(path, overwriteFile: true);
    }

    // --- MusicXML --------------------------------------------------------------------------------

    /// <summary>Grid units per quarter note: 4 = 16th notes, the finest value written.</summary>
    const int Divisions = 4;

    /// <summary>4/4 time.</summary>
    const int MeasureLength = 4 * Divisions;

    /// <summary>Notes from middle C up go to the treble staff, the rest to the bass staff (a piano grand staff).</summary>
    const int SplitPitch = 60;

    static readonly (string Step, int Alter)[] Spelling =
        [("C", 0), ("C", 1), ("D", 0), ("D", 1), ("E", 0), ("F", 0), ("F", 1), ("G", 0), ("G", 1), ("A", 0), ("A", 1), ("B", 0)];

    /// <summary>Written durations, longest first: whole, dotted half, half, dotted quarter, quarter, dotted 8th, 8th, 16th.</summary>
    static readonly (int Length, string Type, bool Dotted)[] NoteValues =
        [(16, "whole", false), (12, "half", true), (8, "half", false), (6, "quarter", true), (4, "quarter", false), (3, "eighth", true), (2, "eighth", false), (1, "16th", false)];

    /// <summary>
    /// What sounds during one stretch of a staff: a chord (one or more pitches) or a rest (none).
    /// <paramref name="TiedFromPrevious"/>: the continuation of a note split at a bar line.
    /// </summary>
    sealed record Segment(int Start, int Length, IReadOnlyList<int> Pitches, bool TiedFromPrevious = false);

    public static void SaveMusicXml(Score score, string path, int bpm)
    {
        double unitsPerSecond = bpm / 60.0 * Divisions;
        var notes = score.Parts.SelectMany(p => p.Notes)
            .Select(n =>
            {
                int start = (int)Math.Round(n.StartSeconds * unitsPerSecond);
                int end = Math.Max(start + 1, (int)Math.Round((n.StartSeconds + n.DurationSeconds) * unitsPerSecond));
                return (n.Pitch, Start: start, End: end);
            })
            .ToList();

        int totalLength = notes.Count > 0 ? notes.Max(n => n.End) : MeasureLength;
        int measures = Math.Max(1, (totalLength + MeasureLength - 1) / MeasureLength);
        var treble = BuildStaff(notes.Where(n => n.Pitch >= SplitPitch), measures);
        var bass = BuildStaff(notes.Where(n => n.Pitch < SplitPitch), measures);

        var part = new XElement("part", new XAttribute("id", "P1"));
        for (int m = 0; m < measures; m++)
        {
            var measure = new XElement("measure", new XAttribute("number", m + 1));
            if (m == 0)
            {
                measure.Add(new XElement("attributes",
                    new XElement("divisions", Divisions),
                    new XElement("key", new XElement("fifths", 0)),
                    new XElement("time", new XElement("beats", 4), new XElement("beat-type", 4)),
                    new XElement("staves", 2),
                    new XElement("clef", new XAttribute("number", 1), new XElement("sign", "G"), new XElement("line", 2)),
                    new XElement("clef", new XAttribute("number", 2), new XElement("sign", "F"), new XElement("line", 4))));
                measure.Add(new XElement("direction", new XAttribute("placement", "above"),
                    new XElement("direction-type", new XElement("metronome",
                        new XElement("beat-unit", "quarter"), new XElement("per-minute", bpm))),
                    new XElement("sound", new XAttribute("tempo", bpm))));
            }
            WriteMeasure(measure, treble, m, staff: 1, voice: 1);
            measure.Add(new XElement("backup", new XElement("duration", MeasureLength)));
            WriteMeasure(measure, bass, m, staff: 2, voice: 5);
            part.Add(measure);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null),
            new XElement("score-partwise", new XAttribute("version", "4.0"),
                new XElement("work", new XElement("work-title", score.Title)),
                new XElement("part-list", new XElement("score-part", new XAttribute("id", "P1"), new XElement("part-name", "Piano"))),
                part));
        document.Save(path);
    }

    /// <summary>
    /// Flattens one staff into back-to-back segments covering whole measures. One voice per staff keeps it
    /// simple: notes starting together form a chord, and each chord lasts until its shortest note ends or the
    /// next chord starts, whichever is first; gaps become rests.
    /// </summary>
    static List<Segment> BuildStaff(IEnumerable<(int Pitch, int Start, int End)> notes, int measures)
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
                end = Math.Min(end, onsets[i + 1].Key);
            segments.Add(new Segment(start, end - start, onsets[i].Select(n => n.Pitch).Distinct().Order().ToList()));
            time = end;
        }
        int total = measures * MeasureLength;
        if (time < total)
            segments.Add(new Segment(time, total - time, []));

        // Nothing may cross a bar line: split there (a note's halves get tied in WriteMeasure).
        var split = new List<Segment>();
        foreach (var segment in segments)
        {
            int start = segment.Start, remaining = segment.Length;
            while (remaining > 0)
            {
                int room = MeasureLength - start % MeasureLength;
                int length = Math.Min(room, remaining);
                split.Add(segment with { Start = start, Length = length, TiedFromPrevious = start != segment.Start && segment.Pitches.Count > 0 });
                start += length;
                remaining -= length;
            }
        }
        return split;
    }

    static void WriteMeasure(XElement measure, List<Segment> staffSegments, int measureIndex, int staff, int voice)
    {
        int from = measureIndex * MeasureLength, to = from + MeasureLength;
        var indices = Enumerable.Range(0, staffSegments.Count).Where(i => staffSegments[i].Start >= from && staffSegments[i].Start < to).ToList();

        if (indices.All(i => staffSegments[i].Pitches.Count == 0))
        {
            measure.Add(new XElement("note", new XElement("rest", new XAttribute("measure", "yes")),
                new XElement("duration", MeasureLength), new XElement("voice", voice), new XElement("staff", staff)));
            return;
        }

        foreach (int index in indices)
        {
            var segment = staffSegments[index];
            // Lengths like 5 or 7 sixteenths have no single symbol: write them as tied pieces (4 + 1, 6 + 1...).
            var pieces = Decompose(segment.Length);
            bool continuesFromBefore = segment.TiedFromPrevious;
            bool continuesAfter = index + 1 < staffSegments.Count && staffSegments[index + 1].TiedFromPrevious;
            for (int p = 0; p < pieces.Count; p++)
            {
                var value = pieces[p];
                if (segment.Pitches.Count == 0)
                {
                    measure.Add(NoteElement(null, chord: false, value, tieStart: false, tieStop: false, staff, voice));
                    continue;
                }
                bool tieStop = p > 0 || continuesFromBefore;
                bool tieStart = p < pieces.Count - 1 || continuesAfter;
                for (int k = 0; k < segment.Pitches.Count; k++)
                    measure.Add(NoteElement(segment.Pitches[k], chord: k > 0, value, tieStart, tieStop, staff, voice));
            }
        }
    }

    static List<(int Length, string Type, bool Dotted)> Decompose(int length)
    {
        var pieces = new List<(int, string, bool)>();
        foreach (var value in NoteValues)
            while (length >= value.Length)
            {
                pieces.Add(value);
                length -= value.Length;
            }
        return pieces;
    }

    static XElement NoteElement(int? pitch, bool chord, (int Length, string Type, bool Dotted) value, bool tieStart, bool tieStop, int staff, int voice)
    {
        // Element order is fixed by the MusicXML schema.
        var note = new XElement("note");
        if (chord)
            note.Add(new XElement("chord"));
        if (pitch is { } p)
        {
            var (step, alter) = Spelling[p % 12];
            var pitchElement = new XElement("pitch", new XElement("step", step));
            if (alter != 0)
                pitchElement.Add(new XElement("alter", alter));
            pitchElement.Add(new XElement("octave", p / 12 - 1));
            note.Add(pitchElement);
        }
        else
            note.Add(new XElement("rest"));
        note.Add(new XElement("duration", value.Length));
        if (tieStop)
            note.Add(new XElement("tie", new XAttribute("type", "stop")));
        if (tieStart)
            note.Add(new XElement("tie", new XAttribute("type", "start")));
        note.Add(new XElement("voice", voice));
        note.Add(new XElement("type", value.Type));
        if (value.Dotted)
            note.Add(new XElement("dot"));
        if (pitch is { } q && Spelling[q % 12].Alter != 0)
            note.Add(new XElement("accidental", "sharp"));
        note.Add(new XElement("staff", staff));
        if (tieStart || tieStop)
        {
            var notations = new XElement("notations");
            if (tieStop)
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));
            if (tieStart)
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            note.Add(notations);
        }
        return note;
    }
}
