namespace KeyboardUtils;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\KeyboardUtils", out bool isFirstInstance);
        if (!isFirstInstance)
            return;

        // Must happen before anything might touch MacroKeyboard / BasicPitch types - see Modules/MacroKeyboardModule.cs.
        MacroKeyboardModule.RegisterAssemblyResolve();
        BasicPitchModule.RegisterAssemblyResolve();

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

        // The macro keyboard (second keyboard controlling the app) is an optional extra: not everyone has a
        // spare keyboard, or wants to use one. If the module isn't downloaded yet, or the Interception driver
        // isn't installed, the app still runs fully - it just offers to set things up when the user opens
        // the "Makro klávesnice" tab.
        IMacroKeyboardInterceptor? interceptor = MacroKeyboardModule.IsInstalled ? MacroKeyboardFactory.TryCreate() : null;

        using (interceptor)
            Application.Run(new MainForm(config, interceptor));
    }
}
