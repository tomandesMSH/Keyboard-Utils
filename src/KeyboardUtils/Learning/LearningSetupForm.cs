namespace KeyboardUtils;

/// <summary>Before learning starts: which parts the player plays, wait vs. tempo, speed and accompaniment.</summary>
sealed class LearningSetupForm : AntdUI.Window
{
    /// <summary>The speeds offered, in %. Also stepped through by the "Learning: speed" macro key.</summary>
    public static readonly int[] Speeds = [25, 50, 75, 100];

    readonly List<AntdUI.Checkbox> partBoxes = [];
    readonly AntdUI.Select modeBox = new() { Size = new Size(480, 36), List = true };
    readonly AntdUI.Select speedBox = new() { Size = new Size(120, 36), List = true };
    readonly AntdUI.Checkbox accompanimentBox = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    readonly AntdUI.Checkbox chordsBox = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    readonly AntdUI.Checkbox rampBox = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    readonly AntdUI.Checkbox recordBox = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    readonly bool systemAudio;
    readonly AntdUI.Label micHint = new() { AutoSize = false, Size = new Size(470, 40), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Button start = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly bool microphone;
    readonly bool audioInput;
    readonly bool audioAccompaniment;
    readonly bool loop;

    /// <summary>The speeds in <see cref="speedBox"/>: <see cref="Speeds"/>, plus one reached by speeding a loop up.</summary>
    readonly List<int> offeredSpeeds = [.. Speeds];

    public List<int> PracticedParts { get; } = [];
    public LearningMode SelectedMode => modeBox.SelectedIndex == 1 ? LearningMode.InTempo : LearningMode.WaitForNote;
    public int SpeedPercent => offeredSpeeds[Math.Max(0, speedBox.SelectedIndex)];
    public bool Accompaniment => accompanimentBox.Checked;
    public bool CheckChords => chordsBox.Checked;
    public bool RampUp => rampBox.Checked;
    public bool RecordTake => recordBox.Checked;

    /// <param name="audioAccompaniment">
    /// The song is loaded stems with notes converted from them: in tempo mode the accompaniment is the original
    /// recording, which can only play at 100 %.
    /// </param>
    /// <param name="loop">A loop is on: speeding it up pass by pass is offered.</param>
    public LearningSetupForm(Score score, AppConfig config, bool audioAccompaniment = false, bool loop = false)
    {
        this.audioAccompaniment = audioAccompaniment;
        this.loop = loop;
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
        // A speed a loop was sped up to (60 %, 85 %...) stays selectable, so the next session picks up from there.
        if (config.LearningSpeedPercent is > 0 and <= 100 && !offeredSpeeds.Contains(config.LearningSpeedPercent))
        {
            offeredSpeeds.Add(config.LearningSpeedPercent);
            offeredSpeeds.Sort();
        }
        foreach (int speed in offeredSpeeds)
            speedBox.Items.Add($"{speed} %");
        int speedIndex = offeredSpeeds.IndexOf(config.LearningSpeedPercent);
        speedBox.SelectedIndex = speedIndex >= 0 ? speedIndex : offeredSpeeds.Count - 1;
        layout.Controls.Add(speedBox);

        rampBox.Text = T($"Postupně zrychlovat smyčku (kolo za {SpeedRamp.GoodPassPercent:0} % a víc → +{SpeedRamp.StepPercent} %, až do 100 %)",
            $"Speed the loop up step by step (a pass of {SpeedRamp.GoodPassPercent:0} % or more → +{SpeedRamp.StepPercent} %, up to 100 %)");
        rampBox.Checked = config.LearningRampUp;
        rampBox.CheckedChanged += (_, _) => UpdateState();
        layout.Controls.Add(rampBox);

        accompanimentBox.Text = audioAccompaniment
            ? T("Přehrávat ostatní stopy (originální nahrávku) jako doprovod", "Play the other tracks (the original recording) as accompaniment")
            : T("Přehrávat ostatní části jako doprovod", "Play the other parts as accompaniment");
        accompanimentBox.Checked = config.LearningAccompaniment;
        accompanimentBox.CheckedChanged += (_, _) => UpdateState();
        layout.Controls.Add(accompanimentBox);

        // Only sound-based inputs need it: MIDI and the on-screen keyboard report every key of a chord anyway.
        microphone = config.NoteInput == NoteInputKind.Microphone;
        audioInput = microphone || config.NoteInput == NoteInputKind.SystemAudio;
        chordsBox.Text = T("U akordů kontrolovat všechny noty", "Check every note of a chord");
        chordsBox.Checked = config.LearningCheckChords;
        chordsBox.Visible = audioInput;
        chordsBox.CheckedChanged += (_, _) => UpdateState();
        layout.Controls.Add(chordsBox);

        // The computer's sound has the accompaniment in it too: nothing to record there.
        systemAudio = config.NoteInput == NoteInputKind.SystemAudio;
        recordBox.Text = microphone
            ? T("Nahrát, jak hraju (zvuk z mikrofonu) - pak si to poslechneš", "Record my playing (the microphone's sound) - to listen back")
            : T("Nahrát, jak hraju (zahrané noty) - pak si to poslechneš", "Record my playing (the notes played) - to listen back");
        recordBox.Checked = config.LearningRecordTake && !systemAudio;
        recordBox.Enabled = !systemAudio;
        layout.Controls.Add(recordBox);

        micHint.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(micHint);

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        start.Text = T("Začít", "Start");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, start]);

        // Enter starts, Esc cancels (also from the macro keyboard's Confirm / Cancel).
        AcceptButton = start;
        CancelButton = cancel;
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
        speedBox.Enabled = !(tempo && audioAccompaniment);
        // Only a loop goes round pass after pass; with stems in tempo the speed is fixed at 100 %.
        rampBox.Enabled = loop && speedBox.Enabled;
        bool anyPart = partBoxes.Any(b => b.Checked);
        start.Enabled = anyPart;

        micHint.Text = !anyPart
            ? T("Vyber aspoň jednu část.", "Pick at least one part.")
            : !loop && RampUp
                ? T("Zrychlování funguje jen se smyčkou - označ úsek v notách a zapni Smyčku (L).",
                    "Speeding up only works with a loop - pick a section in the notes and turn the Loop on (L).")
            : tempo && audioAccompaniment
                ? T("Se stopami jede režim v tempu vždy na 100 % - originální nahrávka zpomalit nejde.",
                    "With stems, tempo mode always runs at 100 % - the original recording can't be slowed down.")
            : tempo && Accompaniment && microphone
                ? T("Tip: s mikrofonem hraj doprovod do sluchátek - z reproduktorů by ho mikrofon slyšel jako tvoje noty.",
                    "Tip: with the microphone, play the accompaniment through headphones - from speakers the mic would hear it as your notes.")
                : !audioInput
                    ? ""
                    : CheckChords
                        ? T("Akord se počítá, až zazní všechny jeho noty. Kdyby appka některou neslyšela, vypni kontrolu akordů.",
                            "A chord counts once all its notes sound. If the app misses one, turn chord checking off.")
                        : T("U akordu stačí zahrát správně jednu z jeho not.", "For a chord, one right note of it is enough.");
    }
}
