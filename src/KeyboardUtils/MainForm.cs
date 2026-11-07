using System.Threading.Channels;

namespace KeyboardUtils;

sealed partial class MainForm : AntdUI.Window
{

    readonly AppConfig config;
    IMacroKeyboardInterceptor? interceptor;
    INoteSource? noteSource;
    LearningSession? learning;
    string learningStatus = "";

    /// <summary>The big message over the song area, for what a macro key or a shortcut did.</summary>
    readonly ToastOverlay toast = new();

    /// <summary>True while a macro key's or a shortcut's action runs: its status message also shows big.</summary>
    bool fromControl;

    readonly PracticeHistory practiceHistory = PracticeHistory.Load();

    /// <summary>What the running learning session practices, for its entry in the history.</summary>
    (LearningMode Mode, int SpeedPercent, string[] Parts, (double Start, double End)? Range) learningInfo;

    /// <summary>The session last written to the history - a session ends through more than one path.</summary>
    LearningSession? recordedSession;
    NoteRecorder? recorder;
    string recordingTitle = "";
    bool resumeAfterSeek;

    /// <summary>The recording shown as the current song until it's saved - removing it then asks first.</summary>
    Score? unsavedRecording;

    /// <summary>Stems whose notes are being converted right now, so a rebuilt parts panel still shows them as busy.</summary>
    readonly HashSet<string> convertingTracks = [];
    readonly PlaybackEngine playbackEngine = new();
    readonly StemPlaybackEngine stemPlaybackEngine = new();
    readonly Channel<Binding> actionQueue = Channel.CreateUnbounded<Binding>(new UnboundedChannelOptions { SingleReader = true });
    readonly List<string> outputDeviceNames = PlaybackEngine.GetOutputDeviceNames().ToList();

    // Read from the interception thread; replaced wholesale, never mutated.
    volatile IReadOnlyDictionary<int, Binding> bindingsByKey = new Dictionary<int, Binding>();

    // Pairing a knob: after picking a step action, the next other key gets the opposite one (read on the interception thread).
    volatile Binding? pendingOpposite;
    volatile int pendingOppositeFrom;
    volatile bool capturingKey;

    // A song is either a note-based Score (MIDI/MusicXML - has a piano roll, supports a playlist) or a
    // StemSet (a folder of synced .wav tracks - audio only, no piano roll, no playlist). Mutually exclusive.
    List<string> playlist = [];
    int playlistIndex = -1;
    Score? currentScore;
    StemSet? currentStems;

    readonly AntdUI.Label macroInfoLabel = new() { AutoSize = false, Dock = DockStyle.Top, Height = 54, Padding = new Padding(6), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Label keyboardLabel = new() { AutoSize = true };
    readonly AntdUI.Button forgetButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Label songLabel = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(8, 0, 6, 0), Text = "" };
    readonly AntdUI.Label keyboardSizeLabel = new() { AutoSize = true, Margin = new Padding(0, 7, 6, 0), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Slider keyboardSizeSlider = new()
    {
        MinValue = PianoRollControl.MinKeyWidth,
        MaxValue = PianoRollControl.MaxKeyWidth,
        Size = new Size(160, 28),
        Margin = new Padding(0, 2, 0, 0),
    };
    readonly AntdUI.Label statusLabel = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
    readonly AntdUI.Button learnButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button addButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button editButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button removeButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button exportButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button importButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button openSongButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button importStemsButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button removeSongButton = new() { AutoSize = true, Radius = 8, Visible = false };
    // Transport row above the song: rewind, fast forward, play/pause, stop, playback speed.
    readonly AntdUI.Button rewindButton = TransportButton(Icons.Rewind);
    readonly AntdUI.Button forwardButton = TransportButton(Icons.FastForward);
    readonly AntdUI.Button playPauseButton = TransportButton(Icons.Play);
    readonly AntdUI.Button stopButton = TransportButton(Icons.Stop);
    readonly Label rewindCaption = TransportCaption("RW");
    readonly Label forwardCaption = TransportCaption("FF");

    // Loop: a stretch of the song played (or practised) over and over. Its start (A) and end (B) are set from the
    // transport row or by dragging across bars in the sheet music.
    readonly AntdUI.Button loopButton = TransportButton(Icons.Loop);
    readonly AntdUI.Button loopStartButton = LetterButton("A");
    readonly AntdUI.Button loopEndButton = LetterButton("B");
    // Wider than the others: "Smyčka" doesn't fit under one button; the button is centred above it.
    readonly Label loopCaption = TransportCaption("", width: 52);
    readonly Label loopStartCaption = TransportCaption("");
    readonly Label loopEndCaption = TransportCaption("");
    double? loopStart, loopEnd;
    bool loopOn;

    /// <summary>Learning a loop: which pass this is, and how the one before went.</summary>
    int loopPass = 1;
    double? lastPassPercent;
    readonly AntdUI.Label speedLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
    readonly AntdUI.Slider speedSlider = new() { MinValue = MinSpeed, MaxValue = MaxSpeed, Value = 100, Size = new Size(170, 22), Margin = new Padding(0, 4, 6, 0) };

    /// <summary>The exact speed typed in, for when the slider is too fiddly. Takes effect on Enter or leaving the box.</summary>
    readonly AntdUI.InputNumber speedBox = new() { Minimum = MinSpeed, Maximum = MaxSpeed, Value = 100, Size = new Size(84, 30), Margin = new Padding(0), SuffixText = "%", ShowControl = false };
    readonly SpinArrows speedArrows = new() { Size = new Size(20, 30), Margin = new Padding(1, 0, 0, 0) };
    readonly AntdUI.Button speedResetButton = new() { IconSvg = Icons.Reset, IconRatio = 1.1f, Size = new Size(30, 28), Radius = 8, Margin = new Padding(0) };
    readonly ToolTip toolTip = new();
    const int MinSpeed = 25, MaxSpeed = 200;
    readonly AntdUI.Checkbox keyLabelsBox = new() { AutoSize = true, Margin = new Padding(16, 6, 0, 0) };
    readonly AntdUI.Checkbox metronomeBox = new() { AutoSize = true, Margin = new Padding(24, 6, 0, 0) };

    /// <summary>The count-in running before playback (or a learning pass) starts; null when none is.</summary>
    CancellationTokenSource? countIn;
    // RW / FF held down: the song winds faster the longer it's held.
    readonly System.Windows.Forms.Timer seekRepeatTimer = new() { Interval = 40 };
    readonly System.Diagnostics.Stopwatch scrubClock = new();
    int scrubDirection;
    double scrubLastSeconds;

    /// <summary>FF over a playing song speeds the playback up (heard); this says it's to be set back.</summary>
    bool scrubSpedUp;

    // The metronome on its own ("without accompaniment"), shown under the Metronome box while it's checked.
    readonly AntdUI.Checkbox standaloneBox = new() { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    readonly AntdUI.InputNumber bpmBox = new()
    {
        Minimum = StandaloneMetronome.MinBpm, Maximum = StandaloneMetronome.MaxBpm, Value = 100,
        Size = new Size(92, 30), Margin = new Padding(6, 0, 0, 0), SuffixText = "BPM", ShowControl = false,
    };
    readonly SpinArrows bpmArrows = new() { Size = new Size(20, 30), Margin = new Padding(1, 0, 0, 0) };
    readonly FlowLayoutPanel metronomeOptions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    StandaloneMetronome? standaloneMetronome;

    StandaloneMetronome Clicker => standaloneMetronome ??= new StandaloneMetronome(playbackEngine.PlayClick)
    {
        Bpm = config.MetronomeBpm,
    };

    /// <summary>"Without accompaniment" is on: Play runs the metronome alone, the song doesn't play.</summary>
    bool StandaloneOn => config.Metronome && config.MetronomeStandalone;

    bool StandaloneRunning => standaloneMetronome?.IsRunning ?? false;

    /// <summary>Playback speed from the slider (1 = as written). Learning mode in tempo uses its own speed meanwhile.</summary>
    double playbackSpeed = 1;
    readonly AntdUI.Button listenButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button learningButton = new() { AutoSize = true, Enabled = false, Type = AntdUI.TTypeMini.Success, Radius = 8 };
    readonly AntdUI.Button createButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Error, Radius = 8 };

    /// <summary>Shown while the open song is an unsaved recording (or converted audio): saving it is the next step.</summary>
    readonly AntdUI.Button saveRecordingButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Visible = false };
    // Plain WinForms labels: AntdUI.Label's AutoSize measures a bit short and clipped the last letter
    // ("Přehrávání" showed as "Přehrávan|"); Label measures and draws with the same GDI text renderer.
    readonly Label loadGroupLabel = CaptionLabel();
    readonly Label playingGroupLabel = CaptionLabel();
    readonly AntdUI.Checkbox enabledBox = new() { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    readonly AntdUI.Label languageLabel = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    // List = true: a pick-only dropdown. Without it AntdUI.Select is an editable text box with suggestions.
    readonly AntdUI.Select languageBox = new() { Size = new Size(110, 36), Margin = new Padding(3, 0, 3, 3), List = true };
    readonly FlowLayoutPanel partsPanel = new() { Dock = DockStyle.Right, Width = 180, AutoScroll = true, Padding = new Padding(6), FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
    readonly PianoRollControl pianoRoll = new() { Dock = DockStyle.Fill };
    readonly SheetMusicControl sheet = new() { Dock = DockStyle.Fill };

    /// <summary>Keyboard on the left, sheet music on the right; one side is collapsed for the single views.</summary>
    readonly ThemedSplitContainer songViews = new() { Dock = DockStyle.Fill, SplitterWidth = 4 };
    readonly AntdUI.Segmented viewSwitch = new() { Size = new Size(240, 30), Margin = new Padding(0, 1, 0, 0) };
    bool applyingSplit;
    int liveNoteCount = -1;
    readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 33 };
    readonly AntdUI.Tabs tabs = new() { Dock = DockStyle.Fill };
    readonly AntdUI.TabPage songTab = new();
    readonly AntdUI.TabPage bindingsTab = new();
    readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    readonly FlowLayoutPanel menuBar = new() { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(248, 248, 248), Padding = new Padding(4, 3, 4, 3), WrapContents = false };
    readonly MenuBarButton fileMenuButton = new();
    readonly MenuBarButton editMenuButton = new();
    readonly MenuBarButton settingsMenuButton = new();
    readonly MenuBarButton macroSetupMenuButton = new();

    public MainForm(AppConfig config, IMacroKeyboardInterceptor? interceptor)
    {
        this.config = config;
        this.interceptor = interceptor;

        Text = "Keyboard Utils";
        Icon = AppIcon.Icon;
        Size = new Size(900, 620);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        ApplyTexts();

        // The macro keyboard is entirely optional - no spare keyboard needed, no module download forced, no
        // nagging. It only does anything once the user deliberately sets it up from the "Makro klávesnice" tab.
        if (interceptor != null)
            WireInterceptor();

        enabledBox.Checked = config.MacroEnabled;
        playbackEngine.SetOutputDevice(config.MidiOutputDevice);
        playbackEngine.Finished += () => Post(() => SetStatus(T("Konec skladby.", "End of song.")));
        stemPlaybackEngine.Finished += () => Post(() => SetStatus(T("Konec skladby.", "End of song.")));
        playbackTimer.Tick += (_, _) =>
        {
            if (learning != null)
                TickLearning();
            else if (recorder != null)
                TickCreateMode();
            else
            {
                KeepPlaybackInLoop();
                if (currentScore != null)
                    SetViewTime(PlaybackSeconds);
            }
            UpdatePlayPauseButton();
            UpdateSaveRecordingButton();
        };
        playbackTimer.Start();
        _ = Task.Run(ActionLoop);
    }

    void BuildLayout()
    {
        list.Columns.Add("", 150);
        list.Columns.Add("", 420);
        foreach (var language in Enum.GetValues<AppLanguage>())
            languageBox.Items.Add(DisplayName(language));
        languageBox.SelectedIndex = (int)Lang.Current;

        // --- Song tab: grouped controls + piano roll + per-part mute panel ---
        // Each group has a caption so it's clear what its buttons act on. The row wraps whole groups when the
        // window is too narrow.
        var songControlsRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        songControlsRow.Controls.AddRange(
        [
            ControlGroup(loadGroupLabel, openSongButton, importStemsButton, removeSongButton),
            ControlGroup(playingGroupLabel, listenButton, learningButton, createButton, saveRecordingButton),
        ]);
        // Song title on the left, the view switch on the right of the same row (Fill first, then Right).
        var viewSwitchGroup = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 6, 0) };
        viewSwitchGroup.Controls.Add(viewSwitch);
        var songInfoRow = new Panel { Dock = DockStyle.Top, Height = 34 };
        songInfoRow.Controls.Add(songLabel);
        songInfoRow.Controls.Add(viewSwitchGroup);

