namespace KeyboardUtils;

/// <summary>Learning mode (play the song yourself, scored) and create mode (what's played is written down and saved).</summary>
sealed partial class MainForm
{
    // --- Learning mode: play the song yourself, notes turn green/red, score at the end ------------

    void ToggleLearningMode(bool showSetup)
    {
        if (learning != null)
            StopLearningMode(showResult: true);
        else
            StartLearningMode(showSetup);
    }

    /// <param name="showSetup">False when started from a macro key or "Try again": reuse the last settings.</param>
    /// <param name="nextPass">Learning a loop: the previous pass just ended, carry on with the next one.</param>
    void StartLearningMode(bool showSetup, bool nextPass = false)
    {
        if (!nextPass)
        {
            loopPass = 1;
            lastPassPercent = null;
        }
        if (currentScore is not { } score)
        {
            ShowModal(this, Text, T("Nejdřív otevři skladbu (MIDI nebo MusicXML).", "Open a song first (MIDI or MusicXML)."), AntdUI.TType.Info);
            return;
        }

        List<int> parts;
        if (showSetup)
        {
            using var setup = new LearningSetupForm(score, config, audioAccompaniment: StemsPlay);
            if (setup.ShowDialog(this) != DialogResult.OK)
                return;
            parts = setup.PracticedParts;
            config.LearningMode = setup.SelectedMode;
            config.LearningSpeedPercent = setup.SpeedPercent;
            config.LearningAccompaniment = setup.Accompaniment;
            config.LearningCheckChords = setup.CheckChords;
            config.LearningPartNames = parts.Select(i => score.Parts[i].Name).ToList();
            config.Save();
        }
        else
            parts = LearningSetupForm.PartsToPractice(score, config.LearningPartNames).Order().ToList();

        if (noteSource == null)
            StartListening();
        if (noteSource is not { } source)
            return; // the error was already shown

        StopLearningMode(showResult: false);
        StopStandalone();
        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        bool tempo = config.LearningMode == LearningMode.InTempo;
        // The stems' audio can't be slowed down (not without changing its pitch), so with them tempo mode is 100 %.
        double speed = tempo && StemsPlay ? 1 : config.LearningSpeedPercent / 100.0;
        if (tempo && StemsPlay)
        {
            // The original recording is the accompaniment: silence the tracks the player plays (notes converted
            // from a track carry its name), or everything if they want no accompaniment.
            var practicedNames = parts.Select(i => score.Parts[i].Name).ToHashSet();
            var tracks = currentStems!.Tracks;
            stemPlaybackEngine.SetSilenced(Enumerable.Range(0, tracks.Count)
                .Where(i => !config.LearningAccompaniment || practicedNames.Contains(tracks[i].Name)));
        }
        else if (tempo)
        {
            // The practiced parts are the player's; the rest plays along only if asked for.
            for (int i = 0; i < score.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, parts.Contains(i) || !config.LearningAccompaniment || score.Parts[i].Muted);
            playbackEngine.Speed = speed;
        }

        // Set before reading source.Polyphonic: with chord checking on, a chord needs all its notes.
        if (source is IExpectedNotesListener listener)
            listener.CheckChords = config.LearningCheckChords;
        var range = loopOn ? LoopRange : null;
        // In tempo, with the metronome on, the lead-in is a bar of count-in clicks, so the player comes in on time.
        bool countInLeadIn = tempo && MetronomeOn;
        double from = range?.Start ?? 0;
        var session = new LearningSession(score, parts, config.LearningMode, speed, source.Polyphonic, source.LatencySeconds,
            tempo ? () => PlaybackSeconds : null,
            tempo ? () => { if (range is { } r) PlaybackSeek(r.Start); PlaybackPlay(); } : null,
            range,
            countInLeadIn ? CountInSeconds(from) : null);
        if (countInLeadIn)
            StartCountIn(from, speed, () => { });
        session.WrongKey += pitch => pianoRoll.FlashKey(pitch, 300, PianoRollControl.WrongColor);
        session.Finished += () => BeginInvoke(() =>
        {
            if (learning != session)
                return;
            // A loop goes round again by itself; its result shows in the status line instead of a window.
            if (session.Range != null && loopOn)
            {
                var pass = session.Stop();
                RecordPractice(session, pass);
                lastPassPercent = pass.Percent;
                loopPass++;
                StartLearningMode(showSetup: false, nextPass: true);
                return;
            }
            StopLearningMode(showResult: true);
        });
        learning = session;
        learningStatus = "";
        learningInfo = (config.LearningMode, (int)Math.Round(speed * 100), parts.Select(i => score.Parts[i].Name).ToArray(), range);
        if (!nextPass)
            SetStatus(T("Režim učení", "Learning mode") + (config.LearningMode == LearningMode.InTempo ? T(" - v tempu", " - in tempo") : ""));
        pianoRoll.Learning = session;
        sheet.Learning = session;
        sheet.NotesChanged();
        partsPanel.Enabled = false;
        UpdateLearningButton();
    }

