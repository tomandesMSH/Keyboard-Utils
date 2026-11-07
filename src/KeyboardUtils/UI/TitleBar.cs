namespace KeyboardUtils;

/// <summary>
/// AntdUI.Window is deliberately chromeless (no native caption, no minimize/maximize/close) - that's on
/// purpose so apps can draw their own. This builds that bar: a draggable header with the window's icon/title
/// and whichever of minimize/maximize/close buttons the window wants. Add it to a window LAST (after every
/// other Dock=Top control) and nothing else - the control added last to a Dock=Top sibling group ends up
/// closest to the edge (outermost/topmost). Do NOT call BringToFront() afterwards: that resets z-index to 0,
/// which for Dock layout means innermost, undoing the correct order.
/// </summary>
sealed class TitleBar : Panel, IThemed
{
    static Color HoverBack => AppTheme.Hover;
    static Color PressBack => AppTheme.Pressed;
    static Color ButtonFore => AppTheme.Dark ? Color.FromArgb(200, 200, 205) : Color.FromArgb(70, 70, 70);
    static Color CloseHoverBack => Color.FromArgb(232, 17, 35);
    static Color ClosePressBack => Color.FromArgb(200, 15, 30);

    public TitleBar(AntdUI.Window owner, bool showMinimize = false, bool showMaximize = false)
    {
        Dock = DockStyle.Top;
        Height = 40;
        BackColor = AppTheme.Window;

        // The window's icon at 20 px (the .ico has that size, so it isn't scaled).
        var icon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 38,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = owner.Icon != null ? new Icon(owner.Icon, 20, 20).ToBitmap() : null,
            BackColor = Color.Transparent,
        };

        var title = new Label
        {
            Text = owner.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            BackColor = Color.Transparent,
        };
        owner.TextChanged += (_, _) => title.Text = owner.Text;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };

        void ToggleMax() { if (owner.IsMax) owner.MaxRestore(); else owner.Max(); }

        if (showMinimize)
            buttons.Controls.Add(new ChromeButton("-", close: false, (_, _) => owner.Min()));
        if (showMaximize)
            buttons.Controls.Add(new ChromeButton("□", close: false, (_, _) => ToggleMax()));
        buttons.Controls.Add(new ChromeButton("×", close: true, (_, _) => owner.Close()));

        // Fill first: the docked-last controls (icon left, buttons right) are laid out before it.
        Controls.Add(title);
        Controls.Add(icon);
        Controls.Add(buttons);

        // DraggableMouseDown() hands the mouse to Windows' native move loop, which eats the second click of a
        // double-click - MouseDoubleClick would never fire after it. So check e.Clicks here instead, before
        // starting the drag.
        void HandleMouseDown(object? _, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (showMaximize && e.Clicks >= 2)
                ToggleMax();
            else
                owner.DraggableMouseDown();
        }
        MouseDown += HandleMouseDown;
        title.MouseDown += HandleMouseDown;
        icon.MouseDown += HandleMouseDown;

        // Every window has one of these, so it's where windows get themed (and re-themed when the mode changes).
        AppTheme.Attach(owner);
        void ApplyTheme()
        {
            BackColor = AppTheme.Window;
            title.ForeColor = AppTheme.Text;
            foreach (var button in buttons.Controls.OfType<ChromeButton>())
                button.Invalidate();
        }
        AppTheme.Changed += ApplyTheme;
        Disposed += (_, _) => AppTheme.Changed -= ApplyTheme;
    }

    /// <summary>
    /// Minimize / maximize / close. Custom-drawn: a flat WinForms Button gets a light border in dark mode
    /// (WinForms' own dark rendering ignores BorderSize = 0).
    /// </summary>
    sealed class ChromeButton : Control
    {
        readonly bool close;
        bool hovered, pressed;

        public ChromeButton(string text, bool close, EventHandler onClick)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.close = close;
            Text = text;
            Size = new Size(44, 40);
            Font = new Font("Segoe UI", 10f);
            Margin = new Padding(0);
            TabStop = false;
            Click += onClick;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var back = pressed ? (close ? ClosePressBack : PressBack)
                : hovered ? (close ? CloseHoverBack : HoverBack)
                : AppTheme.Window;
            e.Graphics.Clear(back);
            var fore = close && (hovered || pressed) ? Color.White : ButtonFore;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
