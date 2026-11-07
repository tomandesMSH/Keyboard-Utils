using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// Flat text button for the top menu bar (Soubor/Úpravy/Nastavení/...) - invisible at rest, rounded highlight
/// on hover/press. Plain WinForms Button + FlatAppearance can't round its hover rectangle, so this is custom-drawn.
/// </summary>
sealed class MenuBarButton : Control, IThemed
{
    const int CornerRadius = 6;

    bool hovered;
    bool pressed;

    public MenuBarButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = AppTheme.Text;
        AppTheme.Changed += OnThemeChanged;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(6, 2, 6, 2);
        Margin = new Padding(1, 0, 1, 0);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    void OnThemeChanged()
    {
        ForeColor = AppTheme.Text;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(textSize.Width + Padding.Horizontal, textSize.Height + Padding.Vertical);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Size = GetPreferredSize(Size.Empty);
        base.OnTextChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (hovered || pressed)
        {
            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
            using var brush = new SolidBrush(pressed ? AppTheme.Pressed : AppTheme.Hover);
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
