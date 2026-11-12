using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// The tuner: listens through the microphone set in Settings (on its own, so it works whatever the note input is)
/// and shows the note and how many cents off it is. For string instruments the open strings are shown - click one to
/// hear it. The reference A4 (440 Hz, or an orchestra's 442...) is kept in <see cref="AppConfig.TunerA4"/>.
/// </summary>
sealed class TunerForm : AntdUI.Window
{
    const int SampleRate = 44100;
    static readonly int[] References = [430, 435, 438, 439, 440, 441, 442, 443, 444, 446];

    readonly AppConfig config;
    readonly Instrument instrument;
    readonly Action<int>? playNote;
    readonly TunerControl display = new() { Dock = DockStyle.Fill };
    readonly AntdUI.Select referenceBox = new() { Size = new Size(150, 36), List = true };
    readonly AntdUI.Label status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 9, 0, 0) };
    readonly TunerAnalyzer analyzer;
    readonly float[] samples = new float[SampleRate];
    WaveIn? waveIn;

    double A4 => References[Math.Max(0, referenceBox.SelectedIndex)];

    /// <param name="playNote">Plays a pitch (a string's reference tone); null = silent.</param>
    public TunerForm(AppConfig config, Instrument instrument, Action<int>? playNote)
    {
        this.config = config;
        this.instrument = instrument;
        this.playNote = playNote;
        Text = T("Ladička", "Tuner") + (instrument.IsPiano ? "" : " – " + instrument.Name);
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 480);
        MaximizeBox = MinimizeBox = false;

        referenceBox.Items.AddRange(References.Select(hz => (object)$"{NoteNames.Name(69)} = {hz} Hz").ToArray());
        int saved = Array.IndexOf(References, (int)Math.Round(config.TunerA4));
        referenceBox.SelectedIndex = saved >= 0 ? saved : Array.IndexOf(References, 440);
        referenceBox.SelectedIndexChanged += (_, _) =>
        {
            config.TunerA4 = A4;
            config.Save();
            Redraw(analyzer!.Frequency);
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(16, 10, 16, 6) };
        top.Controls.AddRange([referenceBox, status]);

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.Add(close);

        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
        box.Controls.Add(display);
        Controls.Add(box);
        Controls.Add(buttons);
        Controls.Add(top);
        Controls.Add(new TitleBar(this));
        CancelButton = close;

        analyzer = new TunerAnalyzer(SampleRate) { ThresholdDb = config.MicrophoneThresholdDb };
        analyzer.Analysed += hz =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() => Redraw(hz));
        };
        display.StringClicked += pitch => playNote?.Invoke(pitch);
        Shown += (_, _) => StartListening();
        FormClosed += (_, _) => StopListening();
        Redraw(null);
    }

    void Redraw(double? hz) => display.Show(hz, A4, instrument);

    /// <summary>Feeds samples straight in (tests: a sine instead of the microphone).</summary>
    public void Feed(ReadOnlySpan<float> input) => analyzer.AddSamples(input);

    void StartListening()
    {
        if (WaveIn.DeviceCount == 0)
        {
            status.Text = T("Nenašel jsem žádný mikrofon.", "No microphone found.");
            return;
        }
        try
        {
            waveIn = new WaveIn
            {
                DeviceNumber = MicrophoneNoteSource.FindDevice(config.MicrophoneDevice),
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 20,
            };
            waveIn.DataAvailable += OnData;
            waveIn.StartRecording();
            status.Text = T("Poslouchám mikrofon", "Listening to the microphone");
        }
        catch (NAudio.MmException ex)
        {
            StopListening();
            status.Text = T("Mikrofon nejde otevřít: ", "Could not open the microphone: ") + ex.Message;
        }
    }

    void StopListening()
    {
        if (waveIn == null)
            return;
        waveIn.DataAvailable -= OnData;
        try { waveIn.StopRecording(); } catch (NAudio.MmException) { }
        waveIn.Dispose();
        waveIn = null;
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var bytes = e.BufferSpan[..e.BytesRecorded];
        int count = Math.Min(bytes.Length / 2, samples.Length);
        for (int i = 0; i < count; i++)
            samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
        analyzer.AddSamples(samples.AsSpan(0, count));
    }
}
