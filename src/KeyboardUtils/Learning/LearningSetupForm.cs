namespace KeyboardUtils;

/// <summary>Before learning starts: which parts the player plays, wait vs. tempo, speed and accompaniment.</summary>
sealed class LearningSetupForm : AntdUI.Window
{
    static readonly int[] Speeds = [25, 50, 75, 100];

    readonly List<AntdUI.Checkbox> partBoxes = [];
    readonly AntdUI.Select modeBox = new() { Size = new Size(480, 36), List = true };
    readonly AntdUI.Select speedBox = new() { Size = new Size(120, 36), List = true };
    readonly AntdUI.Checkbox accompanimentBox = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    readonly AntdUI.Label micHint = new() { AutoSize = false, Size = new Size(470, 40), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Button start = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly bool microphone;

    public List<int> PracticedParts { get; } = [];
    public LearningMode SelectedMode => modeBox.SelectedIndex == 1 ? LearningMode.InTempo : LearningMode.WaitForNote;
    public int SpeedPercent => Speeds[Math.Max(0, speedBox.SelectedIndex)];
    public bool Accompaniment => accompanimentBox.Checked;

    public LearningSetupForm(Score score, AppConfig config)
    {
        Text = T("Režim učení", "Learning mode");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(20, 12, 20, 4),
        };

        layout.Controls.Add(Heading(T("Co budeš hrát:", "What you'll play:")));
        var selected = PartsToPractice(score, config.LearningPartNames);
        for (int i = 0; i < score.Parts.Count; i++)
        {
            var part = score.Parts[i];
            var box = new AntdUI.Checkbox
            {
                Text = $"{part.Name}  ({part.Notes.Count} {T("not", "notes")})",
                Checked = selected.Contains(i),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            box.CheckedChanged += (_, _) => UpdateState();
            partBoxes.Add(box);
            layout.Controls.Add(box);
        }

        layout.Controls.Add(Heading(T("Režim:", "Mode:")));
        modeBox.Items.Add(T("Čekat na notu - skladba počká, než zahraješ správně", "Wait for note - the song waits until you play it right"));
        modeBox.Items.Add(T("V tempu - skladba jede, hraj včas", "In tempo - the song keeps going, play on time"));
        modeBox.SelectedIndex = config.LearningMode == LearningMode.InTempo ? 1 : 0;
        modeBox.ListAutoWidth = true;
        layout.Controls.Add(modeBox);

        layout.Controls.Add(Heading(T("Rychlost:", "Speed:")));
        foreach (int speed in Speeds)
            speedBox.Items.Add($"{speed} %");
        int speedIndex = Array.IndexOf(Speeds, config.LearningSpeedPercent);
        speedBox.SelectedIndex = speedIndex >= 0 ? speedIndex : Speeds.Length - 1;
        layout.Controls.Add(speedBox);

        accompanimentBox.Text = T("Přehrávat ostatní části jako doprovod", "Play the other parts as accompaniment");
        accompanimentBox.Checked = config.LearningAccompaniment;
        accompanimentBox.CheckedChanged += (_, _) => UpdateState();
        layout.Controls.Add(accompanimentBox);

        micHint.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(micHint);
        microphone = config.NoteInput == NoteInputKind.Microphone;

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        start.Text = T("Začít", "Start");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, start]);

        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(titleBar);

        modeBox.SelectedIndexChanged += (_, _) => UpdateState();
        start.Click += (_, _) =>
        {
            PracticedParts.AddRange(partBoxes.Select((box, i) => (box, i)).Where(x => x.box.Checked).Select(x => x.i));
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        UpdateState();
        // Height depends on the number of parts.
        Load += (_, _) =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(540, content.Width), titleBar.Height + content.Height + buttons.GetPreferredSize(Size.Empty).Height);
            CenterToParent();
        };
    }

    static AntdUI.Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

    /// <summary>The parts practiced last time, matched by name; all parts if none of them is in this song.</summary>
    public static HashSet<int> PartsToPractice(Score score, IReadOnlyCollection<string> lastNames)
    {
        var byName = score.Parts.Select((p, i) => (p, i)).Where(x => lastNames.Contains(x.p.Name)).Select(x => x.i).ToHashSet();
        return byName.Count > 0 ? byName : Enumerable.Range(0, score.Parts.Count).ToHashSet();
    }

    void UpdateState()
    {
        bool tempo = SelectedMode == LearningMode.InTempo;
        // Waiting would stall the accompaniment on every note, so it's tempo-only.
        accompanimentBox.Enabled = tempo;
        bool anyPart = partBoxes.Any(b => b.Checked);
        start.Enabled = anyPart;

        micHint.Text = !anyPart
            ? T("Vyber aspoň jednu část.", "Pick at least one part.")
            : tempo && Accompaniment && microphone
                ? T("Tip: s mikrofonem hraj doprovod do sluchátek - z reproduktorů by ho mikrofon slyšel jako tvoje noty.",
                    "Tip: with the microphone, play the accompaniment through headphones - from speakers the mic would hear it as your notes.")
                : !microphone
                    ? ""
                    : T("Mikrofon slyší jen jednu notu naráz: u akordu stačí zahrát správně jednu z jeho not.",
                        "The microphone hears one note at a time: for a chord, one right note of it is enough.");
    }
}
