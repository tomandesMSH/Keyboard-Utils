namespace KeyboardUtils;

/// <summary>
/// Offers the optional chord detection add-on (<see cref="BasicPitchModule"/>) where it would help - converting audio
/// to notes - and downloads it if wanted. Declining is remembered, so it isn't asked again every time; Settings has
/// the same offer for later.
/// </summary>
static class ChordAddonPrompt
{
    /// <summary>
    /// True when the add-on is (now) installed. <paramref name="askEvenIfDeclined"/>: the user explicitly asked
    /// (Settings), so a previous "no" doesn't apply. <paramref name="status"/> shows download progress.
    /// </summary>
    public static async Task<bool> OfferAsync(IWin32Window owner, AppConfig config, Action<string> status, bool askEvenIfDeclined = false)
    {
        if (BasicPitchModule.IsInstalled)
            return true;
        if (config.ChordAddonDeclined && !askEvenIfDeclined)
            return false;

        var answer = MessageBox.Show(owner,
            T($"Rozpoznávání více not najednou (akordy) potřebuje doplněk - neuronovou síť Basic Pitch od Spotify (asi {BasicPitchModule.ApproximateDownloadMb} MB ke stažení).\n\n"
              + "Stáhnout teď?\n\nNe = převést jednohlasně (jedna nota naráz). Doplněk můžeš stáhnout i později v Nastavení.",
              $"Detecting several notes at once (chords) needs an add-on - Spotify's Basic Pitch neural network (about {BasicPitchModule.ApproximateDownloadMb} MB to download).\n\n"
              + "Download it now?\n\nNo = convert one note at a time. You can also download the add-on later in Settings."),
            T("Rozpoznávání akordů", "Chord detection"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            config.ChordAddonDeclined = true;
            config.Save();
            return false;
        }

        status(T("Stahuji doplněk...", "Downloading the add-on..."));
        var progress = new Progress<double>(p => status(T($"Stahuji doplněk... {p:P0}", $"Downloading the add-on... {p:P0}")));
        bool ok = await BasicPitchModule.DownloadAsync(progress, CancellationToken.None);
        if (ok)
        {
            config.ChordAddonDeclined = false;
            config.Save();
        }
        status(ok
            ? T("Doplněk pro rozpoznávání akordů je nainstalovaný.", "The chord detection add-on is installed.")
            : T("Stažení doplňku se nepovedlo - zkontroluj připojení k internetu.", "Downloading the add-on failed - check your internet connection."));
        return ok;
    }

    /// <summary>Settings: install, or show that it's installed and offer to remove it.</summary>
    public static async Task ManageAsync(IWin32Window owner, AppConfig config, Action<string> status)
    {
        if (!BasicPitchModule.IsInstalled)
        {
            await OfferAsync(owner, config, status, askEvenIfDeclined: true);
            return;
        }
        var answer = MessageBox.Show(owner,
            T("Doplněk pro rozpoznávání akordů (Basic Pitch) je nainstalovaný - převod zvuku na noty pozná víc not najednou.\n\nOdebrat ho?",
              "The chord detection add-on (Basic Pitch) is installed - converting audio to notes hears several notes at once.\n\nRemove it?"),
            T("Rozpoznávání akordů", "Chord detection"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
            return;
        if (BasicPitchModule.Remove())
            status(T("Doplněk odebrán.", "Add-on removed."));
        else
            MessageBox.Show(owner,
                T("Doplněk se právě používá. Zavři Keyboard Utils, spusť ho znovu a odeber ho hned po startu.",
                  "The add-on is in use. Close Keyboard Utils, start it again and remove the add-on right away."),
                T("Rozpoznávání akordů", "Chord detection"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
