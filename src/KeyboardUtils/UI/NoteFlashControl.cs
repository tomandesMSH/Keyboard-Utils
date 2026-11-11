using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// One staff with its clef and a single note to read (the note-reading trainer). After an answer the note turns green
/// or red and its name shows under the staff.
/// </summary>
sealed class NoteFlashControl : Control, IThemed
{
    public enum Feedback { None, Right, Wrong }

    QuizNote? note;
    Feedback feedback;

    /// <summary>The clef shown on the empty staff (between rounds).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Clef EmptyClef { get; set; } = Clef.Treble;

    public NoteFlashControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AppTheme.Changed += Invalidate;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= Invalidate;
        base.Dispose(disposing);
    }

    /// <summary>Shows <paramref name="next"/> (null: an empty staff), with how it was answered.</summary>
    public void Show(QuizNote? next, Feedback result = Feedback.None)
    {
        note = next;
        feedback = result;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.Paper);
        // Room for the staff, ledger lines (three above, three and a note below - an instrument's lowest notes) and
        // the name under it all.
        float s = Math.Min(Height / 15.5f, Width / 18f);
        if (s < 2)
            return;
        float staffTop = Height / 2f - 3.55f * s;
        float left = Width / 2f - 7 * s, right = Width / 2f + 7 * s;
        var clef = note?.Clef ?? EmptyClef;

        using var ink = new SolidBrush(AppTheme.Ink);
        using var staffPen = new Pen(AppTheme.StaffLine, Math.Max(1, 0.1f * s));
        for (int i = 0; i < 5; i++)
            g.DrawLine(staffPen, left, staffTop + i * s, right, staffTop + i * s);
        using var barPen = new Pen(AppTheme.Ink, Math.Max(1, 0.14f * s));
        g.DrawLine(barPen, left, staffTop, left, staffTop + 4 * s);
        g.DrawLine(barPen, right, staffTop, right, staffTop + 4 * s);

        // The clef - the same as the sheet music view.
        MusicGlyphs.DrawClef(g, ink, clef, left + 0.8f * s, staffTop, s);

        if (note is not { } shown)
            return;
        var color = feedback switch
        {
            Feedback.Right => Color.FromArgb(22, 163, 74),
            Feedback.Wrong => PianoRollControl.WrongColor,
            _ => AppTheme.Ink,
        };
        using var brush = new SolidBrush(color);
        float headWidth = MusicGlyphs.HeadWidth * s;
        float x = Width / 2f + s - headWidth / 2;
        using var ledger = new Pen(AppTheme.Ink, Math.Max(1, 0.16f * s));
        DrawNote(shown, x);
        // An interval: the upper note on the same stem position - beside it when they're a second apart (no room above).
        if (shown.Upper is { } upper)
            DrawNote(upper, shown.Interval == 2 ? x + headWidth - 0.1f * s : x);

        void DrawNote(QuizNote n, float noteX)
        {
            int step = n.Step;
            float y = staffTop + 4 * s - step * s / 2;
            for (int l = -2; l >= step; l -= 2)
                Ledger(l, noteX);
            for (int l = 10; l <= step; l += 2)
                Ledger(l, noteX);
            MusicGlyphs.Notehead(g, brush, noteX, y, s, 16);
            if (n.Alter != 0)
                MusicGlyphs.Accidental(g, brush, n.Alter > 0 ? MusicGlyphs.Sharp : MusicGlyphs.Flat, noteX - 0.4f * s, y, s);
        }
        void Ledger(int lineStep, float noteX)
        {
            float ly = staffTop + 4 * s - lineStep * s / 2;
            g.DrawLine(ledger, noteX - 0.5f * s, ly, noteX + headWidth + 0.5f * s, ly);
        }

        if (feedback != Feedback.None)
        {
            using var font = new Font("Segoe UI", 1.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            string name = feedback == Feedback.Wrong ? T("Je to ", "It's ") + shown.Name : shown.Name;
            var size = g.MeasureString(name, font);
            g.DrawString(name, font, brush, Width / 2f - size.Width / 2, Height - size.Height - 0.4f * s);
        }
    }
}
