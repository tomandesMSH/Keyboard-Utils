using System.Diagnostics;

namespace KeyboardUtils;

enum LearningMode
{
    /// <summary>The song stops at each note until it's played right. No accompaniment.</summary>
    WaitForNote,

    /// <summary>The song runs at a set speed; notes count if played close enough to their time.</summary>
    InTempo,
}

enum NoteMark
{
    Pending,

    /// <summary>Played right (in wait mode: without a wrong key first).</summary>
    Correct,

    /// <summary>Missed (tempo), or played only after a wrong key (wait).</summary>
    Wrong,
}

sealed record LearningResult(int TotalNotes, int CorrectNotes, int WrongPresses, int MissedNotes, TimeSpan Duration, bool Completed)
{
    public double Percent => TotalNotes == 0 ? 0 : 100.0 * CorrectNotes / TotalNotes;
}

/// <summary>
/// Learning mode logic, independent of any UI or audio: which notes of the score to play, what was heard, and
/// how it went. Practiced notes are grouped into steps by start time (a chord is one step). Driven from the UI
/// thread: <see cref="Tick"/> from a timer, <see cref="OnNoteOn"/> for every note heard.
/// </summary>
sealed class LearningSession
{
    /// <summary>Notes starting within this of each other are one step (a chord, or a "together" in two hands).</summary>
    const double ChordWindowSeconds = 0.04;

    /// <summary>Tempo mode: how early/late (in score seconds at full speed) a note still counts.</summary>
    const double ToleranceSeconds = 0.25;

    /// <summary>Time before the first note so the player can get ready - notes fall in from above.</summary>
    const double LeadInSeconds = 2.0;

    sealed class Step(double start, List<ScoreNote> notes)
    {
        public double Start { get; } = start;
        public List<ScoreNote> Notes { get; } = notes;
        public bool HadMistake { get; set; }
    }

    readonly List<Step> steps;
    readonly Dictionary<ScoreNote, NoteMark> marks = [];
    readonly Func<double>? tempoClock;
    readonly Action? startTempoClock;
    readonly Stopwatch elapsed = new();
    readonly double startSeconds, endSeconds;

    /// <summary>How long the count-in before the first note is, in score time.</summary>
    readonly double leadIn;
    double position;
    long lastTickTicks;
    bool tempoClockStarted;
    int currentStep;
    int wrongPresses;
    LearningResult? result;

