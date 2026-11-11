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
