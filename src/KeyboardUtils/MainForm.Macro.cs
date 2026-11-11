namespace KeyboardUtils;

/// <summary>
/// What the macro keyboard needs so the whole app works without the computer keyboard and the mouse: songs without
/// the file dialog (a songs folder, recent songs), learning settings without their dialog, saving a recording without
/// the save dialog, keys for answering dialogs, and step actions made for knobs.
/// </summary>
sealed partial class MainForm
{
    /// <summary>The song as loaded from a file - to tell whether the open song is that file.</summary>
    (Score Score, string Path)? loadedSong;

    /// <summary>Waits until a knob stops on a song before loading it, instead of loading every song on the way.</summary>
    System.Windows.Forms.Timer? trackLoadTimer;
    bool playAfterTrackLoad;

    static readonly string[] SongExtensions = [".mid", ".midi", ".musicxml", ".mxl", ".xml"];

    /// <summary>The actions added for the macro keyboard; false = not one of them (handled by <see cref="Dispatch"/>).</summary>
    bool DispatchMore(Binding binding, int count)
    {
        int steps = MacroActions.IsStep(binding.Action) ? count : 1;
        switch (binding.Action)
        {
            case MacroAction.SeekBack: SeekStep(-5 * steps); break;
            case MacroAction.SeekForward: SeekStep(5 * steps); break;
            case MacroAction.SeekBackFine: SeekStep(-steps); break;
            case MacroAction.SeekForwardFine: SeekStep(steps); break;
            case MacroAction.SpeedDown: StepSpeed(-steps); break;
            case MacroAction.SpeedUp: StepSpeed(steps); break;
            case MacroAction.ZoomIn: ZoomSheet(steps); break;
            case MacroAction.ZoomOut: ZoomSheet(-steps); break;
            case MacroAction.KeyboardLarger: StepKeyboardSize(steps); break;
            case MacroAction.KeyboardSmaller: StepKeyboardSize(-steps); break;
            case MacroAction.PartVolumeUp: StepPartVolume(binding, steps); break;
            case MacroAction.PartVolumeDown: StepPartVolume(binding, -steps); break;
            case MacroAction.VolumeUp: StepAllVolumes(steps); break;
            case MacroAction.VolumeDown: StepAllVolumes(-steps); break;
            case MacroAction.NextTrack: StepTrack(steps); break;
            case MacroAction.PreviousTrack: StepTrack(-steps); break;
            case MacroAction.SongsFromFolder: LoadSongsFolder(); break;
            case MacroAction.RecentSongs: OpenRecentSongs(0); break;
            case MacroAction.SaveRecording: SaveRecordingQuick(); break;
            case MacroAction.ShowPracticeHistory: ShowPracticeHistory(); break;
            case MacroAction.PracticeHardSpot: PracticeHardSpot(); break;
            case MacroAction.NoteTrainer: OpenNoteTrainer(); break;
            case MacroAction.Exercise: OpenExercise(); break;
            case MacroAction.Tuner: OpenTuner(); break;
            case MacroAction.MetronomeTap: TapMetronome(); break;
            case MacroAction.MetronomeMeterNext: CycleMetronomeMeter(); break;
            case MacroAction.ToggleStandaloneMetronome: ToggleStandaloneOption(); break;
            case MacroAction.LearningModeNext: CycleLearningMode(); break;
            case MacroAction.LearningPartsNext: CycleLearningParts(); break;
            case MacroAction.LearningSpeedNext: CycleLearningSpeed(); break;
            case MacroAction.NoteInputNext: CycleNoteInput(); break;
            case MacroAction.ToggleDarkMode: ToggleDarkMode(); break;
            case MacroAction.ShowWindow:
                RestoreWindow();
                KeySender.TopWindow(this).Activate();
                break;
            case MacroAction.MinimizeWindow: WindowState = FormWindowState.Minimized; break;
            case MacroAction.KeyEnter: SendKey(Keys.Enter, 1); break;
            case MacroAction.KeyEscape: SendKey(Keys.Escape, 1); break;
            case MacroAction.KeySpace: SendKey(Keys.Space, 1); break;
            case MacroAction.KeyTab: SendKey(Keys.Tab, steps); break;
            case MacroAction.KeyShiftTab: SendKey(Keys.Tab | Keys.Shift, steps); break;
            case MacroAction.KeyUp: SendKey(Keys.Up, steps); break;
            case MacroAction.KeyDown: SendKey(Keys.Down, steps); break;
            case MacroAction.KeyLeft: SendKey(Keys.Left, steps); break;
            case MacroAction.KeyRight: SendKey(Keys.Right, steps); break;
            default: return false;
        }
        return true;
    }