    /// <param name="practicedParts">Indices of the parts the player plays.</param>
    /// <param name="tempoClock">Tempo mode only: current score time of the accompaniment playback.</param>
    /// <param name="startTempoClock">Tempo mode only: starts that playback once the lead-in is over.</param>
    /// <param name="range">Only practise the notes starting in this stretch (a loop); null = the whole song.</param>
    /// <param name="leadInSeconds">Score time before the first note (a bar of metronome count-in); null = 2 s.</param>
    public LearningSession(Score score, IReadOnlyCollection<int> practicedParts, LearningMode mode, double speed,
        bool polyphonicInput, double inputLatencySeconds, Func<double>? tempoClock = null, Action? startTempoClock = null,
        (double Start, double End)? range = null, double? leadInSeconds = null)
    {
        Mode = mode;
        Speed = Math.Clamp(speed, 0.1, 2);
        startSeconds = range?.Start ?? 0;
        Range = range;
        // Score time runs at Speed, so this makes the lead-in the same real time at any speed.
        leadIn = leadInSeconds ?? LeadInSeconds * Speed;
        position = startSeconds - leadIn;
        PolyphonicInput = polyphonicInput;
        InputLatencySeconds = inputLatencySeconds;
        this.tempoClock = tempoClock;
        this.startTempoClock = startTempoClock;
        PracticedParts = practicedParts.ToHashSet();

        var notes = practicedParts
            .Where(i => i >= 0 && i < score.Parts.Count)
            .SelectMany(i => score.Parts[i].Notes)
            .Where(n => range is not { } r || (n.StartSeconds >= r.Start - 0.001 && n.StartSeconds < r.End))
            .OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch)
            .ToList();
        steps = [];
        foreach (var note in notes)
        {
            marks[note] = NoteMark.Pending;
            // The same key twice in one step (two parts doubling it) stays two notes, but one press marks both.
            if (steps.Count > 0 && note.StartSeconds - steps[^1].Start <= ChordWindowSeconds)
                steps[^1].Notes.Add(note);
            else
                steps.Add(new Step(note.StartSeconds, [note]));
        }
        // A loop ends where the section ends (so the next pass starts on time), the whole song after its last note.
        endSeconds = range?.End ?? (notes.Count > 0 ? notes.Max(n => n.StartSeconds + n.DurationSeconds) : 0);
        elapsed.Start();
        lastTickTicks = elapsed.ElapsedTicks;
    }

    public LearningMode Mode { get; }
    public double Speed { get; }
    public bool PolyphonicInput { get; }
    public double InputLatencySeconds { get; }
    public IReadOnlySet<int> PracticedParts { get; }
    public bool Paused { get; private set; }
    public bool IsFinished { get; private set; }

    /// <summary>Still counting in, before the first note's time.</summary>
    public bool InLeadIn => position < startSeconds;

    /// <summary>The stretch being practised (a loop), or null for the whole song.</summary>
    public (double Start, double End)? Range { get; }

    /// <summary>Score time the piano roll should show. Before the start (negative for a whole song) during the lead-in.</summary>
    public double Position => position;

    public int TotalNotes => marks.Count;
    public int CorrectNotes => marks.Values.Count(m => m == NoteMark.Correct);
    public int DoneNotes => marks.Values.Count(m => m != NoteMark.Pending);
    public int WrongPresses => wrongPresses;

    /// <summary>Fires once when the last step is done (wait) or the song has passed (tempo).</summary>
    public event Action? Finished;

    /// <summary>A key that isn't expected now was pressed - for a red flash on the keyboard.</summary>
    public event Action<int>? WrongKey;

    public NoteMark MarkOf(ScoreNote note) => marks.TryGetValue(note, out var mark) ? mark : NoteMark.Pending;

    /// <summary>Keys the player should press right now - lit as a hint on the keyboard.</summary>
    public IEnumerable<int> ExpectedKeys()
    {
        if (IsFinished)
            yield break;
        if (Mode == LearningMode.WaitForNote)
        {
            if (currentStep < steps.Count && position >= steps[currentStep].Start - 0.5)
                foreach (var note in steps[currentStep].Notes.Where(n => marks[n] == NoteMark.Pending))
                    yield return note.Pitch;
            yield break;
        }
        foreach (var step in steps)
        {
            if (step.Start > position + 0.05)
                break;
            if (step.Start >= position - ToleranceSeconds)
                foreach (var note in step.Notes.Where(n => marks[n] == NoteMark.Pending))
                    yield return note.Pitch;
        }
    }

    /// <summary>
    /// Notes worth listening for now (chord checking). Unlike <see cref="ExpectedKeys"/> - a hint that lights up as
    /// the notes arrive - in wait mode this is the next step straight away, since playing ahead is allowed.
    /// </summary>
    public IEnumerable<int> ListenFor() =>
        Mode == LearningMode.WaitForNote && !IsFinished && currentStep < steps.Count
            ? steps[currentStep].Notes.Where(n => marks[n] == NoteMark.Pending).Select(n => n.Pitch)
            : ExpectedKeys();

    public void SetPaused(bool paused)
    {
        Paused = paused;
        if (paused)
            elapsed.Stop();
        else
            elapsed.Start();
        lastTickTicks = elapsed.ElapsedTicks;
    }

    /// <summary>Advances time; call often (every frame of the UI timer).</summary>
    public void Tick()
    {
        if (IsFinished)
            return;
        long now = elapsed.ElapsedTicks;
        double dt = (double)(now - lastTickTicks) / Stopwatch.Frequency;
        lastTickTicks = now;
        if (Paused)
            return;

        if (Mode == LearningMode.WaitForNote)
        {
            // Time runs until the next unfinished step reaches the line, then waits there for the player.
            double limit = currentStep < steps.Count ? steps[currentStep].Start : endSeconds;
            position = Math.Min(position + dt * Speed, limit);
            if (currentStep >= steps.Count)
                Finish(completed: true);
            return;
        }

        if (!tempoClockStarted)
        {
            position += dt * Speed;
            if (position < startSeconds)
                return;
            tempoClockStarted = true;
            startTempoClock?.Invoke();
        }
        position = tempoClock?.Invoke() ?? position + dt * Speed;

        // Anything whose window has passed unplayed is missed.
        for (; currentStep < steps.Count && steps[currentStep].Start + ToleranceSeconds < position; currentStep++)
            foreach (var note in steps[currentStep].Notes.Where(n => marks[n] == NoteMark.Pending))
                marks[note] = NoteMark.Wrong;
        if (position > endSeconds + ToleranceSeconds)
            Finish(completed: true);
    }

    public void OnNoteOn(int pitch)
    {
        // Ignore the first half of the lead-in: whatever was still ringing when learning started.
        if (IsFinished || Paused || position < startSeconds - leadIn / 2)
            return;
        if (Mode == LearningMode.WaitForNote)
            OnNoteWait(pitch);
        else
            OnNoteTempo(pitch);
    }

    void OnNoteWait(int pitch)
    {
        if (currentStep >= steps.Count)
            return;
        var step = steps[currentStep];
        var matching = step.Notes.Where(n => n.Pitch == pitch && marks[n] == NoteMark.Pending).ToList();
        if (matching.Count == 0)
        {
            wrongPresses++;
            step.HadMistake = true;
            WrongKey?.Invoke(pitch);
            return;
        }

        var mark = step.HadMistake ? NoteMark.Wrong : NoteMark.Correct;
        foreach (var note in matching)
            marks[note] = mark;

        // The microphone can't hear chords: one right note of the chord has to be enough.
        bool complete = !PolyphonicInput || step.Notes.All(n => marks[n] != NoteMark.Pending);
        if (!complete)
            return;
        foreach (var note in step.Notes.Where(n => marks[n] == NoteMark.Pending))
            marks[note] = mark;
        currentStep++;
        // Playing ahead of the falling notes is fine: jump the line to the step just played.
        position = Math.Max(position, step.Start);
    }

    void OnNoteTempo(int pitch)
    {
        // When the key was actually pressed, in score time.
        double played = position - InputLatencySeconds * Speed;
        Step? best = null;
        foreach (var step in steps.Skip(currentStep))
        {
            if (step.Start > played + ToleranceSeconds)
                break;
            if (Math.Abs(step.Start - played) > ToleranceSeconds)
                continue;
            if (!step.Notes.Any(n => n.Pitch == pitch && marks[n] == NoteMark.Pending))
                continue;
            if (best == null || Math.Abs(step.Start - played) < Math.Abs(best.Start - played))
                best = step;
        }
        if (best == null)
        {
            wrongPresses++;
            WrongKey?.Invoke(pitch);
            return;
        }

        foreach (var note in best.Notes.Where(n => n.Pitch == pitch))
            marks[note] = NoteMark.Correct;
        if (!PolyphonicInput)
            foreach (var note in best.Notes.Where(n => marks[n] == NoteMark.Pending))
                marks[note] = NoteMark.Correct;
    }

    /// <summary>Ends the session (if it hasn't finished on its own) and returns how it went.</summary>
    public LearningResult Stop() => Finish(completed: false);

    LearningResult Finish(bool completed)
    {
        if (result != null)
            return result;
        IsFinished = true;
        elapsed.Stop();
        // Stopped early: score only the part that was actually reached.
        result = new LearningResult(
            TotalNotes: completed || DoneNotes == 0 ? TotalNotes : DoneNotes,
            CorrectNotes: CorrectNotes,
            WrongPresses: wrongPresses,
            MissedNotes: Mode == LearningMode.InTempo ? marks.Values.Count(m => m == NoteMark.Wrong) : 0,
            Duration: elapsed.Elapsed,
            Completed: completed);
        if (completed)
            Finished?.Invoke();
        return result;
    }
}
