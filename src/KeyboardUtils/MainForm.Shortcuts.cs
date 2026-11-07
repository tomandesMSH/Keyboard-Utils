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
                shortcut.Run();
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
