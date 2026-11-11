namespace KeyboardUtils;

/// <summary>Practice tools besides songs: scales and warm-ups, the note-reading trainer.</summary>
sealed partial class MainForm
{
    /// <summary>Open while the note-reading trainer runs: played notes go to it.</summary>
    NoteTrainerForm? noteTrainer;

    /// <summary>Scales and warm-ups: generated as a song, unsaved (saved like a recording, with "Save song as").</summary>
    void OpenExercise()
    {
        FinishCreateMode();
        using var dialog = new ExerciseForm(config.Exercise);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        config.Exercise = dialog.Spec;
        config.Save();
        var score = ExerciseGenerator.Generate(dialog.Spec);
        playlist = [];
        playlistIndex = -1;
        ShowScore(score);
        unsavedRecording = score;
        UpdateSaveRecordingButton();
        SetStatus(T($"{score.Title} - zahraj si ji v Režimu učení (F5). Uložit jde přes Soubor > Uložit skladbu jako.",
            $"{score.Title} - play it in Learning mode (F5). Save it with File > Save song as."));
    }

    // --- The player's take: learning mode records how they played, to listen back to in the results ---------

    /// <summary>Notes from a MIDI / on-screen keyboard while a take is recorded (the microphone records sound instead).</summary>
    NoteRecorder? takeRecorder;

    /// <summary>Set while a loop goes on to its next pass: the take keeps recording across passes.</summary>
    bool keepTake;

    static string TakePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardUtils", "Takes", "take.wav");

    void StartTake(INoteSource source)
    {
        DiscardTake();
        if (!config.LearningRecordTake)
            return;
        if (source is MicrophoneNoteSource microphone)
        {
            try
            {
                microphone.StartTake(TakePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(T("Nahrávání nejde spustit: ", "Couldn't start recording: ") + ex.Message);
            }
        }
        // The computer's sound has the accompaniment in it: not recorded.
        else if (source is not SystemAudioNoteSource)
            takeRecorder = new NoteRecorder(source.LatencySeconds);
    }

    /// <summary>Ends the take: what can be listened back to, or null (nothing recorded, or too short to bother).</summary>
    RecordedTake? StopTake()
    {
        if (takeRecorder is { } recorder)
        {
            takeRecorder = null;
            var notes = recorder.Finish(currentScore?.Title ?? T("Nahrávka", "Recording"));
            return notes.Parts[0].Notes.Count > 0 ? new NotesTake(notes, playbackEngine, RestorePlayback) : null;
        }
        if (noteSource is MicrophoneNoteSource microphone && microphone.StopTake() is { } wav)
            return wav.Seconds >= 1 ? new WavTake(wav.Path) : null;
        return null;
    }

    void DiscardTake()
    {
        takeRecorder = null;
        (noteSource as MicrophoneNoteSource)?.StopTake();
    }

    /// <summary>After a take played through the synth: the song back in the playback engine, as it was.</summary>
    void RestorePlayback()
    {
        if (currentScore is not { } score)
            return;
        playbackEngine.Load(score, ClickTiming);
        playbackEngine.Speed = playbackSpeed;
        for (int i = 0; i < score.Parts.Count; i++)
            playbackEngine.SetPartMuted(i, score.Parts[i].Muted);
    }

    void OpenNoteTrainer()
    {
        if (noteTrainer != null)
            return;
        StopLearningMode(showResult: false);
        FinishCreateMode();
        // An instrument to answer with: listening starts if it isn't on (the on-screen keyboard can't be clicked
        // behind the trainer, so then it's the name buttons).
        if (noteSource == null && config.NoteInput != NoteInputKind.ScreenKeyboard)
            StartListening();
        string? input = noteSource is { } source and not ScreenKeyboardNoteSource ? source.Name : null;
        using var trainer = new NoteTrainerForm(config, pitch => playbackEngine.PlayNote(pitch), input);
        noteTrainer = trainer;
        try
        {
            trainer.ShowDialog(this);
        }
        finally
        {
            noteTrainer = null;
        }
    }
}
