namespace KeyboardUtils;

/// <summary>
/// The player's instruments: one tab each at the top ("Piano | Kytara | +"), the chosen one sets the staff and clef,
/// the sound, how the microphone listens - and what's under the falling notes: a keyboard, or a fingerboard for a
/// string instrument. The song itself is the same on every tab; switching moves it along.
/// </summary>
partial class MainForm
{
    Instrument instrument = Instrument.Piano;

    /// <summary>The fingerboard shown in place of the keyboard for a string instrument.</summary>
    readonly FretboardControl fretboard = new() { Dock = DockStyle.Fill, Visible = false };

    /// <summary>"Ladička" beside the song's buttons, for every instrument but the piano.</summary>
    readonly AntdUI.Button tunerButton = new() { AutoSize = true, Radius = 8, Visible = false };

    /// <summary>Everything that was the "Skladba" tab: moved into the selected instrument's tab.</summary>
    readonly Panel songPanel = new() { Dock = DockStyle.Fill };

    readonly List<(AntdUI.TabPage Page, Instrument Instrument)> instrumentPages = [];
    readonly AntdUI.TabPage addInstrumentPage = new() { Text = "+" };
    bool rebuildingTabs;

    /// <summary>My instruments, in tab order (always at least one, always holding the current one).</summary>
    List<Instrument> MyInstruments
    {
        get
        {
            var list = (config.MyInstruments ?? []).Select(Instrument.ById).Distinct().ToList();
            if (list.Count == 0 || !list.Contains(Instrument.ById(config.Instrument)))
                list.Add(Instrument.ById(config.Instrument));
            return list;
        }
    }

    /// <summary>At start: the saved instrument, before the MIDI output is opened (the piano sound pack is piano-only).</summary>
    void LoadInstrument()
    {
        instrument = Instrument.ById(config.Instrument);
        config.MyInstruments = MyInstruments.Select(i => i.Id).ToList();
        playbackEngine.Program = instrument.GmProgram;
        sheet.Instrument = instrument;
        fretboard.Instrument = instrument;
        ApplyNoteView();
        RebuildInstrumentTabs();
    }

    void SetInstrument(Instrument chosen)
    {
        if (chosen == instrument)
            return;
        // Learning is scored for one instrument: a switch ends it (without a result).
        StopLearningMode(showResult: false);
        instrument = chosen;
        config.Instrument = chosen.Id;
        if (!MyInstruments.Contains(chosen))
            config.MyInstruments = [.. MyInstruments.Select(i => i.Id), chosen.Id];
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
        fretboard.Instrument = chosen;
        ApplyNoteView();
        ApplyInstrumentToInput();
        SelectInstrumentTab();
        SetStatus(chosen.IsPiano
            ? T("Piano - noty na dvou osnovách.", "Piano - music on the grand staff.")
            : T($"{chosen.Name} - noty v jeho klíči, tak jak je čteš.", $"{chosen.Name} - music in its clef, as you read it."));
    }

    /// <summary>The audio inputs listen for the instrument: legato and vibrato for bowed and wind instruments, its range.</summary>
    void ApplyInstrumentToInput()
    {
        if (noteSource is IInstrumentAware aware)
            aware.Instrument = instrument;
    }

    // --- The tabs ----------------------------------------------------------------------------------------

    /// <summary>One tab per instrument of mine, then "+" (while there's any left to add).</summary>
    void RebuildInstrumentTabs()
    {
        rebuildingTabs = true;
        try
        {
            songPanel.Parent?.Controls.Remove(songPanel);
            tabs.Pages.Clear();
            instrumentPages.Clear();
            foreach (var mine in MyInstruments)
            {
                var page = new AntdUI.TabPage { Text = mine.Name };
                instrumentPages.Add((page, mine));
                tabs.Pages.Add(page);
            }
            if (MyInstruments.Count < Instrument.All.Count)
                tabs.Pages.Add(addInstrumentPage);
            SelectInstrumentTab();
        }
        finally
        {
            rebuildingTabs = false;
        }
    }

    /// <summary>Shows the current instrument's tab, with the song in it.</summary>
    void SelectInstrumentTab()
    {
        var page = instrumentPages.FirstOrDefault(p => p.Instrument == instrument).Page;
        if (page == null)
            return;
        bool was = rebuildingTabs;
        rebuildingTabs = true;
        try
        {
            if (songPanel.Parent != page)
            {
                songPanel.Parent?.Controls.Remove(songPanel);
                page.Controls.Add(songPanel);
            }
            if (tabs.SelectedTab != page)
                tabs.SelectedTab = page;
        }
        finally
        {
            rebuildingTabs = was;
        }
    }

