namespace KeyboardUtils;

/// <summary>
/// Cvičení > Knihovna akordů: the chords a guitarist learns first, as songbook boxes. A click plays the chord (and,
/// with the guitar chosen, presses it on the fingerboard and strums it there). Not modal: it can stay open by the song.
/// </summary>
sealed class ChordLibraryForm : AntdUI.Window
{
    public ChordLibraryForm(Action<GuitarChord> play)
    {
        Text = T("Knihovna akordů", "Chord library");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(900, 680);

        var hint = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(18, 0, 18, 0),
            ForeColor = SystemColors.GrayText,
            Text = T("Klikni na akord: zazní a ukáže se na hmatníku. Čísla = prsty levé ruky, × = struna nehraje, ○ = prázdná struna.",
                     "Click a chord to hear it and see it on the fingerboard. Numbers = left-hand fingers, × = not played, ○ = open string."),
        };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 6, 14, 6) };
        foreach (var chord in GuitarChords.All)
        {
            var box = new ChordDiagramControl { Chord = chord, Margin = new Padding(6) };
            box.Click += (_, _) => play(chord);
            grid.Controls.Add(box);
        }
        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.Add(close);

        Controls.Add(grid);
        Controls.Add(hint);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        CancelButton = close;
    }
}

/// <summary>A chord symbol clicked in the sheet music: its box, popped up by the mouse; gone when clicked elsewhere.</summary>
sealed class ChordPopup : Form
{
    public ChordPopup(GuitarChord? chord, string name, Point at)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = AppTheme.Paper;
        Padding = new Padding(1);
        if (chord != null)
        {
            ClientSize = new Size(146, 192);
            Controls.Add(new ChordDiagramControl { Chord = chord, Dock = DockStyle.Fill });
        }
        else
        {
            ClientSize = new Size(220, 48);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = AppTheme.Text,
                Text = T($"{name}: hmat v knihovně akordů není.", $"{name}: no shape in the chord library."),
            });
        }
        var screen = Screen.FromPoint(at).WorkingArea;
        Location = new Point(Math.Min(at.X, screen.Right - Width), Math.Min(at.Y + 4, screen.Bottom - Height));
        Deactivate += (_, _) => Close();
        Click += (_, _) => Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppTheme.SubtleText);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
