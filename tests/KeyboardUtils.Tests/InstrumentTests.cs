namespace KeyboardUtils.Tests;

public class InstrumentTests
{
    static Instrument Get(string id) => Instrument.ById(id);

    [Fact]
    public void Transposing_instruments_read_higher_than_they_sound()
    {
        var clarinet = Get("clarinet");
        Assert.Equal(62, clarinet.Written(60));          // concert C sounds as a written D
        Assert.Equal(60, clarinet.Sounding(62));
        Assert.Equal(1, clarinet.WrittenFifths(-1));     // concert F major -> written G major
        Assert.Equal(-1, clarinet.ConcertFifths(1));
        Assert.Equal(3, Get("altosax").WrittenFifths(0)); // concert C major -> written A major
        Assert.Equal(-5, clarinet.WrittenFifths(5));      // B major -> C♯ major, written as D♭ major
        Assert.Equal(2, Get("guitar").WrittenFifths(2));  // an octave: the same key
        Assert.Equal(52, Get("guitar").Written(40));      // low E sounds E2, is written E3
    }

    [Fact]
    public void Unknown_ids_fall_back_to_piano()
    {
        Assert.True(Instrument.ById(null).IsPiano);
        Assert.True(Instrument.ById("theremin").IsPiano);
        Assert.Equal("violin", Instrument.ById("violin").Id);
        Assert.All(Instrument.All, i => Assert.True(i.Low < i.High));
        Assert.All(Instrument.All.Where(i => i.Strings.Length > 0), i => Assert.All(i.Strings, s => Assert.InRange(s, i.Low, i.High)));
    }

    [Fact]
    public void Key_signatures_transpose_by_fifths()
    {
        Assert.Equal(2, KeySignature.Transposed(0, 2));
        Assert.Equal(-5, KeySignature.Transposed(5, 2));
        Assert.Equal(3, KeySignature.Transposed(0, 9));
        Assert.Equal(0, KeySignature.Transposed(0, 12));
        Assert.Equal(-1, KeySignature.Transposed(1, -2));
    }

    [Fact]
    public void Clef_bottom_lines()
    {
        Assert.Equal(30, Instrument.BottomLine(Clef.Treble)); // E4
        Assert.Equal(24, Instrument.BottomLine(Clef.Alto));   // F3: middle line C4
        Assert.Equal(22, Instrument.BottomLine(Clef.Tenor));  // D3: fourth line C4
        Assert.Equal(18, Instrument.BottomLine(Clef.Bass));   // G2
    }

    [Fact]
    public void A_single_staff_shows_written_pitches_and_keeps_the_sounding_note()
    {
        var notes = new[] { new ScoreNote { Pitch = 60, StartSeconds = 0, DurationSeconds = 0.5 }, new ScoreNote { Pitch = 40, StartSeconds = 0.5, DurationSeconds = 0.5 } };
        var staff = SheetQuantizer.Quantize(notes.Select(n => (n, 0)), s => s * 8, 16, _ => PartStaff.Treble, transpose: 2);
        var written = staff.Treble.SelectMany(s => s.Notes).ToList();
        Assert.Empty(staff.Bass.SelectMany(s => s.Notes));
        Assert.Equal([62, 42], written.Select(n => n.Pitch));
        Assert.Equal([60, 40], written.Select(n => n.Source.Pitch));
    }

    const string ClarinetXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>Clarinet in Bb</part-name></score-part></part-list>
          <part id="P1">
            <measure number="1">
              <attributes>
                <divisions>1</divisions>
                <key><fifths>1</fifths></key>
                <time><beats>4</beats><beat-type>4</beat-type></time>
                <clef><sign>G</sign><line>2</line></clef>
                <transpose><diatonic>-1</diatonic><chromatic>-2</chromatic></transpose>
              </attributes>
              <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration></note>
              <note><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration></note>
            </measure>
          </part>
        </score-partwise>
        """;

    [Fact]
    public void Musicxml_transposition_is_read_as_sounding_pitch()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-clarinet-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, ClarinetXml);
            var score = ScoreImporter.Import(path);
            Assert.Equal([60, 65], score.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch));
            Assert.Equal(-1, score.KeyFifths); // written G major = concert F major
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("clarinet", "<chromatic>-2</chromatic>", "<sign>G</sign>")]
    [InlineData("guitar", "<octave-change>-1</octave-change>", "<clef-octave-change>-1</clef-octave-change>")]
    [InlineData("viola", "<sign>C</sign>", "<line>3</line>")]
    [InlineData("recorder", "<octave-change>1</octave-change>", "<sign>G</sign>")]
    public void Musicxml_export_writes_the_instruments_part_and_reads_back_the_same(string id, string expected1, string expected2)
    {
        var instrument = Get(id);
        string path = Path.Combine(Path.GetTempPath(), $"kbu-export-{Guid.NewGuid():N}.musicxml");
        try
        {
            var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: 1), instrument);
            ScoreExporter.Save(score, path, 80, instrument);
            string xml = File.ReadAllText(path);
            Assert.Contains(expected1, xml);
            Assert.Contains(expected2, xml);
            Assert.DoesNotContain("<staves>", xml);
            var back = ScoreImporter.Import(path);
            Assert.Equal(score.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch),
                back.Parts.SelectMany(p => p.Notes).OrderBy(n => n.StartSeconds).Select(n => n.Pitch));
            Assert.Equal(score.KeyFifths, back.KeyFifths);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
