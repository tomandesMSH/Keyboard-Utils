namespace KeyboardUtils;

/// <summary>
/// Keyboard shortcuts of the main window, and the actions they share with the macro keyboard (seeking, speed, loop,
/// metronome...). Each action checks for itself whether it can run now, like the button it stands for.
/// </summary>
sealed partial class MainForm
{
    /// <summary>A shortcut: its keys, what it's shown as in the list, what it does, and whether only on the song tab.</summary>
    sealed record Shortcut(Keys Keys, string KeyText, string Description, Action Run, bool SongTabOnly = true);

    /// <summary>Built on use, so the texts follow the current language.</summary>
    Shortcut[] Shortcuts =>
    [
        new(Keys.Space, T("Mezerník", "Space"), T("Přehrát / pauza", "Play / pause"), () => IfEnabled(playPauseButton, TogglePlayPause)),
        new(Keys.Home, "Home", T("Zastavit (zpět na začátek)", "Stop (back to the start)"), () => IfEnabled(stopButton, DoStop)),
        new(Keys.Left, "←", T("O 5 s zpět", "Back 5 s"), () => SeekStep(-5)),
        new(Keys.Right, "→", T("O 5 s dopředu", "Forward 5 s"), () => SeekStep(5)),
        new(Keys.Left | Keys.Shift, "Shift + ←", T("O 1 s zpět", "Back 1 s"), () => SeekStep(-1)),
        new(Keys.Right | Keys.Shift, "Shift + →", T("O 1 s dopředu", "Forward 1 s"), () => SeekStep(1)),
        new(Keys.Up, "↑", T("Rychlejší přehrávání (+5 %)", "Faster playback (+5 %)"), () => StepSpeed(1)),
        new(Keys.Down, "↓", T("Pomalejší přehrávání (−5 %)", "Slower playback (−5 %)"), () => StepSpeed(-1)),
        new(Keys.R, "R", T("Rychlost zpět na 100 %", "Speed back to 100 %"), ResetSpeed),
        new(Keys.L, "L", T("Smyčka zapnout / vypnout", "Loop on / off"), LoopToggleAction),
        new(Keys.A, "A", T("Začátek smyčky = aktuální místo", "Loop start = current position"), LoopStartAction),
        new(Keys.B, "B", T("Konec smyčky = aktuální místo", "Loop end = current position"), LoopEndAction),
        new(Keys.M, "M", T("Metronom zapnout / vypnout", "Metronome on / off"), ToggleMetronome),
        new(Keys.T, "T", T("Metronom bez podkladu: naťukat tempo", "Metronome without a song: tap the tempo"), TapMetronome),
        new(Keys.H, "H", T("Procvičit těžké místo (smyčka + učení)", "Practise the hard spot (loop + learning)"), () => IfEnabled(learningButton, () => PracticeHardSpot())),
        new(Keys.K, "K", T("Popisky kláves zapnout / vypnout", "Key labels on / off"), ToggleKeyLabels),
        new(Keys.V, "V", T("Přepnout zobrazení (klávesy / noty / obojí)", "Switch the view (keyboard / sheet / both)"), CycleSongView),
        new(Keys.F5, "F5", T("Režim učení", "Learning mode"), () => IfEnabled(learningButton, () => ToggleLearningMode(showSetup: true))),
        new(Keys.F6, "F6", T("Zapisovat noty", "Write notes"), () => IfEnabled(createButton, ToggleCreateMode)),
        new(Keys.F7, "F7", T("Poslouchat", "Listen"), () => IfEnabled(listenButton, ToggleListening)),
        new(Keys.Oemplus, "+", T("Noty větší", "Sheet music larger"), () => ZoomSheet(1)),
        new(Keys.Add, T("+ (numerická)", "+ (numpad)"), T("Noty větší", "Sheet music larger"), () => ZoomSheet(1)),
        new(Keys.OemMinus, "−", T("Noty menší", "Sheet music smaller"), () => ZoomSheet(-1)),
        new(Keys.Subtract, T("− (numerická)", "− (numpad)"), T("Noty menší", "Sheet music smaller"), () => ZoomSheet(-1)),
        new(Keys.PageUp, "Page Up", T("Předchozí skladba", "Previous song"), () => StepTrack(-1)),
        new(Keys.PageDown, "Page Down", T("Další skladba", "Next song"), () => StepTrack(1)),
        new(Keys.Control | Keys.R, "Ctrl + R", T("Uložit nahrávku (MIDI, bez dialogu)", "Save the recording (MIDI, no dialog)"), SaveRecordingQuick),
        new(Keys.Control | Keys.Z, "Ctrl + Z", T("Zpět (úprava noty)", "Undo (note edit)"), UndoNoteEdit),
        new(Keys.Control | Keys.Y, "Ctrl + Y", T("Vpřed (úprava noty)", "Redo (note edit)"), RedoNoteEdit),
        new(Keys.Control | Keys.O, "Ctrl + O", T("Otevřít skladbu", "Open song"), OpenSong, SongTabOnly: false),
        new(Keys.Control | Keys.S, "Ctrl + S", T("Uložit skladbu jako", "Save song as"), SaveCurrentSong, SongTabOnly: false),
        new(Keys.Control | Keys.D, "Ctrl + D", T("Tmavý / světlý režim", "Dark / light mode"), ToggleDarkMode, SongTabOnly: false),
        new(Keys.F1, "F1", T("Tento seznam zkratek", "This list of shortcuts"), ShowShortcuts, SongTabOnly: false),
    ];

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Typing a value (the speed box) keeps its keys; so does the macro list (Delete, arrows).
        if (!TypingInto(ActiveControl))
            foreach (var shortcut in Shortcuts)
            {
                if (shortcut.Keys != keyData || (shortcut.SongTabOnly && tabs.SelectedTab != songTab))
                    continue;
                RunFromControl(shortcut.Run);
                return true;
            }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>A shortcut for a button does what clicking it would - nothing while it's disabled.</summary>
    static void IfEnabled(Control button, Action action)
    {
        if (button.Enabled)
            action();
    }

