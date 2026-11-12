namespace KeyboardUtils.Tests;

public class TablatureTests
{
    static readonly Instrument Guitar = Instrument.ById("guitar");

    static ScoreNote Note(int pitch, double start = 0, int? tabString = null) =>
        new() { Pitch = pitch, StartSeconds = start, DurationSeconds = 0.5, TabString = tabString };

    [Fact]
    public void Open_c_major_chord_is_x32010()
    {
        var notes = new[] { 48, 52, 55, 60, 64 }.Select(p => Note(p)).ToList();
        var tab = Tablature.Assign(notes, Guitar.Strings);
        // (string, fret): C on A3, E on D2, G open, C on B1, E open.
        Assert.Equal([(1, 3), (2, 2), (3, 0), (4, 1), (5, 0)], notes.Select(n => (tab[n].String, tab[n].Semitones)));
    }

    [Fact]
    public void Notes_together_never_share_a_string()
    {
        // An E major chord written as six notes, plus a seventh that can't fit.
        var notes = new[] { 40, 47, 52, 56, 59, 64, 68 }.Select(p => Note(p)).ToList();
        var tab = Tablature.Assign(notes, Guitar.Strings);
        Assert.Equal(6, tab.Count);
        Assert.Equal(6, tab.Values.Select(s => s.String).Distinct().Count());
    }

    [Fact]
    public void A_scale_stays_in_first_position()
    {
        int[] cMajor = [48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67];
        var notes = cMajor.Select((p, i) => Note(p, i * 0.5)).ToList();
        var tab = Tablature.Assign(notes, Guitar.Strings);
        Assert.All(notes, n => Assert.InRange(tab[n].Semitones, 0, 3));
    }

    [Fact]
    public void A_melody_high_up_stays_in_its_position()
    {
        // Around the 7th fret: once there, the hand doesn't jump back to open strings for each note.
        int[] melody = [69, 71, 72, 74, 72, 71, 69];
        var notes = melody.Select((p, i) => Note(p, i * 0.5)).ToList();
        notes.Insert(0, Note(76, -0.5)); // starts on E5: the 12th fret of the high E string
        var tab = Tablature.Assign(notes, Guitar.Strings);
        // No falling back to open strings / first position in between.
        Assert.All(notes, n => Assert.True(tab[n].Semitones >= 5, $"{n.Pitch}: fret {tab[n].Semitones}"));
    }

    [Fact]
    public void A_string_written_in_the_file_is_kept()
    {
        // E4 on the B string (MusicXML string 2) = 5th fret, not the open high E.
        var note = Note(64, tabString: 2);
        var tab = Tablature.Assign([note], Guitar.Strings);
        Assert.Equal((4, 5), (tab[note].String, tab[note].Semitones));
    }

    [Fact]
    public void Out_of_reach_notes_get_no_number()
    {
        var low = Note(30);
        Assert.Empty(Tablature.Assign([low], Guitar.Strings));
    }

    [Fact]
    public void Musicxml_export_writes_string_and_fret_and_reads_the_string_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-tab-{Guid.NewGuid():N}.musicxml");
        try
        {
            var notes = new[] { 48, 52, 55, 60, 64 }.Select(p => Note(p)).ToList();
            var score = new Score
            {
                Title = "Tab",
                Parts = [new ScorePart { Name = "Guitar", Notes = notes }],
                Timing = ScoreTiming.Constant(120),
            };
            ScoreExporter.SaveMusicXml(score, path, 120, Guitar);
            string xml = File.ReadAllText(path);
            Assert.Contains("<technical>", xml);
            Assert.Contains("<fret>3</fret>", xml);

            var back = ScoreImporter.Import(path);
            var strings = back.Parts.SelectMany(p => p.Notes).OrderBy(n => n.Pitch).Select(n => n.TabString).ToList();
            // MusicXML counts from the highest string: C on A = 5, E on D = 4, G = 3, C on B = 2, E = 1.
            Assert.Equal([5, 4, 3, 2, 1], strings);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