        songViews.Panel1.Controls.Add(pianoRoll);
        songViews.Panel2.Controls.Add(sheet);
        songTab.Controls.Add(songViews);
        // Added before the parts panel, so it spans only the song views' width.
        songTab.Controls.Add(BuildTransportRow());
        songTab.Controls.Add(partsPanel);
        // A thick line between the toolbar and the song, added after the parts panel so it spans the full width.
        songTab.Controls.Add(new SeparatorLine { Dock = DockStyle.Top, Height = 3 });
        songTab.Controls.Add(songInfoRow);

        keyboardSizeSlider.Value = Math.Clamp(config.KeyboardKeyWidth, PianoRollControl.MinKeyWidth, PianoRollControl.MaxKeyWidth);
        pianoRoll.KeyWidth = keyboardSizeSlider.Value;
        keyboardSizeSlider.ValueChanged += (_, _) =>
        {
            pianoRoll.KeyWidth = keyboardSizeSlider.Value;
            config.KeyboardKeyWidth = keyboardSizeSlider.Value;
            config.Save();
        };
        songTab.Controls.Add(songControlsRow);
        // Not docked: floats over the song views when shown.
        songTab.Controls.Add(toast);
        BuildSongViews();

        pianoRoll.Clickable = true;
        pianoRoll.KeySelected += pitch =>
        {
            playbackEngine.PlayNote(pitch);
            // With the on-screen keyboard as note input, a click is a played note (learning, recording, listening).
            if (noteSource is ScreenKeyboardNoteSource screen)
                screen.Press(pitch);
            else
                pianoRoll.FlashKey(pitch);
        };
        pianoRoll.KeyReleased += pitch => (noteSource as ScreenKeyboardNoteSource)?.Release(pitch);

        // Drag the notes to move through the song; playback pauses while dragging and resumes on release.
        pianoRoll.Seekable = true;
        pianoRoll.SeekStarted += () =>
        {
            resumeAfterSeek = PlaybackRunning;
            if (resumeAfterSeek)
                PlaybackPause();
        };
        pianoRoll.Seek += seconds =>
        {
            if (currentScore == null || learning != null || recorder != null)
                return;
            PlaybackSeek(seconds);
            SetViewTime(seconds);
        };
        pianoRoll.SeekEnded += () =>
        {
            if (resumeAfterSeek)
                PlaybackPlay();
            resumeAfterSeek = false;
        };

        // Both ways of getting a song: a file you have, or a free one from the internet.
        openSongButton.Click += (_, _) => ShowMenu(openSongButton,
            MenuItem(T("Ze souboru (MIDI, XML)...", "From a file (MIDI, XML)..."), OpenSong),
            MenuItem(T("Hledat noty zdarma...", "Find free sheet music..."), FindFreeScores));
        importStemsButton.Click += (_, _) => ImportStems();
        removeSongButton.Click += (_, _) => RemoveSong();
        playPauseButton.Click += (_, _) => TogglePlayPause();
        stopButton.Click += (_, _) => DoStop();
        listenButton.Click += (_, _) => ToggleListening();
        learningButton.Click += (_, _) => ToggleLearningMode(showSetup: true);
        createButton.Click += (_, _) => ToggleCreateMode();
        saveRecordingButton.Click += (_, _) => SaveCurrentSong();

        // --- Bindings tab: macro keyboard mapping (same shape as step 1) ---
        var keyboardRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
        keyboardRow.Controls.AddRange([learnButton, forgetButton, keyboardLabel]);
        keyboardLabel.Margin = new Padding(6, 8, 3, 3);

        var bindingRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bindingRow.Controls.AddRange([addButton, editButton, removeButton, exportButton, importButton, enabledBox, languageLabel, languageBox]);

        bindingsTab.Controls.Add(list);
        bindingsTab.Controls.Add(bindingRow);
        bindingsTab.Controls.Add(keyboardRow);
        bindingsTab.Controls.Add(macroInfoLabel);

