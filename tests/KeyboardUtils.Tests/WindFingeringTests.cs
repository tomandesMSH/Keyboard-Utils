namespace KeyboardUtils.Tests;

public class WindFingeringTests
{
    static readonly Instrument Recorder = Instrument.ById("recorder");
    static readonly Instrument Trumpet = Instrument.ById("trumpet");

    /// <summary>The holes as a string: thumb, 1-7; 1 closed, 0 open, 2 half.</summary>
    static string Holes(int writtenPitch, RecorderSystem system) =>
        string.Concat(WindFingerings.For(Recorder, Recorder.Sounding(writtenPitch), system)!.Keys.Select(k => k switch
        {
            KeyState.Closed => '1',
            KeyState.Half => '2',
            _ => '0',
        }));

    static string Valves(int writtenPitch) =>
        string.Concat(WindFingerings.For(Trumpet, Trumpet.Sounding(writtenPitch))!.Keys.Select((k, i) => k == KeyState.Closed ? (char)('1' + i) : '-'));

    [Theory]
    [InlineData("recorder")]
    [InlineData("trumpet")]
    public void Every_note_of_the_chart_has_a_fingering_with_one_state_per_key(string id)
    {
        var instrument = Instrument.ById(id);
        var chart = WindFingerings.ChartFor(instrument)!;
        var (low, high) = WindFingerings.WrittenRange(instrument)!.Value;
        foreach (var system in Enum.GetValues<RecorderSystem>())
            for (int written = low; written <= high; written++)
            {
                var fingering = WindFingerings.For(instrument, instrument.Sounding(written), system);
                Assert.NotNull(fingering);
                Assert.Equal(written, fingering.WrittenPitch);
                Assert.Equal(chart.Keys.Count, fingering.Keys.Count);
            }
    }

    [Fact]
    public void Outside_the_chart_there_is_no_fingering()
    {
        Assert.Null(WindFingerings.For(Recorder, Recorder.Sounding(59)));
        Assert.Null(WindFingerings.For(Recorder, Recorder.Sounding(87)));
        Assert.Null(WindFingerings.For(Trumpet, Trumpet.Sounding(53)));
        Assert.Null(WindFingerings.For(Trumpet, Trumpet.Sounding(85)));
    }

    [Fact]
    public void Instruments_without_a_chart_have_none()
    {
        foreach (var id in new[] { "piano", "guitar", "violin", "flute", "clarinet", "altosax" })
        {
            Assert.Null(WindFingerings.ChartFor(Instrument.ById(id)));
            Assert.Null(WindFingerings.For(Instrument.ById(id), 72));
        }
    }

    [Theory]
    [InlineData(60, "11111111")] // c1: everything
    [InlineData(64, "11111100")] // e1
    [InlineData(67, "11110000")] // g1
    [InlineData(71, "11000000")] // h1
    [InlineData(72, "10100000")] // c2: thumb + 2
    [InlineData(74, "00100000")] // d2: only 2
    [InlineData(76, "21111100")] // e2: pinched thumb
    public void Recorder_notes_both_systems_share(int written, string holes)
    {
        Assert.Equal(holes, Holes(written, RecorderSystem.Baroque));
        Assert.Equal(holes, Holes(written, RecorderSystem.German));
    }

    [Theory]
    [InlineData(65, "11111011", "11111000")] // f1: forked (baroque) / simple (German)
    [InlineData(66, "11110110", "11110111")] // fis1
    [InlineData(68, "11101120", "11101100")] // gis1
    [InlineData(70, "11011000", "11010110")] // b1
    [InlineData(77, "21111010", "21111000")] // f2
    public void Recorder_baroque_and_German_differ_where_they_should(int written, string baroque, string german)
    {
        Assert.Equal(baroque, Holes(written, RecorderSystem.Baroque));
        Assert.Equal(german, Holes(written, RecorderSystem.German));
    }

    [Fact]
    public void Recorder_is_read_an_octave_below_its_sound()
    {
        // The soprano recorder's lowest note sounds C5 (72) and is written c1 (60).
        Assert.Equal(60, WindFingerings.For(Recorder, 72)!.WrittenPitch);
    }

    [Fact]
    public void Recorder_top_c_sharp_says_to_close_the_bell()
    {
        Assert.NotNull(WindFingerings.For(Recorder, Recorder.Sounding(85))!.Hint);
        Assert.Null(WindFingerings.For(Recorder, Recorder.Sounding(84))!.Hint);
    }

    [Theory]
    [InlineData(54, "123")] // fis3
    [InlineData(55, "1-3")] // g3
    [InlineData(60, "---")] // c4: open
    [InlineData(62, "1-3")] // d4
    [InlineData(64, "12-")] // e4
    [InlineData(65, "1--")] // f4
    [InlineData(67, "---")] // g4
    [InlineData(69, "12-")] // a4
    [InlineData(71, "-2-")] // h4
    [InlineData(73, "12-")] // cis5
    [InlineData(75, "-2-")] // dis5
    [InlineData(76, "---")] // e5
    [InlineData(80, "-23")] // gis5
    [InlineData(84, "---")] // c6
    public void Trumpet_valves_by_written_note(int written, string valves) =>
        Assert.Equal(valves, Valves(written));

    [Fact]
    public void Trumpet_is_read_a_tone_above_its_sound()
    {
        // Written C4 sounds B♭3 (58).
        Assert.Equal(60, WindFingerings.For(Trumpet, 58)!.WrittenPitch);
    }
}
