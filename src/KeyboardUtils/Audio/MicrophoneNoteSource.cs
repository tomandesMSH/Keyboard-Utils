using NAudio;
using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// Notes from an acoustic piano via the microphone: captures mono audio, slides a <see cref="FrameSize"/> window
/// forward by <see cref="HopSize"/> samples and runs each window through a <see cref="NoteTracker"/>. Single notes
/// only - a chord has no single period, so it's either reported as one of its notes or not at all.
/// </summary>
sealed class MicrophoneNoteSource : INoteSource
{
    const int SampleRate = 44100;

    /// <summary>~46 ms. Long enough for a 43 Hz period twice over (lowest detectable: about F1).</summary>
    const int FrameSize = 2048;

    /// <summary>~12 ms between analyses: bounds the detection latency on top of the frame length.</summary>
    const int HopSize = 512;

    readonly int deviceNumber;
    readonly NoteTracker tracker = new(new PitchDetector(SampleRate, FrameSize));
    readonly float[] ring = new float[FrameSize];
    readonly float[] frame = new float[FrameSize];
    readonly Lock gate = new();
    int ringFill;
    int sinceLastFrame;
    WaveIn? waveIn;

    /// <param name="deviceName">Null or unknown = the system default recording device.</param>
    public MicrophoneNoteSource(string? deviceName, double thresholdDb)
    {
        deviceNumber = FindDevice(deviceName);
        Name = deviceNumber >= 0 ? WaveIn.GetCapabilities(deviceNumber).ProductName : T("Výchozí mikrofon", "Default microphone");
        tracker.ThresholdDb = thresholdDb;
        tracker.NoteOn += note => NoteOn?.Invoke(note);
        tracker.NoteOff += note => NoteOff?.Invoke(note);
    }

    public string Name { get; }

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>Level of each analysed frame in dBFS (0 = full scale), ~86 times a second. For meters/calibration.</summary>
    public event Action<double>? LevelChanged;

    public double ThresholdDb
    {
        get => tracker.ThresholdDb;
        set => tracker.ThresholdDb = value;
    }

    /// <summary>Product names as Windows reports them (may be cut to 31 characters by the old WinMM API).</summary>
    public static IReadOnlyList<string> GetDeviceNames() =>
        Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();

    /// <summary>-1 is WAVE_MAPPER, i.e. whatever Windows has as the default recording device.</summary>
    static int FindDevice(string? name)
    {
        if (name == null)
            return -1;
        var names = GetDeviceNames();
        for (int i = 0; i < names.Count; i++)
            if (names[i] == name)
                return i;
        return -1;
    }

    public void Start()
    {
        Stop();
        if (WaveIn.DeviceCount == 0)
            throw new NoteSourceException(T("Nenašel jsem žádný mikrofon.", "No microphone found."));

        waveIn = new WaveIn
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 20,
        };
        waveIn.DataAvailable += OnData;
        try
        {
            waveIn.StartRecording();
        }
        catch (MmException ex)
        {
            Stop();
            throw new NoteSourceException(T("Mikrofon nejde otevřít: ", "Could not open the microphone: ") + ex.Message, ex);
        }
    }

    public void Stop()
    {
        if (waveIn == null)
            return;
        waveIn.DataAvailable -= OnData;
        waveIn.StopRecording();
        waveIn.Dispose();
        waveIn = null;
        ringFill = sinceLastFrame = 0;
        // The capture thread may still be finishing its last buffer.
        lock (gate)
            tracker.Release();
    }

    /// <summary>Runs on NAudio's capture thread.</summary>
    void OnData(object? sender, WaveInEventArgs e)
    {
        var bytes = e.BufferSpan[..e.BytesRecorded];
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            // Ring buffer: ring[ringFill % FrameSize] is the oldest sample once full.
            ring[ringFill % FrameSize] = (short)(bytes[i] | bytes[i + 1] << 8) / 32768f;
            ringFill++;
            if (ringFill >= FrameSize && ++sinceLastFrame >= HopSize)
            {
                sinceLastFrame = 0;
                AnalyzeFrame();
            }
        }
        // Keep the counter from overflowing on long sessions while preserving its position in the ring.
        if (ringFill >= FrameSize * 2)
            ringFill = FrameSize + ringFill % FrameSize;
    }

    void AnalyzeFrame()
    {
        int oldest = ringFill % FrameSize;
        ring.AsSpan(oldest).CopyTo(frame);
        ring.AsSpan(0, oldest).CopyTo(frame.AsSpan(FrameSize - oldest));
        double level;
        lock (gate)
            level = tracker.Process(frame);
        LevelChanged?.Invoke(level);
    }

    public void Dispose() => Stop();
}
