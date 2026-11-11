namespace KeyboardUtils;

/// <summary>Scales and warm-ups: pick the exercise, key, range, hands and tempo; it opens as a song to practise.</summary>
sealed class ExerciseForm : AntdUI.Window
{
    readonly AntdUI.Select kindBox = new() { Size = new Size(320, 36), List = true };
    readonly AntdUI.Select keyBox = new() { Size = new Size(320, 36), List = true, MaxCount = 12 };
    readonly AntdUI.Select octavesBox = new() { Size = new Size(150, 36), List = true };
    readonly AntdUI.Select handsBox = new() { Size = new Size(150, 36), List = true };
    readonly AntdUI.InputNumber bpmBox = new() { Size = new Size(150, 36), Minimum = 30, Maximum = 200, SuffixText = "BPM" };
    readonly AntdUI.Checkbox eighthsBox = new() { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };

    public ExerciseSpec Spec
    {
        get
        {
            var key = ExerciseGenerator.Keys[Math.Max(0, keyBox.SelectedIndex)];
            return new ExerciseSpec(
                (ExerciseKind)Math.Max(0, kindBox.SelectedIndex), key.Fifths, key.Minor,
                octavesBox.SelectedIndex + 1, (ExerciseHands)Math.Max(0, handsBox.SelectedIndex),
                (int)bpmBox.Value, eighthsBox.Checked);
        }
    }

    public ExerciseForm(ExerciseSpec last)
    {
        Text = T("Stupnice a rozcvičky", "Scales and warm-ups");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        kindBox.Items.AddRange([T("Stupnice (nahoru a dolů)", "Scale (up and down)"), T("Rozložený akord (arpeggio)", "Arpeggio"),
            T("Kadence T–S–D–T (akordy)", "Cadence I–IV–V–I (chords)")]);
        kindBox.SelectedIndex = (int)last.Kind;
        foreach (var key in ExerciseGenerator.Keys)
            keyBox.Items.Add(key.Name + Signature(key.Fifths));
        keyBox.SelectedIndex = Math.Max(0, Array.FindIndex(ExerciseGenerator.Keys, k => k.Fifths == last.Fifths && k.Minor == last.Minor));
        octavesBox.Items.AddRange([T("1 oktáva", "1 octave"), T("2 oktávy", "2 octaves")]);
        octavesBox.SelectedIndex = Math.Clamp(last.Octaves, 1, 2) - 1;
        handsBox.Items.AddRange([T("Pravá ruka", "Right hand"), T("Levá ruka", "Left hand"), T("Obě ruce", "Both hands")]);
        handsBox.SelectedIndex = (int)last.Hands;
        bpmBox.Value = Math.Clamp(last.Bpm, 30, 200);
        eighthsBox.Text = T("Osminové noty (dvě na dobu)", "Eighth notes (two per beat)");
        eighthsBox.Checked = last.Eighths;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 8, 20, 4) };
        layout.Controls.Add(Heading(T("Cvičení:", "Exercise:")));
        layout.Controls.Add(kindBox);
        layout.Controls.Add(Heading(T("Tónina:", "Key:")));
        layout.Controls.Add(keyBox);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(Column(T("Rozsah:", "Range:"), octavesBox));
        row.Controls.Add(Column(T("Ruce:", "Hands:"), handsBox));
        layout.Controls.Add(row);
        layout.Controls.Add(Heading(T("Tempo:", "Tempo:")));
        layout.Controls.Add(bpmBox);
        layout.Controls.Add(eighthsBox);

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var create = new AntdUI.Button { Text = T("Vytvořit", "Create"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, create]);
        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(titleBar);
        AcceptButton = create;
        CancelButton = cancel;

        create.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        kindBox.SelectedIndexChanged += (_, _) => UpdateState();
        UpdateState();
        Load += (_, _) =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(380, content.Width), titleBar.Height + content.Height + buttons.GetPreferredSize(Size.Empty).Height);
            CenterToParent();
        };
    }

    /// <summary>A cadence is chords in half notes over one octave: range and eighths don't apply.</summary>
    void UpdateState()
    {
        bool cadence = kindBox.SelectedIndex == (int)ExerciseKind.Cadence;
        octavesBox.Enabled = !cadence;
        eighthsBox.Enabled = !cadence;
    }

    static string Signature(int fifths) => fifths switch
    {
        0 => "",
        > 0 => T($"  ({fifths} ♯)", $"  ({fifths} ♯)"),
        _ => T($"  ({-fifths} ♭)", $"  ({-fifths} ♭)"),
    };

    static AntdUI.Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

    static FlowLayoutPanel Column(string heading, Control control)
    {
        var column = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 20, 0) };
        column.Controls.Add(Heading(heading));
        column.Controls.Add(control);
        return column;
    }
}
