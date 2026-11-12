namespace KeyboardUtils;

/// <summary>
/// The player's instruments, as browser-like tabs at the top ("Piano | Kytara | +"): close one with ×, drag them
/// round, + adds one. Only the piano is built in; every other instrument is a pack (its real sound) downloaded the
/// first time it's added (<see cref="InstrumentPacks"/>), managed in Settings > Instruments. The chosen instrument
/// sets the staff and clef, the sound, how the microphone listens, and what's under the falling notes - a keyboard,
/// or a fingerboard for a string instrument. The song itself is the same on every tab.
/// </summary>
partial class MainForm
{
    Instrument instrument = Instrument.Piano;

    /// <summary>The fingerboard shown in place of the keyboard for a string instrument.</summary>
    readonly FretboardControl fretboard = new() { Dock = DockStyle.Fill, Visible = false };

    /// <summary>"Ladička" beside the song's buttons, for every instrument but the piano.</summary>
    readonly AntdUI.Button tunerButton = new() { AutoSize = true, Radius = 8, Visible = false };

    /// <summary>Everything that was the "Skladba" tab: under the instrument tabs, the same for each.</summary>
    readonly Panel songPanel = new() { Dock = DockStyle.Fill };

    readonly BrowserTabStrip instrumentStrip = new() { Dock = DockStyle.Top };

    /// <summary>A pack download in progress (one at a time).</summary>
    bool downloadingPack;

    /// <summary>My instruments, in tab order (always at least one). Instruments whose pack isn't installed show with ⤓.</summary>
    List<Instrument> MyInstruments
    {
        get
        {
            var list = (config.MyInstruments ?? []).Select(Instrument.ById).Distinct().ToList();
            if (list.Count == 0)
                list.Add(Instrument.ById(config.Instrument));
            return list;
        }
    }

    void SaveMyInstruments(IEnumerable<Instrument> list)
    {
        config.MyInstruments = list.Select(i => i.Id).Distinct().ToList();
        config.Save();
    }

    /// <summary>At start: the saved instrument (the piano when its pack isn't here), before the MIDI output is opened.</summary>
    void LoadInstrument()
    {
        var saved = Instrument.ById(config.Instrument);
        SaveMyInstruments(MyInstruments.Contains(saved) ? MyInstruments : [.. MyInstruments, saved]);
        // An instrument chosen in 1.6, before instruments were packs: its tab waits for the download.
        instrument = InstrumentPacks.IsInstalled(saved) ? saved : Instrument.Piano;
        if (instrument != saved)
            Shown += (_, _) => SetStatus(T($"{saved.Name}: nově balíček ke stažení se skutečným zvukem nástroje - stáhneš ho kliknutím na záložku nahoře.",
                $"{saved.Name} now comes as a pack with its real sound - click its tab at the top to download it."));
        if (!MyInstruments.Contains(instrument))
            SaveMyInstruments([instrument, .. MyInstruments]);
        playbackEngine.Program = instrument.GmProgram;
        ApplyInstrumentSound();
        sheet.Instrument = instrument;
        fretboard.Instrument = instrument;
        ApplyNoteView();

        instrumentStrip.TabSelected += id => _ = OnInstrumentTabAsync(Instrument.ById(id));
        instrumentStrip.TabClosed += id => CloseInstrumentTab(Instrument.ById(id));
        instrumentStrip.TabsReordered += ids => SaveMyInstruments(ids.Select(Instrument.ById));
        instrumentStrip.AddClicked += _ => ShowAddInstrumentMenu();
        RebuildInstrumentTabs();
    }