    static bool TypingInto(Control? control)
    {
        // The focused control can sit inside nested containers (panels, split containers).
        while (control is ContainerControl { ActiveControl: { } inner })
            control = inner;
        return control is TextBoxBase or AntdUI.Input or ListView or ComboBox;
    }

    void ShowShortcuts()
    {
        var lines = Shortcuts.Select(s => $"{s.KeyText}  –  {s.Description}");
        ShowModal(this, T("Klávesové zkratky", "Keyboard shortcuts"), string.Join("\n", lines), AntdUI.TType.Info);
    }

    // --- Actions shared by shortcuts and macro keys ------------------------------------------------

    void SeekStep(double seconds)
    {
        if (!rewindButton.Enabled)
            return;
        SeekBy(seconds);
        SetStatus(FormatTime(PlaybackSeconds));
    }

    void ZoomSheet(int steps)
    {
        sheet.ZoomBy(steps);
        SetStatus(T($"Velikost not: {sheet.Zoom * 100:0} %", $"Sheet music size: {sheet.Zoom * 100:0} %")
            + (songViews.Panel2Collapsed ? T(" (noty teď nejsou zobrazené - přepni zobrazení)", " (the sheet music isn't shown now - switch the view)") : ""));
    }

    /// <summary>
    /// Steps of 5 % (to the next multiple of 5, so 73 goes to 75 / 70), like the arrows by the speed box.
    /// Negative = slower.
    /// </summary>
    void StepSpeed(int steps)
    {
        // The metronome on its own: the tempo is its BPM.
        if (StandaloneOn && learning == null)
        {
            SetMetronomeBpm(config.MetronomeBpm + 5 * steps);
            SetStatus(T($"Metronom: {config.MetronomeBpm} BPM", $"Metronome: {config.MetronomeBpm} BPM"));
            return;
        }
        if (!speedSlider.Enabled)
            return;
        int value = speedSlider.Value;
        for (int i = 0; i < Math.Abs(steps); i++)
            value = steps > 0 ? (value / 5 + 1) * 5 : (value + 4) / 5 * 5 - 5;
        SetSpeed(value);
        SetStatus(T($"Rychlost přehrávání: {speedSlider.Value} %", $"Playback speed: {speedSlider.Value} %"));
    }

    void ResetSpeed()
    {
        if (!speedSlider.Enabled)
            return;
        SetSpeed(100);
        SetStatus(T("Rychlost přehrávání: 100 %", "Playback speed: 100 %"));
    }

