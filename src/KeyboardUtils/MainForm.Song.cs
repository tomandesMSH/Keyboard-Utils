namespace KeyboardUtils;

/// <summary>The song: opening, stems, parts and their volume, and its playback.</summary>
sealed partial class MainForm
{
    // --- Song: open, playback, mute parts --------------------------------------------------------

    void OpenSong()
    {
        // Switching songs mid-recording must not silently throw the recording away: finish it (offers saving).
        FinishCreateMode();
        using var dialog = new OpenFileDialog { Filter = ScoreImporter.FileFilter, Title = T("Otevřít skladbu", "Open song"), Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
            return;
        playlist = dialog.FileNames.ToList();
        playlistIndex = 0;
        LoadCurrentSong();
    }

    /// <summary>Searches Mutopia and opens the chosen piece; a piece's movements become the playlist.</summary>
    void FindFreeScores()
    {
        FinishCreateMode();
        using var dialog = new FreeScoresForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Files.Count == 0 || dialog.Piece is not { } piece)
            return;
        playlist = dialog.Files;
        playlistIndex = 0;
        LoadCurrentSong();
        // CC BY needs the author credited: keep the source and license in view.
        SetStatus($"{piece.Title} - {piece.Composer}, {piece.License} (Mutopia Project)"
            + (dialog.Files.Count > 1 ? T($", {dialog.Files.Count} vět", $", {dialog.Files.Count} movements") : ""));
    }

