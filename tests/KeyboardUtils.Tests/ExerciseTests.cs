namespace KeyboardUtils.Tests;

public class ExerciseTests
{
    static List<int> Pitches(Score score, int part = 0) =>
        score.Parts[part].Notes.OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch).Select(n => n.Pitch).ToList();

    [Fact]
    public void D_major_scale_one_octave_up_and_down()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: 2));
        Assert.Equal([62, 64, 66, 67, 69, 71, 73, 74, 73, 71, 69, 67, 66, 64, 62], Pitches(score));
        Assert.Equal(2, score.KeyFifths);
        Assert.False(score.KeyMinor);
        Assert.Equal("Stupnice D dur, 1 oktáva, pravá ruka", score.Title);
    }

    [Fact]
    public void Harmonic_minor_raises_the_seventh()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: 0, Minor: true));
        Assert.Equal([69, 71, 72, 74, 76, 77, 80, 81], Pitches(score).Take(8));
        Assert.True(score.KeyMinor);
    }

    [Fact]
    public void Two_octaves_and_both_hands_an_octave_pair_apart()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: -1, Octaves: 2, Hands: ExerciseHands.Both));
        Assert.Equal(2, score.Parts.Count);
        var right = Pitches(score, 0);
        var left = Pitches(score, 1);
        Assert.Equal(29, right.Count); // 15 up, 14 down
        Assert.Equal(65, right[0]);    // F4
        Assert.Equal(89, right.Max()); // F6
        Assert.Equal(right.Select(p => p - 24), left);
    }

    [Fact]
    public void Notes_fall_on_beats_and_the_last_is_held_to_the_bar_end()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Bpm: 60, Eighths: true));
        var notes = score.Parts[0].Notes;
        // 60 BPM: an eighth is half a second.
        Assert.Equal(Enumerable.Range(0, 15).Select(i => i * 0.5), notes.Select(n => Math.Round(n.StartSeconds, 6)));
        var last = notes[^1];
        Assert.Equal(7, last.StartSeconds, 6);
        Assert.Equal(8, last.StartSeconds + last.DurationSeconds, 1); // to the end of bar 2 (8 s)
        Assert.Equal(4, score.Timing!.BarQuarters);
    }

    [Fact]
    public void Arpeggio_and_cadence()
    {
        var arpeggio = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Arpeggio, Fifths: 1));
        Assert.Equal([67, 71, 74, 79, 74, 71, 67], Pitches(arpeggio));

        var cadence = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Cadence, Fifths: 0, Minor: true, Hands: ExerciseHands.Both));
        var chords = cadence.Parts[0].Notes.GroupBy(n => Math.Round(n.StartSeconds, 3)).Select(g => g.Select(n => n.Pitch).Order().ToArray()).ToList();
        Assert.Equal([[69, 72, 76], [69, 74, 77], [68, 71, 76], [69, 72, 76]], chords); // i, iv, V (G♯ B E), i
        Assert.Equal([57, 62, 52, 57], cadence.Parts[1].Notes.Select(n => n.Pitch));  // A, D, E, A
    }

    [Fact]
    public void Minor_spelling_writes_the_raised_seventh_as_such()
    {
        // D minor: C♯, not D♭; F♯ minor: E♯, not F.
        Assert.Equal((0, 1), KeySignature.Spelling(-1, minor: true)[1]);
        Assert.Equal((2, 1), KeySignature.Spelling(3, minor: true)[5]);
        // Major keys are unchanged.
        Assert.Equal(KeySignature.Spelling(-1), KeySignature.Spelling(-1, minor: false));
        Assert.Equal(9, KeySignature.Tonic(0, minor: true));
        Assert.Equal(11, KeySignature.Tonic(2, minor: true));
    }

    [Fact]
    public void Melodic_minor_goes_up_raised_and_down_natural()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, 0, true, MinorForm: MinorForm.Melodic));
        // A minor: A B C D E F# G# A, then A G F E D C B A.
        Assert.Equal([69, 71, 72, 74, 76, 78, 80, 81, 79, 77, 76, 74, 72, 71, 69], Pitches(score));
        Assert.Equal("Stupnice a moll melodická, 1 oktáva, pravá ruka", score.Title);
        var natural = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, 0, true, MinorForm: MinorForm.Natural));
        Assert.Equal([69, 71, 72, 74, 76, 77, 79, 81], Pitches(natural).Take(8));
    }

    [Fact]
    public void Chromatic_scale_every_semitone()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Chromatic, Fifths: 2));
        var pitches = Pitches(score);
        Assert.Equal(25, pitches.Count);
        Assert.Equal(Enumerable.Range(62, 13), pitches.Take(13));
        Assert.Equal(62, pitches[^1]);
    }

    [Fact]
    public void Inversions_root_first_second_and_back()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Inversions, Fifths: 0));
        var chords = score.Parts[0].Notes.GroupBy(n => Math.Round(n.StartSeconds, 3)).Select(g => g.Select(n => n.Pitch).Order().ToArray()).ToList();
        Assert.Equal([[60, 64, 67], [64, 67, 72], [67, 72, 76], [72, 76, 79], [67, 72, 76], [64, 67, 72], [60, 64, 67]], chords);
    }

    [Fact]
    public void Hanon_one_octave_up_and_down()
    {
        var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Hanon, Fifths: 0, Eighths: true));
        var pitches = Pitches(score);
        Assert.Equal([60, 64, 65, 67, 69, 67, 65, 64], pitches.Take(8));     // C E F G A G F E
        Assert.Equal([62, 65, 67, 69, 71, 69, 67, 65], pitches.Skip(8).Take(8)); // a step higher
        Assert.Equal(7 * 8 * 2 + 1, pitches.Count);
        Assert.Equal(60, pitches[^1]);
        Assert.Equal(79, pitches.Max());                                       // the top: G5 (scale degree 5 an octave up)
    }

    [Fact]
    public void Every_offered_key_generates()
    {
        foreach (var key in ExerciseGenerator.Keys)
            foreach (var kind in Enum.GetValues<ExerciseKind>())
            {
                var score = ExerciseGenerator.Generate(new ExerciseSpec(kind, key.Fifths, key.Minor, 2, ExerciseHands.Both));
                Assert.All(score.Parts.SelectMany(p => p.Notes), n => Assert.InRange(n.Pitch, 21, 108));
            }
    }

    [Fact]
    public void Each_hand_stays_on_its_own_staff()
    {
        foreach (var kind in Enum.GetValues<ExerciseKind>())
            foreach (int octaves in new[] { 1, 2 })
            {
                var score = ExerciseGenerator.Generate(new ExerciseSpec(kind, 5, false, octaves, ExerciseHands.Both));
                var notes = score.Parts.SelectMany((p, i) => p.Notes.Select(n => (n, i)));
                var staff = SheetQuantizer.Quantize(notes, s => s * 4, 16, i => score.Parts[i].Staff);
                var treble = staff.Treble.SelectMany(s => s.Notes).ToList();
                var bass = staff.Bass.SelectMany(s => s.Notes).ToList();
                Assert.All(treble, n => Assert.Equal(0, n.PartIndex));
                Assert.All(bass, n => Assert.Equal(1, n.PartIndex));
                // ...and the left hand mostly within the bass staff's reach: at most a few ledger lines up.
                Assert.True(bass.All(n => n.Pitch is >= 21 and <= 69), $"{kind} {octaves}: {string.Join(" ", bass.Select(n => n.Pitch))}");
            }
    }

    [Fact]
    public void Survives_a_musicxml_round_trip_with_the_minor_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-exercise-{Guid.NewGuid():N}.musicxml");
        try
        {
            var score = ExerciseGenerator.Generate(new ExerciseSpec(ExerciseKind.Scale, Fifths: -1, Minor: true, Hands: ExerciseHands.Both));
            ScoreExporter.Save(score, path, 80);
            var back = ScoreImporter.Import(path);
            Assert.Equal(-1, back.KeyFifths);
            Assert.True(back.KeyMinor);
            // MusicXML holds both hands as one piano part on two staves.
            Assert.Equal(score.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).Order(),
                back.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).Order());
            Assert.Contains("<alter>1</alter>", File.ReadAllText(path)); // C♯ written as C + sharp
        }
        finally
        {
            File.Delete(path);
        }
    }
}
