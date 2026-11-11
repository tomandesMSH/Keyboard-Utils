using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// How the player played in a learning session, to listen back to in the results (and keep): the microphone's sound
/// (<see cref="WavTake"/>) or the notes from a MIDI / on-screen keyboard (<see cref="NotesTake"/>).
/// </summary>
abstract class RecordedTake : IDisposable
{
    /// <summary>Playback came to the end by itself. Raised on any thread.</summary>
    public event Action? Finished;

    public abstract bool IsPlaying { get; }
    public abstract void Play();
    public abstract void Stop();

    /// <summary>Copies the take into <paramref name="folder"/> as <paramref name="name"/> (+ extension); returns the file.</summary>
    public abstract string SaveTo(string folder, string name);

    protected void RaiseFinished() => Finished?.Invoke();

    public virtual void Dispose() => Stop();

    /// <summary>A file name from a song title and the time: no characters Windows refuses.</summary>
    public static string FileName(string title, DateTime when)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (clean.Length > 60)
            clean = clean[..60].Trim();
        return $"{(clean.Length > 0 ? clean : T("Nahrávka", "Recording"))} {when:yyyy-MM-dd HH-mm-ss}";
    }
}

/// <summary>The microphone's sound as a WAV file.</summary>
sealed class WavTake(string path) : RecordedTake
{
    WaveOut? output;
    AudioFileReader? reader;

    public string Path { get; } = path;

    public override bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;

    public override void Play()
    {
        Stop();
        reader = new AudioFileReader(Path);
        output = new WaveOut();
        output.Init(reader);
        output.PlaybackStopped += (_, _) => RaiseFinished();
        output.Play();
    }

    public override void Stop()
    {
        var o = output;
        output = null;
        o?.Stop();
        o?.Dispose();
        reader?.Dispose();
        reader = null;
    }

    /// <summary>As MP3 (WAV where Windows has no MP3 encoder).</summary>
    public override string SaveTo(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        return TakeProcessing.SaveCompressed(Path, System.IO.Path.Combine(folder, name));
    }
}

/// <summary>
/// Notes played on a MIDI / on-screen keyboard, played back through the app's synth. The song is swapped out of the
/// playback engine while the take plays; <paramref name="restore"/> puts it back.
/// </summary>
sealed class NotesTake(Score take, PlaybackEngine engine, Action restore) : RecordedTake
{
    bool loaded;
    bool metronomeWas;

    public Score Score { get; } = take;

    public override bool IsPlaying => loaded && engine.IsRunning;

    public override void Play()
    {
        Stop();
        metronomeWas = engine.Metronome;
        engine.Metronome = false;
        engine.Load(Score, ScoreTiming.Constant(120));
        engine.Speed = 1;
        engine.Finished += OnFinished;
        loaded = true;
        engine.Play();
    }

    void OnFinished() => RaiseFinished();

    public override void Stop()
    {
        if (!loaded)
            return;
        loaded = false;
        engine.Finished -= OnFinished;
        engine.Stop();
        engine.Metronome = metronomeWas;
        restore();
    }

    public override string SaveTo(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string target = System.IO.Path.Combine(folder, name + ".mid");
        ScoreExporter.Save(Score, target, 120);
        return target;
    }
}
