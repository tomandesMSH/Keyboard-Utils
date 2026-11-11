using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace KeyboardUtils;

/// <summary>
/// Minimal MusicXML reader - covers what MuseScore actually exports (score-partwise, measures, notes/rests/chords,
/// backup/forward, divisions, tempo changes and ties). Not a full MusicXML implementation: things like grace notes
/// or multi-staff clefs are not specially handled, but the resulting timing is still correct for a flat note list.
/// </summary>
static class MusicXmlImporter
{
    static readonly Dictionary<string, int> StepSemitones = new() { ["C"] = 0, ["D"] = 2, ["E"] = 4, ["F"] = 5, ["G"] = 7, ["A"] = 9, ["B"] = 11 };

    public static Score Import(string path)
    {
        var xml = path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase) ? ReadFromMxl(path) : File.ReadAllText(path);
        var root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException(T("Prázdný MusicXML soubor.", "Empty MusicXML file."));

        if (root.Name.LocalName != "score-partwise")
            throw new NotSupportedException(T("Je podporovaný jen score-partwise MusicXML (export MuseScore).",
                "Only score-partwise MusicXML is supported (MuseScore's export)."));

        var partNames = root.Element("part-list")?.Elements("score-part")
            .Where(e => e.Attribute("id") != null)
            .ToDictionary(e => e.Attribute("id")!.Value, e => (string?)e.Element("part-name"))
            ?? [];

        var score = new Score { Title = Path.GetFileNameWithoutExtension(path) };
        // Every part repeats the tempo and time signature; the first one's are the score's.
        TimingReader? timing = new();

        foreach (var partElement in root.Elements("part"))
        {
            string id = partElement.Attribute("id")?.Value ?? "";
            string? name = partNames.TryGetValue(id, out var n) ? n : null;
            var part = new ScorePart
            {
                Name = string.IsNullOrWhiteSpace(name) ? T($"Část {score.Parts.Count + 1}", $"Part {score.Parts.Count + 1}") : name,
            };
            ImportPart(partElement, part, timing);
            if (timing != null)
            {
                score.Timing = new ScoreTiming(timing.Tempo, timing.Beats, timing.BeatType);
                score.KeyFifths = timing.KeyFifths;
                score.KeyMinor = timing.KeyMinor;
                timing = null;
            }
            if (part.Notes.Count > 0)
                score.Parts.Add(part);
        }

