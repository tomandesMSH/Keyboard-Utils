namespace KeyboardUtils;

/// <summary>Picks what a macro keyboard key should do. The actions are in groups, each under a heading.</summary>
sealed class ActionPickerForm : AntdUI.Window
{
    sealed record Choice(string Text, MacroAction Action, int? PartIndex = null, string? PartName = null)
    {
        public override string ToString() => Text;
    }

    /// <summary>A group's heading in the list: not selectable.</summary>
    sealed record Heading(string Text)
    {
        public override string ToString() => Text;
    }

    static (string Heading, MacroAction[] Actions)[] Groups =>
    [
        (T("Přehrávání", "Playback"), [MacroAction.PlayPause, MacroAction.Stop, MacroAction.SeekBack, MacroAction.SeekForward,
            MacroAction.SeekBackFine, MacroAction.SeekForwardFine, MacroAction.ToggleMetronome, MacroAction.VolumeDown, MacroAction.VolumeUp]),
        (T("Smyčka a rychlost", "Loop and speed"), [MacroAction.ToggleLoop, MacroAction.LoopStart, MacroAction.LoopEnd,
            MacroAction.SpeedDown, MacroAction.SpeedUp, MacroAction.SpeedReset]),
        (T("Skladby", "Songs"), [MacroAction.NextTrack, MacroAction.PreviousTrack, MacroAction.SongsFromFolder,
            MacroAction.RecentSongs, MacroAction.OpenSong]),
        (T("Učení a nahrávání", "Learning and recording"), [MacroAction.ToggleLearningMode, MacroAction.LearningModeNext,
            MacroAction.LearningPartsNext, MacroAction.LearningSpeedNext, MacroAction.ShowPracticeHistory, MacroAction.ToggleCreateMode, MacroAction.SaveRecording,
            MacroAction.ToggleListening, MacroAction.NoteInputNext]),
        (T("Zobrazení", "View"), [MacroAction.SwitchView, MacroAction.ZoomIn, MacroAction.ZoomOut, MacroAction.KeyboardLarger,
            MacroAction.KeyboardSmaller, MacroAction.ToggleKeyLabels, MacroAction.ToggleDarkMode, MacroAction.ShowWindow,
            MacroAction.MinimizeWindow]),
        (T("Dialogy a okna (jako klávesy počítače)", "Dialogs and windows (like computer keys)"), [MacroAction.KeyEnter,
            MacroAction.KeyEscape, MacroAction.KeyTab, MacroAction.KeyShiftTab, MacroAction.KeyUp, MacroAction.KeyDown,
            MacroAction.KeyLeft, MacroAction.KeyRight, MacroAction.KeySpace]),
        (T("Nota", "Note"), [MacroAction.PlayNote]),
    ];

    readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    readonly AntdUI.Checkbox pairBox = new() { Dock = DockStyle.Bottom, Height = 34, Checked = true };
    int lastIndex;

    public Binding? Result { get; private set; }

    /// <summary>Also give the opposite direction (<see cref="MacroActions.Opposite"/>) to the next key pressed.</summary>
    public bool PairOpposite { get; private set; }

    public ActionPickerForm(string keyName, IReadOnlyList<string>? currentSongParts = null)
    {
        Text = T($"Klávesa {keyName} - vyber akci", $"Key {keyName} - choose an action");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(500, 640);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var ok = new AntdUI.Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(90, 36), Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Size = new Size(90, 36), Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.AddRange([cancel, ok]);
        pairBox.Text = T("Kolečko: otočením na druhou stranu nastavit opačný směr", "Knob: turn it the other way to set the opposite direction");
        pairBox.Padding = new Padding(8, 0, 0, 0);

        Controls.Add(list);
        Controls.Add(pairBox);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(new TitleBar(this));

        list.ItemHeight = LogicalToDeviceUnits(24);
        foreach (var (heading, actions) in Groups)
        {
            list.Items.Add(new Heading(heading));
            foreach (var action in actions)
                list.Items.Add(new Choice(new Binding { Action = action }.Describe(), action));
        }
        // One mute and two volume steps per part of the open song.
        if (currentSongParts is { Count: > 0 })
        {
            list.Items.Add(new Heading(T("Části skladby", "Parts of the song")));
            for (int i = 0; i < currentSongParts.Count; i++)
                foreach (var action in (MacroAction[])[MacroAction.ToggleMutePart, MacroAction.PartVolumeDown, MacroAction.PartVolumeUp])
                    list.Items.Add(new Choice(new Binding { Action = action, PartName = currentSongParts[i] }.Describe(),
                        action, i, currentSongParts[i]));
        }
        list.DrawItem += DrawItem;
        list.SelectedIndexChanged += (_, _) => SkipHeading();
        list.SelectedIndex = 1;

        list.DoubleClick += (_, _) => { if (list.SelectedItem is Choice) { DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
                return;
            if (list.SelectedItem is not Choice choice)
            {
                e.Cancel = true;
                return;
            }
            Result = new Binding { Action = choice.Action, PartIndex = choice.PartIndex, PartName = choice.PartName };
            PairOpposite = pairBox.Visible && pairBox.Checked;
        };

        Shown += (_, _) => list.Focus();
    }

    /// <summary>Headings can't be picked: moving onto one goes on past it, in the direction of the move.</summary>
    void SkipHeading()
    {
        int index = list.SelectedIndex;
        if (index >= 0 && list.Items[index] is Heading)
        {
            int step = index >= lastIndex ? 1 : -1;
            int next = index + step;
            if (next < 0 || next >= list.Items.Count)
                next = index - step;
            list.SelectedIndex = next; // raises the event again, now on a choice
            return;
        }
        lastIndex = index;
        pairBox.Visible = list.SelectedItem is Choice choice && MacroActions.Opposite(choice.Action) != null;
    }

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;
        var item = list.Items[e.Index];
        var bounds = e.Bounds;
        if (item is Heading)
        {
            using var back = new SolidBrush(list.BackColor);
            e.Graphics.FillRectangle(back, bounds);
            using var font = new Font(list.Font, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, item.ToString(), font, new Rectangle(bounds.X + 4, bounds.Y, bounds.Width - 4, bounds.Height),
                AppTheme.SubtleText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            return;
        }
        e.DrawBackground();
        TextRenderer.DrawText(e.Graphics, item.ToString(), list.Font, new Rectangle(bounds.X + 18, bounds.Y, bounds.Width - 18, bounds.Height),
            (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : list.ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
