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
    [InlineData("flute")]
    [InlineData("clarinet")]
    [InlineData("altosax")]
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
        foreach (var id in new[] { "piano", "guitar", "violin", "cello" })
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

    static readonly Instrument Flute = Instrument.ById("flute");

    static string FluteKeys(int written)
    {
        var chart = WindFingerings.ChartFor(Flute)!;
        var fingering = WindFingerings.For(Flute, written)!;
        return string.Join(" ", chart.Keys.Where((k, i) => fingering.Keys[i] == KeyState.Closed).Select(k => k.Label));
    }

    [Theory]
    [InlineData(60, "T L1 L2 L3 R1 R2 R3 C")]  // c1: everything, the low C key
    [InlineData(62, "T L1 L2 L3 R1 R2 R3")]    // d1: no E♭ key
    [InlineData(67, "T L1 L2 L3 Eb")]          // g1
    [InlineData(70, "T L1 R1 Eb")]             // b1 (long B♭)
    [InlineData(72, "L1 Eb")]                  // c2: thumb off
    [InlineData(73, "Eb")]                     // cis2: all open
    [InlineData(74, "T L2 L3 R1 R2 R3")]       // d2: the index lifted
    [InlineData(86, "T L2 L3 Eb")]             // d3
    [InlineData(91, "L1 L2 L3 Eb")]            // g3
    [InlineData(93, "T L2 R1 Eb")]             // a3
    public void Flute_keys_by_note(int written, string keys) => Assert.Equal(keys, FluteKeys(written));

    [Fact]
    public void Flute_overblown_notes_share_the_fingering_an_octave_lower()
    {
        for (int written = 76; written <= 85; written++)
            Assert.Equal(FluteKeys(written - 12), FluteKeys(written));
        Assert.Null(WindFingerings.For(Flute, 94)); // b3 needs trill keys: not in the chart
    }

    [Fact]
    public void A_fingering_names_its_notes()
    {
        // Flute e1 / e2: the same fingering, the breath picks.
        var e = WindFingerings.For(Flute, 64)!.Keys;
        Assert.Equal([64, 76], WindFingerings.NotesFor(Flute, e));
        // Recorder: one note per fingering (the thumb pinched makes the second octave).
        var recorder = Instrument.ById("recorder");
        var g1 = WindFingerings.For(recorder, recorder.Sounding(67))!.Keys;
        Assert.Equal([recorder.Sounding(67)], WindFingerings.NotesFor(recorder, g1));
        // Trumpet, no valves: c4, g4, c5, e5, g5, c6 (written).
        var open = WindFingerings.For(Trumpet, Trumpet.Sounding(60))!.Keys;
        Assert.Equal(new[] { 60, 67, 72, 76, 79, 84 }.Select(Trumpet.Sounding), WindFingerings.NotesFor(Trumpet, open));
        // Not a fingering at all.
        Assert.Empty(WindFingerings.NotesFor(recorder, [KeyState.Open, KeyState.Closed, KeyState.Open, KeyState.Closed, KeyState.Open, KeyState.Closed, KeyState.Open, KeyState.Closed]));
    }

    static string Keys(string id, int written)
    {
        var instrument = Instrument.ById(id);
        var chart = WindFingerings.ChartFor(instrument)!;
        var fingering = WindFingerings.For(instrument, instrument.Sounding(written))!;
        return string.Join(" ", chart.Keys.Where((k, i) => fingering.Keys[i] == KeyState.Closed).Select(k => k.Label));
    }

    [Theory]
    [InlineData(52, "T L1 L2 L3 LE R1 R2 R3")]   // e: everything and the left little finger's E key
    [InlineData(53, "T L1 L2 L3 R1 R2 R3 RF")]   // f
    [InlineData(58, "T L1 L2 L3 R1")]            // b (B♭3)
    [InlineData(59, "T L1 L2 L3 R2")]            // h: forked
    [InlineData(63, "T L1 L2 S4")]               // dis1: side key 4
    [InlineData(67, "")]                         // g1: open
    [InlineData(69, "A")]                        // a1: the A key
    [InlineData(70, "R A")]                      // b1: register + A
    [InlineData(71, "R T L1 L2 L3 LE R1 R2 R3")] // h1: the clarion begins
    [InlineData(84, "R T")]                      // c3
    public void Clarinet_keys_by_written_note(int written, string keys) => Assert.Equal(keys, Keys("clarinet", written));

    [Fact]
    public void Clarinet_clarion_is_the_register_key_a_twelfth_up()
    {
        for (int written = 71; written <= 84; written++)
            Assert.Equal(("R " + Keys("clarinet", written - 19)).Trim(), Keys("clarinet", written));
    }

    [Theory]
    [InlineData(58, "L1 L2 L3 LBb R1 R2 R3 RC")] // b (B♭3): the lowest
    [InlineData(60, "L1 L2 L3 R1 R2 R3 RC")]     // c1
    [InlineData(62, "L1 L2 L3 R1 R2 R3")]        // d1
    [InlineData(70, "L1 L2 SBb")]                // b1: side B♭
    [InlineData(72, "L2")]                       // c2
    [InlineData(73, "")]                         // cis2: open
    [InlineData(74, "O L1 L2 L3 R1 R2 R3")]      // d2: octave key
    [InlineData(85, "O")]                        // cis3
    [InlineData(86, "O PD")]                     // d3: palm keys
    [InlineData(89, "O PD PEb PF SE")]           // f3
    public void Saxophone_keys_by_written_note(int written, string keys) => Assert.Equal(keys, Keys("altosax", written));

    [Fact]
    public void Saxophone_second_octave_is_the_octave_key_on_the_first()
    {
        for (int written = 74; written <= 85; written++)
            Assert.Equal(("O " + Keys("altosax", written - 12)).Trim(), Keys("altosax", written));
    }

    [Fact]
    public void Clarinet_and_saxophone_sound_below_what_they_read()
    {
        // Written c1 sounds B♭3 on the clarinet, E♭3 on the alto saxophone.
        Assert.Equal(60, WindFingerings.For(Instrument.ById("clarinet"), 58)!.WrittenPitch);
        Assert.Equal(60, WindFingerings.For(Instrument.ById("altosax"), 51)!.WrittenPitch);
        Assert.Null(WindFingerings.For(Instrument.ById("clarinet"), Instrument.ById("clarinet").Sounding(85)));
    }
}
