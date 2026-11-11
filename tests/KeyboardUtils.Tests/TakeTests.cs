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

    /// <summary>1 s of quiet hiss, 2 s of a tone at <paramref name="amplitude"/>, 1.5 s of hiss.</summary>
    static short[] HissToneHiss(double amplitude)
    {
        const int rate = 8000;
        var random = new Random(1);
        var samples = new short[(int)(4.5 * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / rate;
            double tone = t >= 1 && t < 3 ? amplitude * Math.Sin(2 * Math.PI * 440 * t) : 0;
            samples[i] = (short)(tone * 32767 + random.Next(-30, 31));
        }
        return samples;
    }

    [Fact]
    public void Cuts_the_silence_before_and_after()
    {
        var samples = HissToneHiss(0.3);
        var (start, end) = TakeProcessing.Trim(samples, 8000);
        // The tone runs 1 s - 3 s; 0.3 s of air before, 0.6 s after.
        Assert.InRange(start / 8000.0, 0.65, 0.75);
        Assert.InRange(end / 8000.0, 3.55, 3.65);
    }

    [Fact]
    public void Makes_quiet_takes_louder_but_not_too_much()
    {
        Assert.Equal(0.89 / 0.3, TakeProcessing.Gain(HissToneHiss(0.3)), 1);
        Assert.Equal(8, TakeProcessing.Gain(HissToneHiss(0.01)), 6); // capped at +18 dB
        Assert.Equal(1, TakeProcessing.Gain(HissToneHiss(0.99)), 6); // loud already: never quieter
    }

    [Fact]
    public void Processes_a_take_file_and_saves_it_compressed()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "take.wav");
            var samples = HissToneHiss(0.2);
            using (var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1)))
            {
                var bytes = new byte[samples.Length * 2];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                writer.Write(bytes, 0, bytes.Length);
            }
            double seconds = TakeProcessing.Process(path);
            Assert.InRange(seconds, 2.8, 3.0);
            using (var reader = new WaveFileReader(path))
                Assert.Equal(seconds, reader.TotalTime.TotalSeconds, 2);

            string saved = TakeProcessing.SaveCompressed(path, Path.Combine(dir, "zkouska"));
            Assert.True(File.Exists(saved));
            Assert.Contains(Path.GetExtension(saved), new[] { ".mp3", ".wav" });
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
