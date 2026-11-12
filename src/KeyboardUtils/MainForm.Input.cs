namespace KeyboardUtils;

/// <summary>Note input: listening to the microphone, the computer's sound or a MIDI keyboard.</summary>
sealed partial class MainForm
{
    // --- Note input ("Listen": notes heard from the microphone or a MIDI input light up the keyboard) ---

    void ToggleListening()
    {
        if (noteSource != null)
        {
            StopListening();
            SetStatus(T("Poslouchání vypnuto.", "Stopped listening."));
        }
        else
            StartListening();
    }

    void StartListening()
    {
        StopListening();
        var source = config.CreateNoteSource();
        // Events queued before a stop must not light keys afterwards - hence the noteSource check.
        source.NoteOn += note => Post(() =>
        {
            if (noteSource != source)
                return;
            ShowKeyHeld(note, true);
            if (noteTrainer != null)
                noteTrainer.OnNoteOn(note);
            else if (learning != null)
            {
                learning.OnNoteOn(note);
                takeRecorder?.OnNoteOn(note);
            }
            else if (recorder != null)
                recorder.OnNoteOn(note);
            else
                SetStatus(T("Slyším: ", "Hearing: ") + NoteNames.Describe(note));
        });
        source.NoteOff += note => Post(() =>
        {
            if (noteSource != source)
                return;
            ShowKeyHeld(note, false);
            recorder?.OnNoteOff(note);
            takeRecorder?.OnNoteOff(note);
        });
        if (source is IInstrumentAware aware)
            aware.Instrument = instrument;
        try
        {
            source.Start();
        }
        catch (NoteSourceException ex)
        {
            source.Dispose();
            ShowModal(this, Text, ex.Message, AntdUI.TType.Error);
            return;
        }
        noteSource = source;
        listenButton.Text = T("Přestat poslouchat", "Stop listening");
        SetStatus(T($"Poslouchám: {source.Name}", $"Listening: {source.Name}"));
    }

    void StopListening()
    {
        noteSource?.Dispose();
        noteSource = null;
        pianoRoll.ClearHeldKeys();
        fretboard.ClearHeldKeys();
        listenButton.Text = T("Poslouchat", "Listen");
    }

    void SetNoteInput(NoteInputKind kind, string? midiDevice)
    {
        // A running session was set up for the old input (chords, latency).
        StopLearningMode(showResult: false);
        config.NoteInput = kind;
        config.MidiInputDevice = midiDevice;
        config.Save();
        if (noteSource != null)
            StartListening();
    }

    void ShowMicrophoneSetup()
    {
        // The dialog opens the microphone itself; don't hold the device twice.
        StopLearningMode(showResult: false);
        bool wasListening = noteSource != null;
        StopListening();
        using var setup = new MicrophoneSetupForm(config.MicrophoneDevice, config.MicrophoneThresholdDb);
        if (setup.ShowDialog(this) == DialogResult.OK)
        {
            config.MicrophoneDevice = setup.DeviceName;
            config.MicrophoneThresholdDb = setup.ThresholdDb;
            config.Save();
            SetStatus(T($"Mikrofon nastaven (práh {setup.ThresholdDb:0} dB).", $"Microphone set (threshold {setup.ThresholdDb:0} dB)."));
        }
        if (wasListening)
            StartListening();
    }
}
