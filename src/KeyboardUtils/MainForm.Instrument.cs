namespace KeyboardUtils;

/// <summary>The player's instrument (Settings > My instrument): its staff and clef, its sound, how the microphone listens.</summary>
partial class MainForm
{
    Instrument instrument = Instrument.Piano;

    /// <summary>At start: the saved instrument, before the MIDI output is opened (the piano sound pack is piano-only).</summary>
    void LoadInstrument()
    {
        instrument = Instrument.ById(config.Instrument);
        playbackEngine.Program = instrument.GmProgram;
        sheet.Instrument = instrument;
    }

    void SetInstrument(Instrument chosen)
    {
        if (chosen == instrument)
            return;
        instrument = chosen;
        config.Instrument = chosen.Id;
        config.Save();

        bool wasPlaying = playbackEngine.IsRunning;
        playbackEngine.Pause();
        double at = playbackEngine.CurrentSeconds;
        playbackEngine.Program = chosen.GmProgram;
        playbackEngine.SetOutputDevice(config.MidiOutputDevice);
        if (currentScore != null)
        {
            playbackEngine.Load(currentScore, ClickTiming);
            playbackEngine.Speed = playbackSpeed;
            playbackEngine.SeekTo(at);
            if (wasPlaying)
                playbackEngine.Play();
        }
        sheet.Instrument = chosen;
        ApplyInstrumentToInput();
        SetStatus(chosen.IsPiano
            ? T("Můj nástroj: klavír - noty na dvou osnovách.", "My instrument: piano - music on the grand staff.")
            : T($"Můj nástroj: {chosen.Name} - noty v jeho klíči, tak jak je čteš.", $"My instrument: {chosen.Name} - music in its clef, as you read it."));
    }

    /// <summary>The audio inputs listen for the instrument: legato and vibrato for bowed and wind instruments, its range.</summary>
    void ApplyInstrumentToInput()
    {
        if (noteSource is IInstrumentAware aware)
            aware.Instrument = instrument;
    }

    TunerForm? tuner;

    /// <summary>The tuner, for the chosen instrument. Not modal: it can stay open beside the song.</summary>
    void OpenTuner()
    {
        if (tuner is { IsDisposed: false })
        {
            tuner.Activate();
            return;
        }
        tuner = new TunerForm(config, instrument, pitch => playbackEngine.PlayNote(pitch, 1500));
        tuner.FormClosed += (_, _) => tuner = null;
        tuner.Show(this);
    }

    AntdUI.IContextMenuStripItem[] BuildInstrumentItems() =>
        Instrument.All.Select(i => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(i.Name)
        {
            Checked = i == instrument,
            Tag = (Action)(() => SetInstrument(i)),
        }).ToArray();
}

/// <summary>A note input that listens differently for each instrument (the audio ones).</summary>
interface IInstrumentAware
{
    Instrument Instrument { get; set; }
}