    /// <summary>The pack's own sound for a pack instrument; the usual output for the piano.</summary>
    void ApplyInstrumentSound()
    {
        var manifest = instrument.IsPiano ? null : InstrumentPacks.Manifest(instrument.Id);
        playbackEngine.SetInstrumentSound(manifest != null ? InstrumentPacks.SoundPath(instrument.Id) : null,
            manifest?.MainPreset ?? 0, manifest?.PizzicatoPreset);
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
            SaveMyInstruments([.. MyInstruments, chosen]);
        config.Save();

        bool wasPlaying = playbackEngine.IsRunning;
        playbackEngine.Pause();
        double at = playbackEngine.CurrentSeconds;
        playbackEngine.Program = chosen.GmProgram;
        ApplyInstrumentSound();
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
        RebuildInstrumentTabs();
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

    void RebuildInstrumentTabs() =>
        instrumentStrip.SetTabs(MyInstruments.Select(i => new StripTab(i.Id, i.Name, !InstrumentPacks.IsInstalled(i))).ToList(), instrument.Id);

    /// <summary>A tab clicked: switch to it - downloading its pack first when it isn't here yet.</summary>
    async Task OnInstrumentTabAsync(Instrument chosen)
    {
        if (chosen == instrument)
            return;
        if (!InstrumentPacks.IsInstalled(chosen) && !await EnsureInstalledAsync(chosen))
        {
            RebuildInstrumentTabs(); // the selection back where it was
            return;
        }
        SetInstrument(chosen);
    }

    /// <summary>×: the tab goes (the pack stays installed - Settings > Instruments removes it). Never the last one.</summary>
    void CloseInstrumentTab(Instrument closed)
    {
        var mine = MyInstruments;
        if (mine.Count <= 1)
            return;
        int index = mine.IndexOf(closed);
        mine.Remove(closed);
        SaveMyInstruments(mine);
        if (closed == instrument)
        {
            // Its neighbour takes over, preferably one that's installed.
            var next = mine.Skip(Math.Max(0, index - 1)).Concat(mine).FirstOrDefault(InstrumentPacks.IsInstalled) ?? Instrument.Piano;
            if (!mine.Contains(next))
                SaveMyInstruments([next, .. mine]);
            SetInstrument(next);
        }
        RebuildInstrumentTabs();
    }

    /// <summary>+: the instruments not on a tab yet - installed ones open at once, the others say what they'd download.</summary>
    void ShowAddInstrumentMenu()
    {
        var mine = MyInstruments;
        var catalog = InstrumentPacks.CachedCatalogEntries();
        var items = new List<AntdUI.IContextMenuStripItem>();
        foreach (var candidate in Instrument.All.Where(i => !mine.Contains(i)))
        {
            string label = candidate.Name;
            if (!InstrumentPacks.IsInstalled(candidate))
            {
                var entry = catalog.FirstOrDefault(e => e.Id == candidate.Id);
                label += entry != null ? $"   ⤓ {InstrumentPacks.SizeText(entry.Bytes)}" : "   ⤓";
            }
            var chosen = candidate;
            items.Add(new AntdUI.ContextMenuStripItem(label) { Tag = (Action)(() => _ = AddInstrumentAsync(chosen)) });
        }
        if (items.Count > 0)
            items.Add(new AntdUI.ContextMenuStripItemDivider());
        items.Add(new AntdUI.ContextMenuStripItem(T("Spravovat nástroje...", "Manage instruments...")) { Tag = (Action)ShowInstrumentPacks });
        ShowMenu(instrumentStrip, items.ToArray());
        // Sizes for next time, if this was the first look (or they changed).
        if (catalog.Count == 0)
            _ = InstrumentPacks.CatalogAsync();
    }

    async Task AddInstrumentAsync(Instrument added)
    {
        if (!InstrumentPacks.IsInstalled(added) && !await EnsureInstalledAsync(added))
            return;
        SaveMyInstruments([.. MyInstruments, added]);
        SetInstrument(added);
    }

    /// <summary>
    /// Downloads an instrument's pack after asking (its size, what it is); true when it's installed now. Progress in
    /// the status line.
    /// </summary>
    async Task<bool> EnsureInstalledAsync(Instrument wanted)
    {
        if (InstrumentPacks.IsInstalled(wanted))
            return true;
        if (downloadingPack)
        {
            SetStatus(T("Jeden balíček se už stahuje - chvilku strpení.", "A pack is already downloading - just a moment."));
            return false;
        }
        downloadingPack = true;
        try
        {
            SetStatus(T("Zjišťuji, co je ke stažení...", "Checking what's available..."));
            var entry = (await InstrumentPacks.CatalogAsync()).FirstOrDefault(e => e.Id == wanted.Id);
            if (entry == null)
            {
                ShowModal(this, wanted.Name, T("Seznam balíčků se nepodařilo načíst - zkontroluj připojení k internetu.",
                    "Couldn't load the list of packs - check your internet connection."), AntdUI.TType.Error);
                return false;
            }
            if (!Confirm(this, wanted.Name,
                    T($"{wanted.Name} je balíček ke stažení ({InstrumentPacks.SizeText(entry.Bytes)}): noty v klíči nástroje, jeho rozsah a skutečný zvuk nástroje.\n\nZvuk: {entry.Credits}",
                      $"{wanted.Name} is a pack to download ({InstrumentPacks.SizeText(entry.Bytes)}): music in the instrument's clef, its range and its real sound.\n\nSound: {entry.Credits}"),
                    T("Stáhnout", "Download")))
                return false;
            var progress = new Progress<double>(p => SetStatus(T($"Stahuji {wanted.Name}... {p:P0}", $"Downloading {wanted.Name}... {p:P0}")));
            string? error = await InstrumentPacks.InstallAsync(entry, progress);
            if (error != null)
            {
                SetStatus(error);
                ShowModal(this, wanted.Name, error, AntdUI.TType.Error);
                return false;
            }
            SetStatus(T($"{wanted.Name}: staženo.", $"{wanted.Name}: downloaded."));
            return true;
        }
        finally
        {
            downloadingPack = false;
        }
    }

    /// <summary>Settings > Instruments: install several, or remove what's not needed.</summary>
    void ShowInstrumentPacks()
    {
        using var form = new InstrumentPacksForm(this);
        form.ShowDialog(this);
        RebuildInstrumentTabs();
    }

    /// <summary>Before a pack is deleted: if it's the one playing, switch to the piano so its sound file is let go.</summary>
    internal void ReleaseInstrumentPack(Instrument removed)
    {
        if (removed == instrument)
            SetInstrument(Instrument.Piano);
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
