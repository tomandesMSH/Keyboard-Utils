using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace KeyboardUtils;

/// <summary>
/// Where played notes come from: the microphone (an acoustic piano) or a MIDI input. Learning mode and create mode
/// only see this interface, so both work the same with either source. Events fire on a background thread.
/// </summary>
interface INoteSource : IDisposable
{
    string Name { get; }

    /// <summary>Whether it reports every note of a chord. The microphone hears one note at a time.</summary>
    bool Polyphonic { get; }

    /// <summary>Typical delay between pressing a key and <see cref="NoteOn"/>, for timing in tempo mode.</summary>
    double LatencySeconds { get; }

    /// <summary>MIDI note number (60 = C4).</summary>
    event Action<int>? NoteOn;
    event Action<int>? NoteOff;

    /// <summary>Throws <see cref="NoteSourceException"/> if the device can't be opened.</summary>
    void Start();
    void Stop();
}

sealed class NoteSourceException(string message, Exception? inner = null) : Exception(message, inner);

enum NoteInputKind
{
    Microphone,
    Midi,
}

/// <summary>Notes from a MIDI keyboard (digital piano over USB etc.).</summary>
sealed class MidiNoteSource : INoteSource
{
    readonly string? deviceName;
    InputDevice? device;

    /// <param name="deviceName">Null = first available MIDI input.</param>
    public MidiNoteSource(string? deviceName) => this.deviceName = deviceName;

    public string Name => device?.Name ?? deviceName ?? "MIDI";
    public bool Polyphonic => true;
    public double LatencySeconds => 0;

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    public static IReadOnlyList<string> GetDeviceNames() => InputDevice.GetAll().Select(d => d.Name).ToList();

    public void Start()
    {
        Stop();
        var all = InputDevice.GetAll().ToList();
        device = (deviceName != null ? all.FirstOrDefault(d => d.Name == deviceName) : null) ?? all.FirstOrDefault();
        foreach (var unused in all.Where(d => d != device))
            unused.Dispose();
        if (device == null)
            throw new NoteSourceException(T("Nenašel jsem žádné MIDI vstupní zařízení.", "No MIDI input device found."));
        device.EventReceived += OnEvent;
        try
        {
            device.StartEventsListening();
        }
        catch (MidiDeviceException ex)
        {
            Stop();
            throw new NoteSourceException(T("MIDI vstup nejde otevřít: ", "Could not open the MIDI input: ") + ex.Message, ex);
        }
    }

    public void Stop()
    {
        if (device == null)
            return;
        device.EventReceived -= OnEvent;
        device.Dispose();
        device = null;
    }

    void OnEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        switch (e.Event)
        {
            case NoteOnEvent on when on.Velocity > 0:
                NoteOn?.Invoke(on.NoteNumber);
                break;
            case NoteOnEvent on:
                NoteOff?.Invoke(on.NoteNumber);
                break;
            case NoteOffEvent off:
                NoteOff?.Invoke(off.NoteNumber);
                break;
        }
    }

    public void Dispose() => Stop();
}
