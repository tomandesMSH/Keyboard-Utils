using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace KeyboardUtils;

/// <summary>
/// Minimal MusicXML reader - covers what MuseScore actually exports (score-partwise, measures, notes/rests/chords,
/// backup/forward, divisions and tempo changes). Not a full MusicXML implementation: things like ties, grace notes
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

        foreach (var partElement in root.Elements("part"))
        {
            string id = partElement.Attribute("id")?.Value ?? "";
            string? name = partNames.TryGetValue(id, out var n) ? n : null;
            var part = new ScorePart
            {
                Name = string.IsNullOrWhiteSpace(name) ? T($"Část {score.Parts.Count + 1}", $"Part {score.Parts.Count + 1}") : name,
            };
            ImportPart(partElement, part);
            if (part.Notes.Count > 0)
                score.Parts.Add(part);
        }

        return score;
    }

    static void ImportPart(XElement partElement, ScorePart part)
    {
        int divisions = 1;
        double microsecondsPerQuarter = 500_000; // 120 BPM default, used until a <sound tempo="..."/> says otherwise.
        double currentSeconds = 0;
        double lastNoteStartSeconds = 0;

        double SecondsPerDivision() => microsecondsPerQuarter / 1_000_000.0 / divisions;

        foreach (var measure in partElement.Elements("measure"))
        {
            foreach (var element in measure.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        if (element.Element("divisions") is { } div)
                            divisions = (int)ParseDouble(div.Value);
                        break;

                    case "direction":
                        if (element.Element("sound")?.Attribute("tempo") is { } dirTempo)
                            microsecondsPerQuarter = 60_000_000.0 / ParseDouble(dirTempo.Value);
                        break;

                    case "sound":
                        if (element.Attribute("tempo") is { } tempo)
                            microsecondsPerQuarter = 60_000_000.0 / ParseDouble(tempo.Value);
                        break;

                    case "backup":
                        currentSeconds -= ParseDouble(element.Element("duration")?.Value ?? "0") * SecondsPerDivision();
                        break;

                    case "forward":
                        currentSeconds += ParseDouble(element.Element("duration")?.Value ?? "0") * SecondsPerDivision();
                        break;

                    case "note":
                        bool isChordNote = element.Element("chord") != null;
                        bool isRest = element.Element("rest") != null;
                        double durationSeconds = ParseDouble(element.Element("duration")?.Value ?? "0") * SecondsPerDivision();
                        double startSeconds = isChordNote ? lastNoteStartSeconds : currentSeconds;

                        if (!isRest && element.Element("pitch") is { } pitch)
                        {
                            part.Notes.Add(new ScoreNote
                            {
                                Pitch = ToMidiNumber(pitch),
                                StartSeconds = startSeconds,
                                DurationSeconds = durationSeconds,
                            });
                        }

                        if (!isChordNote)
                        {
                            lastNoteStartSeconds = currentSeconds;
                            currentSeconds += durationSeconds;
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
