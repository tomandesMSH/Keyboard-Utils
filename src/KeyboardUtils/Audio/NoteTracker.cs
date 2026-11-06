namespace KeyboardUtils;

/// <summary>
/// Turns a stream of overlapping audio frames into discrete note-on/note-off events. Raw per-frame pitch is
/// jumpy (attack transients, octave slips while a note decays), so a pitch only becomes a note after it has held
/// steady for a few frames, and a repeated strike of the same key is told apart from a sustained note by a sudden
/// rise in loudness (onset). Pure logic, no audio I/O - <see cref="MicrophoneNoteSource"/> feeds it.
/// </summary>
sealed class NoteTracker
{
    /// <summary>Consecutive frames a new pitch must hold before it counts (with a fresh onset).</summary>
    const int StableFrames = 3;

    /// <summary>Without an onset, a pitch change needs longer - it's more likely a decaying note slipping an octave.</summary>
    const int StableFramesWithoutOnset = 8;

    /// <summary>Loudness jump (dB above the recent minimum) that counts as a new key strike.</summary>
    const double OnsetRiseDb = 7;

    /// <summary>A sounding note may decay this far below the threshold before it's considered released.</summary>
    const double ReleaseHysteresisDb = 6;

    /// <summary>How many past frames the onset detector compares against.</summary>
    const int OnsetHistory = 4;

    /// <summary>Frames after an onset during which another onset is ignored (one strike = one rise).</summary>
    const int OnsetRefractoryFrames = 6;

    readonly PitchDetector detector;
    readonly Queue<double> recentLevels = new();
    int? candidate;
    int candidateFrames;
    bool onsetPending;
    int framesSinceOnset = int.MaxValue;

    public NoteTracker(PitchDetector detector) => this.detector = detector;

    /// <summary>Frames quieter than this (dBFS) are treated as silence. Settable live from the calibration UI.</summary>
    public double ThresholdDb { get; set; } = -45;

    /// <summary>The note currently sounding, if any.</summary>
    public int? ActiveNote { get; private set; }

    /// <summary>Last raw detected frequency (Hz) - for display/debugging only.</summary>
    public double? LastFrequency { get; private set; }

    public event Action<int>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>Processes one frame and returns its level in dBFS.</summary>
    public double Process(ReadOnlySpan<float> frame)
    {
        double levelDb = LevelDb(frame);
        bool onset = DetectOnset(levelDb);

        double silenceBelow = ActiveNote != null ? ThresholdDb - ReleaseHysteresisDb : ThresholdDb;
        if (levelDb < silenceBelow)
        {
            LastFrequency = null;
            onsetPending = false;
            ResetCandidate();
            Release();
            return levelDb;
        }

        if (onset)
        {
            // A new strike: the next steady pitch is a new note, even if it's the same key again.
            onsetPending = true;
            ResetCandidate();
        }

        LastFrequency = detector.Detect(frame, out _);
        if (LastFrequency is not { } frequency || frequency < detector.MinFrequency)
            return levelDb;

        int note = Math.Clamp(PitchDetector.ToMidi(frequency), 0, 127);
        if (note == candidate)
            candidateFrames++;
        else
        {
            candidate = note;
            candidateFrames = 1;
        }

        int needed = onsetPending || ActiveNote == null ? StableFrames : StableFramesWithoutOnset;
        if (candidateFrames < needed)
            return levelDb;

        if (note != ActiveNote || onsetPending)
        {
            Release();
            ActiveNote = note;
            NoteOn?.Invoke(note);
        }
        onsetPending = false;
        return levelDb;
    }

    /// <summary>Ends the sounding note (if any), e.g. when listening stops.</summary>
    public void Release()
    {
        if (ActiveNote is not { } note)
            return;
        ActiveNote = null;
        NoteOff?.Invoke(note);
    }

    bool DetectOnset(double levelDb)
    {
        framesSinceOnset = framesSinceOnset == int.MaxValue ? int.MaxValue : framesSinceOnset + 1;
        bool onset = recentLevels.Count == OnsetHistory
            && framesSinceOnset > OnsetRefractoryFrames
            && levelDb >= ThresholdDb
            && levelDb - recentLevels.Min() >= OnsetRiseDb;
        if (onset)
            framesSinceOnset = 0;

        recentLevels.Enqueue(levelDb);
        if (recentLevels.Count > OnsetHistory)
            recentLevels.Dequeue();
        return onset;
    }

    void ResetCandidate()
    {
        candidate = null;
        candidateFrames = 0;
    }

    public static double LevelDb(ReadOnlySpan<float> frame)
    {
        double sum = 0;
        foreach (float sample in frame)
            sum += sample * sample;
        double rms = Math.Sqrt(sum / Math.Max(1, frame.Length));
        return 20 * Math.Log10(rms + 1e-10);
    }
}
