namespace KeyboardUtils.Tests;

public class NoteEditorTests
{
    static Score ScoreWith(params ScoreNote[] notes) =>
        new() { Title = "Test", Parts = [new ScorePart { Name = "Nahrávka", Notes = notes.ToList() }] };

    static ScoreNote Note(int pitch = 60, double start = 1, double duration = 0.5) =>
        new() { Pitch = pitch, StartSeconds = start, DurationSeconds = duration };

    [Fact]
    public void Select_replaces_the_whole_selection()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var editor = new NoteEditor(ScoreWith(a, b));

        editor.Select(a);
        Assert.Equal(new HashSet<ScoreNote> { a }, editor.Selected);

        editor.Select(b);
        Assert.Equal(new HashSet<ScoreNote> { b }, editor.Selected);

        editor.Select(null);
        Assert.Empty(editor.Selected);
    }

    [Fact]
    public void ToggleSelect_adds_and_removes_without_touching_the_rest()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var editor = new NoteEditor(ScoreWith(a, b));

        editor.ToggleSelect(a);
        editor.ToggleSelect(b);
        Assert.Equal(new HashSet<ScoreNote> { a, b }, editor.Selected);

        editor.ToggleSelect(a);
        Assert.Equal(new HashSet<ScoreNote> { b }, editor.Selected);
    }

    [Fact]
    public void SelectOnly_replaces_and_SelectAdditional_adds()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var c = Note(pitch: 67);
        var editor = new NoteEditor(ScoreWith(a, b, c));

        editor.SelectOnly([a, b]);
        Assert.Equal(new HashSet<ScoreNote> { a, b }, editor.Selected);

        editor.SelectAdditional([c]);
        Assert.Equal(new HashSet<ScoreNote> { a, b, c }, editor.Selected);

        editor.SelectOnly([c]);
        Assert.Equal(new HashSet<ScoreNote> { c }, editor.Selected);
    }

    [Fact]
    public void MoveSelection_moves_every_selected_note_as_one_undo_step()
    {
        var a = Note(pitch: 60, start: 1);
        var b = Note(pitch: 64, start: 2);
        var untouched = Note(pitch: 67, start: 3);
        var editor = new NoteEditor(ScoreWith(a, b, untouched));
        editor.SelectOnly([a, b]);

        editor.MoveSelection(0.5);
        var notes = editor.Score.Parts[0].Notes;
        Assert.Equal(1.5, notes.Single(n => n.Pitch == 60).StartSeconds, 6);
        Assert.Equal(2.5, notes.Single(n => n.Pitch == 64).StartSeconds, 6);
        Assert.Equal(3.0, notes.Single(n => n.Pitch == 67).StartSeconds, 6); // not selected, not moved

        Assert.True(editor.CanUndo);
        editor.Undo();
        notes = editor.Score.Parts[0].Notes;
        Assert.Equal(1.0, notes.Single(n => n.Pitch == 60).StartSeconds, 6);
        Assert.Equal(2.0, notes.Single(n => n.Pitch == 64).StartSeconds, 6);
        Assert.False(editor.CanUndo); // the whole multi-note move was one step
    }

    [Fact]
    public void DeleteSelection_deletes_every_selected_note_and_undo_restores_all_of_them()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var kept = Note(pitch: 67);
        var editor = new NoteEditor(ScoreWith(a, b, kept));
        editor.SelectOnly([a, b]);

        editor.DeleteSelection();
        Assert.Single(editor.Score.Parts[0].Notes);
        Assert.Empty(editor.Selected);

        editor.Undo();
        Assert.Equal(3, editor.Score.Parts[0].Notes.Count);
        Assert.Equal(new HashSet<ScoreNote> { a, b }, editor.Selected); // selection restored too
    }

    [Fact]
    public void ChangeSelectionPitch_shifts_every_selected_note()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var editor = new NoteEditor(ScoreWith(a, b));
        editor.SelectOnly([a, b]);

        editor.ChangeSelectionPitch(2);
        var pitches = editor.Score.Parts[0].Notes.Select(n => n.Pitch).OrderBy(p => p);
        Assert.Equal([62, 66], pitches);
    }

    [Fact]
    public void A_new_edit_after_undo_clears_the_redo_stack()
    {
        var note = Note();
        var editor = new NoteEditor(ScoreWith(note));
        editor.Select(note);

        editor.MoveSelection(1.0);
        editor.Undo();
        Assert.True(editor.CanRedo);

        editor.Select(editor.Score.Parts[0].Notes[0]);
        editor.MoveSelection(0.5);
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void A_drag_of_several_moves_undoes_as_one_step()
    {
        var note = Note(start: 1);
        var editor = new NoteEditor(ScoreWith(note));
        editor.Select(note);

        editor.BeginEdit();
        editor.MoveSelection(0.1);
        editor.MoveSelection(0.1);
        editor.MoveSelection(0.1);
        editor.EndEdit();

        Assert.Equal(1.3, editor.Score.Parts[0].Notes[0].StartSeconds, 6);

        editor.Undo();
        Assert.Equal(1.0, editor.Score.Parts[0].Notes[0].StartSeconds, 6);
        Assert.False(editor.CanUndo); // the whole drag was one step, not three
    }

    [Fact]
    public void An_EndEdit_gesture_with_no_net_change_pushes_nothing()
    {
        var note = Note(start: 1);
        var editor = new NoteEditor(ScoreWith(note));
        editor.Select(note);

        editor.BeginEdit();
        editor.MoveSelection(1.0);
        editor.MoveSelection(-1.0); // back to the original value (a different note instance, but the same start)
        editor.EndEdit();

        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Undo_and_redo_restore_the_selection()
    {
        var a = Note(pitch: 60);
        var b = Note(pitch: 64);
        var editor = new NoteEditor(ScoreWith(a, b));
        editor.Select(a);

        editor.MoveSelection(1.0);
        var movedA = editor.Score.Parts[0].Notes.Single(n => n.Pitch == 60);
        Assert.Equal(new HashSet<ScoreNote> { movedA }, editor.Selected);

        editor.Undo();
        Assert.Equal(new HashSet<ScoreNote> { a }, editor.Selected);

        editor.Redo();
        Assert.Equal(new HashSet<ScoreNote> { movedA }, editor.Selected);
    }

    [Fact]
    public void Undo_with_nothing_to_undo_does_nothing()
    {
        var editor = new NoteEditor(ScoreWith(Note()));
        Assert.False(editor.CanUndo);
        editor.Undo(); // must not throw
        Assert.False(editor.CanRedo);
    }
}
