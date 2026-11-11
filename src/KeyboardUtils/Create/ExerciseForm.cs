namespace KeyboardUtils;

/// <summary>Scales and warm-ups: pick the exercise, key, range, hands and tempo; it opens as a song to practise.</summary>
sealed class ExerciseForm : AntdUI.Window
{
    readonly AntdUI.Select kindBox = new() { Size = new Size(320, 36), List = true };
    readonly AntdUI.Select keyBox = new() { Size = new Size(320, 36), List = true, MaxCount = 12 };
    readonly AntdUI.Select minorFormBox = new() { Size = new Size(320, 36), List = true };
    readonly FlowLayoutPanel minorFormRow = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    readonly AntdUI.Select octavesBox = new() { Size = new Size(150, 36), List = true };
    readonly AntdUI.Select handsBox = new() { Size = new Size(150, 36), List = true };
    readonly AntdUI.InputNumber bpmBox = new() { Size = new Size(150, 36), Minimum = 30, Maximum = 200, SuffixText = "BPM" };
    readonly AntdUI.Checkbox eighthsBox = new() { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
    readonly AntdUI.Label rangeNote = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0), Visible = false };
    readonly Instrument instrument;
    readonly ExerciseKind[] kinds;

    public ExerciseSpec Spec
    {
        get
        {
            var key = ExerciseGenerator.Keys[Math.Max(0, keyBox.SelectedIndex)];
            return new ExerciseSpec(
                kinds[Math.Max(0, kindBox.SelectedIndex)], key.Fifths, key.Minor,
                octavesBox.SelectedIndex + 1, (ExerciseHands)Math.Max(0, handsBox.SelectedIndex),
                (int)bpmBox.Value, eighthsBox.Checked, (MinorForm)Math.Max(0, minorFormBox.SelectedIndex));
        }
    }

    /// <param name="instrument">Not piano: only single-line exercises, no hands, and the key is the written one.</param>
    public ExerciseForm(ExerciseSpec last, Instrument? instrument = null)
    {
        this.instrument = instrument ?? Instrument.Piano;
        kinds = ExerciseGenerator.KindsFor(this.instrument);
        Text = T("Stupnice a rozcvičky", "Scales and warm-ups") + (this.instrument.IsPiano ? "" : " – " + this.instrument.Name);
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        foreach (var kind in kinds)
            kindBox.Items.Add(kind switch
            {
                ExerciseKind.Chromatic => T("Chromatická stupnice", "Chromatic scale"),
                ExerciseKind.Thirds => T("Stupnice v terciích", "Scale in thirds"),
                ExerciseKind.Arpeggio => T("Rozložený akord (arpeggio)", "Arpeggio"),
                ExerciseKind.Inversions => T("Obraty akordu (základní tvar, 1. a 2. obrat)", "Chord inversions (root, 1st, 2nd)"),
                ExerciseKind.Cadence => T("Kadence T–S–D–T (akordy)", "Cadence I–IV–V–I (chords)"),
                ExerciseKind.Hanon => T("Hanon č. 1 (prstová cvičení)", "Hanon No. 1 (finger exercise)"),
                _ => T("Stupnice (nahoru a dolů)", "Scale (up and down)"),
            });
        kindBox.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, last.Kind));
        minorFormBox.Items.AddRange([T("Harmonická (zvýšený 7. stupeň)", "Harmonic (raised 7th)"),
            T("Melodická (nahoru zvýšený 6. a 7. stupeň)", "Melodic (6th and 7th raised going up)"),
            T("Přirozená (aiolská)", "Natural (Aeolian)")]);
        minorFormBox.SelectedIndex = (int)last.MinorForm;
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
        layout.Controls.Add(Heading(this.instrument.Transpose % 12 != 0
            ? T("Tónina (jak ji čteš v notách):", "Key (as you read it):")
            : T("Tónina:", "Key:")));
        layout.Controls.Add(keyBox);
        minorFormRow.Controls.Add(Heading(T("Moll stupnice:", "Minor scale:")));
        minorFormRow.Controls.Add(minorFormBox);
        layout.Controls.Add(minorFormRow);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(Column(T("Rozsah:", "Range:"), octavesBox));
        if (this.instrument.IsPiano)
            row.Controls.Add(Column(T("Ruce:", "Hands:"), handsBox));
        layout.Controls.Add(row);
        layout.Controls.Add(rangeNote);
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
        keyBox.SelectedIndexChanged += (_, _) => UpdateState();
        octavesBox.SelectedIndexChanged += (_, _) => UpdateState();
        UpdateState();
        FitToContent = () =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(380, content.Width), titleBar.Height + content.Height + buttons.GetPreferredSize(Size.Empty).Height);
        };
        Load += (_, _) =>
        {
            FitToContent?.Invoke();
            CenterToParent();
        };
    }

    /// <summary>Chords come in half notes over one octave (range and eighths don't apply); the minor form only for minor scales.</summary>
    void UpdateState()
    {
        var kind = kinds[Math.Max(0, kindBox.SelectedIndex)];
        bool chordal = ExerciseGenerator.IsChordal(kind);
        // Two octaves that don't fit the instrument: say so (the generator makes one).
        bool tooLow = !instrument.IsPiano && octavesBox.SelectedIndex == 1
            && ExerciseGenerator.PlaceOnInstrument(Spec, instrument).Octaves < 2;
        rangeNote.Text = T($"Dvě oktávy se v této tónině do rozsahu nástroje ({instrument.Name}) nevejdou - bude jedna.",
            $"Two octaves don't fit the range of the {instrument.Name} in this key - it'll be one.");
        if (rangeNote.Visible != tooLow)
        {
            rangeNote.Visible = tooLow;
            if (IsHandleCreated)
                FitToContent?.Invoke();
        }
        octavesBox.Enabled = !chordal;
        eighthsBox.Enabled = !chordal;
        bool minorScale = kind == ExerciseKind.Scale && ExerciseGenerator.Keys[Math.Max(0, keyBox.SelectedIndex)].Minor;
        minorFormBox.Enabled = minorScale;
        if (IsHandleCreated && minorFormRow.Visible != minorScale)
        {
            minorFormRow.Visible = minorScale;
            FitToContent?.Invoke();
        }
        else
            minorFormRow.Visible = minorScale;
    }

    /// <summary>Resizes the window to its content (the minor-form row comes and goes).</summary>
    Action? FitToContent;

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
