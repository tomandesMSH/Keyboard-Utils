using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>A tab of <see cref="BrowserTabStrip"/>: a title, and whether it still needs downloading (shown with ⤓).</summary>
sealed record StripTab(string Id, string Title, bool NeedsDownload);

/// <summary>
/// Browser-like tabs: rounded tabs that join the content below when selected, a × to close each (not the last one),
/// drag a tab sideways to move it, and a + at the end. Raises events; the owner keeps the list.
/// </summary>
sealed class BrowserTabStrip : Control, IThemed
{
    const int TabMinWidth = 90, TabMaxWidth = 190, CloseSize = 16, PlusWidth = 34;

    List<StripTab> tabs = [];
    string? selectedId;
    int hover = -1;
    bool hoverClose, hoverPlus;

    // Dragging a tab: which, from where, and its current x offset.
    int dragIndex = -1;
    int pressX;
    int dragOffset;
    bool dragging;

    /// <summary>A tab was clicked (its id).</summary>
    public event Action<string>? TabSelected;

    /// <summary>A tab's × was clicked (its id).</summary>
    public event Action<string>? TabClosed;

    /// <summary>The tabs were rearranged by dragging: the ids in their new order.</summary>
    public event Action<IReadOnlyList<string>>? TabsReordered;

    /// <summary>The + was clicked; the point to open a menu at (screen coordinates).</summary>
    public event Action<Point>? AddClicked;

