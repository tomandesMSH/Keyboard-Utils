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

/// <summary>
/// An audio source that can check chords: told which notes should be playing (learning mode), it reports each of
/// them it hears, so all notes of a chord count - not just the one note a pitch tracker can pick out.
/// </summary>
interface IExpectedNotesListener
{
    /// <summary>When on, the source is <see cref="INoteSource.Polyphonic"/> and <see cref="SetExpectedNotes"/> takes effect.</summary>
    bool CheckChords { get; set; }

    /// <summary>The notes to listen for now; empty = none. Callable from the UI thread at any time.</summary>
    void SetExpectedNotes(IEnumerable<int> notes);
}

enum NoteInputKind
{
    Microphone,
    Midi,

    /// <summary>Clicking the keys of the on-screen keyboard (piano roll).</summary>
    ScreenKeyboard,

    /// <summary>Whatever the computer is playing (loopback of the default output).</summary>
    SystemAudio,
}

/// <summary>
/// Notes from clicking the piano roll's on-screen keyboard: the owner forwards key presses with
/// <see cref="Press"/>/<see cref="Release"/>. Handy for trying learning mode or create mode without a piano.
/// </summary>
sealed class ScreenKeyboardNoteSource : INoteSource
{
    public string Name => T("Klaviatura v appce", "On-screen keyboard");
    public bool Polyphonic => true;
    public double LatencySeconds => 0;

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    public void Press(int note) => NoteOn?.Invoke(note);
    public void Release(int note) => NoteOff?.Invoke(note);

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
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