    void LoadCurrentSong()
    {
        if (playlistIndex < 0 || playlistIndex >= playlist.Count)
            return;
        string path = playlist[playlistIndex];
        Score score;
        try
        {
            score = ScoreImporter.Import(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowModal(this, Text, T("Skladbu nejde načíst: ", "Could not load the song: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        ShowScore(score);
        loadedSong = (score, path);
        AddRecentSong(path);
        SetStatus(T($"Načteno: {score.Title}", $"Loaded: {score.Title}"));
    }

    /// <summary>Makes <paramref name="score"/> the current song: playback, piano roll, parts panel, buttons.</summary>
    void ShowScore(Score score)
    {
        StopLearningMode(showResult: false);
        stemPlaybackEngine.Stop();
        currentStems = null;
        currentScore = score;
        ClearLoop();

        playbackEngine.Load(currentScore, ClickTiming);
        SetViewScore(currentScore);
        RebuildPartsPanel();
        playbackEngine.Speed = playbackSpeed;
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
    }

    /// <summary>
    /// Asks how to use the audio: a folder of tracks as accompaniment (<see cref="LoadStems"/>), or notes
    /// detected from an audio file (<see cref="ShowConvertedNotes"/>).
    /// </summary>
    void ImportStems()
    {
        FinishCreateMode();
        // Start the pickers in the loaded stems' folder: converting one of them is a likely next step.
        string? stemsFolder = currentStems?.Tracks.FirstOrDefault() is { } track ? Path.GetDirectoryName(track.FilePath) : null;
        using var dialog = new ImportAudioForm(stemsFolder, config);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (dialog.StemsFolder is { } folder)
            LoadStems(folder);
        else if (dialog.Result is { } score)
            ShowConvertedNotes(score, dialog.FilePath);
    }

    void LoadStems(string folder)
    {
        StemSet stems;
        try
        {
            stems = StemSetImporter.ImportFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Stopy nejde načíst: ", "Could not load the stems: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        try
        {
            stemPlaybackEngine.Load(stems);
        }
        catch (InvalidDataException ex)
        {
            ShowModal(this, Text, ex.Message, AntdUI.TType.Error);
            return;
        }

        StopLearningMode(showResult: false);
        playbackEngine.Stop();
        playlist = [];
        playlistIndex = -1;
        currentScore = null;
        currentStems = stems;
        ClearLoop();
        SetViewScore(null);

        RebuildPartsPanel();
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
        SetStatus(T($"Načteny stopy: {stems.Title} ({stems.Tracks.Count})", $"Loaded stems: {stems.Title} ({stems.Tracks.Count})"));
    }

    void UpdateSongLabel()
    {
        int stemNoteTracks = currentStems != null ? currentScore?.Parts.Count ?? 0 : 0;
        songLabel.Text = currentStems != null
            ? $"{currentStems.Title}  ({TimeSpan.FromSeconds(stemPlaybackEngine.DurationSeconds):m\\:ss})  [{T("stopy", "stems")}"
              + (stemNoteTracks > 0 ? T($", noty z {stemNoteTracks}", $", notes from {stemNoteTracks}") : "") + "]"
            : currentScore != null
                ? $"{currentScore.Title}  ({TimeSpan.FromSeconds(currentScore.DurationSeconds):m\\:ss})" +
                  (playlist.Count > 1 ? $"   [{playlistIndex + 1}/{playlist.Count}]" : "")
                : T("Žádná skladba nenačtena.", "No song loaded.");
        // Called on every song change, so it's also where "Remove song" appears/disappears.
        removeSongButton.Visible = currentScore != null || currentStems != null;
    }

    /// <summary>Notes detected from an audio file open as a new, not yet saved song.</summary>
    void ShowConvertedNotes(Score score, string? filePath)
    {
        // One of the loaded stems: add its notes to them instead of replacing the stems with a new song.
        if (currentStems?.Tracks.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.FilePath), Path.GetFullPath(filePath ?? ""), StringComparison.OrdinalIgnoreCase)) is { } stem)
        {
            AttachStemNotes(stem.Name, score.Parts[0]);
            return;
        }
        playlist = [];
        playlistIndex = -1;
        ShowScore(score);
        unsavedRecording = score;
        SetStatus(T($"Převedeno: {score.Parts.Sum(p => p.Notes.Count)} not. Ulož přes Soubor > Uložit skladbu jako...",
            $"Converted: {score.Parts.Sum(p => p.Notes.Count)} notes. Save via File > Save song as..."));
    }

    /// <summary>Takes the song out of the player (the file on disk stays). Asks first if it's an unsaved recording.</summary>
    void RemoveSong()
    {
        if (recorder != null || (currentScore == null && currentStems == null))
            return;
        if (currentScore != null && currentScore == unsavedRecording)
        {
            var confirm = MessageBox.Show(this,
                T("Noty nejsou uložené - odebráním se ztratí. Opravdu odebrat?", "The notes aren't saved - removing loses them. Remove anyway?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;
        }

        StopLearningMode(showResult: false);
        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        string title = currentScore?.Title ?? currentStems?.Title ?? "";
        currentScore = null;
        currentStems = null;
        unsavedRecording = null;
        playlist = [];
        playlistIndex = -1;
        ClearLoop();

        SetViewScore(null);
        RebuildPartsPanel();
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
        UpdatePlayPauseButton();
        SetStatus(T($"Odebráno: {title}", $"Removed: {title}"));
    }

    /// <summary>
    /// One row per part - or per stem when stems are loaded (notes converted from a stem don't get rows of their
    /// own; the stem's row shows whether it has them). Each row: on/off, volume, and for stems a "Notes" button.
    /// </summary>
    void RebuildPartsPanel()
    {
        partsPanel.Controls.Clear();
        var parts = currentStems?.Tracks.Select(t => (t.Name, t.Muted, t.Volume)).ToList()
            ?? currentScore?.Parts.Select(p => (p.Name, p.Muted, p.Volume)).ToList()
            ?? [];
        // No song: give the piano roll the full width instead of leaving an empty strip.
        partsPanel.Visible = parts.Count > 0;
        if (parts.Count == 0)
            return;

        var header = new AntdUI.Label { Text = T("Části / nástroje:", "Parts / instruments:"), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        partsPanel.Controls.Add(header);
        for (int i = 0; i < parts.Count; i++)
        {
            int index = i;
            var box = new AntdUI.Checkbox { Text = parts[i].Name, Checked = !parts[i].Muted, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            // Volume, 0-100 %; the value shows while dragging. Hidden while the part is switched off.
            var volume = new AntdUI.Slider
            {
                MinValue = 0,
                MaxValue = 100,
                Value = (int)Math.Round(parts[i].Volume * 100),
                ShowValue = true,
                Size = new Size(partsPanel.Width - 30, 24),
                Margin = new Padding(18, 0, 0, 2),
                Visible = !parts[i].Muted,
            };
            box.CheckedChanged += (_, _) =>
            {
                SetPartMuted(index, !box.Checked);
                volume.Visible = box.Checked;
            };
            volume.ValueChanged += (_, _) => SetPartVolume(index, volume.Value / 100.0);
            partsPanel.Controls.Add(box);
            partsPanel.Controls.Add(volume);

            if (currentStems != null)
            {
                bool hasNotes = currentScore?.Parts.Any(p => p.Name == parts[i].Name) ?? false;
                bool converting = convertingTracks.Contains(parts[i].Name);
                var notesButton = new AntdUI.Button
                {
                    Text = converting ? "..." : hasNotes ? T("✓ Noty", "✓ Notes") : T("Noty", "Notes"),
                    Size = new Size(76, 26),
                    Margin = new Padding(18, 0, 0, 4),
                    Radius = 6,
                    Enabled = !hasNotes && !converting,
                };
                notesButton.Click += (_, _) => ConvertStemToNotes(index, notesButton);
                partsPanel.Controls.Add(notesButton);
            }
        }
    }

    /// <summary>
    /// Converts one loaded stem to notes in the background and adds them to the song under the stem's name, shown
    /// in the piano roll in sync with the stems' playback.
    /// </summary>
    async void ConvertStemToNotes(int trackIndex, AntdUI.Button button)
    {
        if (currentStems is not { } stems || trackIndex >= stems.Tracks.Count)
            return;
        var track = stems.Tracks[trackIndex];
        convertingTracks.Add(track.Name);
        button.Enabled = false;
        button.Text = "...";
        SetStatus(T($"Převádím stopu {track.Name} na noty...", $"Converting track {track.Name} to notes..."));
        Score converted;
        try
        {
            await ChordAddonPrompt.OfferAsync(this, config, SetStatus);
            SetStatus(T($"Převádím stopu {track.Name} na noty...", $"Converting track {track.Name} to notes..."));
            converted = await Task.Run(() => AudioTranscriber.TranscribeBest(track.FilePath, null, CancellationToken.None));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            convertingTracks.Remove(track.Name);
            ShowModal(this, Text, T("Stopu nejde převést: ", "Could not convert the track: ") + ex.Message, AntdUI.TType.Error);
            RebuildPartsPanel();
            return;
        }
        convertingTracks.Remove(track.Name);
        // Another song may have been opened meanwhile.
        if (currentStems == stems)
            AttachStemNotes(track.Name, converted.Parts[0]);
    }

    void AttachStemNotes(string trackName, ScorePart notes)
    {
        if (notes.Notes.Count == 0)
        {
            SetStatus(T($"Ve stopě {trackName} jsem nenašel žádné noty.", $"No notes found in track {trackName}."));
            RebuildPartsPanel();
            return;
        }
        // Named after the stem: that's how learning mode knows which track to silence for the player.
        notes.Name = trackName;
        currentScore ??= new Score { Title = currentStems!.Title };
        currentScore.Parts.RemoveAll(p => p.Name == trackName);
        currentScore.Parts.Add(notes);
        unsavedRecording = currentScore;

        SetViewScore(currentScore);
        SetViewTime(PlaybackSeconds);
        RebuildPartsPanel();
        UpdateSongLabel();
        UpdateLearningButton();
        SetStatus(T($"Noty ze stopy {trackName}: {notes.Notes.Count}. Uložit je jde přes Soubor > Uložit skladbu jako...",
            $"Notes from track {trackName}: {notes.Notes.Count}. Save them via File > Save song as..."));
    }

    /// <summary>Index = row in the parts panel: a stem when stems are loaded, otherwise a part.</summary>
    void SetPartVolume(int partIndex, double volume)
    {
        if (currentStems != null)
        {
            if (partIndex < 0 || partIndex >= currentStems.Tracks.Count)
                return;
            currentStems.Tracks[partIndex].Volume = volume;
            stemPlaybackEngine.ApplyVolume(partIndex);
        }
        else if (currentScore != null && partIndex >= 0 && partIndex < currentScore.Parts.Count)
        {
            currentScore.Parts[partIndex].Volume = volume;
            playbackEngine.SetPartVolume(partIndex, volume);
        }
    }

    /// <summary>
    /// Index as in <see cref="SetPartVolume"/>. Muting a stem only silences its audio - notes converted from it
    /// stay visible, since muting your instrument to play it yourself is the point.
    /// </summary>
    void SetPartMuted(int partIndex, bool muted)
    {
        if (currentStems != null)
        {
            if (partIndex < 0 || partIndex >= currentStems.Tracks.Count)
                return;
            currentStems.Tracks[partIndex].Muted = muted;
            stemPlaybackEngine.ApplyVolume(partIndex);
        }
        else if (currentScore != null)
        {
            if (partIndex < 0 || partIndex >= currentScore.Parts.Count)
                return;
            currentScore.Parts[partIndex].Muted = muted;
            playbackEngine.SetPartMuted(partIndex, muted);
        }
        pianoRoll.Invalidate();
        sheet.NotesChanged();
    }

    // --- Playback: the stems' audio when stems are loaded (even with notes converted from them - those are
    // only shown, the original recording is the sound and the clock), otherwise the notes through MIDI. ----------

    bool StemsPlay => currentStems != null;
    bool PlaybackRunning => StemsPlay ? stemPlaybackEngine.IsRunning : playbackEngine.IsRunning;
    double PlaybackSeconds => StemsPlay ? stemPlaybackEngine.CurrentSeconds : playbackEngine.CurrentSeconds;
    void PlaybackPlay() { if (StemsPlay) stemPlaybackEngine.Play(); else playbackEngine.Play(); }
    void PlaybackPause() { if (StemsPlay) stemPlaybackEngine.Pause(); else playbackEngine.Pause(); }
    void PlaybackStop() { if (StemsPlay) stemPlaybackEngine.Stop(); else playbackEngine.Stop(); }
    void PlaybackSeek(double seconds) { if (StemsPlay) stemPlaybackEngine.SeekTo(seconds); else playbackEngine.SeekTo(seconds); }

    void TogglePlayPause()
    {
        // Playing the song over a recording would be heard (by the mic) and written down.
        if (recorder != null)
            return;
        if (learning == null && StandaloneOn)
        {
            ToggleStandalone();
            return;
        }
        if (learning != null)
        {
            bool pause = !learning.Paused;
            learning.SetPaused(pause);
            if (pause)
                CancelCountIn();
            // In tempo mode the accompaniment is the clock - but only once the lead-in has started it.
            if (learning.Mode == LearningMode.InTempo && !learning.InLeadIn)
            {
                if (pause) PlaybackPause(); else PlaybackPlay();
            }
        }
        else if (currentScore != null || currentStems != null)
        {
            if (countIn != null)
                CancelCountIn(); // pressed again during the count-in: don't start after all
            else if (PlaybackRunning)
                PlaybackPause();
            else
            {
                // With the loop on, playing starts inside it.
                if (loopOn && LoopRange is { } range && (PlaybackSeconds < range.Start || PlaybackSeconds >= range.End))
                    PlaybackSeek(range.Start);
                if (MetronomeOn)
                    StartCountIn(PlaybackSeconds, playbackSpeed, PlaybackPlay);
                else
                    PlaybackPlay();
            }
        }
        else
        {
            return;
        }
        UpdatePlayPauseButton();
        SetStatus(learning != null ? (learning.Paused ? T("Pauza", "Paused") : T("Pokračuje se", "Going on"))
            : countIn != null ? T("Odpočítávání...", "Count-in...")
            : PlaybackRunning ? T("Přehrávání", "Playing") : T("Pauza", "Paused"));
    }

    void UpdatePlayPauseButton()
    {
        bool running = learning != null ? !learning.Paused
            : StandaloneRunning || countIn != null || ((currentScore != null || currentStems != null) && PlaybackRunning);
        // Called every playback tick: only touch the icon when it changes (setting it re-renders the SVG).
        string icon = running ? Icons.Pause : Icons.Play;
        if (playPauseButton.IconSvg != icon)
            playPauseButton.IconSvg = icon;
    }

    void DoStop()
    {
        if (recorder != null)
        {
            FinishCreateMode();
            return;
        }
        if (learning != null)
        {
            StopLearningMode(showResult: true);
            return;
        }
        CancelCountIn();
        StopStandalone();
        PlaybackStop();
        SetViewTime(0);
        UpdatePlayPauseButton();
        SetStatus(T("Zastaveno.", "Stopped."));
    }

    void DoNextTrack()
    {
        FinishCreateMode();
        if (playlist.Count == 0)
            return;
        playlistIndex = (playlistIndex + 1) % playlist.Count;
        LoadCurrentSong();
        playbackEngine.Play();
        UpdatePlayPauseButton();
    }

    void DoPreviousTrack()
    {
        FinishCreateMode();
        if (playlist.Count == 0)
            return;
        playlistIndex = (playlistIndex - 1 + playlist.Count) % playlist.Count;
        LoadCurrentSong();
        playbackEngine.Play();
        UpdatePlayPauseButton();
    }
}