    void LoopToggleAction()
    {
        if (loopButton.Enabled)
            ToggleLoop();
    }

    void LoopStartAction()
    {
        if (loopStartButton.Enabled)
            SetLoopStart(PlaybackSeconds);
    }

    void LoopEndAction()
    {
        if (loopEndButton.Enabled)
            SetLoopEnd(PlaybackSeconds);
    }

    void ToggleMetronome()
    {
        if (!metronomeBox.Enabled)
            return;
        metronomeBox.Checked = !metronomeBox.Checked;
        SetStatus(metronomeBox.Checked ? T("Metronom zapnutý.", "Metronome on.") : T("Metronom vypnutý.", "Metronome off."));
    }

    // --- The metronome on its own ---------------------------------------------------------------------

    void UpdateMetronomeOptions()
    {
        // Only the rows' contents hide: the rows keep their height, so the transport row - and the keyboard under it -
        // don't change size when the metronome or its options are switched on or off.
        bool shown = config.Metronome && config.MetronomeOptionsShown;
        foreach (var optionsRow in (FlowLayoutPanel[])[metronomeOptions, metronomeDetectRow])
        {
            foreach (Control control in optionsRow.Controls)
                control.Visible = shown;
            optionsRow.MinimumSize = new Size(0, optionsRow.Controls.Cast<Control>().Max(c => c.Height + c.Margin.Vertical));
        }
        AlignDetectRow();
        metronomeExpander.Visible = config.Metronome;
        metronomeExpander.IconSvg = config.MetronomeOptionsShown ? Icons.Expanded : Icons.Collapsed;
        bpmBox.Enabled = bpmArrows.Enabled = tapButton.Enabled = meterBox.Enabled = config.MetronomeStandalone;
        UpdateTransport();
        UpdatePlayPauseButton();
    }

    /// <summary>
    /// Lines the row up with the one above: Detection under "without a song", reaching up to the tempo box, and the
    /// time signature right under the tempo box, as wide. Done from where the tempo box really is once laid out (the
    /// "without a song" box's width isn't final before that, so it can't be computed up front).
    /// </summary>
    void AlignDetectRow()
    {
        if (!bpmBox.Visible)
            return;
        // The tempo box's left edge, from the start of the detection row.
        int bpmLeft = metronomeOptions.Left + bpmBox.Left - metronomeDetectRow.Parent!.Padding.Left;
        int gap = LogicalToDeviceUnits(6);
        int tapWidth = Math.Max(LogicalToDeviceUnits(60), bpmLeft - gap);
        if (tapButton.Width != tapWidth)
            tapButton.Width = tapWidth;
        if (meterBox.Width != bpmBox.Width)
            meterBox.Width = bpmBox.Width;
        if (meterBox.Margin.Left != bpmLeft - tapWidth)
            meterBox.Margin = new Padding(Math.Max(0, bpmLeft - tapWidth), 0, 0, 0);
        if (metronomeDetectRow.Margin.Left != 0)
            metronomeDetectRow.Margin = new Padding(0, 4, 0, 0);
    }

    void SetMetronomeBpm(int bpm)
    {
        bpm = Math.Clamp(bpm, StandaloneMetronome.MinBpm, StandaloneMetronome.MaxBpm);
        if ((int)Math.Round(bpmBox.Value) != bpm)
            bpmBox.Value = bpm; // raises ValueChanged, which comes back here with the same value
        if (config.MetronomeBpm == bpm)
            return;
        config.MetronomeBpm = bpm;
        Clicker.Bpm = bpm;
        config.Save();
    }

    void SetMetronomeMeter(string meter)
    {
        if (meterBox.SelectedIndex != Array.IndexOf(StandaloneMetronome.Meters, meter))
            meterBox.SelectedIndex = Array.IndexOf(StandaloneMetronome.Meters, meter); // comes back here with the same meter
        Clicker.Meter = meter;
        if (config.MetronomeMeter == Clicker.Meter)
            return;
        config.MetronomeMeter = Clicker.Meter;
        config.Save();
    }

