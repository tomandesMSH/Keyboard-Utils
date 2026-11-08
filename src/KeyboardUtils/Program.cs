namespace KeyboardUtils;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // A keyboardutils:// link (Muzio's "Open in Keyboard Utils") or a song file to open.
        string? request = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !int.TryParse(a, out _));
        // Started by the previous version at the end of an update: let it quit first (it holds the mutex).
        bool justUpdated = SelfUpdater.WaitForPreviousInstance(args);

        using var mutex = new Mutex(initiallyOwned: true, @"Local\KeyboardUtils", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running: it opens the link (or just comes to the front) instead.
            ProtocolHandler.SendToRunningInstance(request ?? "");
            return;
        }

        // Must happen before anything might touch MacroKeyboard / BasicPitch types - see Modules/MacroKeyboardModule.cs.
        MacroKeyboardModule.RegisterAssemblyResolve();
        BasicPitchModule.RegisterAssemblyResolve();

        if (Environment.ProcessPath is { } exe && IsSingleFile)
            SelfUpdater.CleanUp(exe);

        Application.SetDefaultFont(new Font("Segoe UI", 9.5f));
        ApplicationConfiguration.Initialize();

        // Light, clean theme (ArtCraft-ish: plenty of whitespace, no visual clutter) for every window/dialog.
        AntdUI.Config.Mode = AntdUI.TMode.Light;

        var config = AppConfig.Load();
        if (config.Language == null)
        {
            using var picker = new LanguagePickerForm();
            if (picker.ShowDialog() == DialogResult.OK && picker.Result is { } language)
            {
                config.Language = language;
                config.Save();
            }
        }
        Lang.Current = config.Language ?? AppLanguage.Czech;
        AppTheme.SetDark(config.DarkMode);

        // Muzio's links and "Open with" for MIDI / MusicXML files (after the language: the file type has a name).
        if (Environment.ProcessPath is { } appExe)
            ProtocolHandler.Register(appExe, isPublished: IsSingleFile);

        // The macro keyboard (second keyboard controlling the app) is an optional extra: not everyone has a
        // spare keyboard, or wants to use one. If the module isn't downloaded yet, or the Interception driver
        // isn't installed, the app still runs fully - it just offers to set things up when the user opens
        // the "Makro klávesnice" tab.
        IMacroKeyboardInterceptor? interceptor = MacroKeyboardModule.IsInstalled ? MacroKeyboardFactory.TryCreate() : null;

        using (interceptor)
            Application.Run(new MainForm(config, interceptor) { StartupRequest = request, JustUpdated = justUpdated });
    }

    static bool IsSingleFile => SelfUpdater.IsSingleFile;
}
