using NAudio.Wave;

namespace KeyboardUtils.Tests;

public class TakeTests
{
    static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kbu-take-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Writes_the_captured_sound_as_wav()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "sub", "take.wav");
            var format = new WaveFormat(44100, 16, 1);
            using (var writer = new TakeWriter(path, format, TimeSpan.FromMinutes(1)))
            {
                var second = new byte[format.AverageBytesPerSecond];
                for (int i = 0; i < second.Length; i += 2)
                    BitConverter.TryWriteBytes(second.AsSpan(i), (short)(Math.Sin(i / 2 * 2 * Math.PI * 440 / 44100) * 8000));
                writer.Write(second);
                writer.Write(second.AsSpan(0, second.Length / 2));
                Assert.Equal(1.5, writer.Seconds, 3);
            }
            using var reader = new WaveFileReader(path);
            Assert.Equal(format, reader.WaveFormat);
            Assert.Equal(1.5, reader.TotalTime.TotalSeconds, 3);
            // The tap's file plays like any WAV.
            using var audio = new AudioFileReader(path);
            Assert.True(audio.Length > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Stops_writing_at_the_limit()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "take.wav");
            var format = new WaveFormat(8000, 16, 1);
            using (var writer = new TakeWriter(path, format, TimeSpan.FromSeconds(2)))
            {
                var second = new byte[format.AverageBytesPerSecond];
                for (int i = 0; i < 5; i++)
                    writer.Write(second);
                Assert.Equal(2, writer.Seconds, 3);
                writer.Dispose();
                writer.Write(second); // after closing: ignored, no exception
            }
            using var reader = new WaveFileReader(path);
            Assert.Equal(2, reader.TotalTime.TotalSeconds, 3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_names_from_the_song_title()
    {
        var when = new DateTime(2026, 11, 12, 18, 5, 9);
        Assert.Equal("Óda na radost 2026-11-12 18-05-09", RecordedTake.FileName("Óda na radost", when));
        Assert.Equal("a_b_c 2026-11-12 18-05-09", RecordedTake.FileName("a/b:c", when));
        Assert.StartsWith("Nahrávka ", RecordedTake.FileName("  ", when));
    }

    [Fact]
    public void A_notes_take_saves_as_midi()
    {
        string dir = TempDir();
        try
        {
            var score = new Score
            {
                Title = "Take",
                Parts = [new ScorePart { Name = "Nahrávka", Notes = [new ScoreNote { Pitch = 60, StartSeconds = 0, DurationSeconds = 0.5 }, new ScoreNote { Pitch = 64, StartSeconds = 0.5, DurationSeconds = 0.5 }] }],
            };
            using var engine = new PlaybackEngine();
            using var take = new NotesTake(score, engine, () => { });
            string saved = take.SaveTo(dir, "zkouska");
            Assert.Equal(Path.Combine(dir, "zkouska.mid"), saved);
            Assert.Equal([60, 64], ScoreImporter.Import(saved).Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).Order());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
