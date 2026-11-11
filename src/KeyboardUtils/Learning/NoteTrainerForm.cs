using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// Note-reading trainer: a note on a staff, play it (MIDI / microphone / computer sound, routed here by MainForm) or
/// pick its name with a button or a letter key. 20 notes a round, the best round per level is kept.
/// </summary>
sealed class NoteTrainerForm : AntdUI.Window
{
    readonly AppConfig config;
    readonly Action<int>? playNote;
    readonly AntdUI.Select levelBox = new() { Size = new Size(330, 36), List = true };
    readonly AntdUI.Label bestLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(14, 9, 0, 0) };
    readonly NoteFlashControl staff = new() { Dock = DockStyle.Fill };
    readonly AntdUI.Label progress = new() { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    readonly AntdUI.Label hint = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText };
    readonly AntdUI.Button[] letterButtons = new AntdUI.Button[7];
    readonly AntdUI.Button again = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Visible = false };
    readonly Stopwatch shown = new();
    readonly System.Windows.Forms.Timer pause = new() { Interval = 700 };

    NoteQuiz quiz;

    /// <summary>Waiting before the next note (the answer is showing): input is ignored meanwhile.</summary>
    bool showingAnswer;

    /// <param name="playNote">Plays a pitch (the right note after a wrong answer); null = silent.</param>
    /// <param name="inputName">What the player plays on ("MIDI: ...", "Mikrofon"), for the hint; null = no instrument listening.</param>
    public NoteTrainerForm(AppConfig config, Action<int>? playNote, string? inputName)
    {
        this.config = config;
        this.playNote = playNote;
        Text = T("Trénink čtení not", "Note-reading trainer");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 560);
        MaximizeBox = MinimizeBox = false;
        KeyPreview = true;

        levelBox.Items.AddRange([
            T("Houslový klíč (c1 – g2, bez posuvek)", "Treble clef (C4 – G5, no accidentals)"),
            T("Basový klíč (F – c1, bez posuvek)", "Bass clef (F2 – C4, no accidentals)"),
            T("Oba klíče", "Both clefs"),
            T("Pokročilý (pomocné linky, křížky a béčka)", "Advanced (ledger lines, sharps and flats)"),
        ]);
        levelBox.SelectedIndex = (int)config.NoteQuizLevel;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(16, 10, 16, 6) };
        top.Controls.AddRange([levelBox, bestLabel]);

        var letters = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(16, 4, 16, 4) };
        for (int letter = 0; letter < 7; letter++)
        {
            int l = letter;
            var button = new AntdUI.Button { Text = NoteQuiz.LetterName(letter), Size = new Size(74, 48), Radius = 8, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0) };
            button.Click += (_, _) => AnswerLetter(l);
            letterButtons[letter] = button;
            letters.Controls.Add(button);
        }
        letters.Padding = new Padding(Math.Max(16, (640 - 7 * 82) / 2), 4, 16, 4);

        hint.Text = inputName != null
            ? T($"Zahraj notu ({inputName}), nebo klikni na její jméno / zmáčkni písmeno na klávesnici.",
                $"Play the note ({inputName}), or click its name / press the letter on the keyboard.")
            : T("Klikni na jméno noty, nebo zmáčkni písmeno na klávesnici.", "Click the note's name, or press the letter on the keyboard.");

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        close.Click += (_, _) => Close();
        again.Text = T("Hrát znovu", "Play again");
        again.Click += (_, _) => StartRound();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, again]);

        var staffBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
        staffBox.Controls.Add(staff);
        Controls.Add(staffBox);
        Controls.Add(progress);
        Controls.Add(hint);
        Controls.Add(letters);
        Controls.Add(buttons);
        Controls.Add(top);
        Controls.Add(new TitleBar(this));
        CancelButton = close;

        pause.Tick += (_, _) =>
        {
            pause.Stop();
            showingAnswer = false;
            ShowNext();
        };
        levelBox.SelectedIndexChanged += (_, _) =>
        {
            config.NoteQuizLevel = (NoteQuizLevel)Math.Max(0, levelBox.SelectedIndex);
            config.Save();
            StartRound();
        };
        KeyDown += OnKeyDown;
        FormClosed += (_, _) => pause.Dispose();
        quiz = new NoteQuiz(config.NoteQuizLevel);
        StartRound();
    }

    void StartRound()
    {
        pause.Stop();
        showingAnswer = false;
        quiz = new NoteQuiz((NoteQuizLevel)Math.Max(0, levelBox.SelectedIndex));
        again.Visible = false;
        UpdateBest();
        ShowNext();
    }

    void ShowNext()
    {
        if (quiz.Next() is not { } note)
        {
            Finish();
            return;
        }
        staff.Show(note);
        shown.Restart();
        UpdateProgress();
    }

    /// <summary>A key played on the instrument (MainForm routes note-ons here while this window is open).</summary>
    public void OnNoteOn(int pitch)
    {
        if (showingAnswer || quiz.Current == null)
            return;
        Answered(quiz.AnswerPitch(pitch, shown.Elapsed.TotalSeconds));
    }

    void AnswerLetter(int letter)
    {
        if (showingAnswer || quiz.Current == null)
            return;
        Answered(quiz.AnswerLetter(letter, shown.Elapsed.TotalSeconds));
    }

    void Answered(bool right)
    {
        var note = quiz.Current!;
        staff.Show(note, right ? NoteFlashControl.Feedback.Right : NoteFlashControl.Feedback.Wrong);
        if (!right)
            playNote?.Invoke(note.Pitch); // hear what it should have been
        UpdateProgress();
        showingAnswer = true;
        pause.Interval = right ? 600 : 1500;
        pause.Start();
    }

    void UpdateProgress()
    {
        progress.Text = T($"Nota {Math.Min(quiz.Done + 1, NoteQuiz.RoundLength)} / {NoteQuiz.RoundLength}  ·  správně {quiz.Correct}",
                $"Note {Math.Min(quiz.Done + 1, NoteQuiz.RoundLength)} / {NoteQuiz.RoundLength}  ·  {quiz.Correct} right")
            + (quiz.Done > 0 ? T($"  ·  {quiz.AverageSeconds:0.0} s na notu", $"  ·  {quiz.AverageSeconds:0.0} s a note") : "");
    }

    void Finish()
    {
        staff.Show(null);
        var level = quiz.Level;
        bool record = !config.NoteQuizBest.TryGetValue(level, out int best) || quiz.Correct > best;
        if (record)
        {
            config.NoteQuizBest[level] = quiz.Correct;
            config.Save();
        }
        progress.Text = T($"Hotovo: {quiz.Correct} z {NoteQuiz.RoundLength} správně, {quiz.AverageSeconds:0.0} s na notu",
                $"Done: {quiz.Correct} of {NoteQuiz.RoundLength} right, {quiz.AverageSeconds:0.0} s a note")
            + (record ? T("  ·  nový rekord! 🎉", "  ·  a new best! 🎉") : "");
        again.Visible = true;
        UpdateBest();
    }

    void UpdateBest() =>
        bestLabel.Text = config.NoteQuizBest.TryGetValue((NoteQuizLevel)Math.Max(0, levelBox.SelectedIndex), out int best)
            ? T($"Nejlepší: {best} / {NoteQuiz.RoundLength}", $"Best: {best} / {NoteQuiz.RoundLength}")
            : "";

    /// <summary>C D E F G A H (and B, both languages) answer like the buttons.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int letter = e.KeyCode switch
        {
            Keys.C => 0,
            Keys.D => 1,
            Keys.E => 2,
            Keys.F => 3,
            Keys.G => 4,
            Keys.A => 5,
            Keys.H or Keys.B => 6,
            _ => -1,
        };
        if (letter < 0 || e.Modifiers != Keys.None || levelBox.Focused)
            return;
        e.Handled = e.SuppressKeyPress = true;
        AnswerLetter(letter);
    }
}