    void SendKey(Keys key, int times)
    {
        for (int i = 0; i < times; i++)
            KeySender.Send(this, key);
    }

    // --- Songs without the file dialog ---------------------------------------------------------

    /// <summary>
    /// Moves through the playlist by <paramref name="steps"/>. The song is loaded once the steps stop for a moment,
    /// so turning a knob across ten songs loads one. With no playlist, the songs folder becomes one.
    /// </summary>
    void StepTrack(int steps)
    {
        if (recorder != null)
            FinishCreateMode();
        if (playlist.Count == 0)
        {
            if (!SetFolderPlaylist())
                return;
            // Forward starts at the first song, back at the last.
            playlistIndex = steps > 0 ? -1 : 0;
        }
        if (playlist.Count == 0)
            return;
        playAfterTrackLoad |= PlaybackRunning;
        playlistIndex = ((playlistIndex + steps) % playlist.Count + playlist.Count) % playlist.Count;
        SetStatus($"[{playlistIndex + 1}/{playlist.Count}]  {Path.GetFileNameWithoutExtension(playlist[playlistIndex])}");
        if (trackLoadTimer == null)
        {
            trackLoadTimer = new System.Windows.Forms.Timer { Interval = 400 };
            trackLoadTimer.Tick += (_, _) =>
            {
                trackLoadTimer.Stop();
                bool play = playAfterTrackLoad;
                playAfterTrackLoad = false;
                LoadCurrentSong();
                // Playing before: play the new one too (like next / previous always did).
                if (play && currentScore != null)
                {
                    PlaybackPlay();
                    UpdatePlayPauseButton();
                }
            };
        }
        trackLoadTimer.Stop();
        trackLoadTimer.Start();
    }

