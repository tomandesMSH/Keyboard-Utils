using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// Noteheads and accidentals drawn from lines and curves (no music font needed), shared by the sheet music view and
/// the note-reading trainer. <c>s</c> is the staff space: the distance between two staff lines.
/// </summary>
static class MusicGlyphs
{
    public const int Sharp = 1, Natural = 2, Flat = 3;

    /// <summary>A notehead's width in staff spaces.</summary>
    public const float HeadWidth = 1.25f;

    /// <summary>
    /// An oval tilted up to the right, its left edge at <paramref name="x"/>: filled for quarters and shorter
    /// (<paramref name="length"/> in sixteenths), open for halves and wholes.
    /// </summary>
    public static void Notehead(Graphics g, Brush brush, float x, float y, float s, int length)
    {
        using var path = new GraphicsPath(FillMode.Alternate);
        float cx = x + HeadWidth * s / 2;
        if (length >= 16)
        {
            path.AddEllipse(cx - 0.8f * s, y - 0.5f * s, 1.6f * s, s);
            using var hole = new GraphicsPath();
            hole.AddEllipse(-0.42f * s, -0.27f * s, 0.84f * s, 0.54f * s);
            using var holeMatrix = new Matrix();
            holeMatrix.Translate(cx, y);
            holeMatrix.Rotate(55);
            hole.Transform(holeMatrix);
            path.AddPath(hole, false);
        }
        else
        {
            path.AddEllipse(-0.64f * s, -0.43f * s, 1.28f * s, 0.86f * s);
            if (length >= 8)
                path.AddEllipse(-0.5f * s, -0.2f * s, 1.0f * s, 0.4f * s);
            using var matrix = new Matrix();
            matrix.Translate(cx, y);
            matrix.Rotate(-22);
            path.Transform(matrix);
        }
        g.FillPath(brush, path);
    }

    /// <summary>
    /// A clef at the start of a staff whose top line is at <paramref name="staffTop"/>: G (treble, and the guitar's
    /// with a small 8 below), F on the fourth line (bass), C on the middle line (alto) or the fourth (tenor).
    /// </summary>
    public static void DrawClef(Graphics g, Brush brush, Clef clef, float x, float staffTop, float s)
    {
        switch (clef)
        {
            case Clef.Treble:
            case Clef.Treble8:
                Glyph(g, brush, "\U0001D11E", 7.2f * s, x, staffTop - 1.55f * s, "G");
                if (clef == Clef.Treble8)
                {
                    using var font = new Font("Segoe UI", 1.35f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                    var size = g.MeasureString("8", font);
                    g.DrawString("8", font, brush, x + 1.3f * s - size.Width / 2, staffTop + 5.35f * s);
                }
                break;
            case Clef.Bass:
                Glyph(g, brush, "\U0001D122", 3.3f * s, x + 0.2f * s, staffTop - 0.05f * s, "F");
                break;
            case Clef.Alto:
                Glyph(g, brush, "\U0001D121", 4.1f * s, x + 0.1f * s, staffTop - 0.05f * s, "C");
                break;
            case Clef.Tenor:
                Glyph(g, brush, "\U0001D121", 4.1f * s, x + 0.1f * s, staffTop - 1.05f * s, "C");
                break;
        }
    }

    /// <summary>Glyph outlines from Segoe UI Symbol at height 1 (null when the font lacks them), by text.</summary>
    static readonly Dictionary<string, GraphicsPath?> glyphs = [];

    /// <summary>A music symbol from Segoe UI Symbol, <paramref name="height"/> tall with its top left at (x, y); the
    /// fallback letter when the font lacks it.</summary>
    public static void Glyph(Graphics g, Brush brush, string text, float height, float x, float y, string fallback)
    {
        if (!glyphs.TryGetValue(text, out var unit))
        {
            unit = new GraphicsPath();
            try
            {
                using var family = new FontFamily("Segoe UI Symbol");
                unit.AddString(text, family, (int)FontStyle.Regular, 100, PointF.Empty, StringFormat.GenericTypographic);
            }
            catch (ArgumentException)
            {
            }
            var bounds = unit.GetBounds();
            if (bounds.Height <= 0)
            {
                unit.Dispose();
                unit = null;
            }
            else
            {
                using var normalize = new Matrix();
                normalize.Scale(1 / bounds.Height, 1 / bounds.Height);
                normalize.Translate(-bounds.X, -bounds.Y);
                unit.Transform(normalize);
            }
            glyphs[text] = unit;
        }
        if (unit == null)
        {
            using var font = new Font("Times New Roman", height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(fallback, font, brush, x, y + height * 0.15f);
            return;
        }
        using var path = (GraphicsPath)unit.Clone();
        using var matrix = new Matrix();
        matrix.Translate(x, y);
        matrix.Scale(height, height);
        path.Transform(matrix);
        g.FillPath(brush, path);
    }

    /// <summary>A sharp, flat or natural (<see cref="Sharp"/>...) ending at <paramref name="right"/>, centred on the note's line or space.</summary>
    public static void Accidental(Graphics g, Brush brush, int kind, float right, float y, float s)
    {
        using var pen = new Pen(brush, Math.Max(1, 0.12f * s));
        using var thick = new Pen(brush, 0.3f * s);
        if (kind == Flat)
        {
            // Flat: a tall stem with a rounded belly on the right, sitting on the note's line or space.
            float left = right - 0.8f * s;
            g.DrawLine(pen, left, y - 1.9f * s, left, y + 0.55f * s);
            using var belly = new GraphicsPath();
            belly.AddBezier(left, y + 0.55f * s, left + 0.5f * s, y + 0.2f * s, left + 1.05f * s, y - 0.35f * s, left + 0.5f * s, y - 0.55f * s);
            belly.AddBezier(left + 0.5f * s, y - 0.55f * s, left + 0.25f * s, y - 0.65f * s, left + 0.05f * s, y - 0.45f * s, left, y - 0.2f * s);
            using var bellyPen = new Pen(brush, 0.22f * s);
            g.DrawPath(bellyPen, belly);
        }
        else if (kind == Sharp)
        {
            // Sharp: two upright strokes crossed by two thick rising bars.
            float left = right - 0.95f * s;
            g.DrawLine(pen, left + 0.3f * s, y - 1.3f * s, left + 0.3f * s, y + 1.4f * s);
            g.DrawLine(pen, left + 0.65f * s, y - 1.4f * s, left + 0.65f * s, y + 1.3f * s);
            g.DrawLine(thick, left, y - 0.3f * s, left + 0.95f * s, y - 0.6f * s);
            g.DrawLine(thick, left, y + 0.6f * s, left + 0.95f * s, y + 0.3f * s);
        }
        else
        {
            // Natural: a box with a stem up on the left and down on the right.
            float left = right - 0.75f * s;
            g.DrawLine(pen, left, y - 1.4f * s, left, y + 0.45f * s);
            g.DrawLine(pen, left + 0.65f * s, y - 0.45f * s, left + 0.65f * s, y + 1.4f * s);
            g.DrawLine(thick, left, y - 0.25f * s, left + 0.65f * s, y - 0.5f * s);
            g.DrawLine(thick, left, y + 0.5f * s, left + 0.65f * s, y + 0.25f * s);
        }
    }
}
