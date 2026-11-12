using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// A guitar chord box as in songbooks: its name, the six strings upright (low E on the left), the frets across, dots
/// with the finger numbers, a bar for a barre, x / o above the strings not played / played open. Clickable.
/// </summary>
sealed class ChordDiagramControl : Control, IThemed
{
    const int FretsShown = 4;
    bool hover;

    public ChordDiagramControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(132, 178);
        Cursor = Cursors.Hand;
        AppTheme.Changed += Invalidate;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= Invalidate;
        base.Dispose(disposing);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public GuitarChord? Chord { get; set { field = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // no colour fringes on the small × and ○
        g.Clear(hover ? (AppTheme.Dark ? Color.FromArgb(44, 44, 50) : Color.FromArgb(236, 240, 248)) : AppTheme.Paper);
        if (Chord is not { } chord)
            return;

        using var ink = new SolidBrush(AppTheme.Ink);
        using var subtle = new SolidBrush(AppTheme.SubtleText);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var nameFont = new Font("Segoe UI Semibold", Math.Max(9f, Height * 0.09f), GraphicsUnit.Pixel);
        float nameHeight = Height * 0.17f;
        g.DrawString(chord.Name, nameFont, ink, new RectangleF(0, 2, Width, nameHeight), center);

        // The grid: strings across, frets down, starting at the nut (or at the lowest fret, numbered).
        int lowest = chord.Frets.Where(f => f > 0).DefaultIfEmpty(1).Min();
        int baseFret = chord.Frets.Max() <= FretsShown ? 1 : lowest;
        float markRow = Height * 0.08f;
        float left = Width * 0.18f, right = Width * 0.88f, top = nameHeight + markRow + 6, bottom = Height - 8;
        float stringGap = (right - left) / 5, fretGap = (bottom - top) / FretsShown;
        using var line = new Pen(AppTheme.SubtleText, 1.2f);
        for (int s = 0; s < 6; s++)
            g.DrawLine(line, left + s * stringGap, top, left + s * stringGap, bottom);
        for (int f = 0; f <= FretsShown; f++)
            g.DrawLine(line, left, top + f * fretGap, right, top + f * fretGap);
        if (baseFret == 1)
        {
            using var nut = new Pen(AppTheme.Ink, 3.5f);
            g.DrawLine(nut, left, top, right, top);
        }
        else
        {
            using var fretFont = new Font("Segoe UI", Math.Max(7f, fretGap * 0.4f), GraphicsUnit.Pixel);
            g.DrawString(baseFret.ToString(), fretFont, subtle, new RectangleF(0, top, left - 3, fretGap), center);
        }

        // x / o above, dots (and the barre) on the frets.
        using var markFont = new Font("Segoe UI", Math.Max(7f, markRow * 0.9f), GraphicsUnit.Pixel);
        float r = Math.Min(stringGap * 0.44f, fretGap * 0.4f);
        using var fingerFont = new Font("Segoe UI", Math.Max(7f, r * 1.25f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var dot = new SolidBrush(AppTheme.Ink);
        using var onDot = new SolidBrush(AppTheme.Paper);
        if (chord.Barre is { } barre)
        {
            var strings = Enumerable.Range(0, 6).Where(s => chord.Frets[s] == barre && chord.Fingers[s] == 1).ToList();
            float y = top + (barre - baseFret + 0.5f) * fretGap;
            using var bar = new Pen(AppTheme.Ink, 2 * r) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(bar, left + strings.Min() * stringGap, y, left + strings.Max() * stringGap, y);
        }
        for (int s = 0; s < 6; s++)
        {
            float x = left + s * stringGap;
            int fret = chord.Frets[s];
            if (fret <= 0)
            {
                g.DrawString(fret < 0 ? "×" : "○", markFont, subtle, new RectangleF(x - stringGap / 2, nameHeight + 2, stringGap, markRow + 2), center);
                continue;
            }
            float y = top + (fret - baseFret + 0.5f) * fretGap;
            g.FillEllipse(dot, x - r, y - r, 2 * r, 2 * r);
            if (chord.Fingers[s] > 0)
                g.DrawString(chord.Fingers[s].ToString(), fingerFont, onDot, new RectangleF(x - r, y - r, 2 * r, 2 * r), center);
        }
    }
}