    void CycleMetronomeMeter()
    {
        var meters = StandaloneMetronome.Meters;
        SetMetronomeMeter(meters[(Array.IndexOf(meters, config.MetronomeMeter) + 1) % meters.Length]);
        SetStatus(T($"Takt metronomu: {config.MetronomeMeter}", $"Metronome time signature: {config.MetronomeMeter}"));
    }

    /// <summary>One tap of the tempo; from the second tap in a row on, the metronome takes the tapped tempo.</summary>
    void TapMetronome()
    {
        if (!StandaloneOn)
        {
            SetStatus(T("Ťukání tempa je pro metronom bez podkladu: zaškrtni Metronom a Bez podkladu.",
                "Tapping the tempo is for the metronome without a song: check Metronome and Without a song."));
            return;
        }
        // Hear the tap - unless the metronome is clicking anyway.
        if (!StandaloneRunning)
            playbackEngine.PlayClick(false);
        if (tapTempo.Tap(tapClock.Elapsed.TotalSeconds) is { } bpm)
        {
            SetMetronomeBpm(bpm);
            SetStatus(T($"Tempo: {bpm} BPM", $"Tempo: {bpm} BPM"));
        }
        else
            SetStatus(T("Ťukej dál v rytmu...", "Keep tapping in time..."));
    }

    /// <summary>"Without a song" on / off - also switching the metronome on, so it's one key from anywhere.</summary>
    void ToggleStandaloneOption()
    {
        bool on = !StandaloneOn;
        if (on && !metronomeBox.Checked)
            metronomeBox.Checked = true;
        standaloneBox.Checked = on;
        SetStatus(on ? T($"Metronom bez podkladu: {config.MetronomeBpm} BPM, {config.MetronomeMeter}. Spustíš ho Přehrát.",
                $"Metronome without a song: {config.MetronomeBpm} BPM, {config.MetronomeMeter}. Start it with Play.")
            : T("Metronom bez podkladu vypnutý.", "Metronome without a song off."));
    }

    // --- Updates ------------------------------------------------------------------------------------

    /// <param name="manual">Asked for in the menu: say so also when there's nothing new or GitHub can't be reached.</param>
    async Task CheckForUpdatesAsync(bool manual)
    {
        if (manual)
            SetStatus(T("Zjišťuji, jestli je nová verze...", "Checking for a new version..."));
        var (reached, update) = await UpdateChecker.CheckAsync(UpdateChecker.CurrentVersion);
        if (IsDisposed)
            return;
        if (reached)
        {
            config.LastUpdateCheck = DateTime.Now;
            config.Save();
        }
        if (update != null)
        {
            ShowUpdate(update);
            if (manual && CanUpdateInPlace(update))
                SetStatus(T($"Je venku nová verze {update.Version.ToString(3)} - aktualizuješ ji tlačítkem vpravo dole.",
                    $"Version {update.Version.ToString(3)} is out - update with the button bottom right."));
            else if (manual)
                SetStatus(T($"Je venku nová verze {update.Version.ToString(3)} - stáhneš ji tlačítkem vpravo dole.",
                    $"Version {update.Version.ToString(3)} is out - download it with the button bottom right."));
        }
        else if (manual)
            SetStatus(reached
                ? T($"Máš nejnovější verzi ({UpdateChecker.CurrentVersion.ToString(3)}).", $"You have the latest version ({UpdateChecker.CurrentVersion.ToString(3)}).")
                : T("GitHub se nepodařilo zastihnout - zkus to později.", "Couldn't reach GitHub - try again later."));
    }

    void ShowUpdate(AvailableUpdate update)
    {
        availableUpdate = update;
        updateButton.Text = CanUpdateInPlace(update)
            ? T($"Nová verze {update.Version.ToString(3)} - aktualizovat", $"New version {update.Version.ToString(3)} - update")
            : T($"Nová verze {update.Version.ToString(3)} - stáhnout", $"New version {update.Version.ToString(3)} - download");
        updateButton.Width = TextRenderer.MeasureText(updateButton.Text, updateButton.Font).Width + LogicalToDeviceUnits(24);
        updateGroup.Visible = true;
    }

    static bool CanUpdateInPlace(AvailableUpdate update) => update.Exe != null && SelfUpdater.UpdatableExe() != null;

    bool updating;

