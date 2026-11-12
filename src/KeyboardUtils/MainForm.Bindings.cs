namespace KeyboardUtils;

/// <summary>The macro keyboard: picking it, its keys and their actions, and running those actions.</summary>
sealed partial class MainForm
{
    // --- Keyboard selection -------------------------------------------------------------------

    void StartLearning()
    {
        CancelCapture();

        if (!MacroKeyboardModule.IsInstalled)
        {
            var confirm = MessageBox.Show(this,
                T("Ovládání appky makro klávesnicí potřebuje stáhnout malý doplňkový modul (pár desítek KB). Stáhnout teď?",
                    "Controlling the app with a macro keyboard needs a small add-on module downloaded first (a few tens of KB). Download it now?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            SetStatus(T("Stahuji modul...", "Downloading module..."));
            Cursor = Cursors.WaitCursor;
            bool downloaded = MacroKeyboardModule.Download();
            Cursor = Cursors.Default;
            if (!downloaded)
            {
                ShowModal(this, Text, T("Stažení modulu se nepovedlo. Zkontroluj připojení k internetu a zkus to znovu.",
                    "Downloading the module failed. Check your internet connection and try again."), AntdUI.TType.Error);
                SetStatus(T("Stažení modulu selhalo.", "Module download failed."));
                return;
            }
            SetStatus(T("Modul stažen.", "Module downloaded."));
        }

        if (interceptor == null)
        {
            interceptor = MacroKeyboardFactory.TryCreate();
            if (interceptor == null)
            {
                // No Interception driver available yet - offer to install it. Purely opt-in: the app
                // works fine without a macro keyboard, this only runs when the user asks for it here.
                using var setup = new DriverSetupForm();
                setup.ShowDialog(this);
                return;
            }
            WireInterceptor();
        }

        using var picker = new KeyboardPickerForm(interceptor, config.MacroTarget);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result is not { } target)
            return;

        config.MacroKeyboardHardwareId = target.HardwareId;
        config.MacroKeyboardDevice = target.Device;
        config.Save();
        interceptor.Target = target;
        UpdateKeyboardLabel();
        SetStatus(T("Klávesnice nastavena. Teď přidej klávesy přes \"Přidat klávesu...\".", "Keyboard set. Now add keys with \"Add key...\"."));
    }

    void WireInterceptor()
    {
        if (interceptor == null)
            return;
        interceptor.Target = config.MacroTarget;
        interceptor.Enabled = config.MacroEnabled;
        interceptor.MacroKeyDown += OnMacroKey;
        interceptor.DeviceMoved += t => BeginInvoke(() => { config.MacroKeyboardDevice = t.Device; config.Save(); });
    }

    void ForgetKeyboard()
    {
        config.MacroKeyboardHardwareId = null;
        config.MacroKeyboardDevice = 0;
        config.Save();
        if (interceptor != null)
            interceptor.Target = null;
        UpdateKeyboardLabel();
        SetStatus(T("Makro klávesnice zapomenuta.", "Macro keyboard forgotten."));
    }

    void UpdateKeyboardLabel()
    {
        forgetButton.Enabled = config.MacroKeyboardHardwareId != null;
        keyboardLabel.Text = config.MacroKeyboardHardwareId == null
            ? T("Makro klávesnice není nastavená (a nemusí být).", "No macro keyboard set up (and it doesn't have to be).")
            : T("Makro klávesnice: ", "Macro keyboard: ") + KeyboardPickerForm.Describe(config.MacroKeyboardHardwareId);
    }

    // --- Bindings -------------------------------------------------------------------------------

    void ToggleCapture()
    {
        if (capturingKey)
        {
            CancelCapture();
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        if (interceptor == null || config.MacroKeyboardHardwareId == null)
        {
            ShowModal(this, Text, T("Nejdřív nastav makro klávesnici.", "Set up the macro keyboard first."), AntdUI.TType.Info);
            return;
        }
        if (!interceptor.Enabled)
            enabledBox.Checked = true;
        interceptor.CancelLearning();
        capturingKey = true;
        addButton.Text = T("Zrušit", "Cancel");
        SetStatus(T("Stiskni klávesu na makro klávesnici...", "Press a key on the macro keyboard..."));
    }

    void CancelCapture()
    {
        capturingKey = false;
        pendingOpposite = null;
        addButton.Text = T("Přidat klávesu...", "Add key...");
    }

    /// <summary>Runs on the interception thread. Must return immediately.</summary>
    void OnMacroKey(int key)
    {
        if (capturingKey)
        {
            if (pendingOpposite is { } opposite)
            {
                // The rest of the burst from turning the knob the first way: wait for the other way.
                if (key == pendingOppositeFrom)
                    return;
                capturingKey = false;
                BeginInvoke(() => { CancelCapture(); Assign(key, opposite); });
                return;
            }
            capturingKey = false;
            BeginInvoke(() => { CancelCapture(); PickActionFor(key); });
            return;
        }
        if (bindingsByKey.TryGetValue(key, out var binding))
            actionQueue.Writer.TryWrite(binding);
        else
            BeginInvoke(() => SetStatus(T($"Klávesa {KeyDisplay.Describe(key)} nemá přiřazenou akci.", $"Key {KeyDisplay.Describe(key)} has no action assigned.")));
    }

    void PickActionFor(int key)
    {
        RestoreWindow();
        var partNames = currentScore?.Parts.Select(p => p.Name).ToList()
            ?? currentStems?.Tracks.Select(t => t.Name).ToList();
        using var picker = new ActionPickerForm(KeyDisplay.Describe(key), partNames);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result == null)
        {
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        var binding = picker.Result;
        if (binding.Action == MacroAction.PlayNote)
        {
            using var notePicker = new NotePickerForm();
            if (notePicker.ShowDialog(this) != DialogResult.OK || notePicker.Result is not { } pitch)
            {
                SetStatus(T("Zrušeno.", "Cancelled."));
                return;
            }
            binding.Pitch = pitch;
        }
        Assign(key, binding);
        // A knob: the other way of turning it gets the opposite action, without going through the list again.
        if (picker.PairOpposite && MacroActions.Opposite(binding.Action) is { } opposite)
        {
            pendingOppositeFrom = key;
            pendingOpposite = new Binding { Action = opposite, PartIndex = binding.PartIndex, PartName = binding.PartName };
            capturingKey = true;
            addButton.Text = T("Zrušit", "Cancel");
            SetStatus(T($"{KeyDisplay.Describe(key)} -> {binding.Describe()}. Teď otoč kolečkem na druhou stranu: {pendingOpposite.Describe()}",
                $"{KeyDisplay.Describe(key)} -> {binding.Describe()}. Now turn the knob the other way: {pendingOpposite.Describe()}"));
        }
    }

    void Assign(int key, Binding binding)
    {
        binding.Key = key;
        config.Bindings.RemoveAll(b => b.Key == key);
        config.Bindings.Add(binding);
        config.Save();
        RebuildBindings(select: binding);
        SetStatus($"{KeyDisplay.Describe(key)} -> {binding.Describe()}");
    }

    void EditSelected()
    {
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is Binding binding)
            PickActionFor(binding.Key);
    }

    void RemoveSelected()
    {
        if (list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not Binding binding)
            return;
        config.Bindings.Remove(binding);
        config.Save();
        RebuildBindings();
    }

    static string BindingsFileFilter => "JSON (*.json)|*.json|" + T("Všechny soubory", "All files") + " (*.*)|*.*";

    void ExportBindings()
    {
        if (config.Bindings.Count == 0)
        {
            ShowModal(this, Text, T("Nejsou žádné klávesy k exportu.", "There are no keys to export."), AntdUI.TType.Info);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = BindingsFileFilter,
            FileName = "keyboardutils-bindings.json",
            Title = T("Exportovat klávesy", "Export keys"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            config.ExportBindings(dialog.FileName);
            SetStatus(T($"Exportováno {config.Bindings.Count} kláves do {dialog.FileName}.", $"Exported {config.Bindings.Count} keys to {dialog.FileName}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Export se nepovedl: ", "Export failed: ") + ex.Message, AntdUI.TType.Error);
        }
    }

    void ImportBindings()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = BindingsFileFilter,
            Title = T("Importovat klávesy", "Import keys"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        List<Binding> imported;
        try
        {
            imported = AppConfig.ImportBindings(dialog.FileName);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowModal(this, Text, T("Soubor nejde načíst: ", "Could not read the file: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        var replace = DialogResult.Yes;
        if (config.Bindings.Count > 0)
        {
            replace = MessageBox.Show(this,
                T($"Soubor obsahuje {imported.Count} kláves.\n\nAno = nahradit všechny současné klávesy\nNe = přidat ke stávajícím (stejné klávesy se přepíšou)",
                    $"The file contains {imported.Count} keys.\n\nYes = replace all current keys\nNo = merge with current keys (matching keys are overwritten)"),
                T("Importovat klávesy", "Import keys"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (replace == DialogResult.Cancel)
                return;
        }

        if (replace == DialogResult.Yes)
            config.Bindings.Clear();
        var importedKeys = imported.Select(b => b.Key).ToHashSet();
        config.Bindings.RemoveAll(b => importedKeys.Contains(b.Key));
        config.Bindings.AddRange(imported);
        config.Save();
        RebuildBindings();
        SetStatus(T($"Importováno {imported.Count} kláves.", $"Imported {imported.Count} keys."));
    }

    void RebuildBindings(Binding? select = null)
    {
        bindingsByKey = config.Bindings.ToDictionary(b => b.Key);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var b in config.Bindings.OrderBy(b => b.Key))
        {
            var item = new ListViewItem([KeyDisplay.Describe(b.Key), b.Describe()]) { Tag = b };
            list.Items.Add(item);
            if (b == select)
                item.Selected = true;
        }
        list.EndUpdate();
    }

    // --- Action dispatch --------------------------------------------------------------------------

    /// <summary>
    /// Runs the macro keys' actions on the UI thread, one at a time. Presses of one key that pile up meanwhile - a
    /// knob turned fast sends a burst - are done as one action with a count, so the UI keeps up instead of seeking
    /// and redrawing for every single step.
    /// </summary>
    async Task ActionLoop()
    {
        var reader = actionQueue.Reader;
        Binding? carried = null;
        while (true)
        {
            var binding = carried;
            carried = null;
            if (binding == null)
            {
                if (!await reader.WaitToReadAsync())
                    return;
                if (!reader.TryRead(out binding))
                    continue;
            }
            int count = 1;
            while (reader.TryRead(out var next))
            {
                if (next == binding)
                    count++;
                else
                {
                    carried = next;
                    break;
                }
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var action = binding;
            int times = count;
            Post(() =>
            {
                try { Dispatch(action, times); }
                finally { done.TrySetResult(); }
            });
            // An action can open a dialog and only return once it's closed; the keys answering that dialog must
            // still get through - so don't wait for it longer than a moment.
            await Task.WhenAny(done.Task, Task.Delay(100));
        }
    }

    Binding? lastDispatched;
    long lastDispatchedAt;

    /// <param name="count">Presses of the key in a row: how many steps for a step action; others run once.</param>
    void Dispatch(Binding binding, int count = 1)
    {
        bool step = MacroActions.IsStep(binding.Action);
        // An on/off switch hit twice within a moment is a knob or a bouncing key, not a decision to switch back.
        long now = Environment.TickCount64;
        bool repeat = binding == lastDispatched && now - lastDispatchedAt < 150;
        lastDispatched = binding;
        lastDispatchedAt = now;
        if (!step && repeat)
            return;
        RunFromControl(() => DispatchAction(binding, count));
    }

    void DispatchAction(Binding binding, int count)
    {
        if (DispatchMore(binding, count))
            return;
        switch (binding.Action)
        {
            case MacroAction.PlayPause:
                TogglePlayPause();
                break;
            case MacroAction.Stop:
                DoStop();
                break;

            case MacroAction.ToggleMutePart:
                // Same rows as the parts panel: stems when loaded, otherwise parts.
                int? partIndex = currentStems != null ? currentStems.ResolvePartIndex(binding) : currentScore?.ResolvePartIndex(binding);
                if (partIndex is { } index)
                {
                    bool currentlyMuted = currentStems != null ? currentStems.Tracks[index].Muted : currentScore!.Parts[index].Muted;
                    SetPartMuted(index, !currentlyMuted);
                    RebuildPartsPanel();
                    SetStatus(binding.Describe());
                }
                break;
            case MacroAction.PlayNote:
                if (binding.Pitch is { } pitch)
                {
                    playbackEngine.PlayNote(pitch);
                    FlashNote(pitch);
                    SetStatus(binding.Describe());
                }
                break;
            case MacroAction.ToggleLearningMode:
                // From a macro key there's no one at the mouse: start straight away with the last settings.
                ToggleLearningMode(showSetup: false);
                break;
            case MacroAction.ToggleCreateMode:
                ToggleCreateMode();
                break;
            case MacroAction.SpeedReset:
                ResetSpeed();
                break;
            case MacroAction.ToggleLoop:
                LoopToggleAction();
                break;
            case MacroAction.LoopStart:
                LoopStartAction();
                break;
            case MacroAction.LoopEnd:
                LoopEndAction();
                break;
            case MacroAction.ToggleMetronome:
                ToggleMetronome();
                break;
            case MacroAction.ToggleKeyLabels:
                ToggleKeyLabels();
                break;
            case MacroAction.SwitchView:
                CycleSongView();
                break;
            case MacroAction.ToggleListening:
                ToggleListening();
                break;
            case MacroAction.OpenSong:
                RestoreWindow();
                OpenSong();
                break;
            default:
                SetStatus(T("Akce: ", "Action: ") + binding.Describe());
                break;
        }
    }
}