        return score;
    }

    /// <summary>Collects tempo changes and the first time signature while a part is read.</summary>
    sealed class TimingReader
    {
        public List<TempoPoint> Tempo { get; } = [];
        public int Beats { get; set; } = 4;
        public int BeatType { get; set; } = 4;
        public bool HasTimeSignature { get; set; }
        public int? KeyFifths { get; set; }
        public bool KeyMinor { get; set; }
    }

    static void ImportPart(XElement partElement, ScorePart part, TimingReader? timing)
    {
        int divisions = 1;
        double microsecondsPerQuarter = 500_000; // 120 BPM default, used until a <sound tempo="..."/> says otherwise.
        double currentSeconds = 0;
        double currentQuarters = 0;
        double lastNoteStartSeconds = 0;
        var tiedNotes = new Dictionary<int, int>(); // pitch -> index in part.Notes of a note whose tie hasn't stopped yet
        int transpose = 0; // semitones from written to sounding (<transpose>)

        double SecondsPerDivision() => microsecondsPerQuarter / 1_000_000.0 / divisions;
        void Advance(double durationDivisions)
        {
            currentSeconds += durationDivisions * SecondsPerDivision();
            currentQuarters += durationDivisions / divisions;
        }
        void SetTempo(string bpm)
        {
            microsecondsPerQuarter = 60_000_000.0 / ParseDouble(bpm);
            timing?.Tempo.Add(new TempoPoint(currentSeconds, currentQuarters, 1_000_000.0 / microsecondsPerQuarter));
        }
        timing?.Tempo.Add(new TempoPoint(0, 0, 2));

        foreach (var measure in partElement.Elements("measure"))
        {
            foreach (var element in measure.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        if (element.Element("divisions") is { } div)
                            divisions = (int)ParseDouble(div.Value);
                        // A transposing instrument (B♭ clarinet: chromatic -2) is written as it's read; the score
                        // holds what sounds.
                        if (element.Element("transpose") is { } transposeElement)
                            transpose = (int)Math.Round(ParseDouble((string?)transposeElement.Element("chromatic") ?? "0"))
                                + 12 * (int)Math.Round(ParseDouble((string?)transposeElement.Element("octave-change") ?? "0"));
                        if (timing is { KeyFifths: null } && int.TryParse((string?)element.Element("key")?.Element("fifths"), out int fifths))
                        {
                            timing.KeyFifths = transpose % 12 == 0
                                ? Math.Clamp(fifths, -7, 7)
                                : KeySignature.Transposed(fifths, transpose);
                            timing.KeyMinor = (string?)element.Element("key")?.Element("mode") == "minor";
                        }
                        if (timing is { HasTimeSignature: false } && element.Element("time") is { } time
                            && int.TryParse((string?)time.Element("beats"), out int beats)
                            && int.TryParse((string?)time.Element("beat-type"), out int beatType))
                        {
                            timing.Beats = beats;
                            timing.BeatType = beatType;
                            timing.HasTimeSignature = true;
                        }
                        break;

                    case "direction":
                        if (element.Element("sound")?.Attribute("tempo") is { } dirTempo)
                            SetTempo(dirTempo.Value);
                        break;

                    case "sound":
                        if (element.Attribute("tempo") is { } tempo)
                            SetTempo(tempo.Value);
                        break;

                    case "backup":
                        Advance(-ParseDouble(element.Element("duration")?.Value ?? "0"));
                        break;

                    case "forward":
                        Advance(ParseDouble(element.Element("duration")?.Value ?? "0"));
                        break;

                    case "note":
                        bool isChordNote = element.Element("chord") != null;
                        bool isRest = element.Element("rest") != null;
                        double durationDivisions = ParseDouble(element.Element("duration")?.Value ?? "0");
                        double durationSeconds = durationDivisions * SecondsPerDivision();
                        double startSeconds = isChordNote ? lastNoteStartSeconds : currentSeconds;

                        if (!isRest && element.Element("pitch") is { } pitch)
                        {
                            int midi = Math.Clamp(ToMidiNumber(pitch) + transpose, 0, 127);
                            var ties = element.Elements("tie").Select(t => t.Attribute("type")?.Value).ToList();
                            // A tie continues the same sounding note (across a bar line, or a length with no single
                            // symbol): extend it instead of striking the key again.
                            if (ties.Contains("stop") && tiedNotes.Remove(midi, out int tiedIndex))
                            {
                                var tied = part.Notes[tiedIndex];
                                part.Notes[tiedIndex] = new ScoreNote
                                {
                                    Pitch = midi,
                                    StartSeconds = tied.StartSeconds,
                                    DurationSeconds = startSeconds + durationSeconds - tied.StartSeconds,
                                    Velocity = tied.Velocity,
                                };
                                if (ties.Contains("start"))
                                    tiedNotes[midi] = tiedIndex;
                            }
                            else
                            {
                                part.Notes.Add(new ScoreNote
                                {
                                    Pitch = midi,
                                    StartSeconds = startSeconds,
                                    DurationSeconds = durationSeconds,
                                });
                                if (ties.Contains("start"))
                                    tiedNotes[midi] = part.Notes.Count - 1;
                            }
                        }

                        if (!isChordNote)
                        {
                            lastNoteStartSeconds = currentSeconds;
                            Advance(durationDivisions);
                        }
                        break;
                }
            }
        }
    }

    static int ToMidiNumber(XElement pitch)
    {
        string step = pitch.Element("step")?.Value ?? "C";
        int octave = (int)ParseDouble(pitch.Element("octave")?.Value ?? "4");
        double alter = pitch.Element("alter") is { } a ? ParseDouble(a.Value) : 0;
        int semitone = StepSemitones.TryGetValue(step, out var s) ? s : 0;
        return Math.Clamp((octave + 1) * 12 + semitone + (int)Math.Round(alter), 0, 127);
    }

    static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>A .mxl is a zip; the real file is named by META-INF/container.xml, or just the first top-level .xml.</summary>
    static string ReadFromMxl(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        string? rootPath = null;
        if (archive.GetEntry("META-INF/container.xml") is { } container)
        {
            using var stream = container.Open();
            rootPath = XDocument.Load(stream).Descendants("rootfile").FirstOrDefault()?.Attribute("full-path")?.Value;
        }

        var entry = (rootPath != null ? archive.GetEntry(rootPath) : null)
            ?? archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new InvalidDataException(T("V .mxl souboru nebyl nalezen notový zápis.", "No score found inside the .mxl file."));

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        return reader.ReadToEnd();
    }
}
