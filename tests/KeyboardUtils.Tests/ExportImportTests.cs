using Melanchall.DryWetMidi.Core;

namespace KeyboardUtils.Tests;

public sealed class ExportImportTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("KeyboardUtilsTests").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    static Score Song() => new()
    {
        Title = "Test",
        KeyFifths = 2,
        Timing = new ScoreTiming([new TempoPoint(0, 0, 2), new TempoPoint(4, 8, 1.5)], 3, 4),
        Parts =
        [
            new ScorePart { Name = "Část 1 – pravá ruka", Notes = [new ScoreNote { Pitch = 72, StartSeconds = 0, DurationSeconds = 0.5 }, new ScoreNote { Pitch = 74, StartSeconds = 4, DurationSeconds = 1 }] },
            new ScorePart { Name = "Levá", Notes = [new ScoreNote { Pitch = 48, StartSeconds = 1, DurationSeconds = 1.5 }] },
        ],
    };

    [Fact]
    public void Midi_keeps_notes_tempo_time_signature_key_and_czech_names()
    {
        string path = Path.Combine(folder, "song.mid");
        ScoreExporter.Save(Song(), path, 100);
        var back = ScoreImporter.Import(path);

        Assert.Equal(["Část 1 – pravá ruka", "Levá"], back.Parts.Select(p => p.Name));
        Assert.Equal(2, back.KeyFifths);
        Assert.Equal((3, 4), (back.Timing!.Beats, back.Timing.BeatType));
        Assert.Equal(1.5, back.Timing.QuartersPerSecondAt(5), 3);
        var original = Song().Parts.SelectMany(p => p.Notes).OrderBy(n => n.StartSeconds).ToList();
        var notes = back.Parts.SelectMany(p => p.Notes).OrderBy(n => n.StartSeconds).ToList();
        Assert.Equal(original.Select(n => n.Pitch), notes.Select(n => n.Pitch));
        for (int i = 0; i < notes.Count; i++)
        {
            Assert.Equal(original[i].StartSeconds, notes[i].StartSeconds, 3);
            Assert.Equal(original[i].DurationSeconds, notes[i].DurationSeconds, 3);
        }
    }

    [Fact]
    public void Midi_from_elsewhere_with_latin1_names_reads()
    {
        // An older program's file: 8-bit text that isn't valid UTF-8.
        // One track: its name and a note.
        var track = new TrackChunk();
        track.Events.Add(new SequenceTrackNameEvent("Café piano"));
        track.Events.Add(new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)60, (Melanchall.DryWetMidi.Common.SevenBitNumber)100));
        track.Events.Add(new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)60, (Melanchall.DryWetMidi.Common.SevenBitNumber)0) { DeltaTime = 480 });
        var file = new MidiFile(track);
        string path = Path.Combine(folder, "latin1.mid");
        // Single-track format: as multi-track, DryWetMidi would move the name into a track of its own.
        file.Write(path, overwriteFile: true, format: MidiFileFormat.SingleTrack, settings: new WritingSettings { TextEncoding = System.Text.Encoding.Latin1 });

        Assert.Equal("Café piano", ScoreImporter.Import(path).Parts[0].Name);
    }

    [Fact]
    public void MusicXml_keeps_key_time_and_pitches()
    {
        string path = Path.Combine(folder, "song.musicxml");
        ScoreExporter.Save(Song(), path, 100);
        var back = ScoreImporter.Import(path);

        Assert.Equal(2, back.KeyFifths);
        Assert.Equal((3, 4), (back.Timing!.Beats, back.Timing.BeatType));
        Assert.Equal([48, 72, 74], back.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).Order());
    }
}
