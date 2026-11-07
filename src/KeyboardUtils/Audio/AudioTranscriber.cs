using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// Turns an audio file (e.g. one stem exported from FL Studio) into notes, faster than real time. With the chord
/// detection add-on installed (<see cref="BasicPitchModule"/>) it uses Basic Pitch, which hears several notes at once;
/// otherwise the same detection as the microphone (<see cref="AudioNoteDetector"/>), one note at a time - fine for a
/// melody or bass line, not for chords.
/// </summary>
static class AudioTranscriber
{
    public static string FileFilter =>
        T("Zvuk", "Audio") + " (*.wav;*.mp3;*.flac;*.aiff;*.aif)|*.wav;*.mp3;*.flac;*.aiff;*.aif|" +
        T("Všechny soubory", "All files") + " (*.*)|*.*";

    /// <summary>
    /// Several notes at once with the add-on when it's installed, one at a time otherwise. Throws like
    /// <see cref="Transcribe"/>.
    /// </summary>
    public static Score TranscribeBest(string path, IProgress<double>? progress, CancellationToken cancel) =>
        BasicPitchModule.IsInstalled ? TranscribePolyphonic(path, progress, cancel) : Transcribe(path, progress, cancel);

    /// <summary>Basic Pitch: the file mixed to mono and resampled to its 22 050 Hz. Call only with the add-on installed.</summary>
    static Score TranscribePolyphonic(string path, IProgress<double>? progress, CancellationToken cancel)
    {
        float[] mono;
        using (var reader = new AudioFileReader(path))
        {
            ISampleProvider source = reader.WaveFormat.Channels > 1 ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(ToStereo(reader)) : reader;
            var resampled = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(source, BasicPitchFactory.SampleRate);
            var all = new List<float>((int)(reader.TotalTime.TotalSeconds * BasicPitchFactory.SampleRate) + 1024);
            var buffer = new float[16384];
            int read;
            while ((read = resampled.Read(buffer)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                all.AddRange(buffer.AsSpan(0, read));
                // Reading/resampling is the first fifth of the work, the model the rest.
                progress?.Report(0.2 * reader.Position / Math.Max(1, reader.Length));
            }
            mono = all.ToArray();
        }
        var modelProgress = progress == null ? null : new Progress<double>(p => progress.Report(0.2 + 0.8 * p));
        var notes = BasicPitchFactory.Transcribe(mono, modelProgress, cancel);

        string name = Path.GetFileNameWithoutExtension(path);
        var part = new ScorePart { Name = name };
        part.Notes.AddRange(notes.OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch));
        return new Score { Title = T($"Noty z {name}", $"Notes from {name}"), Parts = [part] };
    }

    /// <summary>StereoToMonoSampleProvider takes exactly two channels; more (surround) are cut to the first two.</summary>
    static ISampleProvider ToStereo(ISampleProvider source) =>
        source.WaveFormat.Channels == 2 ? source : new NAudio.Wave.SampleProviders.MultiplexingSampleProvider([source], 2);

    /// <summary>Samples fed per step - also the time resolution of the detected notes (~10 ms).</summary>
    const int ChunkFrames = 512;

    /// <summary>Same idea as <see cref="NoteRecorder"/>: anything shorter is a glitch, not a played note.</summary>
    const double MinNoteSeconds = 0.05;

    /// <summary>
    /// Silence threshold relative to the file's loudest moment: a quiet stem works as well as a loud one,
    /// and the reverb tail of a note doesn't get read as a held note.
    /// </summary>
    const double ThresholdBelowPeakDb = 30;

    /// <summary>
    /// Throws IOException/InvalidDataException/COMException for unreadable files, OperationCanceledException
    /// when cancelled. <paramref name="progress"/> gets 0-1.
    /// </summary>
    public static Score Transcribe(string path, IProgress<double>? progress, CancellationToken cancel)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider samples = reader;
        int rate = reader.WaveFormat.SampleRate;
        int channels = Math.Max(1, reader.WaveFormat.Channels);

        // Pass 1 (first half of the progress): the loudest stretch, to set the threshold from.
        double peakDb = -100;
        var block = new float[2048 * channels];
        var mono = new float[2048];
        int read;
        while ((read = samples.Read(block)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            int frames = Mix(block, read, channels, mono);
            peakDb = Math.Max(peakDb, NoteTracker.LevelDb(mono.AsSpan(0, frames)));
            progress?.Report(0.5 * reader.Position / Math.Max(1, reader.Length));
        }
        double thresholdDb = Math.Clamp(peakDb - ThresholdBelowPeakDb, -60, -20);

        // Pass 2: detect, timing each note by how many samples had been fed when it was reported.
        reader.Position = 0;
        var detector = new AudioNoteDetector(rate, thresholdDb);
        var notes = new List<ScoreNote>();
        var open = new Dictionary<int, double>();
        long fed = 0;
        double Now() => Math.Max(0, (double)fed / rate - AudioNoteDetector.LatencySeconds);
        void Close(int pitch)
        {
            if (!open.Remove(pitch, out double start))
                return;
            double end = Now();
            if (end - start >= MinNoteSeconds)
                notes.Add(new ScoreNote { Pitch = pitch, StartSeconds = start, DurationSeconds = end - start });
        }
        detector.NoteOn += pitch => { Close(pitch); open[pitch] = Now(); };
        detector.NoteOff += Close;

        block = new float[ChunkFrames * channels];
        mono = new float[ChunkFrames];
        while ((read = samples.Read(block)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            int frames = Mix(block, read, channels, mono);
            fed += frames;
            detector.AddSamples(mono.AsSpan(0, frames));
            progress?.Report(0.5 + 0.5 * reader.Position / Math.Max(1, reader.Length));
        }
        detector.Reset(); // ends a note still sounding at the end of the file

        string name = Path.GetFileNameWithoutExtension(path);
        var part = new ScorePart { Name = name };
        part.Notes.AddRange(notes.OrderBy(n => n.StartSeconds));
        return new Score { Title = T($"Noty z {name}", $"Notes from {name}"), Parts = [part] };
    }

    /// <summary>Interleaved samples -> mono; returns the number of frames.</summary>
    static int Mix(float[] interleaved, int count, int channels, float[] mono)
    {
        int frames = count / channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            mono[f] = sum / channels;
        }
        return frames;
    }
}
