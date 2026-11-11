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

    /// <summary>Live while a just-stopped recording can still be fixed up (click/drag/delete notes) before saving.</summary>
    NoteEditor? noteEditor;
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
    readonly AntdUI.Button tapButton = new() { Size = new Size(92, 30), Margin = new Padding(0), Radius = 6 }; // width set by AlignDetectRow
    // List = true: pick-only, not an editable box.
    readonly AntdUI.Select meterBox = new() { Size = new Size(78, 30), Margin = new Padding(6, 0, 0, 0), List = true };
    readonly TapTempo tapTempo = new();
    readonly System.Diagnostics.Stopwatch tapClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>"New version x - download" in the bottom bar, once a newer release is found.</summary>
    readonly AntdUI.Button updateButton = new() { Height = 26, Type = AntdUI.TTypeMini.Primary, Radius = 6, Margin = new Padding(0) };

    /// <summary>Holds <see cref="updateButton"/> in the bottom bar, its height fitting the bar; hidden until there's an update.</summary>
    readonly FlowLayoutPanel updateGroup = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 3, 12, 0), Visible = false };
    AvailableUpdate? availableUpdate;
    readonly FlowLayoutPanel metronomeOptions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };

    /// <summary>Under <see cref="metronomeOptions"/>: tempo detection (tapping) and the time signature.</summary>
    readonly FlowLayoutPanel metronomeDetectRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };

    /// <summary>The arrow by the Metronome box: hides / shows its extra options, the metronome staying on.</summary>
    readonly AntdUI.Button metronomeExpander = new() { Size = new Size(24, 24), Margin = new Padding(2, 7, 0, 0), Ghost = true, IconRatio = 0.9f, Radius = 4 };
    StandaloneMetronome? standaloneMetronome;

    StandaloneMetronome Clicker => standaloneMetronome ??= new StandaloneMetronome(playbackEngine.PlayClick)
    {
        Bpm = config.MetronomeBpm,
        Meter = config.MetronomeMeter,
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
    readonly MenuBarButton helpMenuButton = new();

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
        // At most once a day, in the background; offline or not, the app starts the same.
        Shown += (_, _) =>
        {
            if (config.CheckForUpdates && (config.LastUpdateCheck is not { } last || DateTime.Now - last > TimeSpan.FromHours(24)))
                _ = CheckForUpdatesAsync(manual: false);
        };
        InitIntegration();
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

        // Create mode, after recording: fix a wrong note instead of re-recording the whole take.
        pianoRoll.NoteSelected += EditNoteSelected;
        pianoRoll.NoteToggleSelectRequested += ToggleNoteSelection;
        pianoRoll.MarqueeSelectRequested += ApplyMarqueeSelection;
        // A drag can call NoteDragged once per pixel - grouped into one undo step by Begin/EndEdit.
        pianoRoll.NoteDragStarted += () => noteEditor?.BeginEdit();
        pianoRoll.NoteDragEnded += () => noteEditor?.EndEdit();
        pianoRoll.NoteDragged += MoveSelection;
        pianoRoll.NotePitchChangeRequested += RepitchSelection;
        pianoRoll.NoteDeleteRequested += DeleteSelection;
        pianoRoll.NoteEditRequested += EditSelectedNoteProperties;

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
        updateGroup.Controls.Add(updateButton);
        statusRow.Controls.Add(updateGroup);
        statusRow.Controls.Add(keyboardSizeGroup);
        updateButton.Click += (_, _) => _ = UpdateAsync();
        tabs.SelectedIndexChanged += (_, _) => keyboardSizeGroup.Visible = tabs.SelectedTab == songTab;
        Controls.Add(statusRow);

        menuBar.Controls.AddRange([fileMenuButton, editMenuButton, settingsMenuButton, helpMenuButton]);
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
            new AntdUI.ContextMenuStripItem(T("Knihovna", "Library")) { Sub = BuildLibraryMenuItems() },
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

        helpMenuButton.Click += (_, _) => ShowMenu(helpMenuButton,
            MenuItem(T("O aplikaci", "About"), () => { using var about = new AboutForm(); about.ShowDialog(this); }),
            MenuItem(T("Nahlásit chybu...", "Report a bug..."), () => AboutForm.Open(AboutForm.IssuesUrl)));

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
            new AntdUI.ContextMenuStripItem(T("Kontrolovat aktualizace", "Check for updates"))
            {
                Checked = config.CheckForUpdates,
                Tag = (Action)(() => { config.CheckForUpdates = !config.CheckForUpdates; config.Save(); }),
            },
            new AntdUI.ContextMenuStripItem(T("Zkontrolovat aktualizace teď", "Check for updates now")) { Tag = (Action)(() => _ = CheckForUpdatesAsync(manual: true)) },
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
        helpMenuButton.Text = T("Nápověda", "Help");
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
