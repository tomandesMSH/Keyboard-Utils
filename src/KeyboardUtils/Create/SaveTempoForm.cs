namespace KeyboardUtils;

/// <summary>
/// Saving a recording (or converted audio) as MusicXML: it has no tempo of its own, and MusicXML snaps notes to
/// 16ths at one - so ask for roughly the tempo it was played in.
/// </summary>
sealed class SaveTempoForm : AntdUI.Window
{
    readonly AntdUI.InputNumber tempoBox = new() { Size = new Size(110, 36), Minimum = 30, Maximum = 240, Margin = new Padding(6, 0, 0, 0) };

    public int Bpm => (int)tempoBox.Value;

    public SaveTempoForm(int bpm)
    {
        Text = T("Tempo zápisu", "Tempo to write in");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(480, 250);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        tempoBox.Value = Math.Clamp(bpm, 30, 240);
        var tempoRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(20, 16, 20, 0) };
        tempoRow.Controls.Add(new AntdUI.Label { Text = T("Tempo zápisu (BPM):", "Tempo to write in (BPM):"), AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        tempoRow.Controls.Add(tempoBox);

        var help = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 8, 20, 0),
            ForeColor = SystemColors.GrayText,
            Text = T("MusicXML (pro MuseScore) zarovná noty na šestnáctiny v tomhle tempu - nastav ho zhruba na tempo, "
                     + "ve kterém jsi hrál, jinak budou délky not nesmyslné.",
                "MusicXML (for MuseScore) snaps notes to 16ths at this tempo - set it to roughly the tempo you played in, "
                     + "or the note lengths won't make sense."),
        };

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var ok = new AntdUI.Button { Text = T("Uložit", "Save"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(help);
        Controls.Add(tempoRow);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}