    void OnTabChanged()
    {
        if (rebuildingTabs)
            return;
        if (tabs.SelectedTab == addInstrumentPage)
        {
            // "+": back to where we were, and a menu of the instruments not added yet.
            SelectInstrumentTab();
            var mine = MyInstruments;
            ShowMenu(tabs, Instrument.All.Where(i => !mine.Contains(i))
                .Select(i => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(i.Name) { Tag = (Action)(() => AddInstrument(i)) })
                .ToArray());
            return;
        }
        var chosen = instrumentPages.FirstOrDefault(p => p.Page == tabs.SelectedTab).Instrument;
        if (chosen == null || chosen == instrument)
            return;
        SetInstrument(chosen);
    }

    void AddInstrument(Instrument added)
    {
        config.MyInstruments = [.. MyInstruments.Select(i => i.Id), added.Id];
        config.Save();
        RebuildInstrumentTabs();
        SetInstrument(added);
    }

    /// <summary>Settings > My instruments: which tabs there are (at least one).</summary>
    void EditMyInstruments()
    {
        using var form = new MyInstrumentsForm(MyInstruments);
        if (form.ShowDialog(this) != DialogResult.OK)
            return;
        var chosen = form.Chosen;
        config.MyInstruments = chosen.Select(i => i.Id).ToList();
        config.Save();
        if (chosen.Contains(instrument))
        {
            RebuildInstrumentTabs();
            return;
        }
        // The current instrument was removed: the first one left takes over.
        config.Instrument = chosen[0].Id;
        RebuildInstrumentTabs();
        SetInstrument(chosen[0]);
    }

    // --- Keyboard or fingerboard, the tuner button ------------------------------------------------------

    /// <summary>A string instrument gets the fingerboard; writing and editing notes always use the keyboard view.</summary>
    bool ShowsFretboard => instrument.Strings.Length > 0 && recorder == null && noteEditor == null;

    /// <summary>Puts the keyboard or the fingerboard under the notes, with the matching names (called often: cheap).</summary>
    void ApplyNoteView()
    {
        bool board = ShowsFretboard;
        if (fretboard.Visible != board)
        {
            fretboard.Visible = board;
            pianoRoll.Visible = !board;
            ApplyViewNames();
        }
        bool tuner = !instrument.IsPiano;
        if (tunerButton.Visible != tuner)
            tunerButton.Visible = tuner;
        // The key-size slider only means something for the keyboard.
        if (keyboardSizeGroup != null && keyboardSizeGroup.Visible == board)
            keyboardSizeGroup.Visible = !board;
    }

    /// <summary>"Klávesy / Noty / Noty + klávesy", or "Hmatník" for a string instrument.</summary>
    void ApplyViewNames()
    {
        bool board = fretboard.Visible;
        string[] viewNames = board
            ? [T("Hmatník", "Fingerboard"), T("Noty", "Sheet"), T("Noty + hmatník", "Sheet + fingerboard")]
            : [T("Klávesy", "Keyboard"), T("Noty", "Sheet"), T("Noty + klávesy", "Sheet + keyboard")];
        for (int i = 0; i < viewSwitch.Items.Count && i < viewNames.Length; i++)
            viewSwitch.Items[i].Text = viewNames[i];
        // As wide as its items: extra width stays empty after the last one. (AntdUI's AutoSize shrinks it to nothing.)
        viewSwitch.Width = viewNames.Sum(name => TextRenderer.MeasureText(name, viewSwitch.Font).Width + LogicalToDeviceUnits(12));
        viewSwitch.Invalidate();
    }

    // --- Display: the keyboard and the fingerboard show the same ---------------------------------------------

    void ShowKeyHeld(int pitch, bool down)
    {
        pianoRoll.SetKeyHeld(pitch, down);
        fretboard.SetKeyHeld(pitch, down);
    }

    void FlashNote(int pitch, int durationMs = 400, Color? color = null)
    {
        pianoRoll.FlashKey(pitch, durationMs, color);
        fretboard.FlashKey(pitch, durationMs, color);
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
}

/// <summary>A note input that listens differently for each instrument (the audio ones).</summary>
interface IInstrumentAware
{
    Instrument Instrument { get; set; }
}
