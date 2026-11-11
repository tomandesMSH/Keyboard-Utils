namespace KeyboardUtils;

/// <summary>Precise fallback for note editing (pitch, start, length) for anyone who doesn't want to drag pixels.</summary>
sealed class EditNoteForm : AntdUI.Window
{
    readonly AntdUI.InputNumber pitchBox = new() { Size = new Size(110, 36), Minimum = 0, Maximum = 127, Margin = new Padding(6, 0, 0, 0) };
    readonly AntdUI.Label pitchNameLabel = new() { AutoSize = true, Margin = new Padding(8, 8, 0, 0), ForeColor = SystemColors.GrayText };
    readonly AntdUI.InputNumber startMsBox = new() { Size = new Size(110, 36), Minimum = 0, Maximum = 600_000, Margin = new Padding(6, 0, 0, 0) };
    readonly AntdUI.InputNumber durationMsBox = new() { Size = new Size(110, 36), Minimum = 20, Maximum = 600_000, Margin = new Padding(6, 0, 0, 0) };

    public ScoreNote Result => new()
    {
        Pitch = (int)pitchBox.Value,
        StartSeconds = (double)startMsBox.Value / 1000,
        DurationSeconds = (double)durationMsBox.Value / 1000,
        Velocity = originalVelocity,
    };

    readonly int originalVelocity;

    public EditNoteForm(ScoreNote note)
    {
        originalVelocity = note.Velocity;
        Text = T("Upravit notu", "Edit note");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(420, 320);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        pitchBox.Value = note.Pitch;
        startMsBox.Value = (int)Math.Round(note.StartSeconds * 1000);
        durationMsBox.Value = (int)Math.Round(note.DurationSeconds * 1000);

        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 16, 20, 0) };
        fields.Controls.Add(Row(T("Tón (0-127):", "Pitch (0-127):"), pitchBox, pitchNameLabel));
        fields.Controls.Add(Row(T("Začátek (ms):", "Start (ms):"), startMsBox, null));
        fields.Controls.Add(Row(T("Délka (ms):", "Length (ms):"), durationMsBox, null));

        pitchNameLabel.Text = NoteNames.Describe((int)pitchBox.Value);
        pitchBox.ValueChanged += (_, _) => pitchNameLabel.Text = NoteNames.Describe((int)pitchBox.Value);

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var ok = new AntdUI.Button { Text = T("OK", "OK"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(fields);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    static FlowLayoutPanel Row(string label, Control input, Control? extra)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        row.Controls.Add(new AntdUI.Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        row.Controls.Add(input);
        if (extra != null)
            row.Controls.Add(extra);
        return row;
    }
}