        tabs.Pages.Add(songTab);
        tabs.Pages.Add(bindingsTab);
        Controls.Add(tabs);
        // Bottom bar: status text on the left, the keyboard size slider on the right (only on the song tab).
        var keyboardSizeGroup = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 12, 0) };
        keyboardSizeGroup.Controls.AddRange([keyboardSizeLabel, keyboardSizeSlider]);
        var statusRow = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        statusRow.Controls.Add(statusLabel);
        statusRow.Controls.Add(keyboardSizeGroup);
        tabs.SelectedIndexChanged += (_, _) => keyboardSizeGroup.Visible = tabs.SelectedTab == songTab;
        Controls.Add(statusRow);

        menuBar.Controls.AddRange([fileMenuButton, editMenuButton, settingsMenuButton, macroSetupMenuButton]);
        Controls.Add(menuBar);

        // Title bar must be added LAST (and never BringToFront - that resets its z-index to 0, which for
        // Dock layout means innermost, not outermost). Last-added Dock=Top control sits closest to the edge.
        var titleBar = new TitleBar(this, showMinimize: true, showMaximize: true);
        Controls.Add(titleBar);

        fileMenuButton.Click += (_, _) => ShowMenu(fileMenuButton,
            MenuItem(T("Otevřít skladbu...", "Open song..."), OpenSong),
            MenuItem(T("Hledat noty zdarma...", "Find free sheet music..."), FindFreeScores),
            MenuItem(T("Importovat stopy...", "Import stems..."), ImportStems),
            MenuItem(T("Uložit skladbu jako...", "Save song as..."), SaveCurrentSong),
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem(T("Nedávné skladby", "Recent songs")) { Sub = BuildRecentSongItems(), Enabled = config.RecentSongs.Count > 0 },
            new AntdUI.ContextMenuStripItem(T("Skladby ze složky", "Songs from the folder")) { Tag = (Action)(() => LoadSongsFolder()) },
            new AntdUI.ContextMenuStripItem(T("Historie cvičení...", "Practice history...")) { Enabled = currentScore != null, Tag = (Action)ShowPracticeHistory },
            new AntdUI.ContextMenuStripItem(T("Předchozí skladba", "Previous song")) { Enabled = playlist.Count > 1, Tag = (Action)DoPreviousTrack },
            new AntdUI.ContextMenuStripItem(T("Další skladba", "Next song")) { Enabled = playlist.Count > 1, Tag = (Action)DoNextTrack },
            new AntdUI.ContextMenuStripItemDivider(),
            MenuItem(T("Exportovat klávesy...", "Export keys..."), ExportBindings),
            MenuItem(T("Importovat klávesy...", "Import keys..."), ImportBindings),
            new AntdUI.ContextMenuStripItemDivider(),
            MenuItem(T("Ukončit", "Exit"), Close));

        editMenuButton.Click += (_, _) => ShowMenu(editMenuButton,
            MenuItem(T("Přidat klávesu...", "Add key..."), ToggleCapture),
            MenuItem(T("Změnit akci...", "Change action..."), EditSelected),
            MenuItem(T("Odebrat", "Remove"), RemoveSelected));

        settingsMenuButton.Click += (_, _) => ShowMenu(settingsMenuButton, BuildSettingsMenuItems());

        macroSetupMenuButton.Click += (_, _) => StartLearning();

        learnButton.Click += (_, _) => StartLearning();
        forgetButton.Click += (_, _) => ForgetKeyboard();
        addButton.Click += (_, _) => ToggleCapture();
        editButton.Click += (_, _) => EditSelected();
        removeButton.Click += (_, _) => RemoveSelected();
        exportButton.Click += (_, _) => ExportBindings();
        importButton.Click += (_, _) => ImportBindings();
        list.DoubleClick += (_, _) => EditSelected();
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        enabledBox.CheckedChanged += (_, _) =>
        {
            // The constructor sets the box from the config; that's not a change to save or announce.
            if (enabledBox.Checked == config.MacroEnabled)
                return;
            config.MacroEnabled = enabledBox.Checked;
            if (interceptor != null)
                interceptor.Enabled = enabledBox.Checked;
            config.Save();
            SetStatus(enabledBox.Checked
                ? T("Ovládání zapnuté.", "Controls on.")
                : T("Ovládání vypnuté - makro klávesnice píše normálně.", "Controls off - the macro keyboard types normally."));
        };
        languageBox.SelectedIndexChanged += (_, _) =>
        {
            var language = (AppLanguage)languageBox.SelectedIndex;
            if (language == Lang.Current)
                return;
            Lang.Current = language;
            config.Language = language;
            config.Save();
            ApplyTexts();
            SetStatus(T("Jazyk změněn.", "Language changed."));
        };
    }

    void BuildSongViews()
    {
        foreach (var _ in Enum.GetValues<SongView>())
            viewSwitch.Items.Add(new AntdUI.SegmentedItem());
        viewSwitch.SelectIndex = (int)config.SongView;
        viewSwitch.SelectIndexChanged += (_, _) =>
        {
            var view = (SongView)viewSwitch.SelectIndex;
            if (view == config.SongView)
                return;
            config.SongView = view;
            config.Save();
            ApplySongView();
        };

        // The keyboard keeps its share of the width when the window is resized; dragging the divider changes it.
        songViews.SizeChanged += (_, _) => ApplySplitRatio();
        songViews.SplitterMoved += (_, _) =>
        {
            if (applyingSplit || songViews.Panel1Collapsed || songViews.Panel2Collapsed || songViews.Width <= 0)
                return;
            config.SplitRatio = Math.Clamp(songViews.SplitterDistance / (double)songViews.Width, 0.15, 0.85);
            config.Save();
        };

        sheet.FallbackBpm = config.CreateTempoBpm;
        sheet.Seekable = true;
        sheet.LoopSelected += (start, end) =>
        {
            if (learning == null && recorder == null)
                SetLoop(start, end);
        };
        sheet.Seek += seconds =>
        {
            if (currentScore == null || learning != null || recorder != null)
                return;
            PlaybackSeek(seconds);
            SetViewTime(seconds);
        };
        ApplySongView();
    }

    void ApplySongView()
    {
        var view = config.SongView;
        songViews.Panel1Collapsed = view == SongView.Sheet;
        songViews.Panel2Collapsed = view == SongView.Keyboard;
        // The key width only matters while the keyboard is shown (disabled, not hidden, so the switch doesn't jump).
        keyboardSizeLabel.Enabled = keyboardSizeSlider.Enabled = view != SongView.Sheet;
        ApplySplitRatio();
    }

    void ApplySplitRatio()
    {
        if (songViews.Panel1Collapsed || songViews.Panel2Collapsed || songViews.Width <= 0)
            return;
        applyingSplit = true;
        try
        {
            int max = Math.Max(songViews.Panel1MinSize, songViews.Width - songViews.Panel2MinSize - songViews.SplitterWidth);
            songViews.SplitterDistance = Math.Clamp((int)Math.Round(songViews.Width * config.SplitRatio), songViews.Panel1MinSize, max);
        }
        finally
        {
            applyingSplit = false;
        }
    }

    /// <summary>The song shown in the piano roll and the sheet music.</summary>
    void SetViewScore(Score? score)
    {
        pianoRoll.SetScore(score);
        sheet.FallbackBpm = config.CreateTempoBpm;
        sheet.SetScore(score);
    }

    void SetViewTime(double seconds)
    {
        pianoRoll.SetCurrentTime(seconds);
        sheet.SetCurrentTime(seconds);
    }

    /// <summary>
    /// Create mode: the piano roll follows the recording every frame; the sheet music, which has to be laid out
    /// again, only when a note was added (or <paramref name="force"/>).
    /// </summary>
    void UpdateLiveViews(Score recording, bool force)
    {
        pianoRoll.UpdateLiveScore(recording);
        int count = recording.Parts.Sum(p => p.Notes.Count);
        if (!force && (count == liveNoteCount || songViews.Panel2Collapsed))
            return;
        liveNoteCount = count;
        sheet.FallbackBpm = config.CreateTempoBpm;
        sheet.UpdateLiveScore(recording);
    }

    void ToggleDarkMode()
    {
        config.DarkMode = !config.DarkMode;
        config.Save();
        AppTheme.SetDark(config.DarkMode);
        SetStatus(config.DarkMode ? T("Tmavý režim zapnutý.", "Dark mode on.") : T("Světlý režim zapnutý.", "Light mode on."));
    }

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
        // Metronome and key labels side by side; under them, while the metronome is checked: "without accompaniment"
        // and its tempo.
        var metronomeGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(24, 0, 0, 0) };
        var checkboxRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        metronomeBox.Margin = new Padding(0, 6, 0, 0);
        keyLabelsBox.Margin = new Padding(24, 6, 0, 0);
        checkboxRow.Controls.AddRange([metronomeBox, keyLabelsBox]);
        metronomeOptions.Controls.AddRange([standaloneBox, bpmBox, bpmArrows]);
        metronomeGroup.Controls.AddRange([checkboxRow, metronomeOptions]);
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
    static double ScrubRate(double heldSeconds) => Math.Min(16, 2 * Math.Pow(2, heldSeconds));

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

    /// <summary>A small grey caption above a row of related controls.</summary>
    static Label CaptionLabel() => new()
    {
        AutoSize = true,
        Margin = new Padding(2, 0, 0, 2),
        ForeColor = SystemColors.GrayText,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 9f),
    };

    static FlowLayoutPanel ControlGroup(Label caption, params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.AddRange(controls);
        var group = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 24, 6) };
        group.Controls.AddRange([caption, row]);
        return group;
    }

    static AntdUI.ContextMenuStripItem MenuItem(string text, Action action) => new(text) { Tag = action };

    static void ShowMenu(Control anchor, params AntdUI.IContextMenuStripItem[] items)
    {
        var config = new AntdUI.ContextMenuStrip.Config(anchor, item => { if (item.Tag is Action action) action(); }, items, 0)
        {
            Font = new Font("Segoe UI", 9.5f),
            Gap = 0.15f,
            PaddRatio = [0.5f, 0.25f],
        };
        AntdUI.ContextMenuStrip.open(config);
    }

    /// <summary>Built fresh every time the menu opens, so it always reflects current language/state - nothing to keep in sync.</summary>
    AntdUI.IContextMenuStripItem[] BuildSettingsMenuItems()
    {
        var languageItems = Enum.GetValues<AppLanguage>()
            .Select(language => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(DisplayName(language))
            {
                Checked = language == Lang.Current,
                Tag = (Action)(() => languageBox.SelectedIndex = (int)language),
            })
            .ToArray();

        // A saved device that's no longer connected falls back to the default, so show the default as checked.
        bool usingDefault = config.MidiOutputDevice == null || !outputDeviceNames.Contains(config.MidiOutputDevice);
        var deviceItems = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(T("(systémové výchozí)", "(system default)"))
            {
                Checked = usingDefault,
                Tag = (Action)(() => SetMidiOutput(null)),
            },
        };
        foreach (var name in outputDeviceNames)
            deviceItems.Add(new AntdUI.ContextMenuStripItem(name)
            {
                Checked = !usingDefault && config.MidiOutputDevice == name,
                Tag = (Action)(() => SetMidiOutput(name)),
            });

        return
        [
            new AntdUI.ContextMenuStripItem(T("Ovládání zapnuté", "Controls on")) { Checked = enabledBox.Checked, Tag = (Action)(() => enabledBox.Checked = !enabledBox.Checked) },
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem(T("Jazyk", "Language")) { Sub = languageItems },
            new AntdUI.ContextMenuStripItem(T("Tmavý režim", "Dark mode")) { Checked = config.DarkMode, Tag = (Action)ToggleDarkMode },
            new AntdUI.ContextMenuStripItem(T("Velké hlášky (makro klávesy, zkratky)", "Big messages (macro keys, shortcuts)"))
            {
                Checked = config.BigNotices,
                Tag = (Action)(() => { config.BigNotices = !config.BigNotices; config.Save(); }),
            },
            new AntdUI.ContextMenuStripItem(T("MIDI výstup", "MIDI output")) { Sub = deviceItems.ToArray() },
            new AntdUI.ContextMenuStripItem(T("Vstup not", "Note input")) { Sub = BuildNoteInputItems() },
            new AntdUI.ContextMenuStripItem(T("Mikrofon a citlivost...", "Microphone and sensitivity...")) { Tag = (Action)ShowMicrophoneSetup },
            new AntdUI.ContextMenuStripItem(T("Složka se skladbami...", "Songs folder...")) { Tag = (Action)PickSongsFolder },
            new AntdUI.ContextMenuStripItem(T("Rozpoznávání akordů (doplněk)...", "Chord detection (add-on)..."))
            {
                Checked = BasicPitchModule.IsInstalled,
                Tag = (Action)(async () => await ChordAddonPrompt.ManageAsync(this, config, SetStatus)),
            },
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem(T("Klávesové zkratky... (F1)", "Keyboard shortcuts... (F1)")) { Tag = (Action)ShowShortcuts },
        ];
    }

    /// <summary>All note inputs as one flat list: microphone, computer sound, on-screen keyboard, then each MIDI input by name.</summary>
    AntdUI.IContextMenuStripItem[] BuildNoteInputItems()
    {
        AntdUI.ContextMenuStripItem Item(string text, NoteInputKind kind) => new(text)
        {
            Checked = config.NoteInput == kind,
            Tag = (Action)(() => SetNoteInput(kind, config.MidiInputDevice)),
        };
        var items = new List<AntdUI.IContextMenuStripItem>
        {
            Item(T("Mikrofon (akustické piano)", "Microphone (acoustic piano)"), NoteInputKind.Microphone),
            Item(T("Zvuk z počítače (YouTube, Spotify...)", "Computer sound (YouTube, Spotify...)"), NoteInputKind.SystemAudio),
            Item(T("Klikání na klaviaturu v appce", "Clicking the on-screen keyboard"), NoteInputKind.ScreenKeyboard),
        };
        foreach (var name in MidiNoteSource.GetDeviceNames())
            items.Add(new AntdUI.ContextMenuStripItem("MIDI: " + name)
            {
                Checked = config.NoteInput == NoteInputKind.Midi && config.MidiInputDevice == name,
                Tag = (Action)(() => SetNoteInput(NoteInputKind.Midi, name)),
            });
        return items.ToArray();
    }

    /// <param name="name">Null = the system default output.</param>
    void SetMidiOutput(string? name)
    {
        config.MidiOutputDevice = name;
        config.Save();
        playbackEngine.SetOutputDevice(name);
        if (currentScore != null)
        {
            playbackEngine.Load(currentScore, ClickTiming);
            playbackEngine.Speed = playbackSpeed;
        }
        SetStatus(T("MIDI výstup: ", "MIDI output: ") + (name ?? T("systémový výchozí", "system default")));
    }

    /// <summary>Sets every text in the window to the current language.</summary>
    void ApplyTexts()
    {
        fileMenuButton.Text = T("Soubor", "File");
        editMenuButton.Text = T("Úpravy", "Edit");
        settingsMenuButton.Text = T("Nastavení", "Settings");
        macroSetupMenuButton.Text = T("Makro klávesnice", "Macro keyboard");
        songTab.Text = T("Skladba", "Song");
        bindingsTab.Text = T("Makro klávesnice", "Macro keyboard");
        openSongButton.Text = T("Otevřít skladbu ▾", "Open song ▾");
        importStemsButton.Text = T("Importovat stopy...", "Import stems...");
        removeSongButton.Text = T("Odebrat skladbu", "Remove song");
        loadGroupLabel.Text = T("Načíst", "Load");
        playingGroupLabel.Text = T("Nahrávání", "Recording");
        keyboardSizeLabel.Text = T("Velikost klávesnice", "Keyboard size");
        string[] viewNames = [T("Klávesy", "Keyboard"), T("Noty", "Sheet"), T("Noty + klávesy", "Sheet + keyboard")];
        for (int i = 0; i < viewSwitch.Items.Count && i < viewNames.Length; i++)
            viewSwitch.Items[i].Text = viewNames[i];
        // As wide as its items: extra width stays empty after the last one, so the switch wouldn't reach the right
        // edge. (AntdUI's AutoSize shrinks it to nothing.)
        viewSwitch.Width = viewNames.Sum(name => TextRenderer.MeasureText(name, viewSwitch.Font).Width + LogicalToDeviceUnits(12));
        viewSwitch.Invalidate();
        UpdateSpeedLabel();
        listenButton.Text = noteSource != null ? T("Přestat poslouchat", "Stop listening") : T("Poslouchat", "Listen");
        UpdateLearningButton();
        UpdateCreateButton();
        learnButton.Text = T("Nastavit makro klávesnici...", "Set up macro keyboard...");
        forgetButton.Text = T("Zapomenout klávesnici", "Forget keyboard");
        macroInfoLabel.Text = T(
            "Fun fact: pokud máš doma druhou klávesnici (programovatelnou typu SayoDevice, starou klávesnici, numpad...), "
            + "můžeš si ji nastavit jako makro klávesnici a ovládat jí přehrávání, mute nástrojů a další akce - aniž by rušila "
            + "psaní na tvé hlavní klávesnici. Je to čistě volitelné, appka běží v pohodě i bez toho.",
            "Fun fact: if you have a spare keyboard lying around (a programmable one like SayoDevice, an old keyboard, a numpad...), "
            + "you can set it up as a macro keyboard to control playback, mute instruments and more - without it interfering with "
            + "typing on your main keyboard. Totally optional, the app works fine without it.");
        addButton.Text = capturingKey ? T("Zrušit", "Cancel") : T("Přidat klávesu...", "Add key...");
        editButton.Text = T("Změnit akci...", "Change action...");
        removeButton.Text = T("Odebrat", "Remove");
        exportButton.Text = T("Exportovat...", "Export...");
        importButton.Text = T("Importovat...", "Import...");
        enabledBox.Text = T("Ovládání zapnuté", "Controls on");
        languageLabel.Text = T("Jazyk:", "Language:");
        list.Columns[0].Text = T("Klávesa", "Key");
        list.Columns[1].Text = T("Akce", "Action");
        RebuildBindings();
        UpdateKeyboardLabel();
        UpdateSongLabel();
        UpdatePlayPauseButton();
        // Its header and the stems' "Notes" buttons are created with the language of the moment.
        RebuildPartsPanel();
    }

    /// <summary>Brings the main window to front, restoring it first if it's minimized.</summary>
    void RestoreWindow()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        actionQueue.Writer.TryComplete();
        base.OnFormClosing(e);
    }

    // --- Note input ("Listen": notes heard from the microphone or a MIDI input light up the keyboard) ---

    void ToggleListening()
    {
        if (noteSource != null)
        {
            StopListening();
            SetStatus(T("Poslouchání vypnuto.", "Stopped listening."));
        }
        else
            StartListening();
    }

    void StartListening()
    {
        StopListening();
        var source = config.CreateNoteSource();
        // Events queued before a stop must not light keys afterwards - hence the noteSource check.
        source.NoteOn += note => Post(() =>
        {
            if (noteSource != source)
                return;
            pianoRoll.SetKeyHeld(note, true);
            if (learning != null)
                learning.OnNoteOn(note);
            else if (recorder != null)
                recorder.OnNoteOn(note);
            else
                SetStatus(T("Slyším: ", "Hearing: ") + NoteNames.Describe(note));
        });
        source.NoteOff += note => Post(() =>
        {
            if (noteSource != source)
                return;
            pianoRoll.SetKeyHeld(note, false);
            recorder?.OnNoteOff(note);
        });
        try
        {
            source.Start();
        }
        catch (NoteSourceException ex)
        {
            source.Dispose();
            ShowModal(this, Text, ex.Message, AntdUI.TType.Error);
            return;
        }
        noteSource = source;
        listenButton.Text = T("Přestat poslouchat", "Stop listening");
        SetStatus(T($"Poslouchám: {source.Name}", $"Listening: {source.Name}"));
    }

    void StopListening()
    {
        noteSource?.Dispose();
        noteSource = null;
        pianoRoll.ClearHeldKeys();
        listenButton.Text = T("Poslouchat", "Listen");
    }

    void SetNoteInput(NoteInputKind kind, string? midiDevice)
    {
        // A running session was set up for the old input (chords, latency).
        StopLearningMode(showResult: false);
        config.NoteInput = kind;
        config.MidiInputDevice = midiDevice;
        config.Save();
        if (noteSource != null)
            StartListening();
    }

    void ShowMicrophoneSetup()
    {
        // The dialog opens the microphone itself; don't hold the device twice.
        StopLearningMode(showResult: false);
        bool wasListening = noteSource != null;
        StopListening();
        using var setup = new MicrophoneSetupForm(config.MicrophoneDevice, config.MicrophoneThresholdDb);
        if (setup.ShowDialog(this) == DialogResult.OK)
        {
            config.MicrophoneDevice = setup.DeviceName;
            config.MicrophoneThresholdDb = setup.ThresholdDb;
            config.Save();
            SetStatus(T($"Mikrofon nastaven (práh {setup.ThresholdDb:0} dB).", $"Microphone set (threshold {setup.ThresholdDb:0} dB)."));
        }
        if (wasListening)
            StartListening();
    }

    // --- Learning mode: play the song yourself, notes turn green/red, score at the end ------------

    void ToggleLearningMode(bool showSetup)
    {
        if (learning != null)
            StopLearningMode(showResult: true);
        else
            StartLearningMode(showSetup);
    }

    /// <param name="showSetup">False when started from a macro key or "Try again": reuse the last settings.</param>
    /// <param name="nextPass">Learning a loop: the previous pass just ended, carry on with the next one.</param>
    void StartLearningMode(bool showSetup, bool nextPass = false)
    {
        if (!nextPass)
        {
            loopPass = 1;
            lastPassPercent = null;
        }
        if (currentScore is not { } score)
        {
            ShowModal(this, Text, T("Nejdřív otevři skladbu (MIDI nebo MusicXML).", "Open a song first (MIDI or MusicXML)."), AntdUI.TType.Info);
            return;
        }

        List<int> parts;
        if (showSetup)
        {
            using var setup = new LearningSetupForm(score, config, audioAccompaniment: StemsPlay);
            if (setup.ShowDialog(this) != DialogResult.OK)
                return;
            parts = setup.PracticedParts;
            config.LearningMode = setup.SelectedMode;
            config.LearningSpeedPercent = setup.SpeedPercent;
            config.LearningAccompaniment = setup.Accompaniment;
            config.LearningCheckChords = setup.CheckChords;
            config.LearningPartNames = parts.Select(i => score.Parts[i].Name).ToList();
            config.Save();
        }
        else
            parts = LearningSetupForm.PartsToPractice(score, config.LearningPartNames).Order().ToList();

        if (noteSource == null)
            StartListening();
        if (noteSource is not { } source)
            return; // the error was already shown

        StopLearningMode(showResult: false);
        StopStandalone();
        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        bool tempo = config.LearningMode == LearningMode.InTempo;
        // The stems' audio can't be slowed down (not without changing its pitch), so with them tempo mode is 100 %.
        double speed = tempo && StemsPlay ? 1 : config.LearningSpeedPercent / 100.0;
        if (tempo && StemsPlay)
        {
            // The original recording is the accompaniment: silence the tracks the player plays (notes converted
            // from a track carry its name), or everything if they want no accompaniment.
            var practicedNames = parts.Select(i => score.Parts[i].Name).ToHashSet();
            var tracks = currentStems!.Tracks;
            stemPlaybackEngine.SetSilenced(Enumerable.Range(0, tracks.Count)
                .Where(i => !config.LearningAccompaniment || practicedNames.Contains(tracks[i].Name)));
        }
        else if (tempo)
        {
            // The practiced parts are the player's; the rest plays along only if asked for.
            for (int i = 0; i < score.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, parts.Contains(i) || !config.LearningAccompaniment || score.Parts[i].Muted);
            playbackEngine.Speed = speed;
        }

        // Set before reading source.Polyphonic: with chord checking on, a chord needs all its notes.
        if (source is IExpectedNotesListener listener)
            listener.CheckChords = config.LearningCheckChords;
        var range = loopOn ? LoopRange : null;
        // In tempo, with the metronome on, the lead-in is a bar of count-in clicks, so the player comes in on time.
        bool countInLeadIn = tempo && MetronomeOn;
        double from = range?.Start ?? 0;
        var session = new LearningSession(score, parts, config.LearningMode, speed, source.Polyphonic, source.LatencySeconds,
            tempo ? () => PlaybackSeconds : null,
            tempo ? () => { if (range is { } r) PlaybackSeek(r.Start); PlaybackPlay(); } : null,
            range,
            countInLeadIn ? CountInSeconds(from) : null);
        if (countInLeadIn)
            StartCountIn(from, speed, () => { });
        session.WrongKey += pitch => pianoRoll.FlashKey(pitch, 300, PianoRollControl.WrongColor);
        session.Finished += () => BeginInvoke(() =>
        {
            if (learning != session)
                return;
            // A loop goes round again by itself; its result shows in the status line instead of a window.
            if (session.Range != null && loopOn)
            {
                var pass = session.Stop();
                RecordPractice(session, pass);
                lastPassPercent = pass.Percent;
                loopPass++;
                StartLearningMode(showSetup: false, nextPass: true);
                return;
            }
            StopLearningMode(showResult: true);
        });
        learning = session;
        learningStatus = "";
        learningInfo = (config.LearningMode, (int)Math.Round(speed * 100), parts.Select(i => score.Parts[i].Name).ToArray(), range);
        if (!nextPass)
            SetStatus(T("Režim učení", "Learning mode") + (config.LearningMode == LearningMode.InTempo ? T(" - v tempu", " - in tempo") : ""));
        pianoRoll.Learning = session;
        sheet.Learning = session;
        sheet.NotesChanged();
        partsPanel.Enabled = false;
        UpdateLearningButton();
    }

    void TickLearning()
    {
        var session = learning!;
        session.Tick();
        SetViewTime(session.Position);
        // Tell an audio source which notes to listen for now (chord checking).
        (noteSource as IExpectedNotesListener)?.SetExpectedNotes(session.ListenFor());
        string status = session.InLeadIn
            ? T("Připrav se...", "Get ready...")
            : T($"Učení: {session.DoneNotes}/{session.TotalNotes} not, správně {session.CorrectNotes}, špatné klávesy {session.WrongPresses}",
                $"Learning: {session.DoneNotes}/{session.TotalNotes} notes, {session.CorrectNotes} correct, {session.WrongPresses} wrong keys");
        if (session.Range != null)
            status = T($"Smyčka, kolo {loopPass}", $"Loop, pass {loopPass}")
                + (lastPassPercent is { } last ? T($" (minule {last:0} %)", $" (last {last:0} %)") : "") + " - " + status;
        if (session.Paused)
            status = T("Pozastaveno - ", "Paused - ") + status;
        if (status != learningStatus)
            SetStatus(learningStatus = status);
    }

    void StopLearningMode(bool showResult)
    {
        if (learning is not { } session)
            return;
        CancelCountIn();
        var result = session.Stop();
        learning = null;
        pianoRoll.Learning = null;
        sheet.Learning = null;
        sheet.NotesChanged();
        if (noteSource is IExpectedNotesListener listener)
        {
            listener.SetExpectedNotes([]);
            listener.CheckChords = false;
        }
        partsPanel.Enabled = true;

        playbackEngine.Stop();
        playbackEngine.Speed = playbackSpeed;
        if (currentScore != null)
            for (int i = 0; i < currentScore.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, currentScore.Parts[i].Muted);
        stemPlaybackEngine.Stop();
        stemPlaybackEngine.SetSilenced([]);
        SetViewTime(0);
        UpdateLearningButton();
        UpdateCreateButton();
        SetStatus(T("Režim učení ukončen.", "Learning mode ended."));

        // Nothing played yet: there's nothing to score.
        if (!RecordPractice(session, result) || !showResult)
            return;
        using var dialog = new LearningResultForm(result, currentScore?.Title ?? "", practiceHistory.For(PracticeKey()));
        if (dialog.ShowDialog(this) == DialogResult.Retry)
            StartLearningMode(showSetup: false);
    }

    void UpdateLearningButton()
    {
        UpdateTransport();
        learningButton.Enabled = currentScore != null && recorder == null;
        // Moving the time would break a learning session's scoring.
        pianoRoll.Seekable = learning == null;
        sheet.Seekable = learning == null && recorder == null;
        learningButton.Text = learning != null ? T("Ukončit učení", "End learning") : T("Režim učení...", "Learning mode...");
    }

    // --- Create mode: what's played gets written down, then saved as MIDI / MusicXML --------------

    void ToggleCreateMode()
    {
        if (recorder != null)
            FinishCreateMode();
        else
            StartCreateMode();
    }

    void StartCreateMode()
    {
        StopLearningMode(showResult: false);
        if (noteSource == null)
            StartListening();
        if (noteSource is not { } source)
            return; // the error was already shown

        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        recorder = new NoteRecorder(source.LatencySeconds);
        recordingTitle = T("Nahrávka ", "Recording ") + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
        pianoRoll.RecordingView = true;
        UpdateLiveViews(recorder.Snapshot(recordingTitle), force: true);
        SetViewTime(0);
        UpdateCreateButton();
        UpdateLearningButton();
        SetStatus(T("Nahrávám - hraj. Noty se zapisují od první zahrané.", "Recording - play. Notes are written from the first one you play."));
    }

    void TickCreateMode()
    {
        var active = recorder!;
        UpdateLiveViews(active.Snapshot(recordingTitle), force: false);
        SetViewTime(active.CurrentSeconds);
        if (active.NoteCount > 0)
            SetStatus(T($"Nahrávám: {active.NoteCount} not", $"Recording: {active.NoteCount} notes"));
    }

    /// <summary>Ends recording without asking anything; the recording (if any) becomes the current song.</summary>
    void StopCreateMode()
    {
        if (recorder is not { } active)
            return;
        recorder = null;
        pianoRoll.RecordingView = false;
        var recording = active.Finish(recordingTitle);
        if (recording.Parts[0].Notes.Count > 0)
        {
            // The recording isn't one of the opened files, so previous/next no longer apply.
            playlist = [];
            playlistIndex = -1;
            ShowScore(recording);
            unsavedRecording = recording;
        }
        else
            SetViewScore(currentScore);
        UpdateCreateButton();
        UpdateLearningButton();
    }

    void FinishCreateMode()
    {
        if (recorder == null)
            return;
        StopCreateMode();
        if (currentScore is not { } recording || recording.Title != recordingTitle)
        {
            SetStatus(T("Nic se nezapsalo - nebyla slyšet žádná nota.", "Nothing was recorded - no notes were heard."));
            return;
        }
        // No dialog: listen to it first (Play), then save it with the Save button if it's worth keeping.
        SetStatus(T($"Nahráno: {recording.Parts[0].Notes.Count} not. Poslechni si to (Přehrát) a když se ti to líbí, ulož to tlačítkem Uložit.",
            $"Recorded: {recording.Parts[0].Notes.Count} notes. Listen to it (Play) and if you like it, save it with the Save button."));
    }

    /// <summary>Called on every playback tick, so it can't miss a change of the open song or of its saved state.</summary>
    void UpdateSaveRecordingButton()
    {
        bool show = recorder == null && currentScore != null && currentScore == unsavedRecording;
        if (saveRecordingButton.Visible != show)
            saveRecordingButton.Visible = show;
    }

    void UpdateCreateButton()
    {
        createButton.Text = recorder != null ? T("Ukončit nahrávání", "Stop recording") : T("Zapisovat noty", "Write down notes");
        saveRecordingButton.Text = T("Uložit", "Save");
        removeSongButton.Enabled = recorder == null;
    }

    void SaveCurrentSong()
    {
        if (currentScore == null)
        {
            ShowModal(this, Text, T("Není otevřená žádná skladba (MIDI, MusicXML nebo nahrávka).", "No song is open (MIDI, MusicXML or a recording)."), AntdUI.TType.Info);
            return;
        }
        SaveScore(currentScore);
    }

    /// <summary>
    /// Asks where and saves; true if the file was written. A song without its own tempo (a recording) saved as
    /// MusicXML also asks for the tempo to write it in.
    /// </summary>
    bool SaveScore(Score score)
    {
        string fileName = string.Concat(score.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var dialog = new SaveFileDialog
        {
            Filter = ScoreExporter.FileFilter,
            FileName = fileName,
            Title = T("Uložit skladbu", "Save song"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;
        int bpm = config.CreateTempoBpm;
        bool midi = Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".mid" or ".midi";
        if (!midi && score.Timing == null)
        {
            using var tempo = new SaveTempoForm(bpm);
            if (tempo.ShowDialog(this) != DialogResult.OK)
                return false;
            bpm = tempo.Bpm;
            if (bpm != config.CreateTempoBpm)
            {
                config.CreateTempoBpm = bpm;
                config.Save();
            }
        }
        try
        {
            ScoreExporter.Save(score, dialog.FileName, bpm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Uložení se nepovedlo: ", "Saving failed: ") + ex.Message, AntdUI.TType.Error);
            return false;
        }
        if (score == unsavedRecording)
            unsavedRecording = null;
        SetStatus(T($"Uloženo: {dialog.FileName}", $"Saved: {dialog.FileName}"));
        return true;
    }

    // --- Keyboard selection -------------------------------------------------------------------

    void StartLearning()
    {
        CancelCapture();

        if (!MacroKeyboardModule.IsInstalled)
        {
            var confirm = MessageBox.Show(this,
                T("Ovládání appky makro klávesnicí potřebuje stáhnout malý doplňkový modul (pár desítek KB). Stáhnout teď?",
                    "Controlling the app with a macro keyboard needs a small add-on module downloaded first (a few tens of KB). Download it now?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            SetStatus(T("Stahuji modul...", "Downloading module..."));
            Cursor = Cursors.WaitCursor;
            bool downloaded = MacroKeyboardModule.Download();
            Cursor = Cursors.Default;
            if (!downloaded)
            {
                ShowModal(this, Text, T("Stažení modulu se nepovedlo. Zkontroluj připojení k internetu a zkus to znovu.",
                    "Downloading the module failed. Check your internet connection and try again."), AntdUI.TType.Error);
                SetStatus(T("Stažení modulu selhalo.", "Module download failed."));
                return;
            }
            SetStatus(T("Modul stažen.", "Module downloaded."));
        }

        if (interceptor == null)
        {
            interceptor = MacroKeyboardFactory.TryCreate();
            if (interceptor == null)
            {
                // No Interception driver available yet - offer to install it. Purely opt-in: the app
                // works fine without a macro keyboard, this only runs when the user asks for it here.
                using var setup = new DriverSetupForm();
                setup.ShowDialog(this);
                return;
            }
            WireInterceptor();
        }

        using var picker = new KeyboardPickerForm(interceptor, config.MacroTarget);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result is not { } target)
            return;

        config.MacroKeyboardHardwareId = target.HardwareId;
        config.MacroKeyboardDevice = target.Device;
        config.Save();
        interceptor.Target = target;
        UpdateKeyboardLabel();
        SetStatus(T("Klávesnice nastavena. Teď přidej klávesy přes \"Přidat klávesu...\".", "Keyboard set. Now add keys with \"Add key...\"."));
    }

    void WireInterceptor()
    {
        if (interceptor == null)
            return;
        interceptor.Target = config.MacroTarget;
        interceptor.Enabled = config.MacroEnabled;
        interceptor.MacroKeyDown += OnMacroKey;
        interceptor.DeviceMoved += t => BeginInvoke(() => { config.MacroKeyboardDevice = t.Device; config.Save(); });
    }

    void ForgetKeyboard()
    {
        config.MacroKeyboardHardwareId = null;
        config.MacroKeyboardDevice = 0;
        config.Save();
        if (interceptor != null)
            interceptor.Target = null;
        UpdateKeyboardLabel();
        SetStatus(T("Makro klávesnice zapomenuta.", "Macro keyboard forgotten."));
    }

    void UpdateKeyboardLabel()
    {
        forgetButton.Enabled = config.MacroKeyboardHardwareId != null;
        keyboardLabel.Text = config.MacroKeyboardHardwareId == null
            ? T("Makro klávesnice není nastavená (a nemusí být).", "No macro keyboard set up (and it doesn't have to be).")
            : T("Makro klávesnice: ", "Macro keyboard: ") + KeyboardPickerForm.Describe(config.MacroKeyboardHardwareId);
    }

    // --- Song: open, playback, mute parts --------------------------------------------------------

    void OpenSong()
    {
        // Switching songs mid-recording must not silently throw the recording away: finish it (offers saving).
        FinishCreateMode();
        using var dialog = new OpenFileDialog { Filter = ScoreImporter.FileFilter, Title = T("Otevřít skladbu", "Open song"), Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
            return;
        playlist = dialog.FileNames.ToList();
        playlistIndex = 0;
        LoadCurrentSong();
    }

    /// <summary>Searches Mutopia and opens the chosen piece; a piece's movements become the playlist.</summary>
    void FindFreeScores()
    {
        FinishCreateMode();
        using var dialog = new FreeScoresForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Files.Count == 0 || dialog.Piece is not { } piece)
            return;
        playlist = dialog.Files;
        playlistIndex = 0;
        LoadCurrentSong();
        // CC BY needs the author credited: keep the source and license in view.
        SetStatus($"{piece.Title} - {piece.Composer}, {piece.License} (Mutopia Project)"
            + (dialog.Files.Count > 1 ? T($", {dialog.Files.Count} vět", $", {dialog.Files.Count} movements") : ""));
    }

    void LoadCurrentSong()
    {
        if (playlistIndex < 0 || playlistIndex >= playlist.Count)
            return;
        string path = playlist[playlistIndex];
        Score score;
        try
        {
            score = ScoreImporter.Import(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowModal(this, Text, T("Skladbu nejde načíst: ", "Could not load the song: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        ShowScore(score);
        loadedSong = (score, path);
        AddRecentSong(path);
        SetStatus(T($"Načteno: {score.Title}", $"Loaded: {score.Title}"));
    }

    /// <summary>Makes <paramref name="score"/> the current song: playback, piano roll, parts panel, buttons.</summary>
    void ShowScore(Score score)
    {
        StopLearningMode(showResult: false);
        stemPlaybackEngine.Stop();
        currentStems = null;
        currentScore = score;
        ClearLoop();

        playbackEngine.Load(currentScore, ClickTiming);
        SetViewScore(currentScore);
        RebuildPartsPanel();
        playbackEngine.Speed = playbackSpeed;
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
    }

    /// <summary>
    /// Asks how to use the audio: a folder of tracks as accompaniment (<see cref="LoadStems"/>), or notes
    /// detected from an audio file (<see cref="ShowConvertedNotes"/>).
    /// </summary>
    void ImportStems()
    {
        FinishCreateMode();
        // Start the pickers in the loaded stems' folder: converting one of them is a likely next step.
        string? stemsFolder = currentStems?.Tracks.FirstOrDefault() is { } track ? Path.GetDirectoryName(track.FilePath) : null;
        using var dialog = new ImportAudioForm(stemsFolder, config);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (dialog.StemsFolder is { } folder)
            LoadStems(folder);
        else if (dialog.Result is { } score)
            ShowConvertedNotes(score, dialog.FilePath);
    }

    void LoadStems(string folder)
    {
        StemSet stems;
        try
        {
            stems = StemSetImporter.ImportFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Stopy nejde načíst: ", "Could not load the stems: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        try
        {
            stemPlaybackEngine.Load(stems);
        }
        catch (InvalidDataException ex)
        {
            ShowModal(this, Text, ex.Message, AntdUI.TType.Error);
            return;
        }

        StopLearningMode(showResult: false);
        playbackEngine.Stop();
        playlist = [];
        playlistIndex = -1;
        currentScore = null;
        currentStems = stems;
        ClearLoop();
        SetViewScore(null);

        RebuildPartsPanel();
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
        SetStatus(T($"Načteny stopy: {stems.Title} ({stems.Tracks.Count})", $"Loaded stems: {stems.Title} ({stems.Tracks.Count})"));
    }

    void UpdateSongLabel()
    {
        int stemNoteTracks = currentStems != null ? currentScore?.Parts.Count ?? 0 : 0;
        songLabel.Text = currentStems != null
            ? $"{currentStems.Title}  ({TimeSpan.FromSeconds(stemPlaybackEngine.DurationSeconds):m\\:ss})  [{T("stopy", "stems")}"
              + (stemNoteTracks > 0 ? T($", noty z {stemNoteTracks}", $", notes from {stemNoteTracks}") : "") + "]"
            : currentScore != null
                ? $"{currentScore.Title}  ({TimeSpan.FromSeconds(currentScore.DurationSeconds):m\\:ss})" +
                  (playlist.Count > 1 ? $"   [{playlistIndex + 1}/{playlist.Count}]" : "")
                : T("Žádná skladba nenačtena.", "No song loaded.");
        // Called on every song change, so it's also where "Remove song" appears/disappears.
        removeSongButton.Visible = currentScore != null || currentStems != null;
    }

    /// <summary>Notes detected from an audio file open as a new, not yet saved song.</summary>
    void ShowConvertedNotes(Score score, string? filePath)
    {
        // One of the loaded stems: add its notes to them instead of replacing the stems with a new song.
        if (currentStems?.Tracks.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.FilePath), Path.GetFullPath(filePath ?? ""), StringComparison.OrdinalIgnoreCase)) is { } stem)
        {
            AttachStemNotes(stem.Name, score.Parts[0]);
            return;
        }
        playlist = [];
        playlistIndex = -1;
        ShowScore(score);
        unsavedRecording = score;
        SetStatus(T($"Převedeno: {score.Parts.Sum(p => p.Notes.Count)} not. Ulož přes Soubor > Uložit skladbu jako...",
            $"Converted: {score.Parts.Sum(p => p.Notes.Count)} notes. Save via File > Save song as..."));
    }

    /// <summary>Takes the song out of the player (the file on disk stays). Asks first if it's an unsaved recording.</summary>
    void RemoveSong()
    {
        if (recorder != null || (currentScore == null && currentStems == null))
            return;
        if (currentScore != null && currentScore == unsavedRecording)
        {
            var confirm = MessageBox.Show(this,
                T("Noty nejsou uložené - odebráním se ztratí. Opravdu odebrat?", "The notes aren't saved - removing loses them. Remove anyway?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;
        }

        StopLearningMode(showResult: false);
        playbackEngine.Stop();
        stemPlaybackEngine.Stop();
        string title = currentScore?.Title ?? currentStems?.Title ?? "";
        currentScore = null;
        currentStems = null;
        unsavedRecording = null;
        playlist = [];
        playlistIndex = -1;
        ClearLoop();

        SetViewScore(null);
        RebuildPartsPanel();
        UpdateTransport();
        UpdateSongLabel();
        UpdateLearningButton();
        UpdatePlayPauseButton();
        SetStatus(T($"Odebráno: {title}", $"Removed: {title}"));
    }

    /// <summary>
    /// One row per part - or per stem when stems are loaded (notes converted from a stem don't get rows of their
    /// own; the stem's row shows whether it has them). Each row: on/off, volume, and for stems a "Notes" button.
    /// </summary>
    void RebuildPartsPanel()
    {
        partsPanel.Controls.Clear();
        var parts = currentStems?.Tracks.Select(t => (t.Name, t.Muted, t.Volume)).ToList()
            ?? currentScore?.Parts.Select(p => (p.Name, p.Muted, p.Volume)).ToList()
            ?? [];
        // No song: give the piano roll the full width instead of leaving an empty strip.
        partsPanel.Visible = parts.Count > 0;
        if (parts.Count == 0)
            return;

        var header = new AntdUI.Label { Text = T("Části / nástroje:", "Parts / instruments:"), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        partsPanel.Controls.Add(header);
        for (int i = 0; i < parts.Count; i++)
        {
            int index = i;
            var box = new AntdUI.Checkbox { Text = parts[i].Name, Checked = !parts[i].Muted, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            // Volume, 0-100 %; the value shows while dragging. Hidden while the part is switched off.
            var volume = new AntdUI.Slider
            {
                MinValue = 0,
                MaxValue = 100,
                Value = (int)Math.Round(parts[i].Volume * 100),
                ShowValue = true,
                Size = new Size(partsPanel.Width - 30, 24),
                Margin = new Padding(18, 0, 0, 2),
                Visible = !parts[i].Muted,
            };
            box.CheckedChanged += (_, _) =>
            {
                SetPartMuted(index, !box.Checked);
                volume.Visible = box.Checked;
            };
            volume.ValueChanged += (_, _) => SetPartVolume(index, volume.Value / 100.0);
            partsPanel.Controls.Add(box);
            partsPanel.Controls.Add(volume);

            if (currentStems != null)
            {
                bool hasNotes = currentScore?.Parts.Any(p => p.Name == parts[i].Name) ?? false;
                bool converting = convertingTracks.Contains(parts[i].Name);
                var notesButton = new AntdUI.Button
                {
                    Text = converting ? "..." : hasNotes ? T("✓ Noty", "✓ Notes") : T("Noty", "Notes"),
                    Size = new Size(76, 26),
                    Margin = new Padding(18, 0, 0, 4),
                    Radius = 6,
                    Enabled = !hasNotes && !converting,
                };
                notesButton.Click += (_, _) => ConvertStemToNotes(index, notesButton);
                partsPanel.Controls.Add(notesButton);
            }
        }
    }

    /// <summary>
    /// Converts one loaded stem to notes in the background and adds them to the song under the stem's name, shown
    /// in the piano roll in sync with the stems' playback.
    /// </summary>
    async void ConvertStemToNotes(int trackIndex, AntdUI.Button button)
    {
        if (currentStems is not { } stems || trackIndex >= stems.Tracks.Count)
            return;
        var track = stems.Tracks[trackIndex];
        convertingTracks.Add(track.Name);
        button.Enabled = false;
        button.Text = "...";
        SetStatus(T($"Převádím stopu {track.Name} na noty...", $"Converting track {track.Name} to notes..."));
        Score converted;
        try
        {
            await ChordAddonPrompt.OfferAsync(this, config, SetStatus);
            SetStatus(T($"Převádím stopu {track.Name} na noty...", $"Converting track {track.Name} to notes..."));
            converted = await Task.Run(() => AudioTranscriber.TranscribeBest(track.FilePath, null, CancellationToken.None));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            convertingTracks.Remove(track.Name);
            ShowModal(this, Text, T("Stopu nejde převést: ", "Could not convert the track: ") + ex.Message, AntdUI.TType.Error);
            RebuildPartsPanel();
            return;
        }
        convertingTracks.Remove(track.Name);
        // Another song may have been opened meanwhile.
        if (currentStems == stems)
            AttachStemNotes(track.Name, converted.Parts[0]);
    }

    void AttachStemNotes(string trackName, ScorePart notes)
    {
        if (notes.Notes.Count == 0)
        {
            SetStatus(T($"Ve stopě {trackName} jsem nenašel žádné noty.", $"No notes found in track {trackName}."));
            RebuildPartsPanel();
            return;
        }
        // Named after the stem: that's how learning mode knows which track to silence for the player.
        notes.Name = trackName;
        currentScore ??= new Score { Title = currentStems!.Title };
        currentScore.Parts.RemoveAll(p => p.Name == trackName);
        currentScore.Parts.Add(notes);
        unsavedRecording = currentScore;

        SetViewScore(currentScore);
        SetViewTime(PlaybackSeconds);
        RebuildPartsPanel();
        UpdateSongLabel();
        UpdateLearningButton();
        SetStatus(T($"Noty ze stopy {trackName}: {notes.Notes.Count}. Uložit je jde přes Soubor > Uložit skladbu jako...",
            $"Notes from track {trackName}: {notes.Notes.Count}. Save them via File > Save song as..."));
    }

    /// <summary>Index = row in the parts panel: a stem when stems are loaded, otherwise a part.</summary>
    void SetPartVolume(int partIndex, double volume)
    {
        if (currentStems != null)
        {
            if (partIndex < 0 || partIndex >= currentStems.Tracks.Count)
                return;
            currentStems.Tracks[partIndex].Volume = volume;
            stemPlaybackEngine.ApplyVolume(partIndex);
        }
        else if (currentScore != null && partIndex >= 0 && partIndex < currentScore.Parts.Count)
        {
            currentScore.Parts[partIndex].Volume = volume;
            playbackEngine.SetPartVolume(partIndex, volume);
        }
    }

    /// <summary>
    /// Index as in <see cref="SetPartVolume"/>. Muting a stem only silences its audio - notes converted from it
    /// stay visible, since muting your instrument to play it yourself is the point.
    /// </summary>
    void SetPartMuted(int partIndex, bool muted)
    {
        if (currentStems != null)
        {
            if (partIndex < 0 || partIndex >= currentStems.Tracks.Count)
                return;
            currentStems.Tracks[partIndex].Muted = muted;
            stemPlaybackEngine.ApplyVolume(partIndex);
        }
        else if (currentScore != null)
        {
            if (partIndex < 0 || partIndex >= currentScore.Parts.Count)
                return;
            currentScore.Parts[partIndex].Muted = muted;
            playbackEngine.SetPartMuted(partIndex, muted);
        }
        pianoRoll.Invalidate();
        sheet.NotesChanged();
    }

    // --- Playback: the stems' audio when stems are loaded (even with notes converted from them - those are
    // only shown, the original recording is the sound and the clock), otherwise the notes through MIDI. ----------

    bool StemsPlay => currentStems != null;
    bool PlaybackRunning => StemsPlay ? stemPlaybackEngine.IsRunning : playbackEngine.IsRunning;
    double PlaybackSeconds => StemsPlay ? stemPlaybackEngine.CurrentSeconds : playbackEngine.CurrentSeconds;
    void PlaybackPlay() { if (StemsPlay) stemPlaybackEngine.Play(); else playbackEngine.Play(); }
    void PlaybackPause() { if (StemsPlay) stemPlaybackEngine.Pause(); else playbackEngine.Pause(); }
    void PlaybackStop() { if (StemsPlay) stemPlaybackEngine.Stop(); else playbackEngine.Stop(); }
    void PlaybackSeek(double seconds) { if (StemsPlay) stemPlaybackEngine.SeekTo(seconds); else playbackEngine.SeekTo(seconds); }

    void TogglePlayPause()
    {
        // Playing the song over a recording would be heard (by the mic) and written down.
        if (recorder != null)
            return;
        if (learning == null && StandaloneOn)
        {
            ToggleStandalone();
            return;
        }
        if (learning != null)
        {
            bool pause = !learning.Paused;
            learning.SetPaused(pause);
            if (pause)
                CancelCountIn();
            // In tempo mode the accompaniment is the clock - but only once the lead-in has started it.
            if (learning.Mode == LearningMode.InTempo && !learning.InLeadIn)
            {
                if (pause) PlaybackPause(); else PlaybackPlay();
            }
        }
        else if (currentScore != null || currentStems != null)
        {
            if (countIn != null)
                CancelCountIn(); // pressed again during the count-in: don't start after all
            else if (PlaybackRunning)
                PlaybackPause();
            else
            {
                // With the loop on, playing starts inside it.
                if (loopOn && LoopRange is { } range && (PlaybackSeconds < range.Start || PlaybackSeconds >= range.End))
                    PlaybackSeek(range.Start);
                if (MetronomeOn)
                    StartCountIn(PlaybackSeconds, playbackSpeed, PlaybackPlay);
                else
                    PlaybackPlay();
            }
        }
        else
        {
            return;
        }
        UpdatePlayPauseButton();
        SetStatus(learning != null ? (learning.Paused ? T("Pauza", "Paused") : T("Pokračuje se", "Going on"))
            : countIn != null ? T("Odpočítávání...", "Count-in...")
            : PlaybackRunning ? T("Přehrávání", "Playing") : T("Pauza", "Paused"));
    }

    void UpdatePlayPauseButton()
    {
        bool running = learning != null ? !learning.Paused
            : StandaloneRunning || countIn != null || ((currentScore != null || currentStems != null) && PlaybackRunning);
        // Called every playback tick: only touch the icon when it changes (setting it re-renders the SVG).
        string icon = running ? Icons.Pause : Icons.Play;
        if (playPauseButton.IconSvg != icon)
            playPauseButton.IconSvg = icon;
    }

    void DoStop()
    {
        if (recorder != null)
        {
            FinishCreateMode();
            return;
        }
        if (learning != null)
        {
            StopLearningMode(showResult: true);
            return;
        }
        CancelCountIn();
        StopStandalone();
        PlaybackStop();
        SetViewTime(0);
        UpdatePlayPauseButton();
        SetStatus(T("Zastaveno.", "Stopped."));
    }

    void DoNextTrack()
    {
        FinishCreateMode();
        if (playlist.Count == 0)
            return;
        playlistIndex = (playlistIndex + 1) % playlist.Count;
        LoadCurrentSong();
        playbackEngine.Play();
        UpdatePlayPauseButton();
    }

    void DoPreviousTrack()
    {
        FinishCreateMode();
        if (playlist.Count == 0)
            return;
        playlistIndex = (playlistIndex - 1 + playlist.Count) % playlist.Count;
        LoadCurrentSong();
        playbackEngine.Play();
        UpdatePlayPauseButton();
    }

    // --- Bindings -------------------------------------------------------------------------------

    void ToggleCapture()
    {
        if (capturingKey)
        {
            CancelCapture();
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        if (interceptor == null || config.MacroKeyboardHardwareId == null)
        {
            ShowModal(this, Text, T("Nejdřív nastav makro klávesnici.", "Set up the macro keyboard first."), AntdUI.TType.Info);
            return;
        }
        if (!interceptor.Enabled)
            enabledBox.Checked = true;
        interceptor.CancelLearning();
        capturingKey = true;
        addButton.Text = T("Zrušit", "Cancel");
        SetStatus(T("Stiskni klávesu na makro klávesnici...", "Press a key on the macro keyboard..."));
    }

    void CancelCapture()
    {
        capturingKey = false;
        pendingOpposite = null;
        addButton.Text = T("Přidat klávesu...", "Add key...");
    }

    /// <summary>Runs on the interception thread. Must return immediately.</summary>
    void OnMacroKey(int key)
    {
        if (capturingKey)
        {
            if (pendingOpposite is { } opposite)
            {
                // The rest of the burst from turning the knob the first way: wait for the other way.
                if (key == pendingOppositeFrom)
                    return;
                capturingKey = false;
                BeginInvoke(() => { CancelCapture(); Assign(key, opposite); });
                return;
            }
            capturingKey = false;
            BeginInvoke(() => { CancelCapture(); PickActionFor(key); });
            return;
        }
        if (bindingsByKey.TryGetValue(key, out var binding))
            actionQueue.Writer.TryWrite(binding);
        else
            BeginInvoke(() => SetStatus(T($"Klávesa {KeyDisplay.Describe(key)} nemá přiřazenou akci.", $"Key {KeyDisplay.Describe(key)} has no action assigned.")));
    }

    void PickActionFor(int key)
    {
        RestoreWindow();
        var partNames = currentScore?.Parts.Select(p => p.Name).ToList()
            ?? currentStems?.Tracks.Select(t => t.Name).ToList();
        using var picker = new ActionPickerForm(KeyDisplay.Describe(key), partNames);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result == null)
        {
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        var binding = picker.Result;
        if (binding.Action == MacroAction.PlayNote)
        {
            using var notePicker = new NotePickerForm();
            if (notePicker.ShowDialog(this) != DialogResult.OK || notePicker.Result is not { } pitch)
            {
                SetStatus(T("Zrušeno.", "Cancelled."));
                return;
            }
            binding.Pitch = pitch;
        }
        Assign(key, binding);
        // A knob: the other way of turning it gets the opposite action, without going through the list again.
        if (picker.PairOpposite && MacroActions.Opposite(binding.Action) is { } opposite)
        {
            pendingOppositeFrom = key;
            pendingOpposite = new Binding { Action = opposite, PartIndex = binding.PartIndex, PartName = binding.PartName };
            capturingKey = true;
            addButton.Text = T("Zrušit", "Cancel");
            SetStatus(T($"{KeyDisplay.Describe(key)} -> {binding.Describe()}. Teď otoč kolečkem na druhou stranu: {pendingOpposite.Describe()}",
                $"{KeyDisplay.Describe(key)} -> {binding.Describe()}. Now turn the knob the other way: {pendingOpposite.Describe()}"));
        }
    }

    void Assign(int key, Binding binding)
    {
        binding.Key = key;
        config.Bindings.RemoveAll(b => b.Key == key);
        config.Bindings.Add(binding);
        config.Save();
        RebuildBindings(select: binding);
        SetStatus($"{KeyDisplay.Describe(key)} -> {binding.Describe()}");
    }

    void EditSelected()
    {
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is Binding binding)
            PickActionFor(binding.Key);
    }

    void RemoveSelected()
    {
        if (list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not Binding binding)
            return;
        config.Bindings.Remove(binding);
        config.Save();
        RebuildBindings();
    }

    static string BindingsFileFilter => "JSON (*.json)|*.json|" + T("Všechny soubory", "All files") + " (*.*)|*.*";

    void ExportBindings()
    {
        if (config.Bindings.Count == 0)
        {
            ShowModal(this, Text, T("Nejsou žádné klávesy k exportu.", "There are no keys to export."), AntdUI.TType.Info);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = BindingsFileFilter,
            FileName = "keyboardutils-bindings.json",
            Title = T("Exportovat klávesy", "Export keys"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            config.ExportBindings(dialog.FileName);
            SetStatus(T($"Exportováno {config.Bindings.Count} kláves do {dialog.FileName}.", $"Exported {config.Bindings.Count} keys to {dialog.FileName}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Export se nepovedl: ", "Export failed: ") + ex.Message, AntdUI.TType.Error);
        }
    }

    void ImportBindings()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = BindingsFileFilter,
            Title = T("Importovat klávesy", "Import keys"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        List<Binding> imported;
        try
        {
            imported = AppConfig.ImportBindings(dialog.FileName);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowModal(this, Text, T("Soubor nejde načíst: ", "Could not read the file: ") + ex.Message, AntdUI.TType.Error);
            return;
        }

        var replace = DialogResult.Yes;
        if (config.Bindings.Count > 0)
        {
            replace = MessageBox.Show(this,
                T($"Soubor obsahuje {imported.Count} kláves.\n\nAno = nahradit všechny současné klávesy\nNe = přidat ke stávajícím (stejné klávesy se přepíšou)",
                    $"The file contains {imported.Count} keys.\n\nYes = replace all current keys\nNo = merge with current keys (matching keys are overwritten)"),
                T("Importovat klávesy", "Import keys"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (replace == DialogResult.Cancel)
                return;
        }

        if (replace == DialogResult.Yes)
            config.Bindings.Clear();
        var importedKeys = imported.Select(b => b.Key).ToHashSet();
        config.Bindings.RemoveAll(b => importedKeys.Contains(b.Key));
        config.Bindings.AddRange(imported);
        config.Save();
        RebuildBindings();
        SetStatus(T($"Importováno {imported.Count} kláves.", $"Imported {imported.Count} keys."));
    }

    void RebuildBindings(Binding? select = null)
    {
        bindingsByKey = config.Bindings.ToDictionary(b => b.Key);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var b in config.Bindings.OrderBy(b => b.Key))
        {
            var item = new ListViewItem([KeyDisplay.Describe(b.Key), b.Describe()]) { Tag = b };
            list.Items.Add(item);
            if (b == select)
                item.Selected = true;
        }
        list.EndUpdate();
    }

    // --- Action dispatch --------------------------------------------------------------------------

    /// <summary>
    /// Runs the macro keys' actions on the UI thread, one at a time. Presses of one key that pile up meanwhile - a
    /// knob turned fast sends a burst - are done as one action with a count, so the UI keeps up instead of seeking
    /// and redrawing for every single step.
    /// </summary>
    async Task ActionLoop()
    {
        var reader = actionQueue.Reader;
        Binding? carried = null;
        while (true)
        {
            var binding = carried;
            carried = null;
            if (binding == null)
            {
                if (!await reader.WaitToReadAsync())
                    return;
                if (!reader.TryRead(out binding))
                    continue;
            }
            int count = 1;
            while (reader.TryRead(out var next))
            {
                if (next == binding)
                    count++;
                else
                {
                    carried = next;
                    break;
                }
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var action = binding;
            int times = count;
            Post(() =>
            {
                try { Dispatch(action, times); }
                finally { done.TrySetResult(); }
            });
            // An action can open a dialog and only return once it's closed; the keys answering that dialog must
            // still get through - so don't wait for it longer than a moment.
            await Task.WhenAny(done.Task, Task.Delay(100));
        }
    }

    Binding? lastDispatched;
    long lastDispatchedAt;

    /// <param name="count">Presses of the key in a row: how many steps for a step action; others run once.</param>
    void Dispatch(Binding binding, int count = 1)
    {
        bool step = MacroActions.IsStep(binding.Action);
        // An on/off switch hit twice within a moment is a knob or a bouncing key, not a decision to switch back.
        long now = Environment.TickCount64;
        bool repeat = binding == lastDispatched && now - lastDispatchedAt < 150;
        lastDispatched = binding;
        lastDispatchedAt = now;
        if (!step && repeat)
            return;
        RunFromControl(() => DispatchAction(binding, count));
    }

    void DispatchAction(Binding binding, int count)
    {
        if (DispatchMore(binding, count))
            return;
        switch (binding.Action)
        {
            case MacroAction.PlayPause:
                TogglePlayPause();
                break;
            case MacroAction.Stop:
                DoStop();
                break;

            case MacroAction.ToggleMutePart:
                // Same rows as the parts panel: stems when loaded, otherwise parts.
                int? partIndex = currentStems != null ? currentStems.ResolvePartIndex(binding) : currentScore?.ResolvePartIndex(binding);
                if (partIndex is { } index)
                {
                    bool currentlyMuted = currentStems != null ? currentStems.Tracks[index].Muted : currentScore!.Parts[index].Muted;
                    SetPartMuted(index, !currentlyMuted);
                    RebuildPartsPanel();
                    SetStatus(binding.Describe());
                }
                break;
            case MacroAction.PlayNote:
                if (binding.Pitch is { } pitch)
                {
                    playbackEngine.PlayNote(pitch);
                    pianoRoll.FlashKey(pitch);
                    SetStatus(binding.Describe());
                }
                break;
            case MacroAction.ToggleLearningMode:
                // From a macro key there's no one at the mouse: start straight away with the last settings.
                ToggleLearningMode(showSetup: false);
                break;
            case MacroAction.ToggleCreateMode:
                ToggleCreateMode();
                break;
            case MacroAction.SpeedReset:
                ResetSpeed();
                break;
            case MacroAction.ToggleLoop:
                LoopToggleAction();
                break;
            case MacroAction.LoopStart:
                LoopStartAction();
                break;
            case MacroAction.LoopEnd:
                LoopEndAction();
                break;
            case MacroAction.ToggleMetronome:
                ToggleMetronome();
                break;
            case MacroAction.ToggleKeyLabels:
                ToggleKeyLabels();
                break;
            case MacroAction.SwitchView:
                CycleSongView();
                break;
            case MacroAction.ToggleListening:
                ToggleListening();
                break;
            case MacroAction.OpenSong:
                RestoreWindow();
                OpenSong();
                break;
            default:
                SetStatus(T("Akce: ", "Action: ") + binding.Describe());
                break;
        }
    }

    void Post(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(action);
    }

    void SetStatus(string text)
    {
        statusLabel.Text = text;
        if (fromControl && config.BigNotices)
            toast.ShowMessage(text);
    }

    /// <summary>Runs a macro key's or a shortcut's action, its status message shown big.</summary>
    void RunFromControl(Action action)
    {
        bool outer = fromControl;
        fromControl = true;
        try { action(); }
        finally { fromControl = outer; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            playbackTimer.Dispose();
            seekRepeatTimer.Dispose();
            standaloneMetronome?.Stop();
            toolTip.Dispose();
            playbackEngine.Dispose();
            stemPlaybackEngine.Dispose();
            noteSource?.Dispose();
        }
        base.Dispose(disposing);
    }
}
