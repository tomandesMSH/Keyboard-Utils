namespace KeyboardUtils;

/// <summary>The transport row above the song: rewind / fast forward, play, stop, the loop, playback speed and the metronome's count-in.</summary>
sealed partial class MainForm
{
    /// <summary>
    /// Transport icons as SVG (AntdUI draws them in the button's text colour). Text characters like ▶ come out as
    /// boxed emoji.
    /// </summary>
    static class Icons
    {
        const string Head = "<svg viewBox=\"0 0 1024 1024\"><path d=\"";
        const string Tail = "\"/></svg>";
        public const string Play = Head + "M336 208v608l480-304z" + Tail;
        public const string Pause = Head + "M296 208h144v608H296zM584 208h144v608H584z" + Tail;
        public const string Stop = Head + "M288 288h448v448H288z" + Tail;
        public const string Rewind = Head + "M512 272v480L208 512zM816 272v480L512 512z" + Tail;
        public const string FastForward = Head + "M208 272v480l304-240zM512 272v480l304-240z" + Tail;

        /// <summary>Small triangles: options shown (pointing down) / hidden (pointing right).</summary>
        public const string Expanded = Head + "M240 384h544L512 672z" + Tail;
        public const string Collapsed = Head + "M384 240v544L672 512z" + Tail;

        /// <summary>A circular arrow (↺): back to the default.</summary>
        /// <summary>Two arrows chasing each other round a circle: repeat.</summary>
        public const string Loop = Head + "M202 625L226 677L259 724L300 765L347 798L399 822L455 837L512 842L569 837L625 822L677 798L724 765L765 724L696 666L666 696L632 720L594 738L554 748L512 752L470 748L430 738L392 720L358 696L328 666L304 632L286 594z"
            + "M822 772L827 580L638 618z"
            + "M822 399L798 347L765 300L724 259L677 226L625 202L569 187L512 182L455 187L399 202L347 226L300 259L259 300L328 358L358 328L392 304L430 286L470 276L512 272L554 276L594 286L632 304L666 328L696 358L720 392L738 430z"
            + "M202 252L197 444L386 406z" + Tail;

        public const string Reset = Head + "M455 187L399 202L347 226L300 259L259 300L226 347L202 399L187 455L182 512L187 569L202 625L226 677L259 724L300 765L347 798L399 822L455 837L512 842L569 837L625 822L677 798L724 765L765 724L798 677L822 625L837 569L842 512L837 455L822 399L798 347L765 300L688 364L711 397L728 433L739 472L742 512L739 552L728 591L711 627L688 660L660 688L627 711L591 728L552 739L512 742L472 739L433 728L397 711L364 688L336 660L313 627L296 591L285 552L282 512L285 472L296 433L313 397L336 364L364 336L397 313L433 296L472 285z"
            + "M818 255L630 217L635 409z" + Tail;
    }

    static AntdUI.Button TransportButton(string icon) => new()
    {
        IconSvg = icon,
        IconRatio = 1.2f,
        Size = new Size(38, 32),
        Radius = 8,
        Enabled = false,
        Margin = new Padding(0, 0, 4, 0),
    };

    static AntdUI.Button LetterButton(string letter) => new()
    {
        Text = letter,
        Size = new Size(38, 32),
        Radius = 8,
        Enabled = false,
        Margin = new Padding(0, 0, 4, 0),
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
    };

