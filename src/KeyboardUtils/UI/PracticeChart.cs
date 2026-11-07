namespace KeyboardUtils;

/// <summary>
/// Bars of a song's learning results (%), oldest on the left, coloured like the result's big number: green from
/// 90 %, yellow from 60 %, red below. The last one can be highlighted (the attempt just played).
/// </summary>
sealed class PracticeChart : Control, IThemed
{
    IReadOnlyList<PracticeEntry> entries = [];

    public PracticeChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AppTheme.Changed += Invalidate;
    }

    /// <summary>How many of the latest attempts are shown.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MaxBars { get; set; } = 20;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool HighlightLast { get; set; }

    public void SetEntries(IReadOnlyList<PracticeEntry> all)
    {
        entries = all.Skip(Math.Max(0, all.Count - MaxBars)).ToList();
        Invalidate();
    }

    public static Color ColorFor(double percent) =>
        percent >= 90 ? PianoRollControl.CorrectColor
        : percent >= 60 ? Color.FromArgb(244, 180, 0)
        : PianoRollControl.WrongColor;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // The window's own background (the dialogs aren't white in the light theme).
        g.Clear(Parent?.BackColor ?? AppTheme.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 8f);
        float labelWidth = TextRenderer.MeasureText("100 %", font).Width + 4;
        var plot = new RectangleF(labelWidth, 6, Width - labelWidth - 6, Height - 12);
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        // Guide lines at 60 and 90 % (where the colours change) and 100 % - that one unlabelled, it'd sit on the 90.
        using var guide = new Pen(Color.FromArgb(70, AppTheme.SubtleText)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        foreach (int level in (int[])[60, 90, 100])
        {
            float y = plot.Bottom - plot.Height * level / 100f;
            g.DrawLine(guide, plot.Left, y, plot.Right, y);
            if (level < 100)
                TextRenderer.DrawText(g, $"{level} %", font, new Rectangle(0, (int)y - 8, (int)labelWidth - 2, 16), AppTheme.SubtleText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        if (entries.Count == 0)
        {
            TextRenderer.DrawText(g, T("Zatím žádné pokusy", "No attempts yet"), Font, Rectangle.Round(plot), AppTheme.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Bars at most 26 px wide, left-aligned, so a few attempts don't turn into huge blocks.
        float slot = Math.Min(plot.Width / MaxBars, 26);
        float barWidth = Math.Max(3, slot * 0.7f);
        for (int i = 0; i < entries.Count; i++)
        {
            double percent = Math.Clamp(entries[i].Percent, 0, 100);
            float height = Math.Max(2, (float)(plot.Height * percent / 100));
            var bar = new RectangleF(plot.Left + i * slot + (slot - barWidth) / 2, plot.Bottom - height, barWidth, height);
            bool last = HighlightLast && i == entries.Count - 1;
            using var brush = new SolidBrush(Color.FromArgb(last ? 255 : 170, ColorFor(percent)));
            g.FillRectangle(brush, bar);
            if (last)
            {
                using var outline = new Pen(AppTheme.Text, 1.5f);
                g.DrawRectangle(outline, bar.X, bar.Y, bar.Width, bar.Height);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}
