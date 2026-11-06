namespace KeyboardUtils;

/// <summary>Picks a MIDI note by clicking it on the same piano control used for playback visualization.</summary>
sealed class NotePickerForm : AntdUI.Window
{
    readonly PianoRollControl piano = new() { Dock = DockStyle.Fill, FillKeyboard = true, Clickable = true };
    readonly AntdUI.Button ok = new() { Text = "OK", AutoSize = true, Enabled = false, Type = AntdUI.TTypeMini.Primary, Radius = 8 };

    public int? Result { get; private set; }

    public NotePickerForm(int? current = null)
    {
        Text = T("Vyber klávesu k přehrání", "Choose a note to play");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 360);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancel, ok]);

        piano.SelectedKey = current;
        piano.SetScore(null);

        Controls.Add(piano);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        piano.KeySelected += key =>
        {
            Result = key;
            piano.SelectedKey = key;
            piano.Invalidate();
            ok.Enabled = true;
        };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}
