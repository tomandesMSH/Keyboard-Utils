using System.Threading.Channels;

namespace KeyboardUtils;

sealed class MainForm : AntdUI.Window
{

    readonly AppConfig config;
    IMacroKeyboardInterceptor? interceptor;
    INoteSource? noteSource;
    LearningSession? learning;
    string learningStatus = "";
    NoteRecorder? recorder;
    string recordingTitle = "";
    bool resumeAfterSeek;
    readonly PlaybackEngine playbackEngine = new();
    readonly StemPlaybackEngine stemPlaybackEngine = new();
    readonly Channel<Binding> actionQueue = Channel.CreateUnbounded<Binding>(new UnboundedChannelOptions { SingleReader = true });
    readonly List<string> outputDeviceNames = PlaybackEngine.GetOutputDeviceNames().ToList();

    // Read from the interception thread; replaced wholesale, never mutated.
    volatile IReadOnlyDictionary<int, Binding> bindingsByKey = new Dictionary<int, Binding>();
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
    readonly AntdUI.Label songLabel = new() { AutoSize = true, Text = "" };
    readonly AntdUI.Label stemsInfoLabel = new() { AutoSize = false, Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 0, 6, 0), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Label statusLabel = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(6) };
    readonly AntdUI.Button learnButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button addButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button editButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button removeButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button exportButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button importButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button openSongButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button importStemsButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button playPauseButton = new() { AutoSize = true, Enabled = false, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button stopButton = new() { AutoSize = true, Enabled = false, Radius = 8 };
    readonly AntdUI.Button previousButton = new() { AutoSize = true, Enabled = false, Radius = 8 };
    readonly AntdUI.Button nextButton = new() { AutoSize = true, Enabled = false, Radius = 8 };
    readonly AntdUI.Button listenButton = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button learningButton = new() { AutoSize = true, Enabled = false, Type = AntdUI.TTypeMini.Success, Radius = 8 };
    readonly AntdUI.Button createButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Error, Radius = 8 };
    // List = true: a pick-only dropdown. Without it AntdUI.Select is an editable text box with suggestions.
    readonly AntdUI.Select outputDeviceBox = new() { Size = new Size(240, 36), Margin = new Padding(3), List = true, ListAutoWidth = true };
    readonly AntdUI.Label outputDeviceLabel = new() { AutoSize = true, Margin = new Padding(2, 0, 0, 2), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Label loadGroupLabel = new() { AutoSize = true, Margin = new Padding(2, 0, 0, 2), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Label playbackGroupLabel = new() { AutoSize = true, Margin = new Padding(2, 0, 0, 2), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Label playingGroupLabel = new() { AutoSize = true, Margin = new Padding(2, 0, 0, 2), ForeColor = SystemColors.GrayText };
    readonly AntdUI.Checkbox enabledBox = new() { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    readonly AntdUI.Label languageLabel = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    readonly AntdUI.Select languageBox = new() { Size = new Size(110, 36), Margin = new Padding(3, 0, 3, 3), List = true };
    readonly FlowLayoutPanel partsPanel = new() { Dock = DockStyle.Right, Width = 180, AutoScroll = true, Padding = new Padding(6), FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly PianoRollControl pianoRoll = new() { Dock = DockStyle.Fill };
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
        Icon = SystemIcons.Application;
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
            else if (currentScore != null)
                pianoRoll.SetCurrentTime(playbackEngine.CurrentSeconds);
            UpdatePlayPauseButton();
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

        RefreshOutputDeviceItems();

        // --- Song tab: grouped controls + piano roll + per-part mute panel ---
        // Each group has a caption so it's clear what its buttons act on. The row wraps whole groups when the
        // window is too narrow.
        var songControlsRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        songControlsRow.Controls.AddRange(
        [
            ControlGroup(loadGroupLabel, openSongButton, importStemsButton),
            ControlGroup(playbackGroupLabel, previousButton, playPauseButton, stopButton, nextButton),
            ControlGroup(outputDeviceLabel, outputDeviceBox),
            ControlGroup(playingGroupLabel, listenButton, learningButton, createButton),
        ]);
        var songLabelRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        songLabelRow.Controls.Add(songLabel);

        songTab.Controls.Add(pianoRoll);
        songTab.Controls.Add(partsPanel);
        songTab.Controls.Add(songLabelRow);
        songTab.Controls.Add(stemsInfoLabel);
        songTab.Controls.Add(songControlsRow);

        pianoRoll.Clickable = true;
        pianoRoll.KeySelected += pitch => { playbackEngine.PlayNote(pitch); pianoRoll.FlashKey(pitch); };

        // Drag the notes to move through the song; playback pauses while dragging and resumes on release.
        pianoRoll.Seekable = true;
        pianoRoll.SeekStarted += () =>
        {
            resumeAfterSeek = playbackEngine.IsRunning;
            if (resumeAfterSeek)
                playbackEngine.Pause();
        };
        pianoRoll.Seek += seconds =>
        {
            if (currentScore == null || learning != null || recorder != null)
                return;
            playbackEngine.SeekTo(seconds);
            pianoRoll.SetCurrentTime(seconds);
        };
        pianoRoll.SeekEnded += () =>
        {
            if (resumeAfterSeek)
                playbackEngine.Play();
            resumeAfterSeek = false;
        };

        openSongButton.Click += (_, _) => OpenSong();
        importStemsButton.Click += (_, _) => ImportStems();
        playPauseButton.Click += (_, _) => TogglePlayPause();
        stopButton.Click += (_, _) => DoStop();
        previousButton.Click += (_, _) => DoPreviousTrack();
        nextButton.Click += (_, _) => DoNextTrack();
        listenButton.Click += (_, _) => ToggleListening();
        learningButton.Click += (_, _) => ToggleLearningMode(showSetup: true);
        createButton.Click += (_, _) => ToggleCreateMode();
        outputDeviceBox.SelectedIndexChanged += (_, _) =>
        {
            string? name = outputDeviceBox.SelectedIndex > 0 ? outputDeviceNames[outputDeviceBox.SelectedIndex - 1] : null;
            config.MidiOutputDevice = name;
            config.Save();
            playbackEngine.SetOutputDevice(name);
            if (currentScore != null)
                playbackEngine.Load(currentScore);
        };

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
        Controls.Add(statusLabel);

        menuBar.Controls.AddRange([fileMenuButton, editMenuButton, settingsMenuButton, macroSetupMenuButton]);
        Controls.Add(menuBar);

        // Title bar must be added LAST (and never BringToFront - that resets its z-index to 0, which for
        // Dock layout means innermost, not outermost). Last-added Dock=Top control sits closest to the edge.
        var titleBar = new TitleBar(this, showMinimize: true, showMaximize: true);
        Controls.Add(titleBar);

        fileMenuButton.Click += (_, _) => ShowMenu(fileMenuButton,
            MenuItem(T("Otevřít skladbu...", "Open song..."), OpenSong),
            MenuItem(T("Importovat stopy (WAV)...", "Import stems (WAV)..."), ImportStems),
            MenuItem(T("Uložit skladbu jako...", "Save song as..."), SaveCurrentSong),
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

    /// <summary>A small grey caption above a row of related controls.</summary>
    static FlowLayoutPanel ControlGroup(AntdUI.Label caption, params Control[] controls)
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

        var deviceItems = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(T("(systémové výchozí)", "(system default)"))
            {
                Checked = outputDeviceBox.SelectedIndex == 0,
                Tag = (Action)(() => outputDeviceBox.SelectedIndex = 0),
            },
        };
        for (int i = 0; i < outputDeviceNames.Count; i++)
        {
            int selectIndex = i + 1;
            deviceItems.Add(new AntdUI.ContextMenuStripItem(outputDeviceNames[i])
            {
                Checked = outputDeviceBox.SelectedIndex == selectIndex,
                Tag = (Action)(() => outputDeviceBox.SelectedIndex = selectIndex),
            });
        }

        return
        [
            new AntdUI.ContextMenuStripItem(T("Ovládání zapnuté", "Controls on")) { Checked = enabledBox.Checked, Tag = (Action)(() => enabledBox.Checked = !enabledBox.Checked) },
            new AntdUI.ContextMenuStripItemDivider(),
            new AntdUI.ContextMenuStripItem(T("Jazyk", "Language")) { Sub = languageItems },
            new AntdUI.ContextMenuStripItem(T("MIDI výstup", "MIDI output")) { Sub = deviceItems.ToArray() },
            new AntdUI.ContextMenuStripItem(T("Vstup not", "Note input")) { Sub = BuildNoteInputItems() },
            new AntdUI.ContextMenuStripItem(T("Mikrofon a citlivost...", "Microphone and sensitivity...")) { Tag = (Action)ShowMicrophoneSetup },
        ];
    }

    /// <summary>Microphone vs. MIDI input, as one flat list: "Mikrofon" first, then each MIDI input by name.</summary>
    AntdUI.IContextMenuStripItem[] BuildNoteInputItems()
    {
        var items = new List<AntdUI.IContextMenuStripItem>
        {
            new AntdUI.ContextMenuStripItem(T("Mikrofon (akustické piano)", "Microphone (acoustic piano)"))
            {
                Checked = config.NoteInput == NoteInputKind.Microphone,
                Tag = (Action)(() => SetNoteInput(NoteInputKind.Microphone, config.MidiInputDevice)),
            },
        };
        foreach (var name in MidiNoteSource.GetDeviceNames())
            items.Add(new AntdUI.ContextMenuStripItem("MIDI: " + name)
            {
                Checked = config.NoteInput == NoteInputKind.Midi && config.MidiInputDevice == name,
                Tag = (Action)(() => SetNoteInput(NoteInputKind.Midi, name)),
            });
        return items.ToArray();
    }

    void RefreshOutputDeviceItems()
    {
        int selected = config.MidiOutputDevice != null ? outputDeviceNames.IndexOf(config.MidiOutputDevice) + 1 : 0;
        outputDeviceBox.Items.Clear();
        outputDeviceBox.Items.Add(T("(systémové výchozí)", "(system default)"));
        foreach (var name in outputDeviceNames)
            outputDeviceBox.Items.Add(name);
        outputDeviceBox.SelectedIndex = selected >= 0 ? selected : 0;
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
        openSongButton.Text = T("Otevřít skladbu...", "Open song...");
        importStemsButton.Text = T("Importovat stopy...", "Import stems...");
        loadGroupLabel.Text = T("Načíst", "Load");
        playbackGroupLabel.Text = T("Přehrávání", "Playback");
        outputDeviceLabel.Text = T("Zvuk přehrávání (MIDI výstup)", "Playback sound (MIDI output)");
        playingGroupLabel.Text = T("Hraješ ty (mikrofon / MIDI)", "You play (microphone / MIDI)");
        stemsInfoLabel.Text = T(
            "Tip: FL Studio - Export > WAV File > Miscellaneous > \"Split mixer tracks\" vyexportuje každou stopu zvlášť. "
            + "Rozpracované verze skladeb (stems) se dají občas stáhnout i hotové, např. na RemixPacks a podobných stránkách.",
            "Tip: FL Studio - Export > WAV File > Miscellaneous > \"Split mixer tracks\" exports each track separately. "
            + "Stems (a song split into its instruments) can also sometimes be downloaded ready-made, e.g. from RemixPacks and similar sites.");
        playPauseButton.Text = T("Přehrát", "Play");
        stopButton.Text = T("Stop", "Stop");
        previousButton.Text = T("<< Předchozí", "<< Previous");
        nextButton.Text = T("Další >>", "Next >>");
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
        RefreshOutputDeviceItems();
        RebuildBindings();
        UpdateKeyboardLabel();
        UpdateSongLabel();
        UpdatePlayPauseButton();
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
    void StartLearningMode(bool showSetup)
    {
        if (currentScore is not { } score)
        {
            ShowModal(this, Text, T("Nejdřív otevři skladbu (MIDI nebo MusicXML).", "Open a song first (MIDI or MusicXML)."), AntdUI.TType.Info);
            return;
        }

        List<int> parts;
        if (showSetup)
        {
            using var setup = new LearningSetupForm(score, config);
            if (setup.ShowDialog(this) != DialogResult.OK)
                return;
            parts = setup.PracticedParts;
            config.LearningMode = setup.SelectedMode;
            config.LearningSpeedPercent = setup.SpeedPercent;
            config.LearningAccompaniment = setup.Accompaniment;
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
        playbackEngine.Stop();
        double speed = config.LearningSpeedPercent / 100.0;
        bool tempo = config.LearningMode == LearningMode.InTempo;
        if (tempo)
        {
            // The practiced parts are the player's; the rest plays along only if asked for.
            for (int i = 0; i < score.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, parts.Contains(i) || !config.LearningAccompaniment || score.Parts[i].Muted);
            playbackEngine.Speed = speed;
        }

        var session = new LearningSession(score, parts, config.LearningMode, speed, source.Polyphonic, source.LatencySeconds,
            tempo ? () => playbackEngine.CurrentSeconds : null,
            tempo ? playbackEngine.Play : null);
        session.WrongKey += pitch => pianoRoll.FlashKey(pitch, 300, PianoRollControl.WrongColor);
        session.Finished += () => BeginInvoke(() => { if (learning == session) StopLearningMode(showResult: true); });
        learning = session;
        learningStatus = "";
        pianoRoll.Learning = session;
        partsPanel.Enabled = false;
        UpdateLearningButton();
    }

    void TickLearning()
    {
        var session = learning!;
        session.Tick();
        pianoRoll.SetCurrentTime(session.Position);
        string status = session.Position < 0
            ? T("Připrav se...", "Get ready...")
            : T($"Učení: {session.DoneNotes}/{session.TotalNotes} not, správně {session.CorrectNotes}, špatné klávesy {session.WrongPresses}",
                $"Learning: {session.DoneNotes}/{session.TotalNotes} notes, {session.CorrectNotes} correct, {session.WrongPresses} wrong keys");
        if (session.Paused)
            status = T("Pozastaveno - ", "Paused - ") + status;
        if (status != learningStatus)
            SetStatus(learningStatus = status);
    }

    void StopLearningMode(bool showResult)
    {
        if (learning is not { } session)
            return;
        var result = session.Stop();
        learning = null;
        pianoRoll.Learning = null;
        partsPanel.Enabled = true;

        playbackEngine.Stop();
        playbackEngine.Speed = 1;
        if (currentScore != null)
            for (int i = 0; i < currentScore.Parts.Count; i++)
                playbackEngine.SetPartMuted(i, currentScore.Parts[i].Muted);
        pianoRoll.SetCurrentTime(0);
        UpdateLearningButton();
        UpdateCreateButton();
        SetStatus(T("Režim učení ukončen.", "Learning mode ended."));

        // Nothing played yet: there's nothing to score.
        if (!showResult || (session.DoneNotes == 0 && !result.Completed))
            return;
        using var dialog = new LearningResultForm(result, currentScore?.Title ?? "");
        if (dialog.ShowDialog(this) == DialogResult.Retry)
            StartLearningMode(showSetup: false);
    }

    void UpdateLearningButton()
    {
        learningButton.Enabled = currentScore != null && recorder == null;
        // Moving the time would break a learning session's scoring.
        pianoRoll.Seekable = learning == null;
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
        pianoRoll.UpdateLiveScore(recorder.Snapshot(recordingTitle));
        pianoRoll.SetCurrentTime(0);
        UpdateCreateButton();
        UpdateLearningButton();
        SetStatus(T("Nahrávám - hraj. Noty se zapisují od první zahrané.", "Recording - play. Notes are written from the first one you play."));
    }

    void TickCreateMode()
    {
        var active = recorder!;
        pianoRoll.UpdateLiveScore(active.Snapshot(recordingTitle));
        pianoRoll.SetCurrentTime(active.CurrentSeconds);
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
        }
        else
            pianoRoll.SetScore(currentScore);
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
        SetStatus(T($"Nahráno: {recording.Parts[0].Notes.Count} not.", $"Recorded: {recording.Parts[0].Notes.Count} notes."));

        using var dialog = new CreateResultForm(recording, config.CreateTempoBpm, bpm => SaveScore(recording, bpm));
        dialog.ShowDialog(this);
        if (dialog.Bpm != config.CreateTempoBpm)
        {
            config.CreateTempoBpm = dialog.Bpm;
            config.Save();
        }
    }

    void UpdateCreateButton() =>
        createButton.Text = recorder != null ? T("Ukončit nahrávání", "Stop recording") : T("Zapisovat noty", "Write down notes");

    void SaveCurrentSong()
    {
        if (currentScore == null)
        {
            ShowModal(this, Text, T("Není otevřená žádná skladba (MIDI, MusicXML nebo nahrávka).", "No song is open (MIDI, MusicXML or a recording)."), AntdUI.TType.Info);
            return;
        }
        SaveScore(currentScore, config.CreateTempoBpm);
    }

    /// <summary>Asks where and saves; true if the file was written.</summary>
    bool SaveScore(Score score, int bpm)
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
        try
        {
            ScoreExporter.Save(score, dialog.FileName, bpm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("Uložení se nepovedlo: ", "Saving failed: ") + ex.Message, AntdUI.TType.Error);
            return false;
        }
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
        SetStatus(T($"Načteno: {score.Title}", $"Loaded: {score.Title}"));
    }

    /// <summary>Makes <paramref name="score"/> the current song: playback, piano roll, parts panel, buttons.</summary>
    void ShowScore(Score score)
    {
        StopLearningMode(showResult: false);
        stemPlaybackEngine.Stop();
        currentStems = null;
        currentScore = score;

        playbackEngine.Load(currentScore);
        pianoRoll.SetScore(currentScore);
        RebuildPartsPanel();
        playPauseButton.Enabled = stopButton.Enabled = true;
        previousButton.Enabled = nextButton.Enabled = playlist.Count > 1;
        UpdateSongLabel();
        UpdateLearningButton();
    }

    void ImportStems()
    {
        FinishCreateMode();
        using var dialog = new FolderBrowserDialog { Description = T("Vyber složku se stopami (.wav)", "Choose the folder with the stems (.wav)") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        StemSet stems;
        try
        {
            stems = StemSetImporter.ImportFolder(dialog.SelectedPath);
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
        pianoRoll.SetScore(null);

        RebuildPartsPanel();
        playPauseButton.Enabled = stopButton.Enabled = true;
        previousButton.Enabled = nextButton.Enabled = false;
        UpdateSongLabel();
        UpdateLearningButton();
        SetStatus(T($"Načteny stopy: {stems.Title} ({stems.Tracks.Count})", $"Loaded stems: {stems.Title} ({stems.Tracks.Count})"));
    }

    void UpdateSongLabel()
    {
        songLabel.Text = currentScore != null
            ? $"{currentScore.Title}  ({TimeSpan.FromSeconds(currentScore.DurationSeconds):m\\:ss})" +
              (playlist.Count > 1 ? $"   [{playlistIndex + 1}/{playlist.Count}]" : "")
            : currentStems != null
                ? $"{currentStems.Title}  ({TimeSpan.FromSeconds(stemPlaybackEngine.DurationSeconds):m\\:ss})  [{T("stopy", "stems")}]"
                : T("Žádná skladba nenačtena.", "No song loaded.");
    }

    void RebuildPartsPanel()
    {
        partsPanel.Controls.Clear();
        var parts = currentScore?.Parts.Select(p => (p.Name, p.Muted)).ToList()
            ?? currentStems?.Tracks.Select(t => (t.Name, t.Muted)).ToList()
            ?? [];
        if (parts.Count == 0)
            return;

        var header = new AntdUI.Label { Text = T("Části / nástroje:", "Parts / instruments:"), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        partsPanel.Controls.Add(header);
        for (int i = 0; i < parts.Count; i++)
        {
            int index = i;
            var box = new AntdUI.Checkbox { Text = parts[i].Name, Checked = !parts[i].Muted, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            box.CheckedChanged += (_, _) => SetPartMuted(index, !box.Checked);
            partsPanel.Controls.Add(box);
        }
    }

    void SetPartMuted(int partIndex, bool muted)
    {
        if (currentScore != null)
        {
            if (partIndex < 0 || partIndex >= currentScore.Parts.Count)
                return;
            currentScore.Parts[partIndex].Muted = muted;
            playbackEngine.SetPartMuted(partIndex, muted);
        }
        else if (currentStems != null)
        {
            if (partIndex < 0 || partIndex >= currentStems.Tracks.Count)
                return;
            currentStems.Tracks[partIndex].Muted = muted;
            stemPlaybackEngine.SetTrackMuted(partIndex, muted);
        }
        pianoRoll.Invalidate();
    }

    void TogglePlayPause()
    {
        // Playing the song over a recording would be heard (by the mic) and written down.
        if (recorder != null)
            return;
        if (learning != null)
        {
            bool pause = !learning.Paused;
            learning.SetPaused(pause);
            // In tempo mode the accompaniment is the clock - but only once the lead-in has started it.
            if (learning.Mode == LearningMode.InTempo && learning.Position >= 0)
            {
                if (pause) playbackEngine.Pause(); else playbackEngine.Play();
            }
        }
        else if (currentScore != null)
        {
            if (playbackEngine.IsRunning) playbackEngine.Pause(); else playbackEngine.Play();
        }
        else if (currentStems != null)
        {
            if (stemPlaybackEngine.IsRunning) stemPlaybackEngine.Pause(); else stemPlaybackEngine.Play();
        }
        else
        {
            return;
        }
        UpdatePlayPauseButton();
    }

    void UpdatePlayPauseButton()
    {
        bool running = learning != null ? !learning.Paused
            : currentScore != null ? playbackEngine.IsRunning
            : currentStems != null && stemPlaybackEngine.IsRunning;
        playPauseButton.Text = running ? T("Pauza", "Pause") : T("Přehrát", "Play");
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
        if (currentScore != null)
            playbackEngine.Stop();
        else if (currentStems != null)
            stemPlaybackEngine.Stop();
        pianoRoll.SetCurrentTime(0);
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
        addButton.Text = T("Přidat klávesu...", "Add key...");
    }

    /// <summary>Runs on the interception thread. Must return immediately.</summary>
    void OnMacroKey(int key)
    {
        if (capturingKey)
        {
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

    async Task ActionLoop()
    {
        await foreach (var binding in actionQueue.Reader.ReadAllAsync())
            Post(() => Dispatch(binding));
    }

    void Dispatch(Binding binding)
    {
        switch (binding.Action)
        {
            case MacroAction.PlayPause:
                TogglePlayPause();
                break;
            case MacroAction.Stop:
                DoStop();
                break;
            case MacroAction.NextTrack:
                DoNextTrack();
                break;
            case MacroAction.PreviousTrack:
                DoPreviousTrack();
                break;
            case MacroAction.ToggleMutePart:
                int? partIndex = currentScore?.ResolvePartIndex(binding) ?? currentStems?.ResolvePartIndex(binding);
                if (partIndex is { } index)
                {
                    bool currentlyMuted = currentScore != null ? currentScore.Parts[index].Muted : currentStems!.Tracks[index].Muted;
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

    void SetStatus(string text) => statusLabel.Text = text;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            playbackTimer.Dispose();
            playbackEngine.Dispose();
            stemPlaybackEngine.Dispose();
            noteSource?.Dispose();
        }
        base.Dispose(disposing);
    }
}