    /// <summary>
    /// The update button: downloads the new exe, swaps it in and restarts (<see cref="SelfUpdater"/>). Where that
    /// can't work (a development build, a folder without write access, a release without a digest) it opens the
    /// release page as before.
    /// </summary>
    async Task UpdateAsync()
    {
        if (updating || availableUpdate is not { } update)
            return;
        if (update.Exe is not { } asset || SelfUpdater.UpdatableExe() is not { } exe)
        {
            OpenUpdatePage();
            return;
        }
        // A restart loses an unsaved recording and ends learning: ask first.
        bool unsaved = recorder != null || (currentScore != null && currentScore == unsavedRecording);
        if ((unsaved || learning != null) && !Confirm(this, Text,
                unsaved
                    ? T("Máš neuloženou nahrávku - po aktualizaci bude pryč. Aktualizovat a restartovat i tak?",
                        "You have an unsaved recording - it will be gone after the update. Update and restart anyway?")
                    : T("Aktualizace ukončí režim učení a restartuje aplikaci. Pokračovat?",
                        "The update ends learning mode and restarts the app. Continue?"),
                T("Aktualizovat", "Update")))
            return;

        updating = true;
        updateButton.Enabled = false;
        string version = update.Version.ToString(3);
        string newExe = SelfUpdater.NewPath(exe);
        try
        {
            int lastPercent = -1;
            var progress = new Progress<double>(fraction =>
            {
                int percent = (int)(fraction * 100);
                if (percent == lastPercent || IsDisposed)
                    return;
                lastPercent = percent;
                statusLabel.Text = T($"Stahuji verzi {version}: {percent} %", $"Downloading version {version}: {percent} %");
            });
            await SelfUpdater.DownloadAsync(asset, newExe, progress);
            if (IsDisposed)
                return;
            StopLearningMode(showResult: false);
            if (recorder != null)
                StopCreateMode();
            SelfUpdater.Swap(exe, newExe);
            try
            {
                SelfUpdater.StartNew(exe);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The new exe won't start: put the old one back.
                SelfUpdater.RollBack(exe);
                throw;
            }
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
            or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            if (IsDisposed)
                return;
            updating = false;
            updateButton.Enabled = true;
            statusLabel.Text = "";
            ShowModal(this, Text,
                T($"Aktualizace se nepovedla ({ex.Message}). Otevřu stránku, odkud si novou verzi stáhneš ručně.",
                    $"The update failed ({ex.Message}). Opening the page to download the new version by hand."),
                AntdUI.TType.Error);
            OpenUpdatePage();
        }
    }

    void OpenUpdatePage()
    {
        string url = availableUpdate?.PageUrl ?? UpdateChecker.ReleasesPage;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus(T("Prohlížeč se nepodařilo otevřít: ", "Couldn't open the browser: ") + url);
        }
    }

    void ToggleStandalone()
    {
        if (StandaloneRunning)
        {
            StopStandalone();
            SetStatus(T("Metronom zastaven.", "Metronome stopped."));
            return;
        }
        // Not over the song: "without accompaniment" means just the clicks.
        CancelCountIn();
        if (PlaybackRunning)
            PlaybackPause();
        Clicker.Bpm = config.MetronomeBpm;
        Clicker.Start();
        UpdatePlayPauseButton();
        SetStatus(T($"Metronom: {config.MetronomeBpm} BPM", $"Metronome: {config.MetronomeBpm} BPM"));
    }

    void StopStandalone()
    {
        if (!StandaloneRunning)
            return;
        standaloneMetronome!.Stop();
        UpdatePlayPauseButton();
    }

    void ToggleKeyLabels()
    {
        keyLabelsBox.Checked = !keyLabelsBox.Checked;
        SetStatus(keyLabelsBox.Checked ? T("Popisky kláves zapnuté.", "Key labels on.") : T("Popisky kláves vypnuté.", "Key labels off."));
    }

    /// <summary>Keyboard → sheet music → both → keyboard...</summary>
    void CycleSongView()
    {
        int count = Enum.GetValues<SongView>().Length;
        viewSwitch.SelectIndex = (viewSwitch.SelectIndex + 1) % count;
        if (viewSwitch.SelectIndex >= 0 && viewSwitch.SelectIndex < viewSwitch.Items.Count)
            SetStatus(T("Zobrazení: ", "View: ") + viewSwitch.Items[viewSwitch.SelectIndex].Text);
    }
}