    void TickLearning()
    {
        var session = learning!;
        session.Tick();
        SetViewTime(session.Position);
        // Tell an audio source which notes to listen for now (chord checking).
        (noteSource as IExpectedNotesListener)?.SetExpectedNotes(session.ListenFor());
        string status = session.InLeadIn
            ? T("Připrav se...", "Get ready...")
            : T($"Učení: {session.DoneNotes}/{session.TotalNotes} not, správně {session.CorrectNotes}, špatné klávesy {session.WrongPresses}",
                $"Learning: {session.DoneNotes}/{session.TotalNotes} notes, {session.CorrectNotes} correct, {session.WrongPresses} wrong keys");
        if (session.Range != null)
            status = T($"Smyčka, kolo {loopPass}", $"Loop, pass {loopPass}")
                + (lastPassPercent is { } last ? T($" (minule {last:0} %)", $" (last {last:0} %)") : "") + " - " + status;
        if (session.Paused)
            status = T("Pozastaveno - ", "Paused - ") + status;
        if (status != learningStatus)
            SetStatus(learningStatus = status);
    }

    void StopLearningMode(bool showResult)
    {
        if (learning is not { } session)
            return;
        CancelCountIn();
        var result = session.Stop();
        learning = null;
        pianoRoll.Learning = null;
        sheet.Learning = null;
        sheet.NotesChanged();
        if (noteSource is IExpectedNotesListener listener)
        {
            listener.SetExpectedNotes([]);
            listener.CheckChords = false;
        }
        partsPanel.Enabled = true;

        playbackEngine.Stop();
        playbackEngine.Speed = playbackSpeed;
        if (currentScore != null)
            for (int i = 0; i < currentScore.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, currentScore.Parts[i].Muted);
        stemPlaybackEngine.Stop();
        stemPlaybackEngine.SetSilenced([]);
        SetViewTime(0);
        UpdateLearningButton();
        UpdateCreateButton();
        SetStatus(T("Režim učení ukončen.", "Learning mode ended."));

        // Nothing played yet: there's nothing to score.
        if (!RecordPractice(session, result) || !showResult)
            return;
        using var dialog = new LearningResultForm(result, currentScore?.Title ?? "", practiceHistory.For(PracticeKey()));
        if (dialog.ShowDialog(this) == DialogResult.Retry)
            StartLearningMode(showSetup: false);
    }

    void UpdateLearningButton()
    {
        UpdateTransport();
        learningButton.Enabled = currentScore != null && recorder == null;
        // Moving the time would break a learning session's scoring.
        pianoRoll.Seekable = learning == null;
        sheet.Seekable = learning == null && recorder == null;
        learningButton.Text = learning != null ? T("Ukončit učení", "End learning") : T("Režim učení...", "Learning mode...");
    }

    // --- Create mode: what's played gets written down, then saved as MIDI / MusicXML --------------

    void ToggleCreateMode()
    {
        if (recorder != null)
            FinishCreateMode();
        else
            StartCreateMode();
    }

    void StartCreateMode()
    {
        StopLearningMode(showResult: false);
        if (noteSource == null)
            StartListening();
        if (noteSource is not { } source)
            return; // the error was already shown

        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        recorder = new NoteRecorder(source.LatencySeconds);
        recordingTitle = T("Nahrávka ", "Recording ") + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
        pianoRoll.RecordingView = true;
        UpdateLiveViews(recorder.Snapshot(recordingTitle), force: true);
        SetViewTime(0);
        UpdateCreateButton();
        UpdateLearningButton();
        SetStatus(T("Nahrávám - hraj. Noty se zapisují od první zahrané.", "Recording - play. Notes are written from the first one you play."));
    }