    void PickSongsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T("Složka se skladbami (MIDI, MusicXML) - další / předchozí skladba jimi pak listuje",
                "Folder with songs (MIDI, MusicXML) - next / previous song then go through them"),
            UseDescriptionForTitle = true,
            InitialDirectory = config.SongsFolder ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        config.SongsFolder = dialog.SelectedPath;
        config.Save();
        LoadSongsFolder();
    }

    /// <summary>The songs folder (with its subfolders) as the playlist, the first song loaded.</summary>
    void LoadSongsFolder()
    {
        if (!SetFolderPlaylist())
            return;
        playlistIndex = 0;
        LoadCurrentSong();
        SetStatus(T($"Skladby ze složky: {playlist.Count}. Další / předchozí skladba jimi listuje.",
            $"Songs from the folder: {playlist.Count}. Next / previous song go through them."));
    }

    bool SetFolderPlaylist()
    {
        if (config.SongsFolder is not { } folder || !Directory.Exists(folder))
        {
            SetStatus(T("Nejdřív vyber složku se skladbami: Nastavení > Složka se skladbami...",
                "Pick a songs folder first: Settings > Songs folder..."));
            return false;
        }
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => SongExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetRelativePath(folder, f), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(T("Složku nejde přečíst: ", "Could not read the folder: ") + ex.Message);
            return false;
        }
        if (files.Count == 0)
        {
            SetStatus(T($"Ve složce {folder} nejsou žádné skladby (MIDI, MusicXML).", $"There are no songs (MIDI, MusicXML) in {folder}."));
            return false;
        }
        FinishCreateMode();
        playlist = files;
        playlistIndex = 0;
        return true;
    }

    void AddRecentSong(string path)
    {
        string full = Path.GetFullPath(path);
        config.RecentSongs.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        config.RecentSongs.Insert(0, full);
        if (config.RecentSongs.Count > AppConfig.MaxRecentSongs)
            config.RecentSongs.RemoveRange(AppConfig.MaxRecentSongs, config.RecentSongs.Count - AppConfig.MaxRecentSongs);
        config.Save();
    }

    AntdUI.IContextMenuStripItem[] BuildRecentSongItems() =>
        config.RecentSongs.Select((path, i) => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(Path.GetFileNameWithoutExtension(path))
        {
            Tag = (Action)(() => OpenRecentSongs(i)),
        }).ToArray();

    /// <summary>The recent songs as the playlist (newest first), starting at <paramref name="index"/>.</summary>
    void OpenRecentSongs(int index)
    {
        var target = index < config.RecentSongs.Count ? config.RecentSongs[index] : null;
        var files = config.RecentSongs.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            SetStatus(T("Zatím žádné nedávné skladby.", "No recent songs yet."));
            return;
        }
        FinishCreateMode();
        playlist = files;
        playlistIndex = Math.Max(0, target != null ? files.IndexOf(target) : 0);
        // Already open (the newest one usually is): just make the recent songs the playlist.
        if (loadedSong is { } loaded && loaded.Score == currentScore
            && string.Equals(Path.GetFullPath(loaded.Path), playlist[playlistIndex], StringComparison.OrdinalIgnoreCase))
            UpdateSongLabel();
        else
            LoadCurrentSong();
        SetStatus(T($"Nedávné skladby: {files.Count}. Další / předchozí skladba mezi nimi listuje.",
            $"Recent songs: {files.Count}. Next / previous song go through them."));
    }

    // --- Practice history ---------------------------------------------------------------------------

    /// <summary>How the open song is known in the history: its file, or its title when it has none.</summary>
    string PracticeKey() =>
        loadedSong is { } loaded && loaded.Score == currentScore
            ? PracticeHistory.KeyFor(loaded.Path, currentScore!.Title)
            : PracticeHistory.KeyFor(null, currentScore?.Title ?? "");

    /// <summary>Writes the session to the history once; false when nothing was played (nothing to score).</summary>
    bool RecordPractice(LearningSession session, LearningResult result)
    {
        if (session.DoneNotes == 0 && !result.Completed)
            return false;
        if (recordedSession == session)
            return true;
        recordedSession = session;
        var info = learningInfo;
        practiceHistory.Add(PracticeKey(), new PracticeEntry(DateTime.Now, Math.Round(result.Percent, 1), result.CorrectNotes, result.TotalNotes,
            result.WrongPresses, result.MissedNotes, Math.Round(result.Duration.TotalSeconds, 1), result.Completed, info.Mode, info.SpeedPercent,
            info.Parts, info.Range?.Start, info.Range?.End,
            // Empty when all went right: a clean attempt counts too, it makes old mistakes weigh less.
            HardSpots.CountMistakes(session.WrongNotes.Select(n => n.StartSeconds), ClickTiming)));
        practiceHistory.Save();
        UpdateHardSpots();
        UpdateGoal();
        return true;
    }

    /// <summary>The open song's hard spots (worst first), from its practice history.</summary>
    IReadOnlyList<HardSpot> CurrentHardSpots() =>
        currentScore == null ? [] : HardSpots.For(practiceHistory.For(PracticeKey()));

    /// <summary>Marks the hard spots' bars in the sheet music.</summary>
    void UpdateHardSpots()
    {
        var spots = CurrentHardSpots();
        var weighted = spots.Count == 0 ? [] : HardSpots.Weighted(practiceHistory.For(PracticeKey()));
        sheet.HardBars = spots
            .SelectMany(s => Enumerable.Range(s.FirstBar, s.LastBar - s.FirstBar + 1))
            .Where(bar => weighted.GetValueOrDefault(bar) > 0)
            .ToDictionary(bar => bar, bar => weighted[bar]);
    }

    /// <summary>A loop over <paramref name="spot"/> (or the worst hard spot) and learning mode on it.</summary>
    void PracticeHardSpot(HardSpot? spot = null)
    {
        if (currentScore == null)
        {
            SetStatus(T("Nejdřív otevři skladbu.", "Open a song first."));
            return;
        }
        spot ??= CurrentHardSpots().FirstOrDefault();
        if (spot == null)
        {
            SetStatus(T("Zatím žádná těžká místa - nejdřív si skladbu zkus v režimu učení.",
                "No hard spots yet - try the song in learning mode first."));
            return;
        }
        StopLearningMode(showResult: false);
        var (start, end) = HardSpots.Range(spot.FirstBar, spot.LastBar, ClickTiming);
        SetLoop(start, end);
        StartLearningMode(showSetup: false);
        SetStatus(T($"Procvičuješ takty {spot.Bars} ve smyčce.", $"Practising bars {spot.Bars} in a loop."));
    }

    void ShowPracticeHistory()
    {
        if (currentScore == null)
        {
            SetStatus(T("Nejdřív otevři skladbu.", "Open a song first."));
            return;
        }
        RestoreWindow();
        using var form = new PracticeHistoryForm(currentScore.Title, practiceHistory.For(PracticeKey()), CurrentHardSpots());
        form.ShowDialog(this);
    }

    // --- Recording ------------------------------------------------------------------------------

    /// <summary>The recording as MIDI into Documents\Keyboard Utils\Recordings, named by the time - no dialog.</summary>
    void SaveRecordingQuick()
    {
        if (recorder != null)
            FinishCreateMode();
        if (unsavedRecording is not { } recording || currentScore != recording)
        {
            SetStatus(T("Není co uložit - nejdřív něco nahraj (Zapisovat noty).", "Nothing to save - record something first (Write notes)."));
            return;
        }
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Keyboard Utils", T("Nahrávky", "Recordings"));
        string path = Path.Combine(folder, $"{T("Nahrávka", "Recording")} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mid");
        try
        {
            Directory.CreateDirectory(folder);
            ScoreExporter.Save(recording, path, config.CreateTempoBpm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(T("Uložení se nepovedlo: ", "Saving failed: ") + ex.Message);
            return;
        }
        unsavedRecording = null;
        noteEditor = null;
        pianoRoll.Editable = false;
        loadedSong = (recording, path);
        AddRecentSong(path);
        UpdateSaveRecordingButton();
        SetStatus(T($"Uloženo: {path}", $"Saved: {path}"));
    }

    // --- Learning settings without the dialog ----------------------------------------------------

    string LearningSettingNote => learning != null ? T(" (platí od dalšího spuštění)", " (applies from the next start)") : "";

    void CycleLearningMode()
    {
        config.LearningMode = config.LearningMode == LearningMode.WaitForNote ? LearningMode.InTempo : LearningMode.WaitForNote;
        config.Save();
        SetStatus(T("Učení: ", "Learning: ")
            + (config.LearningMode == LearningMode.InTempo ? T("v tempu", "in tempo") : T("čekat na notu", "wait for note"))
            + LearningSettingNote);
    }

    /// <summary>All parts → the first part alone → the second alone → ... → all parts.</summary>
    void CycleLearningParts()
    {
        if (currentScore is not { } score)
        {
            SetStatus(T("Nejdřív otevři skladbu.", "Open a song first."));
            return;
        }
        var names = score.Parts.Select(p => p.Name).ToList();
        if (names.Count < 2)
        {
            SetStatus(T("Skladba má jen jednu část.", "The song has only one part."));
            return;
        }
        var current = LearningSetupForm.PartsToPractice(score, config.LearningPartNames);
        // 0 = all, i + 1 = part i alone
        int option = current.Count == names.Count ? 0 : current.Count == 1 ? current.First() + 1 : -1;
        int next = (option + 1) % (names.Count + 1);
        config.LearningPartNames = next == 0 ? names : [names[next - 1]];
        config.Save();
        SetStatus(T("Učení - hraješ: ", "Learning - you play: ")
            + (next == 0 ? T("všechny části", "all parts") : names[next - 1]) + LearningSettingNote);
    }

    void CycleLearningSpeed()
    {
        var speeds = LearningSetupForm.Speeds;
        // From a speed a loop was sped up to (60 %...): the next one of the usual ones above it.
        int next = Array.FindIndex(speeds, s => s > config.LearningSpeedPercent);
        config.LearningSpeedPercent = speeds[next < 0 ? 0 : next];
        config.Save();
        SetStatus(T($"Učení - rychlost: {config.LearningSpeedPercent} %", $"Learning - speed: {config.LearningSpeedPercent} %") + LearningSettingNote);
    }

    /// <summary>Microphone → computer sound → on-screen keyboard → each MIDI input → microphone...</summary>
    void CycleNoteInput()
    {
        var inputs = new List<(NoteInputKind Kind, string? Device, string Name)>
        {
            (NoteInputKind.Microphone, config.MidiInputDevice, T("mikrofon", "microphone")),
            (NoteInputKind.SystemAudio, config.MidiInputDevice, T("zvuk z počítače", "computer sound")),
            (NoteInputKind.ScreenKeyboard, config.MidiInputDevice, T("klikání na klaviaturu", "clicking the on-screen keyboard")),
        };
        foreach (var name in MidiNoteSource.GetDeviceNames())
            inputs.Add((NoteInputKind.Midi, name, "MIDI: " + name));
        int current = inputs.FindIndex(i => i.Kind == config.NoteInput && (i.Kind != NoteInputKind.Midi || i.Device == config.MidiInputDevice));
        var next = inputs[(current + 1) % inputs.Count];
        SetNoteInput(next.Kind, next.Device);
        SetStatus(T("Vstup not: ", "Note input: ") + next.Name);
    }

    // --- Knob steps --------------------------------------------------------------------------------

    void StepKeyboardSize(int steps)
    {
        keyboardSizeSlider.Value = Math.Clamp(keyboardSizeSlider.Value + 2 * steps, PianoRollControl.MinKeyWidth, PianoRollControl.MaxKeyWidth);
        SetStatus(T($"Velikost klávesnice: {keyboardSizeSlider.Value}", $"Keyboard size: {keyboardSizeSlider.Value}"));
    }

    /// <summary>Volumes of the parts (or stems) as shown in the parts panel.</summary>
    List<(string Name, double Volume)> PartVolumes() =>
        currentStems?.Tracks.Select(t => (t.Name, t.Volume)).ToList()
        ?? currentScore?.Parts.Select(p => (p.Name, p.Volume)).ToList()
        ?? [];

    void StepPartVolume(Binding binding, int steps)
    {
        int? index = currentStems != null ? currentStems.ResolvePartIndex(binding) : currentScore?.ResolvePartIndex(binding);
        var parts = PartVolumes();
        if (index is not { } i || i >= parts.Count)
            return;
        double volume = Math.Clamp(Math.Round(parts[i].Volume + 0.1 * steps, 2), 0, 1);
        SetPartVolume(i, volume);
        RebuildPartsPanel();
        SetStatus(T($"Hlasitost {parts[i].Name}: {volume * 100:0} %", $"Volume of {parts[i].Name}: {volume * 100:0} %"));
    }

    void StepAllVolumes(int steps)
    {
        var parts = PartVolumes();
        if (parts.Count == 0)
            return;
        for (int i = 0; i < parts.Count; i++)
            SetPartVolume(i, Math.Clamp(Math.Round(parts[i].Volume + 0.1 * steps, 2), 0, 1));
        RebuildPartsPanel();
        SetStatus(T("Hlasitost: ", "Volume: ") + string.Join(", ", PartVolumes().Select(p => $"{p.Name} {p.Volume * 100:0} %")));
    }
}