    public BrowserTabStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 38;
        Font = new Font("Segoe UI", 9.5f);
        AppTheme.Changed += Invalidate;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= Invalidate;
        base.Dispose(disposing);
    }

    public void SetTabs(IReadOnlyList<StripTab> newTabs, string? selected)
    {
        tabs = newTabs.ToList();
        selectedId = selected;
        hover = -1;
        Invalidate();
    }

    public IReadOnlyList<StripTab> Tabs => tabs;

    // --- Geometry -------------------------------------------------------------------------------------

    int TabWidth
    {
        get
        {
            if (tabs.Count == 0)
                return TabMaxWidth;
            int room = Width - 8 - LogicalToDeviceUnits(PlusWidth) - 8;
            return Math.Clamp(room / tabs.Count, LogicalToDeviceUnits(TabMinWidth), LogicalToDeviceUnits(TabMaxWidth));
        }
    }

    Rectangle TabRect(int index)
    {
        int w = TabWidth;
        int x = 8 + index * w;
        if (dragging && index == dragIndex)
            x += dragOffset;
        return new Rectangle(x, 6, w, Height - 6);
    }

    Rectangle CloseRect(int index)
    {
        var tab = TabRect(index);
        int size = LogicalToDeviceUnits(CloseSize);
        return new Rectangle(tab.Right - size - 8, tab.Top + (tab.Height - size) / 2, size, size);
    }

    Rectangle PlusRect => new(8 + tabs.Count * TabWidth + 4, 8, LogicalToDeviceUnits(PlusWidth) - 6, Height - 14);

    int TabAt(Point p)
    {
        for (int i = 0; i < tabs.Count; i++)
            if (TabRect(i).Contains(p))
                return i;
        return -1;
    }

    /// <summary>The × only where it can be used: not when this is the only tab.</summary>
    bool Closable => tabs.Count > 1;

    // --- Mouse ----------------------------------------------------------------------------------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragIndex >= 0 && e.Button == MouseButtons.Left)
        {
            if (!dragging && Math.Abs(e.X - pressX) > 6)
                dragging = true;
            if (dragging)
            {
                dragOffset = e.X - pressX;
                // Past the middle of a neighbour: swap with it.
                int w = TabWidth;
                if (dragOffset > w / 2 && dragIndex < tabs.Count - 1)
                    Swap(dragIndex, dragIndex + 1, -w);
                else if (dragOffset < -w / 2 && dragIndex > 0)
                    Swap(dragIndex, dragIndex - 1, w);
                Invalidate();
            }
            return;
        }
        int at = TabAt(e.Location);
        bool overClose = at >= 0 && Closable && CloseRect(at).Contains(e.Location);
        bool overPlus = PlusRect.Contains(e.Location);
        if (at != hover || overClose != hoverClose || overPlus != hoverPlus)
        {
            hover = at;
            hoverClose = overClose;
            hoverPlus = overPlus;
            Invalidate();
        }
    }

    void Swap(int from, int to, int offsetShift)
    {
        (tabs[from], tabs[to]) = (tabs[to], tabs[from]);
        dragIndex = to;
        pressX += -offsetShift;
        dragOffset += offsetShift;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        hoverClose = hoverPlus = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Middle && TabAt(e.Location) is var middle and >= 0 && Closable)
        {
            TabClosed?.Invoke(tabs[middle].Id);
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;
        if (PlusRect.Contains(e.Location))
        {
            AddClicked?.Invoke(PointToScreen(new Point(PlusRect.Left, PlusRect.Bottom)));
            return;
        }
        int at = TabAt(e.Location);
        if (at < 0)
            return;
        if (Closable && CloseRect(at).Contains(e.Location))
        {
            TabClosed?.Invoke(tabs[at].Id);
            return;
        }
        dragIndex = at;
        pressX = e.X;
        dragOffset = 0;
        dragging = false;
        Capture = true;
        // A tab still to download asks first - only once it's clicked, not when it's just being dragged.
        if (!tabs[at].NeedsDownload)
            TabSelected?.Invoke(tabs[at].Id);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int released = dragIndex;
        bool moved = dragging;
        EndDrag();
        if (released >= 0 && moved)
            TabsReordered?.Invoke(tabs.Select(t => t.Id).ToList());
        else if (released >= 0 && released < tabs.Count && tabs[released].NeedsDownload && TabRect(released).Contains(e.Location))
            TabSelected?.Invoke(tabs[released].Id);
    }

    /// <summary>A dialog took the mouse mid-click: the drag is over.</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && dragIndex >= 0)
        {
            if (dragging)
                TabsReordered?.Invoke(tabs.Select(t => t.Id).ToList());
            EndDrag();
        }
    }

    void EndDrag()
    {
        dragIndex = -1;
        dragging = false;
        dragOffset = 0;
        if (Capture)
            Capture = false;
        Invalidate();
    }

    // --- Drawing --------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var strip = AppTheme.Dark ? Color.FromArgb(20, 20, 23) : Color.FromArgb(226, 228, 232);
        g.Clear(strip);
        var selectedFill = AppTheme.Window;
        var hoverFill = AppTheme.Dark ? Color.FromArgb(42, 42, 48) : Color.FromArgb(238, 239, 242);
        using var text = new SolidBrush(AppTheme.Text);
        using var subtle = new SolidBrush(AppTheme.SubtleText);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        // The dragged tab is drawn last, on top.
        var order = Enumerable.Range(0, tabs.Count).OrderBy(i => i == dragIndex && dragging ? 1 : 0).ThenBy(i => tabs[i].Id == selectedId ? 1 : 0);
        foreach (int i in order)
        {
            var tab = tabs[i];
            var r = TabRect(i);
            bool selected = tab.Id == selectedId;
            if (selected || i == hover || (dragging && i == dragIndex))
            {
                using var path = TabShape(r, selected);
                using var fill = new SolidBrush(selected ? selectedFill : hoverFill);
                g.FillPath(fill, path);
            }
            else if (i + 1 < tabs.Count && tabs[i + 1].Id != selectedId && i + 1 != hover)
            {
                // A thin divider between two plain tabs, as browsers do.
                using var divider = new Pen(AppTheme.Dark ? Color.FromArgb(60, 60, 66) : Color.FromArgb(190, 192, 198));
                g.DrawLine(divider, r.Right - 1, r.Top + 8, r.Right - 1, r.Bottom - 8);
            }
            int closeRoom = Closable ? LogicalToDeviceUnits(CloseSize) + 12 : 6;
            var textRect = new RectangleF(r.Left + 12, r.Top, r.Width - 12 - closeRoom, r.Height);
            string title = tab.NeedsDownload ? "⤓ " + tab.Title : tab.Title;
            using var font = new Font(Font, selected ? FontStyle.Bold : FontStyle.Regular);
            g.DrawString(title, font, tab.NeedsDownload ? subtle : text, textRect, format);
            if (Closable && (selected || i == hover))
            {
                var c = CloseRect(i);
                if (hoverClose && i == hover)
                {
                    using var circle = new SolidBrush(AppTheme.Dark ? Color.FromArgb(70, 70, 78) : Color.FromArgb(214, 216, 222));
                    g.FillEllipse(circle, c);
                }
                using var pen = new Pen(AppTheme.SubtleText, 1.4f);
                int inset = c.Width / 4 + 1;
                g.DrawLine(pen, c.Left + inset, c.Top + inset, c.Right - inset, c.Bottom - inset);
                g.DrawLine(pen, c.Right - inset, c.Top + inset, c.Left + inset, c.Bottom - inset);
            }
        }

        var plus = PlusRect;
        if (hoverPlus)
        {
            using var fill = new SolidBrush(hoverFill);
            using var round = Rounded(plus, 6);
            g.FillPath(fill, round);
        }
        using var plusPen = new Pen(AppTheme.Text, 1.6f);
        int cx = plus.Left + plus.Width / 2, cy = plus.Top + plus.Height / 2, arm = LogicalToDeviceUnits(6);
        g.DrawLine(plusPen, cx - arm, cy, cx + arm, cy);
        g.DrawLine(plusPen, cx, cy - arm, cx, cy + arm);
    }

    /// <summary>A tab: rounded top corners; a selected one flares out at the bottom into the content.</summary>
    static GraphicsPath TabShape(Rectangle r, bool selected)
    {
        const int radius = 8;
        var path = new GraphicsPath();
        int bottom = r.Bottom + (selected ? 1 : -3);
        if (selected)
        {
            path.AddArc(r.Left - radius, bottom - radius, radius, radius, 90, -90);
        }
        path.AddLine(r.Left, bottom - (selected ? radius / 2 : 0), r.Left, r.Top + radius);
        path.AddArc(r.Left, r.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius * 2, r.Top, radius * 2, radius * 2, 270, 90);
        path.AddLine(r.Right, r.Top + radius, r.Right, bottom - (selected ? radius / 2 : 0));
        if (selected)
            path.AddArc(r.Right, bottom - radius, radius, radius, 180, -90);
        path.CloseFigure();
        return path;
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