    void TickCreateMode()
    {
        var active = recorder!;
        UpdateLiveViews(active.Snapshot(recordingTitle), force: false);
        SetViewTime(active.CurrentSeconds);
        if (active.NoteCount > 0)
            SetStatus(T($"Nahrávám: {active.NoteCount} not", $"Recording: {active.NoteCount} notes"));
    }

    /// <summary>Ends recording without asking anything; the recording (if any) becomes the current song.</summary>
    void StopCreateMode()
    {
        if (recorder is not { } active)
            return;
        recorder = null;
        pianoRoll.RecordingView = false;
        var recording = active.Finish(recordingTitle);
        if (recording.Parts[0].Notes.Count > 0)
        {
            // The recording isn't one of the opened files, so previous/next no longer apply.
            playlist = [];
            playlistIndex = -1;
            ShowScore(recording);
            unsavedRecording = recording;
        }
        else
            SetViewScore(currentScore);
        UpdateCreateButton();
        UpdateLearningButton();
    }

    void FinishCreateMode()
    {
        if (recorder == null)
            return;
        StopCreateMode();
        if (currentScore is not { } recording || recording.Title != recordingTitle)
        {
            SetStatus(T("Nic se nezapsalo - nebyla slyšet žádná nota.", "Nothing was recorded - no notes were heard."));
            return;
        }
        // No dialog: listen to it first (Play), then save it with the Save button if it's worth keeping.
        SetStatus(T($"Nahráno: {recording.Parts[0].Notes.Count} not. Poslechni si to (Přehrát) a když se ti to líbí, ulož to tlačítkem Uložit.",
            $"Recorded: {recording.Parts[0].Notes.Count} notes. Listen to it (Play) and if you like it, save it with the Save button."));
    }

    /// <summary>Called on every playback tick, so it can't miss a change of the open song or of its saved state.</summary>
    void UpdateSaveRecordingButton()
    {
        bool show = recorder == null && currentScore != null && currentScore == unsavedRecording;
        if (saveRecordingButton.Visible != show)
            saveRecordingButton.Visible = show;
    }

    void UpdateCreateButton()
    {
        createButton.Text = recorder != null ? T("Ukončit nahrávání", "Stop recording") : T("Zapisovat noty", "Write down notes");
        saveRecordingButton.Text = T("Uložit", "Save");
        removeSongButton.Enabled = recorder == null;
    }

    void SaveCurrentSong()
    {
        if (currentScore == null)
        {
            ShowModal(this, Text, T("Není otevřená žádná skladba (MIDI, MusicXML nebo nahrávka).", "No song is open (MIDI, MusicXML or a recording)."), AntdUI.TType.Info);
            return;
        }
        SaveScore(currentScore);
    }

    /// <summary>
    /// Asks where and saves; true if the file was written. A song without its own tempo (a recording) saved as
    /// MusicXML also asks for the tempo to write it in.
    /// </summary>
    bool SaveScore(Score score)
    {
        string fileName = string.Concat(score.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var dialog = new SaveFileDialog
        {
            Filter = ScoreExporter.FileFilter,
            FileName = fileName,
            Title = T("Uložit skladbu", "Save song"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;
        int bpm = config.CreateTempoBpm;
        bool midi = Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".mid" or ".midi";
        if (!midi && score.Timing == null)
        {
            using var tempo = new SaveTempoForm(bpm);
            if (tempo.ShowDialog(this) != DialogResult.OK)
                return false;
            bpm = tempo.Bpm;
            if (bpm != config.CreateTempoBpm)
            {
                config.CreateTempoBpm = bpm;
                config.Save();
            }
        }
        try
        {
            ScoreExporter.Save(score, dialog.FileName, bpm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Uložení se nepovedlo: ", "Saving failed: ") + ex.Message, AntdUI.TType.Error);
            return false;
        }
        if (score == unsavedRecording)
            unsavedRecording = null;
        SetStatus(T($"Uloženo: {dialog.FileName}", $"Saved: {dialog.FileName}"));
        return true;
    }
}
