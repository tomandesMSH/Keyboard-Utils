namespace KeyboardUtils;

/// <summary>A solid line in <see cref="AppTheme.Separator"/> - the thick rule under the song toolbar.</summary>
sealed class SeparatorLine : Control, IThemed
{
    public SeparatorLine()
    {
        BackColor = AppTheme.Separator;
        TabStop = false;
        AppTheme.Changed += OnThemeChanged;
    }

    void OnThemeChanged() => BackColor = AppTheme.Separator;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

/// <summary>
/// A split container whose divider is drawn in <see cref="AppTheme.Separator"/>. Its two halves hold custom-drawn
/// controls that theme themselves, so the generic recolouring leaves the whole thing alone.
/// </summary>
sealed class ThemedSplitContainer : SplitContainer, IThemed
{
    public ThemedSplitContainer()
    {
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;
    }

    void ApplyTheme()
    {
        BackColor = AppTheme.Separator;
        Panel1.BackColor = Panel2.BackColor = AppTheme.Window;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Up / down arrows for a number box, drawn in the theme's text colour (AntdUI's own are too faint in dark mode).
/// Raises <see cref="Step"/> with +1 or -1 on press, and keeps repeating while held.
/// </summary>
sealed class SpinArrows : Control, IThemed
{
    readonly System.Windows.Forms.Timer repeat = new();
    int hovered, pressed; // +1 = up half, -1 = down half, 0 = none

    public event Action<int>? Step;

    public SpinArrows()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
        repeat.Tick += (_, _) =>
        {
            repeat.Interval = 80;
            if (pressed != 0)
                Step?.Invoke(pressed);
        };
        AppTheme.Changed += Invalidate;
    }

    int HalfAt(int y) => y < Height / 2 ? 1 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (HalfAt(e.Y) != hovered)
        {
            hovered = HalfAt(e.Y);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = pressed = 0;
        repeat.Stop();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled)
            return;
        pressed = HalfAt(e.Y);
        Step?.Invoke(pressed);
        repeat.Interval = 400;
        repeat.Start();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        pressed = 0;
        repeat.Stop();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float w = Width, h = Height, half = h / 2;
        foreach (int dir in new[] { 1, -1 })
        {
            float top = dir == 1 ? 0 : half;
            if (Enabled && (pressed == dir || hovered == dir))
            {
                using var back = new SolidBrush(pressed == dir ? AppTheme.Pressed : AppTheme.Hover);
                g.FillRectangle(back, 0, top, w, half);
            }
            var color = Enabled ? AppTheme.Text : AppTheme.SubtleText;
            using var brush = new SolidBrush(color);
            float cx = w / 2, cy = top + half / 2, size = Math.Min(w, half) * 0.32f;
            PointF[] triangle = dir == 1
                ? [new(cx - size, cy + size * 0.55f), new(cx + size, cy + size * 0.55f), new(cx, cy - size * 0.6f)]
                : [new(cx - size, cy - size * 0.55f), new(cx + size, cy - size * 0.55f), new(cx, cy + size * 0.6f)];
            g.FillPolygon(brush, triangle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppTheme.Changed -= Invalidate;
            repeat.Dispose();
        }
        base.Dispose(disposing);
    }
}