    static Label TransportCaption(string text, int width = 38) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(width, 18),
        TextAlign = ContentAlignment.TopCenter,
        Margin = new Padding(0, 1, 4, 0),
        ForeColor = SystemColors.GrayText,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 8.5f),
    };

    /// <summary>
    /// Above the keyboard / sheet music: rewind and fast forward (click for 5 s, hold to keep going), play/pause,
    /// stop and the playback speed.
    /// </summary>
    Control BuildTransportRow()
    {
        Control WithCaption(Control button, Control caption)
        {
            var column = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            column.Controls.AddRange([button, caption]);
            return column;
        }

        var speedGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(14, 0, 0, 0) };
        // Compact: the label with the exact value next to it, the slider and reset button underneath.
        var speedHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        speedHeader.Controls.AddRange([speedLabel, speedBox, speedArrows]);
        var speedControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        speedControls.Controls.AddRange([speedSlider, speedResetButton]);
        speedGroup.Controls.AddRange([speedHeader, speedControls]);
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(8, 6, 8, 2) };
        var loopGroup = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(14, 0, 0, 0) };
        loopGroup.Controls.AddRange([WithCaption(loopButton, loopCaption), WithCaption(loopStartButton, loopStartCaption), WithCaption(loopEndButton, loopEndCaption)]);
        // Metronome (with the arrow that folds its options away) and key labels side by side; under them, while the
        // metronome is checked: "without a song" with its tempo, and under that tempo detection and the time signature.
        var metronomeGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(24, 0, 0, 0) };
        var checkboxRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        metronomeBox.Margin = new Padding(0, 6, 0, 0);
        keyLabelsBox.Margin = new Padding(20, 6, 0, 0);
        checkboxRow.Controls.AddRange([metronomeBox, metronomeExpander, keyLabelsBox]);
        metronomeOptions.Controls.AddRange([standaloneBox, bpmBox, bpmArrows]);
        meterBox.Margin = new Padding(6, 0, 0, 0);
        metronomeDetectRow.Controls.AddRange([tapButton, meterBox]);
        // Re-aligned whenever the row above is laid out again (a checkbox's width settling, another language...).
        metronomeOptions.Layout += (_, _) => AlignDetectRow();
        metronomeGroup.Controls.AddRange([checkboxRow, metronomeOptions, metronomeDetectRow]);
        metronomeExpander.Click += (_, _) =>
        {
            config.MetronomeOptionsShown = !config.MetronomeOptionsShown;
            config.Save();
            UpdateMetronomeOptions();
        };
        row.Controls.AddRange([WithCaption(rewindButton, rewindCaption), WithCaption(forwardButton, forwardCaption), playPauseButton, stopButton, loopGroup, speedGroup, metronomeGroup]);

        metronomeBox.Checked = playbackEngine.Metronome = config.Metronome;
        metronomeBox.CheckedChanged += (_, _) =>
        {
            if (metronomeBox.Checked == config.Metronome)
                return;
            playbackEngine.Metronome = config.Metronome = metronomeBox.Checked;
            config.Save();
            if (!config.Metronome)
                StopStandalone();
            UpdateMetronomeOptions();
        };
        standaloneBox.Checked = config.MetronomeStandalone;
        standaloneBox.CheckedChanged += (_, _) =>
        {
            if (standaloneBox.Checked == config.MetronomeStandalone)
                return;
            config.MetronomeStandalone = standaloneBox.Checked;
            config.Save();
            if (!config.MetronomeStandalone)
                StopStandalone();
            UpdateMetronomeOptions();
        };
        bpmBox.Value = Math.Clamp(config.MetronomeBpm, StandaloneMetronome.MinBpm, StandaloneMetronome.MaxBpm);
        bpmBox.ValueChanged += (_, _) => SetMetronomeBpm((int)Math.Round(bpmBox.Value));
        bpmArrows.Step += direction => SetMetronomeBpm(config.MetronomeBpm + direction);
        tapButton.Click += (_, _) => TapMetronome();
        foreach (var meter in StandaloneMetronome.Meters)
            meterBox.Items.Add(meter);
        meterBox.SelectedIndex = Math.Max(0, Array.IndexOf(StandaloneMetronome.Meters, config.MetronomeMeter));
        meterBox.SelectedIndexChanged += (_, _) =>
        {
            if (meterBox.SelectedIndex >= 0)
                SetMetronomeMeter(StandaloneMetronome.Meters[meterBox.SelectedIndex]);
        };
        UpdateMetronomeOptions();

        loopButton.Margin = new Padding((loopCaption.Width - loopButton.Width) / 2, 0, 4 + (loopCaption.Width - loopButton.Width) / 2, 0);
        loopButton.Click += (_, _) => ToggleLoop();
        loopStartButton.Click += (_, _) => SetLoopStart(PlaybackSeconds);
        loopEndButton.Click += (_, _) => SetLoopEnd(PlaybackSeconds);

        keyLabelsBox.Checked = pianoRoll.AllKeyLabels = config.AllKeyLabels;
        keyLabelsBox.CheckedChanged += (_, _) =>
        {
            if (keyLabelsBox.Checked == config.AllKeyLabels)
                return;
            pianoRoll.AllKeyLabels = config.AllKeyLabels = keyLabelsBox.Checked;
            config.Save();
        };

        // Held down, RW / FF wind the song faster and faster; released, it carries on as before.
        seekRepeatTimer.Tick += (_, _) => ScrubTick();
        void HoldToScrub(AntdUI.Button button, int direction)
        {
            button.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && button.Enabled)
                    StartScrub(direction);
            };
            button.MouseUp += (_, _) => StopScrub();
            button.MouseLeave += (_, _) => StopScrub();
        }
        HoldToScrub(rewindButton, -1);
        HoldToScrub(forwardButton, 1);

        // Slider, box and reset button all set the same speed; SetSpeed keeps the other two in step.
        speedSlider.ValueChanged += (_, _) => SetSpeed(speedSlider.Value);
        speedBox.ValueChanged += (_, _) => SetSpeed((int)Math.Round(speedBox.Value));
        speedResetButton.Click += (_, _) => SetSpeed(100);
        // Arrows step by 5 % (to the next multiple of 5, so 73 goes to 75 / 70).
        speedArrows.Step += StepSpeed;
        return row;
    }

    /// <summary>How many times faster than playing RW / FF wind after being held <paramref name="heldSeconds"/>: 2× doubling every second, up to 16×.</summary>
    internal static double ScrubRate(double heldSeconds) => Math.Min(16, 2 * Math.Pow(2, heldSeconds));

    void StartScrub(int direction)
    {
        StopScrub();
        scrubDirection = direction;
        scrubLastSeconds = 0;
        scrubClock.Restart();
        seekRepeatTimer.Start();
        ScrubTick();
    }

    /// <summary>
    /// FF over a playing song plays it faster (you hear it wind); otherwise - RW, paused, stems (audio can't be
    /// sped up) - the position moves at that rate.
    /// </summary>
    void ScrubTick()
    {
        if ((currentScore == null && currentStems == null) || learning != null || recorder != null)
        {
            StopScrub();
            return;
        }
        double held = scrubClock.Elapsed.TotalSeconds;
        double rate = ScrubRate(held);
        double elapsed = held - scrubLastSeconds;
        scrubLastSeconds = held;
        if (scrubDirection > 0 && PlaybackRunning && !StemsPlay)
        {
            playbackEngine.Speed = playbackSpeed * rate;
            scrubSpedUp = true;
        }
        else
        {
            // Playing on meanwhile moves it 1× forward: make up for that, so RW goes back at the full rate.
            double playing = PlaybackRunning ? 1 : 0;
            SeekBy(scrubDirection > 0 ? (rate - playing) * elapsed : -(rate + playing) * elapsed);
        }
        SetStatus((scrubDirection > 0 ? T("Přetáčení vpřed ", "Fast forward ") : T("Přetáčení zpět ", "Rewind ")) + $"{rate:0}×  {FormatTime(PlaybackSeconds)}");
    }

    void StopScrub()
    {
        seekRepeatTimer.Stop();
        scrubClock.Reset();
        if (scrubSpedUp)
        {
            scrubSpedUp = false;
            playbackEngine.Speed = playbackSpeed;
        }
    }

    /// <summary>Moves the song by <paramref name="seconds"/> (negative = back); not while learning or recording.</summary>
    void SeekBy(double seconds)
    {
        if ((currentScore == null && currentStems == null) || learning != null || recorder != null)
            return;
        double duration = StemsPlay ? stemPlaybackEngine.DurationSeconds : currentScore!.DurationSeconds;
        double target = Math.Clamp(PlaybackSeconds + seconds, 0, duration);
        PlaybackSeek(target);
        SetViewTime(target);
    }

    void SetSpeed(int percent)
    {
        percent = Math.Clamp(percent, MinSpeed, MaxSpeed);
        // Setting the other control raises its ValueChanged again; the same value then changes nothing.
        if (speedSlider.Value != percent)
            speedSlider.Value = percent;
        if (speedBox.Value != percent)
            speedBox.Value = percent;
        playbackSpeed = percent / 100.0;
        if (learning == null)
            playbackEngine.Speed = playbackSpeed;
    }

    void UpdateSpeedLabel()
    {
        speedLabel.Text = T("Rychlost přehrávání:", "Playback speed:");
        keyLabelsBox.Text = T("Popisky kláves", "Key labels");
        metronomeBox.Text = T("Metronom", "Metronome");
        standaloneBox.Text = T("Bez podkladu", "Without a song");
        toolTip.SetToolTip(standaloneBox, T("Jen metronom: Přehrát ho spustí v tempu vedle, i když není otevřená žádná skladba. ↑ / ↓ mění tempo o 5.",
            "The metronome alone: Play starts it at the tempo next to it, even with no song open. ↑ / ↓ change the tempo by 5."));
        toolTip.SetToolTip(bpmBox, T("Tempo metronomu bez podkladu (doby za minutu)", "Tempo of the metronome on its own (beats per minute)"));
        tapButton.Text = T("Detekce", "Detect");
        toolTip.SetToolTip(tapButton, T("Detekce tempa: klikej (nebo mačkej T) v rytmu, stačí dvakrát", "Tempo detection: click (or press T) in time, twice is enough"));
        toolTip.SetToolTip(metronomeExpander, T("Skrýt / ukázat volby metronomu (metronom zůstane zapnutý)", "Hide / show the metronome's options (the metronome stays on)"));
        toolTip.SetToolTip(meterBox, T("Takt metronomu bez podkladu; v 6/8 kliká osminy a BPM je tečkovaná čtvrťová",
            "Time signature of the metronome on its own; 6/8 clicks eighths and the BPM is the dotted quarter"));
        if (availableUpdate is { } update)
            ShowUpdate(update); // the button's text in the new language
        toolTip.SetToolTip(metronomeBox, T("Klikání na každou dobu a takt předehry před začátkem (ne u stop). (M)",
            "A click on every beat and a bar of count-in before starting (not with stems). (M)"));
        loopCaption.Text = T("Smyčka", "Loop");
        loopStartCaption.Text = T("od", "from");
        loopEndCaption.Text = T("do", "to");
        toolTip.SetToolTip(loopButton, T("Opakovat úsek (zapnout / vypnout). Úsek označíš tažením přes takty v notách, nebo tlačítky A a B.",
            "Repeat a section (on / off). Pick it by dragging across bars in the sheet music, or with A and B.") + " (L)");
        toolTip.SetToolTip(loopStartButton, T("Začátek smyčky = aktuální místo (A)", "Loop start = current position (A)"));
        toolTip.SetToolTip(loopEndButton, T("Konec smyčky = aktuální místo (B)", "Loop end = current position (B)"));
        toolTip.SetToolTip(speedResetButton, T("Vrátit na 100 % (R)", "Reset to 100 % (R)"));
        toolTip.SetToolTip(rewindButton, T("Přetáčet zpět - čím déle držíš, tím rychleji (← skočí o 5 s)", "Rewind - the longer you hold it, the faster (← jumps 5 s)"));
        toolTip.SetToolTip(forwardButton, T("Přetáčet vpřed - čím déle držíš, tím rychleji (→ skočí o 5 s)", "Fast forward - the longer you hold it, the faster (→ jumps 5 s)"));
        toolTip.SetToolTip(playPauseButton, T("Přehrát / pauza (mezerník)", "Play / pause (Space)"));
        toolTip.SetToolTip(stopButton, T("Zastavit (Home)", "Stop (Home)"));
        toolTip.SetToolTip(speedSlider, T("Rychlost přehrávání (↑ / ↓)", "Playback speed (↑ / ↓)"));
        toolTip.SetToolTip(keyLabelsBox, T("Názvy not na všech bílých klávesách (K)", "Note names on every white key (K)"));
    }

    /// <summary>
    /// What the transport row can do right now. The stems' audio can't be slowed down without changing its pitch,
    /// and learning mode sets its own speed - so the speed slider is off then.
    /// </summary>
    void UpdateTransport()
    {
        bool song = currentScore != null || currentStems != null;
        playPauseButton.Enabled = stopButton.Enabled = song || (StandaloneOn && learning == null && recorder == null);
        rewindButton.Enabled = forwardButton.Enabled = song && learning == null && recorder == null;
        // The loop is set up before learning starts; a running session keeps the one it started with.
        loopButton.Enabled = loopStartButton.Enabled = loopEndButton.Enabled = sheet.LoopSelectable = song && learning == null && recorder == null;
        speedSlider.Enabled = speedBox.Enabled = speedArrows.Enabled = speedResetButton.Enabled = speedLabel.Enabled = !StemsPlay && learning == null;
    }

    (double Start, double End)? LoopRange => loopStart is { } start && loopEnd is { } end && end > start ? (start, end) : null;

    /// <summary>Shows the loop in both views and on the loop button (highlighted while on).</summary>
    void ApplyLoop()
    {
        if (LoopRange == null)
            loopOn = false;
        var shown = loopOn ? LoopRange : null;
        pianoRoll.Loop = shown;
        sheet.Loop = shown;
        loopButton.Type = loopOn ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
    }

    void ToggleLoop()
    {
        if (LoopRange is not { } range)
        {
            SetStatus(T("Nejdřív označ úsek: táhni přes takty v notách, nebo nastav začátek (A) a konec (B) během přehrávání.",
                "Pick a section first: drag across bars in the sheet music, or set the start (A) and end (B) while it plays."));
            return;
        }
        loopOn = !loopOn;
        ApplyLoop();
        SetStatus(loopOn ? T($"Smyčka zapnutá: {FormatTime(range.Start)}–{FormatTime(range.End)}", $"Loop on: {FormatTime(range.Start)}–{FormatTime(range.End)}")
            : T("Smyčka vypnutá.", "Loop off."));
    }

    void SetLoopStart(double seconds)
    {
        loopStart = seconds;
        if (loopEnd <= seconds)
            loopEnd = null;
        loopOn = LoopRange != null;
        ApplyLoop();
        SetStatus(LoopRange is { } range
            ? T($"Smyčka: {FormatTime(range.Start)}–{FormatTime(range.End)}", $"Loop: {FormatTime(range.Start)}–{FormatTime(range.End)}")
            : T($"Začátek smyčky: {FormatTime(seconds)}. Teď na konci úseku stiskni B.", $"Loop start: {FormatTime(seconds)}. Now press B at the end of the section."));
    }

    void SetLoopEnd(double seconds)
    {
        if (loopStart is not { } start || seconds <= start + 0.2)
        {
            SetStatus(T("Konec smyčky musí být za začátkem - nejdřív nastav začátek (A).", "The loop end must come after its start - set the start (A) first."));
            return;
        }
        loopEnd = seconds;
        loopOn = true;
        ApplyLoop();
        SetStatus(T($"Smyčka: {FormatTime(start)}–{FormatTime(seconds)}", $"Loop: {FormatTime(start)}–{FormatTime(seconds)}"));
    }

    void SetLoop(double start, double end)
    {
        loopStart = start;
        loopEnd = end;
        loopOn = true;
        ApplyLoop();
        SetStatus(T($"Smyčka: {FormatTime(start)}–{FormatTime(end)}. Vypneš ji tlačítkem Smyčka.", $"Loop: {FormatTime(start)}–{FormatTime(end)}. Turn it off with the Loop button."));
    }

    /// <summary>A new song: the old loop's times mean nothing in it.</summary>
    void ClearLoop()
    {
        loopStart = loopEnd = null;
        loopOn = false;
        ApplyLoop();
    }

    static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss\.f");

    /// <summary>Playback reaching the loop's end goes back to its start (learning mode restarts its own pass instead).</summary>
    void KeepPlaybackInLoop()
    {
        if (!loopOn || LoopRange is not { } range || learning != null || recorder != null || !PlaybackRunning)
            return;
        if (PlaybackSeconds >= range.End)
            PlaybackSeek(range.Start);
    }

    /// <summary>Where the metronome's beats are: the song's own tempo, or the recording tempo for a recording.</summary>
    ScoreTiming ClickTiming => currentScore?.Timing ?? ScoreTiming.Constant(config.CreateTempoBpm);

    bool MetronomeOn => config.Metronome && !StemsPlay;

    /// <summary>A bar of clicks at the tempo found at <paramref name="atSeconds"/>, then <paramref name="then"/>.</summary>
    void StartCountIn(double atSeconds, double speed, Action then)
    {
        CancelCountIn();
        var timing = ClickTiming;
        double interval = timing.ClickQuarters / timing.QuartersPerSecondAt(atSeconds) / speed;
        var cts = countIn = new CancellationTokenSource();
        _ = RunCountIn();
        async Task RunCountIn()
        {
            bool done = await CountIn.Run(timing.ClicksPerBar, interval, playbackEngine.PlayClick, cts.Token);
            if (!done || countIn != cts)
                return;
            countIn = null;
            then();
            UpdatePlayPauseButton();
        }
    }

    void CancelCountIn()
    {
        countIn?.Cancel();
        countIn = null;
    }

    /// <summary>Score time of a bar of count-in starting at <paramref name="atSeconds"/> (the learning lead-in).</summary>
    double CountInSeconds(double atSeconds)
    {
        var timing = ClickTiming;
        return timing.ClicksPerBar * timing.ClickQuarters / timing.QuartersPerSecondAt(atSeconds);
    }
}
