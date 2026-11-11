namespace KeyboardUtils;

/// <summary>
/// Owns an in-progress edit of a just-recorded <see cref="Score"/>: which notes are selected, applying
/// moves/pitch changes/deletes to the whole selection, and undo/redo. Only ever points at a still-unsaved
/// recording - see <see cref="MainForm"/>'s <c>noteEditor</c> field for when it's created/cleared.
/// </summary>
sealed class NoteEditor(Score score)
{
    public Score Score { get; } = score;

    /// <summary>Reference-identity membership: <see cref="ScoreNote"/> has no value equality, which this relies on.</summary>
    readonly HashSet<ScoreNote> selected = [];

    public IReadOnlySet<ScoreNote> Selected => selected;

    /// <summary>Replaces the selection with zero or one note - a plain click.</summary>
    public void Select(ScoreNote? note)
    {
        selected.Clear();
        if (note != null)
            selected.Add(note);
    }

    /// <summary>Adds or removes one note from the selection - a shift-click.</summary>
    public void ToggleSelect(ScoreNote note)
    {
        if (!selected.Remove(note))
            selected.Add(note);
    }

    /// <summary>Replaces the selection with <paramref name="notes"/> - a marquee drag-select.</summary>
    public void SelectOnly(IEnumerable<ScoreNote> notes)
    {
        selected.Clear();
        selected.UnionWith(notes);
    }

    /// <summary>Adds <paramref name="notes"/> to the selection - a shift+marquee drag-select.</summary>
    public void SelectAdditional(IEnumerable<ScoreNote> notes) => selected.UnionWith(notes);

    public void MoveSelection(double deltaSeconds) => Edit(() =>
    {
        foreach (var note in selected.ToList())
            ReplaceCore(note, ScoreEditing.Nudge(note, deltaSeconds));
    });

    public void ChangeSelectionPitch(int deltaSemitones) => Edit(() =>
    {
        foreach (var note in selected.ToList())
            ReplaceCore(note, ScoreEditing.Repitch(note, deltaSemitones));
    });

    public void DeleteSelection() => Edit(() =>
    {
        foreach (var note in selected.ToList())
        {
            PartOf(note)?.Notes.Remove(note);
            selected.Remove(note);
        }
    });

    /// <summary>A precise edit of one specific note (the properties dialog) - independent of the selection size.</summary>
    public void Replace(ScoreNote oldNote, ScoreNote newNote) => Edit(() => ReplaceCore(oldNote, newNote));

    void ReplaceCore(ScoreNote oldNote, ScoreNote newNote)
    {
        if (PartOf(oldNote) is not { } part)
            return;
        int index = part.Notes.IndexOf(oldNote);
        if (index < 0)
            return;
        part.Notes[index] = newNote;
        if (selected.Remove(oldNote))
            selected.Add(newNote);
    }

    ScorePart? PartOf(ScoreNote note) => Score.Parts.FirstOrDefault(p => p.Notes.Contains(note));

    // --- Undo/redo: a snapshot of every part's note list (just the references - ScoreNote is immutable) ------

    readonly record struct Snapshot(List<ScoreNote>[] PartNotes, HashSet<ScoreNote> Selected);

    readonly Stack<Snapshot> undo = [];
    readonly Stack<Snapshot> redo = [];

    /// <summary>Set while a multi-step gesture (a drag) is in progress: its edits apply but don't each push a step.</summary>
    Snapshot? pending;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    /// <summary>Starts a gesture that may call <see cref="MoveSelection"/> (etc.) many times before <see cref="EndEdit"/> -
    /// the whole gesture becomes one undo step instead of one per call.</summary>
    public void BeginEdit() => pending ??= Capture();

    /// <summary>Ends a gesture started by <see cref="BeginEdit"/>, pushing one undo step (skipped if nothing changed).</summary>
    public void EndEdit()
    {
        if (pending is not { } before)
            return;
        pending = null;
        PushIfChanged(before);
    }

    /// <summary>A one-shot edit outside any <see cref="BeginEdit"/> gesture becomes its own undo step.</summary>
    void Edit(Action apply)
    {
        if (pending != null)
        {
            apply();
            return;
        }
        var before = Capture();
        apply();
        PushIfChanged(before);
    }

    void PushIfChanged(Snapshot before)
    {
        if (Same(before, Capture()))
            return;
        undo.Push(before);
        redo.Clear();
    }

    public void Undo()
    {
        if (undo.Count == 0)
            return;
        redo.Push(Capture());
        Restore(undo.Pop());
    }

    public void Redo()
    {
        if (redo.Count == 0)
            return;
        undo.Push(Capture());
        Restore(redo.Pop());
    }

    Snapshot Capture() => new(Score.Parts.Select(p => new List<ScoreNote>(p.Notes)).ToArray(), new HashSet<ScoreNote>(selected));

    void Restore(Snapshot snapshot)
    {
        for (int i = 0; i < Score.Parts.Count && i < snapshot.PartNotes.Length; i++)
            Score.Parts[i].Notes = new List<ScoreNote>(snapshot.PartNotes[i]);
        selected.Clear();
        selected.UnionWith(snapshot.Selected);
    }

    static bool Same(Snapshot a, Snapshot b)
    {
        if (a.PartNotes.Length != b.PartNotes.Length)
            return false;
        for (int i = 0; i < a.PartNotes.Length; i++)
            if (!a.PartNotes[i].SequenceEqual(b.PartNotes[i], ValueComparer))
                return false;
        return true;
    }

    // By value, not reference: a drag (or Begin/EndEdit gesture) that nets back to its starting values must not
    // push a no-op undo step, even though every intermediate move/pitch-change created new ScoreNote instances.
    static readonly IEqualityComparer<ScoreNote> ValueComparer = EqualityComparer<ScoreNote>.Create(
        (a, b) => a is null ? b is null : b is not null && a.Pitch == b.Pitch && a.StartSeconds == b.StartSeconds
            && a.DurationSeconds == b.DurationSeconds && a.Velocity == b.Velocity);
}
