using NAudio;
using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>Notes from an acoustic instrument via the microphone (16-bit mono at 44.1 kHz into an <see cref="AudioNoteDetector"/>).</summary>
sealed class MicrophoneNoteSource : INoteSource, IExpectedNotesListener, IInstrumentAware
{
    const int SampleRate = 44100;

    readonly int deviceNumber;
    readonly AudioNoteDetector detector;
    readonly float[] samples = new float[SampleRate]; // generous: one buffer is ~20 ms
    WaveIn? waveIn;
    volatile TakeWriter? take;

    /// <summary>The longest take kept (5 MB a minute): a forgotten session doesn't fill the disk.</summary>
    static readonly TimeSpan MaxTake = TimeSpan.FromMinutes(30);

    /// <summary>Starts writing what the microphone hears to <paramref name="path"/> (WAV), alongside note detection.</summary>
    public void StartTake(string path)
    {
        StopTake();
        take = new TakeWriter(path, new WaveFormat(SampleRate, 16, 1), MaxTake);
    }

    /// <summary>Ends the take; its file and length, or null when none was running.</summary>
    public (string Path, double Seconds)? StopTake()
    {
        var current = take;
        take = null;
        if (current == null)
            return null;
        double seconds = current.Seconds;
        current.Dispose();
        return (current.Path, seconds);
    }

    /// <param name="deviceName">Null or unknown = the system default recording device.</param>
    public MicrophoneNoteSource(string? deviceName, double thresholdDb)
    {
        deviceNumber = FindDevice(deviceName);
        Name = deviceNumber >= 0 ? WaveIn.GetCapabilities(deviceNumber).ProductName : T("Výchozí mikrofon", "Default microphone");
        detector = new AudioNoteDetector(SampleRate, thresholdDb);
        detector.NoteOn += note => NoteOn?.Invoke(note);
        detector.NoteOff += note => NoteOff?.Invoke(note);
        detector.LevelChanged += level => LevelChanged?.Invoke(level);
    }

    public string Name { get; }

    Instrument instrument = Instrument.Piano;

    public Instrument Instrument
    {
        get => instrument;
        set
        {
            instrument = value;
            detector.SetInstrument(value);
        }
    }
    public bool Polyphonic => CheckChords;
    public double LatencySeconds => AudioNoteDetector.LatencySeconds;

    public bool CheckChords { get; set; }

    public void SetExpectedNotes(IEnumerable<int> notes) => detector.SetExpectedNotes(CheckChords ? notes : []);

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>Level of each analysed frame in dBFS (0 = full scale), ~86 times a second. For meters/calibration.</summary>
    public event Action<double>? LevelChanged;

    public double ThresholdDb
    {
        get => detector.ThresholdDb;
        set => detector.ThresholdDb = value;
    }

    /// <summary>Product names as Windows reports them (may be cut to 31 characters by the old WinMM API).</summary>
    public static IReadOnlyList<string> GetDeviceNames() =>
        Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();

    /// <summary>-1 is WAVE_MAPPER, i.e. whatever Windows has as the default recording device.</summary>
    public static int FindDevice(string? name)
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
        detector.Reset();
        StopTake();
    }

    /// <summary>Runs on NAudio's capture thread.</summary>
    void OnData(object? sender, WaveInEventArgs e)
    {
        var bytes = e.BufferSpan[..e.BytesRecorded];
        take?.Write(bytes);
        int count = Math.Min(bytes.Length / 2, samples.Length);
        for (int i = 0; i < count; i++)
            samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
        detector.AddSamples(samples.AsSpan(0, count));
    }

    public void Dispose() => Stop();
}
