using System.Xml.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace KeyboardUtils;

/// <summary>
/// Saves a <see cref="Score"/> as MIDI (exact timing) or MusicXML (sheet music for MuseScore). A song opened from a
/// file keeps its own tempo (with its changes), time signature and key; a recording or converted audio has none, so
/// it's written at the tempo picked when saving, in 4/4, in a key guessed from its notes. MusicXML snaps every
/// note to a 16th-note grid - the same notes the sheet music view shows.
/// </summary>
static class ScoreExporter
{
    const int TicksPerQuarter = 480;

    public static string FileFilter =>
        "MIDI (*.mid)|*.mid|MusicXML (*.musicxml)|*.musicxml";

    /// <summary>Picks the format by extension. Throws IOException/UnauthorizedAccessException on write errors.</summary>
    /// <param name="bpm">Tempo for a score without its own (<see cref="Score.Timing"/> null).</param>
    public static void Save(Score score, string path, int bpm)
    {
        if (Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase))
            SaveMidi(score, path, bpm);
        else
            SaveMusicXml(score, path, bpm);
    }

    static ScoreTiming TimingOf(Score score, int bpm) => score.Timing ?? ScoreTiming.Constant(bpm);

    // --- MIDI ------------------------------------------------------------------------------------

    public static void SaveMidi(Score score, string path, int bpm)
    {
        var timing = TimingOf(score, bpm);
        long Ticks(double seconds) => (long)Math.Round(Math.Max(0, timing.ToQuarters(seconds)) * TicksPerQuarter);

        TempoMap tempoMap;
        using (var tempoManager = new TempoMapManager(new TicksPerQuarterNoteTimeDivision(TicksPerQuarter)))
        {
            foreach (var point in timing.Tempo)
                tempoManager.SetTempo((long)Math.Round(point.Quarters * TicksPerQuarter), new Tempo((long)Math.Round(1_000_000 / point.QuartersPerSecond)));
            tempoManager.SetTimeSignature(0, new TimeSignature(timing.Beats, timing.BeatType));
            tempoMap = tempoManager.TempoMap;
        }

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
        // The key goes in with the first track, at the very start.
        if (chunks.Count > 0)
            chunks[0].Events.Insert(0, new KeySignatureEvent((sbyte)KeySignature.Of(score), 0));

        var file = new MidiFile(chunks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarter) };
        file.ReplaceTempoMap(tempoMap);
        // Texts (part names) as UTF-8: DryWetMidi's default, ASCII, turns "Část 1" into "??st 1".
        file.Write(path, overwriteFile: true, settings: new WritingSettings { TextEncoding = System.Text.Encoding.UTF8 });
    }

    // --- MusicXML --------------------------------------------------------------------------------

    const int Divisions = SheetQuantizer.Divisions;

    public static void SaveMusicXml(Score score, string path, int bpm)
    {
        var timing = TimingOf(score, bpm);
        int measureLength = timing.MeasureSixteenths;
        int fifths = KeySignature.Of(score);
        bool minor = KeySignature.IsMinor(score);
        var spelling = KeySignature.Spelling(fifths, minor);
        double ToUnits(double seconds) => timing.ToQuarters(seconds) * Divisions;

        var notes = score.Parts.SelectMany((p, index) => p.Notes.Select(n => (n, index)));
        var (treble, bass, measures, _) = SheetQuantizer.Quantize(notes, ToUnits, measureLength);

        // Tempo changes, on the grid, by bar: the first one is written with the opening attributes.
        var tempoChanges = timing.Tempo
            .Select(p => (Units: (int)Math.Round(p.Quarters * Divisions), Bpm: Math.Round(p.QuartersPerSecond * 60, 1)))
            .GroupBy(t => t.Units).Select(g => g.Last())
            .ToList();

        var trebleAccidentals = new AccidentalState(fifths, minor);
        var bassAccidentals = new AccidentalState(fifths, minor);
        var part = new XElement("part", new XAttribute("id", "P1"));
        double? lastBpm = null;
        for (int m = 0; m < measures; m++)
        {
            var measure = new XElement("measure", new XAttribute("number", m + 1));
            if (m == 0)
                measure.Add(new XElement("attributes",
                    new XElement("divisions", Divisions),
                    new XElement("key", new XElement("fifths", fifths), new XElement("mode", minor ? "minor" : "major")),
                    new XElement("time", new XElement("beats", timing.Beats), new XElement("beat-type", timing.BeatType)),
                    new XElement("staves", 2),
                    new XElement("clef", new XAttribute("number", 1), new XElement("sign", "G"), new XElement("line", 2)),
                    new XElement("clef", new XAttribute("number", 2), new XElement("sign", "F"), new XElement("line", 4))));

            int measureStart = m * measureLength;
            foreach (var (units, tempo) in tempoChanges.Where(t => (m == 0 || t.Units >= measureStart) && t.Units < measureStart + measureLength))
            {
                if (tempo == lastBpm)
                    continue;
                lastBpm = tempo;
                var direction = new XElement("direction", new XAttribute("placement", "above"),
                    new XElement("direction-type", new XElement("metronome",
                        new XElement("beat-unit", "quarter"), new XElement("per-minute", Math.Round(tempo)))));
                // Where in the bar the change happens, counted from the bar's start.
                if (units > measureStart)
                    direction.Add(new XElement("offset", units - measureStart));
                direction.Add(new XElement("sound", new XAttribute("tempo", tempo.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                measure.Add(direction);
            }

            WriteMeasure(measure, treble, m, measureLength, trebleAccidentals, spelling, staff: 1, voice: 1);
            measure.Add(new XElement("backup", new XElement("duration", measureLength)));
            WriteMeasure(measure, bass, m, measureLength, bassAccidentals, spelling, staff: 2, voice: 5);
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

    static void WriteMeasure(XElement measure, List<Segment> staffSegments, int measureIndex, int measureLength,
        AccidentalState accidentals, (int Letter, int Alter)[] spelling, int staff, int voice)
    {
        var indices = SheetQuantizer.InMeasure(staffSegments, measureIndex, measureLength).Select(s => s.Index).ToList();
        accidentals.NewBar();

        if (indices.All(i => staffSegments[i].IsRest))
        {
            measure.Add(new XElement("note", new XElement("rest", new XAttribute("measure", "yes")),
                new XElement("duration", measureLength), new XElement("voice", voice), new XElement("staff", staff)));
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
                    measure.Add(NoteElement(null, null, chord: false, value, tieStart: false, tieStop: false, staff, voice));
                    continue;
                }
                bool tieStop = p > 0 || continuesFromBefore;
                bool tieStart = p < pieces.Count - 1 || continuesAfter;
                for (int k = 0; k < segment.Notes.Count; k++)
                {
                    int pitch = segment.Notes[k].Pitch;
                    int? sign = accidentals.Next(pitch, tieStop);
                    measure.Add(NoteElement((pitch, spelling[pitch % 12]), sign, chord: k > 0, value, tieStart, tieStop, staff, voice));
                }
            }
        }
    }

    static XElement NoteElement((int Pitch, (int Letter, int Alter) Spelled)? note, int? accidental, bool chord, NoteValue value,
        bool tieStart, bool tieStop, int staff, int voice)
    {
        // Element order is fixed by the MusicXML schema.
        var element = new XElement("note");
        if (chord)
            element.Add(new XElement("chord"));
        if (note is { } n)
        {
            var pitch = new XElement("pitch", new XElement("step", KeySignature.LetterNames[n.Spelled.Letter]));
            if (n.Spelled.Alter != 0)
                pitch.Add(new XElement("alter", n.Spelled.Alter));
            pitch.Add(new XElement("octave", KeySignature.Diatonic(n.Pitch, n.Spelled) / 7));
            element.Add(pitch);
        }
        else
            element.Add(new XElement("rest"));
        element.Add(new XElement("duration", value.Length));
        if (tieStop)
            element.Add(new XElement("tie", new XAttribute("type", "stop")));
        if (tieStart)
            element.Add(new XElement("tie", new XAttribute("type", "start")));
        element.Add(new XElement("voice", voice));
        element.Add(new XElement("type", value.Type));
        if (value.Dotted)
            element.Add(new XElement("dot"));
        if (accidental is { } sign)
            element.Add(new XElement("accidental", sign > 0 ? "sharp" : sign < 0 ? "flat" : "natural"));
        element.Add(new XElement("staff", staff));
        if (tieStart || tieStop)
        {
            var notations = new XElement("notations");
            if (tieStop)
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));
            if (tieStart)
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            element.Add(notations);
        }
        return element;
    }
}
