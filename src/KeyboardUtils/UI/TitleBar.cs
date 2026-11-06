namespace KeyboardUtils;

/// <summary>
/// AntdUI.Window is deliberately chromeless (no native caption, no minimize/maximize/close) - that's on
/// purpose so apps can draw their own. This builds that bar: a draggable header with the window's icon/title
/// and whichever of minimize/maximize/close buttons the window wants. Add it to a window LAST (after every
/// other Dock=Top control) and nothing else - the control added last to a Dock=Top sibling group ends up
/// closest to the edge (outermost/topmost). Do NOT call BringToFront() afterwards: that resets z-index to 0,
/// which for Dock layout means innermost, undoing the correct order.
/// </summary>
sealed class TitleBar : Panel
{
    static Color HoverBack => Color.FromArgb(235, 235, 235);
    static Color PressBack => Color.FromArgb(220, 220, 220);
    static Color CloseHoverBack => Color.FromArgb(232, 17, 35);
    static Color ClosePressBack => Color.FromArgb(200, 15, 30);

    public TitleBar(AntdUI.Window owner, bool showMinimize = false, bool showMaximize = false)
    {
        Dock = DockStyle.Top;
        Height = 40;
        BackColor = Color.White;

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
            ForeColor = Color.FromArgb(31, 31, 31),
            BackColor = Color.Transparent,
        };
        owner.TextChanged += (_, _) => title.Text = owner.Text;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };

        void ToggleMax() { if (owner.IsMax) owner.MaxRestore(); else owner.Max(); }

        if (showMinimize)
            buttons.Controls.Add(ChromeButton("-", HoverBack, PressBack, (_, _) => owner.Min()));
        if (showMaximize)
            buttons.Controls.Add(ChromeButton("□", HoverBack, PressBack, (_, _) => ToggleMax()));
        buttons.Controls.Add(ChromeButton("×", CloseHoverBack, ClosePressBack, (_, _) => owner.Close(), hoverForeWhite: true));

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
    }

    static Button ChromeButton(string text, Color hoverBack, Color pressBack, EventHandler onClick, bool hoverForeWhite = false)
    {
        var fore = Color.FromArgb(70, 70, 70);
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(44, 40),
            Font = new Font("Segoe UI", 10f),
            ForeColor = fore,
            BackColor = Color.White,
            TabStop = false,
            Margin = new Padding(0),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hoverBack;
        button.FlatAppearance.MouseDownBackColor = pressBack;
        if (hoverForeWhite)
        {
            button.MouseEnter += (_, _) => button.ForeColor = Color.White;
            button.MouseLeave += (_, _) => button.ForeColor = fore;
        }
        button.Click += onClick;
        return button;
    }
}
