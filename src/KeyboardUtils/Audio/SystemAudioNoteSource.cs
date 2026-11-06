using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// Notes from whatever the computer is playing (YouTube, Spotify, another program) via WASAPI loopback of the default
/// output device, mixed down to mono and fed to an <see cref="AudioNoteDetector"/>. Like the microphone it hears one
/// note at a time, so it works for a solo melody but not for a full mix with chords and drums.
/// </summary>
sealed class SystemAudioNoteSource : INoteSource
{
    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT - how a WAVE_FORMAT_EXTENSIBLE mix format says "32-bit float".</summary>
    static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    readonly double thresholdDb;
    AudioNoteDetector? detector;
    WasapiRecorder? capture;
    float[] samples = [];

    public SystemAudioNoteSource(double thresholdDb) => this.thresholdDb = thresholdDb;

    public string Name => T("Zvuk z počítače", "Computer sound");
    public bool Polyphonic => false;
    public double LatencySeconds => AudioNoteDetector.LatencySeconds;

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    public void Start()
    {
        Stop();
        capture = CreateCapture();

        // The device's own mix format: usually 32-bit float stereo at 48 kHz, sometimes 16-bit PCM.
        var format = capture.WaveFormat;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubFormat);
        if (!(isFloat && format.BitsPerSample == 32) && format.BitsPerSample != 16)
        {
            Stop();
            throw new NoteSourceException(T($"Nepodporovaný formát zvuku: {format}", $"Unsupported audio format: {format}"));
        }

        detector = new AudioNoteDetector(format.SampleRate, thresholdDb);
        detector.NoteOn += note => NoteOn?.Invoke(note);
        detector.NoteOff += note => NoteOff?.Invoke(note);
        capture.DataAvailable += (buffer, _, _, _) => OnData(buffer, format, isFloat);
        try
        {
            capture.StartRecording();
        }
        catch (COMException ex)
        {
            Stop();
            throw new NoteSourceException(T("Zvuk z počítače nejde zachytit: ", "Could not capture the computer's sound: ") + ex.Message, ex);
        }
    }

    /// <summary>
    /// Everything the computer plays except this app (process loopback excluding our own process tree, Windows 10
    /// 2004+), so the app doesn't "hear" its own playback. Falls back to plain loopback of the default output.
    /// </summary>
    WasapiRecorder CreateCapture()
    {
        ExcludesOwnSound = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            try
            {
                // BuildAsync completes on a COM callback; run it off the UI thread so blocking on it can't deadlock.
                var builder = new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)Environment.ProcessId, ProcessLoopbackMode.ExcludeTargetProcessTree)
                    .WithFormat(new WaveFormat(48000, 16, 2));
                var recorder = Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult();
                ExcludesOwnSound = true;
                return recorder;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
            {
                // Fall through to plain loopback below.
            }
        }
        try
        {
            return new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new NoteSourceException(T("Zvuk z počítače nejde zachytit: ", "Could not capture the computer's sound: ") + ex.Message, ex);
        }
    }

    /// <summary>False when only plain loopback was available, which also hears this app's own playback.</summary>
    public bool ExcludesOwnSound { get; private set; }

    /// <summary>Runs on the capture thread: interleaved frames -> mono floats.</summary>
    void OnData(ReadOnlySpan<byte> bytes, WaveFormat format, bool isFloat)
    {
        // Stop() may clear the field while this thread is still delivering a last buffer.
        if (detector is not { } target)
            return;
        int channels = Math.Max(1, format.Channels);
        int bytesPerSample = format.BitsPerSample / 8;
        int frames = bytes.Length / (bytesPerSample * channels);
        if (samples.Length < frames)
            samples = new float[frames];

        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int offset = (f * channels + c) * bytesPerSample;
                sum += isFloat
                    ? BitConverter.ToSingle(bytes.Slice(offset, 4))
                    : (short)(bytes[offset] | bytes[offset + 1] << 8) / 32768f;
            }
            samples[f] = sum / channels;
        }
        target.AddSamples(samples.AsSpan(0, frames));
    }

    public void Stop()
    {
        if (capture != null)
        {
            capture.StopRecording();
            capture.Dispose();
            capture = null;
        }
        detector?.Reset();
        detector = null;
    }

    public void Dispose() => Stop();
}
