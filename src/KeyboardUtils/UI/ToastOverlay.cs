using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// A big message in the middle of the song area for a moment - what a macro key or shortcut just did, readable from
/// the piano. A new message replaces the one showing and restarts its time, so turning a knob just updates it.
/// </summary>
sealed class ToastOverlay : Control, IThemed
{
    readonly System.Windows.Forms.Timer hideTimer = new() { Interval = 1500 };

    public ToastOverlay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        Visible = false;
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Visible = false;
        };
        AppTheme.Changed += Invalidate;
    }

    /// <summary>Shows <paramref name="text"/> centred over the parent.</summary>
    public void ShowMessage(string text)
    {
        if (Parent is not { } parent)
            return;
        Text = text;
        int maxWidth = Math.Max(200, parent.ClientSize.Width - LogicalToDeviceUnits(80));
        var size = TextRenderer.MeasureText(text, Font, new Size(maxWidth, int.MaxValue), TextFormatFlags.WordBreak);
        int padX = LogicalToDeviceUnits(28), padY = LogicalToDeviceUnits(16);
        Size = new Size(Math.Min(maxWidth, size.Width) + 2 * padX, size.Height + 2 * padY);
        Location = new Point((parent.ClientSize.Width - Width) / 2, (parent.ClientSize.Height - Height) / 2);
        // Rounded corners: the parent shows around them.
        using (var path = RoundedRect(new Rectangle(Point.Empty, Size), LogicalToDeviceUnits(14)))
            Region = new Region(path);
        Visible = true;
        BringToFront();
        Invalidate();
        hideTimer.Stop();
        hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.Surface);
        using (var path = RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), LogicalToDeviceUnits(13)))
        using (var pen = new Pen(AppTheme.Separator, LogicalToDeviceUnits(2)))
            g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, AppTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hideTimer.Dispose();
            AppTheme.Changed -= Invalidate;
        }
        base.Dispose(disposing);
    }
}
