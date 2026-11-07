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

    const int Divisions = SheetQuantizer.Divisions;

    /// <summary>4/4 time.</summary>
    const int MeasureLength = 4 * Divisions;

    public static void SaveMusicXml(Score score, string path, int bpm)
    {
        double unitsPerSecond = bpm / 60.0 * Divisions;
        var notes = score.Parts.SelectMany((p, index) => p.Notes.Select(n => (n, index)));
        var (treble, bass, measures, _) = SheetQuantizer.Quantize(notes, seconds => seconds * unitsPerSecond, MeasureLength);

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

    static void WriteMeasure(XElement measure, List<Segment> staffSegments, int measureIndex, int staff, int voice)
    {
        var indices = SheetQuantizer.InMeasure(staffSegments, measureIndex, MeasureLength).Select(s => s.Index).ToList();

        if (indices.All(i => staffSegments[i].IsRest))
        {
            measure.Add(new XElement("note", new XElement("rest", new XAttribute("measure", "yes")),
                new XElement("duration", MeasureLength), new XElement("voice", voice), new XElement("staff", staff)));
            return;
        }

        foreach (int index in indices)
        {
            var segment = staffSegments[index];
            // Lengths like 5 or 7 sixteenths have no single symbol: write them as tied pieces (4 + 1, 6 + 1...).
            var pieces = SheetQuantizer.Decompose(segment.Length);
            bool continuesFromBefore = segment.TiedFromPrevious;
            bool continuesAfter = index + 1 < staffSegments.Count && staffSegments[index + 1].TiedFromPrevious;
            for (int p = 0; p < pieces.Count; p++)
            {
                var value = pieces[p];
                if (segment.IsRest)
                {
                    measure.Add(NoteElement(null, chord: false, value, tieStart: false, tieStop: false, staff, voice));
                    continue;
                }
                bool tieStop = p > 0 || continuesFromBefore;
                bool tieStart = p < pieces.Count - 1 || continuesAfter;
                for (int k = 0; k < segment.Notes.Count; k++)
                    measure.Add(NoteElement(segment.Notes[k].Pitch, chord: k > 0, value, tieStart, tieStop, staff, voice));
            }
        }
    }

    static XElement NoteElement(int? pitch, bool chord, NoteValue value, bool tieStart, bool tieStop, int staff, int voice)
    {
        // Element order is fixed by the MusicXML schema.
        var note = new XElement("note");
        if (chord)
            note.Add(new XElement("chord"));
        if (pitch is { } p)
        {
            var (step, alter) = SheetQuantizer.Spelling[p % 12];
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
        if (pitch is { } q && SheetQuantizer.Spelling[q % 12].Alter != 0)
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
