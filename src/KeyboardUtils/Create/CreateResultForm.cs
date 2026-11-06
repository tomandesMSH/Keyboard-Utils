namespace KeyboardUtils;

/// <summary>After recording in create mode: what was captured, the tempo to write it in, and saving.</summary>
sealed class CreateResultForm : AntdUI.Window
{
    readonly AntdUI.InputNumber tempoBox = new() { Size = new Size(110, 36), Minimum = 30, Maximum = 240, Margin = new Padding(6, 0, 0, 0) };

    public int Bpm => (int)tempoBox.Value;

    /// <param name="save">Opens the save dialog; returns true if the file was written.</param>
    public CreateResultForm(Score recording, int bpm, Func<int, bool> save)
    {
        Text = T("Nahrávka", "Recording");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(500, 330);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        int noteCount = recording.Parts.Sum(p => p.Notes.Count);
        var summary = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Text = T($"Zapsáno {noteCount} not ({TimeSpan.FromSeconds(recording.DurationSeconds):m\\:ss})",
                $"Recorded {noteCount} notes ({TimeSpan.FromSeconds(recording.DurationSeconds):m\\:ss})"),
        };

        tempoBox.Value = Math.Clamp(bpm, 30, 240);
        var tempoRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(20, 4, 20, 0) };
        tempoRow.Controls.Add(new AntdUI.Label { Text = T("Tempo zápisu (BPM):", "Tempo to write in (BPM):"), AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        tempoRow.Controls.Add(tempoBox);

        var help = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 8, 20, 0),
            ForeColor = SystemColors.GrayText,
            Text = T("MIDI uloží přesně to, co jsi zahrál. MusicXML (pro MuseScore) zarovná noty na šestnáctiny v tomhle tempu - "
                     + "nastav ho zhruba na tempo, ve kterém jsi hrál, jinak budou délky not nesmyslné.\n"
                     + "Nahrávka je teď otevřená jako skladba: můžeš si ji přehrát a uložit i později přes Soubor.",
                "MIDI saves exactly what you played. MusicXML (for MuseScore) snaps notes to 16ths at this tempo - "
                     + "set it to roughly the tempo you played in, or the note lengths won't make sense.\n"
                     + "The recording is now open as a song: you can play it back and save it later via File too."),
        };

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        var saveButton = new AntdUI.Button { Text = T("Uložit...", "Save..."), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Enabled = noteCount > 0 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, saveButton]);

        Controls.Add(help);
        Controls.Add(tempoRow);
        Controls.Add(summary);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        saveButton.Click += (_, _) =>
        {
            if (save(Bpm))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}
