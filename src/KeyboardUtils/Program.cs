namespace KeyboardUtils;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\KeyboardUtils", out bool isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();

        // Light, clean theme (ArtCraft-ish: plenty of whitespace, no visual clutter) for every window/dialog.
        AntdUI.Config.Mode = AntdUI.TMode.Light;
        AntdUI.Config.Font = new Font("Segoe UI", 9.5f);

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
        // spare keyboard, or wants to use one. If the Interception driver isn't installed, the app still runs
        // fully - it just offers to set the driver up when the user opens the "Makro klávesnice" tab.
        KeyboardInterceptor? interceptor = null;
        try
        {
            interceptor = KeyboardInterceptor.Create();
        }
        catch (InterceptionUnavailableException)
        {
        }
        catch (DllNotFoundException)
        {
        }

        using (interceptor)
        {
            bool startHidden = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            Application.Run(new MainForm(config, interceptor, startHidden));
        }
    }
}
