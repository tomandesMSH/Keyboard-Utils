using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// The tuner's display: the note heard, a needle from -50 to +50 cents (green when in tune), the frequency, and for
/// string instruments a row of the open strings with the one being tuned lit. Clicking a string raises
/// <see cref="StringClicked"/> (the window plays it).
/// </summary>
sealed class TunerControl : Control, IThemed
{
    static Color InTune => Color.FromArgb(22, 163, 74);
    static Color OffTune => Color.FromArgb(234, 88, 12);

    double? hz;
    double a4 = 440;
    Instrument instrument = Instrument.Piano;
    readonly List<RectangleF> stringBoxes = [];

    public event Action<int>? StringClicked;

    public TunerControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AppTheme.Changed += Invalidate;
        Cursor = Cursors.Default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= Invalidate;
        base.Dispose(disposing);
    }

    /// <summary>The sound heard now (Hz), or null for silence.</summary>
    public void Show(double? frequency, double reference, Instrument forInstrument)
    {
        hz = frequency;
        a4 = reference;
        instrument = forInstrument;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = stringBoxes.Any(b => b.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = stringBoxes.FindIndex(b => b.Contains(e.Location));
        if (index >= 0)
            StringClicked?.Invoke(instrument.Strings[index]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(AppTheme.Paper);
        float u = Math.Min(Width / 40f, Height / 30f);
        if (u < 2)
            return;

        using var ink = new SolidBrush(AppTheme.Ink);
        using var subtle = new SolidBrush(AppTheme.SubtleText);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        (int Note, double Cents)? heard = hz is { } f ? Tuning.Nearest(f, a4) : null;
        int? stringIndex = hz is { } f2 ? Tuning.NearestString(f2, instrument.Strings, a4) : null;
        // Tuning a string: cents against that string (it may be far off yet), otherwise against the nearest note.
        int? target = stringIndex is { } si ? instrument.Strings[si] : heard?.Note;
        double? cents = hz is { } f3 && target is { } t ? Tuning.CentsFrom(f3, t, a4) : null;
        bool inTune = cents is { } c0 && Math.Abs(c0) <= Tuning.InTuneCents;
        var accent = cents == null ? AppTheme.SubtleText : inTune ? InTune : OffTune;

        // The note's name, big.
        float y = 1.2f * u;
        using (var big = new Font("Segoe UI Semibold", 6.5f * u, GraphicsUnit.Pixel))
        using (var noteBrush = new SolidBrush(accent))
            g.DrawString(target is { } shown ? Tuning.Name(shown) : "–", big, noteBrush, new RectangleF(0, y, Width, 8 * u), center);
        y += 8.4f * u;
        using (var small = new Font("Segoe UI", 1.5f * u, GraphicsUnit.Pixel))
        {
            string detail = hz is { } f4
                ? $"{f4:0.0} Hz" + (cents is { } c1 ? $"  ·  {(c1 >= 0 ? "+" : "−")}{Math.Abs(c1):0} " + T("centů", "cents") : "")
                : T("Zahraj jeden tón…", "Play one note…");
            if (target is { } t2 && instrument.Transpose != 0 && instrument.Transpose % 12 != 0)
                detail += "  ·  " + T("psaná ", "written ") + Tuning.Name(instrument.Written(t2));
            g.DrawString(detail, small, subtle, new RectangleF(0, y, Width, 2.4f * u), center);
        }
        y += 3.4f * u;

        // The scale and the needle: -50 ... +50 cents across.
        float scaleLeft = Width / 2f - 16 * u, scaleRight = Width / 2f + 16 * u, scaleY = y + 5 * u;
        using var tickPen = new Pen(AppTheme.StaffLine, Math.Max(1, 0.15f * u));
        using var zeroPen = new Pen(AppTheme.Ink, Math.Max(1.5f, 0.25f * u));
        using (var band = new SolidBrush(Color.FromArgb(AppTheme.Dark ? 50 : 40, InTune)))
        {
            float bandHalf = (float)(Tuning.InTuneCents / 50 * 16 * u);
            g.FillRectangle(band, Width / 2f - bandHalf, scaleY - 4 * u, 2 * bandHalf, 5 * u);
        }
        using (var labels = new Font("Segoe UI", 1.1f * u, GraphicsUnit.Pixel))
            for (int c = -50; c <= 50; c += 10)
            {
                float x = Width / 2f + c / 50f * 16 * u;
                float len = c == 0 ? 4.5f * u : c % 50 == 0 ? 3 * u : 1.6f * u;
                g.DrawLine(c == 0 ? zeroPen : tickPen, x, scaleY - len, x, scaleY);
                if (c % 50 == 0 && c != 0)
                    g.DrawString(c > 0 ? "+50" : "−50", labels, subtle, x, scaleY + 1.3f * u, center);
            }
        g.DrawLine(tickPen, scaleLeft, scaleY, scaleRight, scaleY);
        if (cents is { } c2)
        {
            float x = Width / 2f + (float)(Math.Clamp(c2, -50, 50) / 50 * 16 * u);
            using var needle = new Pen(accent, Math.Max(2, 0.45f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(needle, x, scaleY - 5.5f * u, x, scaleY + 0.5f * u);
            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, x - 0.8f * u, scaleY - 0.8f * u, 1.6f * u, 1.6f * u);
        }
        using (var hintFont = new Font("Segoe UI", 1.3f * u, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var hintBrush = new SolidBrush(accent))
        {
            string hint = cents is not { } c3 ? "" : inTune ? T("Čistě ✓", "In tune ✓")
                : c3 < 0 ? T("Moc nízko - přitáhni", "Too low - tighten") : T("Moc vysoko - povol", "Too high - loosen");
            if (instrument.Strings.Length == 0 && cents is { } c4 && !inTune)
                hint = c4 < 0 ? T("Moc nízko", "Too low") : T("Moc vysoko", "Too high");
            g.DrawString(hint, hintFont, hintBrush, new RectangleF(0, scaleY + 3 * u, Width, 2.4f * u), center);
        }

        // The open strings, low to high.
        stringBoxes.Clear();
        if (instrument.Strings.Length == 0)
            return;
        float boxW = 4.6f * u, gap = 1.2f * u, rowY = scaleY + 6.6f * u;
        float total = instrument.Strings.Length * boxW + (instrument.Strings.Length - 1) * gap;
        float bx = Width / 2f - total / 2;
        using var boxFont = new Font("Segoe UI Semibold", 1.5f * u, GraphicsUnit.Pixel);
        for (int i = 0; i < instrument.Strings.Length; i++)
        {
            var box = new RectangleF(bx + i * (boxW + gap), rowY, boxW, 3.2f * u);
            stringBoxes.Add(box);
            bool lit = stringIndex == i;
            using var path = new GraphicsPath();
            float r = 0.8f * u;
            path.AddArc(box.X, box.Y, 2 * r, 2 * r, 180, 90);
            path.AddArc(box.Right - 2 * r, box.Y, 2 * r, 2 * r, 270, 90);
            path.AddArc(box.Right - 2 * r, box.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
            path.AddArc(box.X, box.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(lit ? accent : Color.FromArgb(AppTheme.Dark ? 40 : 18, AppTheme.Ink));
            g.FillPath(fill, path);
            using var text = new SolidBrush(lit ? Color.White : AppTheme.Ink);
            g.DrawString(Tuning.Name(instrument.Strings[i]), boxFont, text, box, center);
        }
    }
}
