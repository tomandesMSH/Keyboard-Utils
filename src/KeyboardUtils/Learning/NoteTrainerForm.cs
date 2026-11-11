using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// Note-reading trainer: a note on a staff, play it (MIDI / microphone / computer sound, routed here by MainForm) or
/// pick its name with a button or a letter key; or two notes and their interval. 20 a round, optionally against the
/// clock; the best round per level is kept, and notes missed often come up more often.
/// </summary>
sealed class NoteTrainerForm : AntdUI.Window
{
    static readonly int[] TimeLimits = [0, 5, 3, 2];

    readonly AppConfig config;
    readonly Instrument instrument;
    readonly NoteQuizLevel[] levels;
    readonly Action<int>? playNote;
    readonly string? inputName;
    readonly AntdUI.Select levelBox = new() { Size = new Size(330, 36), List = true };
    readonly AntdUI.Select timeBox = new() { Size = new Size(140, 36), List = true, Margin = new Padding(10, 0, 0, 0) };
    readonly AntdUI.Label bestLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 9, 0, 0) };
    readonly NoteFlashControl staff = new() { Dock = DockStyle.Fill };
    readonly AntdUI.Label progress = new() { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    readonly AntdUI.Label hint = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText };
    readonly AntdUI.Button[] answerButtons = new AntdUI.Button[7];
    readonly AntdUI.Button again = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Visible = false };
    readonly Stopwatch shown = new();
    readonly System.Windows.Forms.Timer pause = new() { Interval = 700 };
    readonly System.Windows.Forms.Timer clock = new() { Interval = 100 };

    NoteQuiz quiz;

    /// <summary>Waiting before the next note (the answer is showing): input is ignored meanwhile.</summary>
    bool showingAnswer;

    NoteQuizLevel Level => levels[Math.Max(0, levelBox.SelectedIndex)];
    bool Intervals => Level == NoteQuizLevel.Intervals;
    int SecondsPerNote => TimeLimits[Math.Max(0, timeBox.SelectedIndex)];

    /// <param name="instrument">The player's: its own levels, and played notes are taken as it sounds.</param>
    /// <param name="playNote">Plays a pitch (the right note after a wrong answer); null = silent.</param>
    /// <param name="inputName">What the player plays on ("MIDI: ...", "Mikrofon"), for the hint; null = no instrument listening.</param>
    public NoteTrainerForm(AppConfig config, Action<int>? playNote, string? inputName, Instrument? instrument = null)
    {
        this.config = config;
        this.instrument = instrument ?? Instrument.Piano;
        levels = NoteQuiz.LevelsFor(this.instrument);
        this.playNote = playNote;
        this.inputName = inputName;
        Text = T("Trénink čtení not", "Note-reading trainer");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(660, 580);
        MaximizeBox = MinimizeBox = false;
        KeyPreview = true;

        levelBox.Items.AddRange(levels.Select(l => (object)LevelName(l)).ToArray());
        levelBox.SelectedIndex = Math.Max(0, Array.IndexOf(levels, config.NoteQuizLevel));
        timeBox.Items.AddRange([T("Bez limitu", "No time limit"), T("5 s na notu", "5 s a note"), T("3 s na notu", "3 s a note"), T("2 s na notu", "2 s a note")]);
        timeBox.SelectedIndex = Math.Max(0, Array.IndexOf(TimeLimits, config.NoteQuizSeconds));
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(16, 10, 16, 6) };
        top.Controls.AddRange([levelBox, timeBox, bestLabel]);

        var answers = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false };
        for (int i = 0; i < 7; i++)
        {
            int index = i;
            var button = new AntdUI.Button { Size = new Size(78, 48), Radius = 8, Margin = new Padding(0, 0, 8, 0) };
            button.Click += (_, _) => AnswerButton(index);
            answerButtons[i] = button;
            answers.Controls.Add(button);
        }
        answers.Padding = new Padding(Math.Max(16, (660 - 7 * 86) / 2), 4, 16, 4);

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
        Controls.Add(answers);
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
        clock.Tick += (_, _) => OnClock();
        levelBox.SelectedIndexChanged += (_, _) =>
        {
            config.NoteQuizLevel = Level;
            config.Save();
            StartRound();
        };
        timeBox.SelectedIndexChanged += (_, _) =>
        {
            config.NoteQuizSeconds = SecondsPerNote;
            config.Save();
            StartRound();
        };
        KeyDown += OnKeyDown;
        FormClosed += (_, _) =>
        {
            pause.Dispose();
            clock.Dispose();
            config.Save(); // the per-note mistake counts
        };
        quiz = new NoteQuiz(Level, weakness: config.NoteQuizWeakness, instrument: this.instrument);
        StartRound();
    }

    string LevelName(NoteQuizLevel level) => level switch
    {
        NoteQuizLevel.Treble => T("Houslový klíč (c1 – g2, bez posuvek)", "Treble clef (C4 – G5, no accidentals)"),
        NoteQuizLevel.Bass => T("Basový klíč (F – c1, bez posuvek)", "Bass clef (F2 – C4, no accidentals)"),
        NoteQuizLevel.Both => T("Oba klíče", "Both clefs"),
        NoteQuizLevel.Advanced => T("Pokročilý (pomocné linky, křížky a béčka)", "Advanced (ledger lines, sharps and flats)"),
        NoteQuizLevel.Intervals => T("Intervaly (sekunda – oktáva)", "Intervals (second – octave)"),
        NoteQuizLevel.Alto => T("Altový klíč (bez posuvek)", "Alto clef (no accidentals)"),
        NoteQuizLevel.Tenor => T("Tenorový klíč (bez posuvek)", "Tenor clef (no accidentals)"),
        NoteQuizLevel.Instrument => T($"{instrument.Name}: začátek (bez posuvek)", $"{instrument.Name}: first notes (no accidentals)"),
        _ => T($"{instrument.Name}: celý rozsah, křížky a béčka", $"{instrument.Name}: whole range, sharps and flats"),
    };

    void StartRound()
    {
        pause.Stop();
        showingAnswer = false;
        quiz = new NoteQuiz(Level, weakness: config.NoteQuizWeakness, instrument: instrument);
        staff.EmptyClef = Level switch
        {
            NoteQuizLevel.Bass => Clef.Bass,
            NoteQuizLevel.Alto => Clef.Alto,
            NoteQuizLevel.Tenor => Clef.Tenor,
            NoteQuizLevel.Instrument or NoteQuizLevel.InstrumentAdvanced => instrument.Clef,
            _ => Clef.Treble,
        };
        again.Visible = false;
        for (int i = 0; i < 7; i++)
        {
            answerButtons[i].Text = Intervals ? NoteQuiz.IntervalName(i + 2) : NoteQuiz.LetterName(i);
            answerButtons[i].Font = new Font("Segoe UI", Intervals ? 9.5f : 14f, FontStyle.Bold);
        }
        hint.Text = Intervals
            ? T("Jaký je to interval? Klikni, nebo zmáčkni číslo 2–8 (2 = sekunda … 8 = oktáva).",
                "Which interval is it? Click, or press 2–8 (2 = second … 8 = octave).")
            : inputName != null
                ? T($"Zahraj notu ({inputName}), nebo klikni na její jméno / zmáčkni písmeno na klávesnici.",
                    $"Play the note ({inputName}), or click its name / press the letter on the keyboard.")
                : T("Klikni na jméno noty, nebo zmáčkni písmeno na klávesnici.", "Click the note's name, or press the letter on the keyboard.");
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
        if (SecondsPerNote > 0)
            clock.Start();
    }

    /// <summary>The clock against which each note is answered, when there's a time limit.</summary>
    void OnClock()
    {
        if (showingAnswer || quiz.Current == null || SecondsPerNote == 0)
        {
            clock.Stop();
            return;
        }
        if (shown.Elapsed.TotalSeconds >= SecondsPerNote)
        {
            quiz.TimeUp(SecondsPerNote);
            Answered(false, timeUp: true);
            return;
        }
        UpdateProgress();
    }

    /// <summary>A key played on the instrument (MainForm routes note-ons here while this window is open).</summary>
    public void OnNoteOn(int pitch)
    {
        if (showingAnswer || quiz.Current == null || Intervals)
            return;
        Answered(quiz.AnswerPitch(pitch, shown.Elapsed.TotalSeconds));
    }

    /// <summary>Button (or key) <paramref name="index"/>: letter C ... H, or interval second ... octave.</summary>
    void AnswerButton(int index)
    {
        if (showingAnswer || quiz.Current == null)
            return;
        double seconds = shown.Elapsed.TotalSeconds;
        Answered(Intervals ? quiz.AnswerInterval(index + 2, seconds) : quiz.AnswerLetter(index, seconds));
    }

    void Answered(bool right, bool timeUp = false)
    {
        clock.Stop();
        var note = quiz.Current!;
        staff.Show(note, right ? NoteFlashControl.Feedback.Right : NoteFlashControl.Feedback.Wrong);
        if (!right)
        {
            // Hear what it should have been - as the instrument sounds it.
            playNote?.Invoke(instrument.Sounding(note.Pitch));
            if (note.Upper is { } upper)
                playNote?.Invoke(instrument.Sounding(upper.Pitch));
        }
        UpdateProgress(timeUp);
        showingAnswer = true;
        pause.Interval = right ? 600 : 1500;
        pause.Start();
    }

    void UpdateProgress(bool timeUp = false)
    {
        string what = Intervals ? T("Interval", "Interval") : T("Nota", "Note");
        string text = T($"{what} {Math.Min(quiz.Done + 1, NoteQuiz.RoundLength)} / {NoteQuiz.RoundLength}  ·  správně {quiz.Correct}",
            $"{what} {Math.Min(quiz.Done + 1, NoteQuiz.RoundLength)} / {NoteQuiz.RoundLength}  ·  {quiz.Correct} right");
        if (quiz.Streak >= 3)
            text += T($"  ·  {quiz.Streak} v řadě 🔥", $"  ·  {quiz.Streak} in a row 🔥");
        if (timeUp)
            text += T("  ·  čas vypršel", "  ·  time's up");
        else if (SecondsPerNote > 0 && !showingAnswer && quiz.Current != null)
            text += $"  ·  ⏱ {Math.Max(0, SecondsPerNote - shown.Elapsed.TotalSeconds):0.0} s";
        progress.Text = text;
    }

    void Finish()
    {
        clock.Stop();
        staff.Show(null);
        var level = quiz.Level;
        bool record = !config.NoteQuizBest.TryGetValue(level, out int best) || quiz.Correct > best;
        if (record)
            config.NoteQuizBest[level] = quiz.Correct;
        config.Save();
        string text = T($"Hotovo: {quiz.Correct} z {NoteQuiz.RoundLength} správně, {quiz.AverageSeconds:0.0} s na notu",
                $"Done: {quiz.Correct} of {NoteQuiz.RoundLength} right, {quiz.AverageSeconds:0.0} s a note")
            + (quiz.BestStreak >= 3 ? T($", nejdelší série {quiz.BestStreak}", $", longest run {quiz.BestStreak}") : "")
            + (record ? T("  ·  nový rekord! 🎉", "  ·  a new best! 🎉") : "");
        progress.Text = text;
        var missed = quiz.MostMissed();
        hint.Text = missed.Count > 0
            ? T("Nejvíc chyb: ", "Most missed: ") + string.Join(", ", missed) + T(" - příště přijdou častěji.", " - they'll come up more often.")
            : T("Bez jediné chyby!", "Not a single mistake!");
        again.Visible = true;
        UpdateBest();
    }

    void UpdateBest() =>
        bestLabel.Text = config.NoteQuizBest.TryGetValue(Level, out int best)
            ? T($"Nejlepší: {best} / {NoteQuiz.RoundLength}", $"Best: {best} / {NoteQuiz.RoundLength}")
            : "";

    /// <summary>C D E F G A H (and B, both languages) answer like the buttons; for intervals the digits 2 - 8.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Modifiers != Keys.None || levelBox.Focused || timeBox.Focused)
            return;
        int index = Intervals
            ? e.KeyCode switch
            {
                >= Keys.D2 and <= Keys.D8 => e.KeyCode - Keys.D2,
                >= Keys.NumPad2 and <= Keys.NumPad8 => e.KeyCode - Keys.NumPad2,
                _ => -1,
            }
            : e.KeyCode switch
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
        if (index < 0)
            return;
        e.Handled = e.SuppressKeyPress = true;
        AnswerButton(index);
    }
}
