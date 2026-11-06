namespace KeyboardUtils;

/// <summary>
/// Microphone choice + calibration: a live level meter with the silence threshold drawn on it, a threshold slider,
/// a "measure noise" button that sets the threshold from a few seconds of room noise, and the note it hears - so
/// the user can tune it by just playing a few keys.
/// </summary>
sealed class MicrophoneSetupForm : AntdUI.Window
{
    const int MinDb = -80;
    const int MaxDb = -10;

    /// <summary>How far above the measured noise the threshold goes - room noise fluctuates.</summary>
    const double NoiseMarginDb = 8;

    readonly List<string> deviceNames = MicrophoneNoteSource.GetDeviceNames().ToList();
    readonly AntdUI.Select deviceBox = new() { Size = new Size(340, 36), List = true, ListAutoWidth = true };
    readonly LevelMeter meter = new() { Dock = DockStyle.Top, Height = 26, Margin = new Padding(0) };
    readonly AntdUI.Slider thresholdSlider = new() { Dock = DockStyle.Top, Height = 32, MinValue = MinDb, MaxValue = MaxDb };
    readonly AntdUI.Label thresholdLabel = new() { Dock = DockStyle.Top, Height = 24 };
    readonly AntdUI.Label noteLabel = new() { Dock = DockStyle.Top, Height = 64, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 26f, FontStyle.Bold) };
    readonly AntdUI.Label statusLabel = new() { Dock = DockStyle.Top, Height = 24, ForeColor = SystemColors.GrayText };
    readonly AntdUI.Button measureButton = new() { AutoSize = true, Radius = 8 };
    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 50 };

    MicrophoneNoteSource? source;
    volatile float latestLevel = MinDb;
    volatile int heardNote = -1;
    List<double>? noiseSamples;
    DateTime noiseUntil;

    public string? DeviceName { get; private set; }
    public double ThresholdDb { get; private set; }

    public MicrophoneSetupForm(string? deviceName, double thresholdDb)
    {
        DeviceName = deviceNames.Contains(deviceName ?? "") ? deviceName : null;
        ThresholdDb = Math.Clamp(thresholdDb, MinDb, MaxDb);

        Text = T("Mikrofon a citlivost", "Microphone and sensitivity");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 510);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        deviceBox.Items.Add(T("(systémový výchozí)", "(system default)"));
        foreach (var name in deviceNames)
            deviceBox.Items.Add(name);
        deviceBox.SelectedIndex = DeviceName != null ? deviceNames.IndexOf(DeviceName) + 1 : 0;

        var deviceRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        deviceRow.Controls.Add(new AntdUI.Label { Text = T("Mikrofon:", "Microphone:"), AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        deviceRow.Controls.Add(deviceBox);

        var help = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            ForeColor = SystemColors.GrayText,
            Text = T("1) Buď chvíli potichu a klikni na \"Změřit šum\".\n2) Zahraj pár jednotlivých not - dole by se měly ukázat správně.\n"
                     + "   Když appka reaguje na šum, posuň práh doprava; když neslyší tiché tóny, doleva.",
                "1) Stay quiet for a moment and click \"Measure noise\".\n2) Play a few single notes - they should show up below.\n"
                     + "   If the app reacts to noise, move the threshold right; if it misses quiet notes, move it left."),
        };

        thresholdSlider.Value = (int)Math.Round(ThresholdDb);
        var measureRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        measureRow.Controls.Add(measureButton);
        measureButton.Text = T("Změřit šum", "Measure noise");

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 8) };
        // Dock=Top stacks in reverse add order: the last added is topmost.
        content.Controls.AddRange([statusLabel, noteLabel, measureRow, thresholdSlider, thresholdLabel, meter, Spacer(10), help, Spacer(8), deviceRow]);

        var ok = new AntdUI.Button { Text = "OK", AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(content);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        deviceBox.SelectedIndexChanged += (_, _) =>
        {
            DeviceName = deviceBox.SelectedIndex > 0 ? deviceNames[deviceBox.SelectedIndex - 1] : null;
            StartSource();
        };
        thresholdSlider.ValueChanged += (_, _) => SetThreshold(thresholdSlider.Value);
        measureButton.Click += (_, _) => StartNoiseMeasurement();
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        uiTimer.Tick += (_, _) => RefreshLive();

        SetThreshold(ThresholdDb);
        noteLabel.Text = "–";
        Load += (_, _) => { StartSource(); uiTimer.Start(); };
    }

    static Control Spacer(int height) => new Panel { Dock = DockStyle.Top, Height = height };

    void SetThreshold(double db)
    {
        ThresholdDb = db;
        meter.ThresholdDb = db;
        if (source != null)
            source.ThresholdDb = db;
        thresholdLabel.Text = T($"Práh ticha: {db:0} dB", $"Silence threshold: {db:0} dB");
    }

    void StartSource()
    {
        StopSource();
        source = new MicrophoneNoteSource(DeviceName, ThresholdDb);
        source.LevelChanged += level =>
        {
            latestLevel = (float)level;
            if (noiseSamples is { } samples)
                lock (samples)
                    samples.Add(level);
        };
        source.NoteOn += note => heardNote = note;
        source.NoteOff += note => { if (heardNote == note) heardNote = -1; };
        try
        {
            source.Start();
            statusLabel.Text = T($"Poslouchám: {source.Name}", $"Listening: {source.Name}");
        }
        catch (NoteSourceException ex)
        {
            statusLabel.Text = ex.Message;
            StopSource();
        }
    }

    void StopSource()
    {
        source?.Dispose();
        source = null;
        heardNote = -1;
        latestLevel = MinDb;
    }

    void StartNoiseMeasurement()
    {
        if (source == null)
            return;
        noiseSamples = [];
        noiseUntil = DateTime.UtcNow.AddSeconds(2.5);
        measureButton.Enabled = false;
        statusLabel.Text = T("Měřím šum... buď potichu.", "Measuring noise... stay quiet.");
    }

    /// <summary>UI thread, every 50 ms: meter, heard note, and the end of a noise measurement.</summary>
    void RefreshLive()
    {
        meter.LevelDb = latestLevel;
        int note = heardNote;
        noteLabel.Text = note >= 0 ? NoteNames.Describe(note) : "–";
        noteLabel.ForeColor = note >= 0 ? Color.FromArgb(255, 152, 0) : SystemColors.GrayText;

        if (noiseSamples != null && DateTime.UtcNow >= noiseUntil)
        {
            var samples = noiseSamples;
            noiseSamples = null;
            measureButton.Enabled = true;
            List<double> sorted;
            lock (samples)
                sorted = samples.Order().ToList();
            if (sorted.Count == 0)
                return;
            // 95th percentile: ignores a single click/cough but covers the steady hum.
            double noise = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))];
            int threshold = (int)Math.Round(Math.Clamp(noise + NoiseMarginDb, MinDb, MaxDb));
            thresholdSlider.Value = threshold;
            SetThreshold(threshold);
            statusLabel.Text = T($"Šum: {noise:0} dB -> práh nastaven na {threshold} dB.", $"Noise: {noise:0} dB -> threshold set to {threshold} dB.");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        uiTimer.Stop();
        StopSource();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            uiTimer.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Horizontal dB bar: green above the threshold (counts as sound), grey below, threshold as a line.</summary>
    sealed class LevelMeter : Control
    {
        double levelDb = MinDb;
        double thresholdDb;

        public LevelMeter() => DoubleBuffered = true;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double LevelDb
        {
            get => levelDb;
            set { levelDb = value; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double ThresholdDb
        {
            get => thresholdDb;
            set { thresholdDb = value; Invalidate(); }
        }

        float XFor(double db) => (float)((Math.Clamp(db, MinDb, 0) - MinDb) / -MinDb * (ClientSize.Width - 1));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(230, 230, 230));
            using var fill = new SolidBrush(levelDb >= thresholdDb ? Color.FromArgb(15, 157, 88) : Color.FromArgb(160, 160, 160));
            g.FillRectangle(fill, 0, 0, XFor(levelDb), ClientSize.Height);
            using var line = new Pen(Color.FromArgb(219, 68, 55), 2);
            float x = XFor(thresholdDb);
            g.DrawLine(line, x, 0, x, ClientSize.Height);
        }
    }
}
