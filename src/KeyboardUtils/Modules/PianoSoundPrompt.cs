namespace KeyboardUtils;

/// <summary>
/// The MIDI output menu's "better piano sound" item: downloads the sound pack (<see cref="PianoSoundModule"/>) when it
/// isn't there yet, then switches to it.
/// </summary>
static class PianoSoundPrompt
{
    static string Title => T("Klavír Keyboard Utils", "Keyboard Utils piano");

    /// <summary>True when the pack is (now) installed and can be chosen. <paramref name="status"/> shows download progress.</summary>
    public static async Task<bool> EnsureInstalledAsync(IWin32Window owner, Action<string> status)
    {
        if (PianoSoundModule.IsInstalled)
            return true;
        var answer = MessageBox.Show(owner,
            T($"Lepší zvuk klavíru je nahrávka skutečného pianina (Kawai, volně šiřitelná - FreePats, CC0). Je potřeba ji jednou stáhnout, asi {PianoSoundModule.ApproximateDownloadMb} MB.\n\nStáhnout teď?",
              $"The better piano sound is a recording of a real upright piano (Kawai, free to share - FreePats, CC0). It has to be downloaded once, about {PianoSoundModule.ApproximateDownloadMb} MB.\n\nDownload it now?"),
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return false;

        status(T("Stahuji zvuk klavíru...", "Downloading the piano sound..."));
        var progress = new Progress<double>(p => status(T($"Stahuji zvuk klavíru... {p:P0}", $"Downloading the piano sound... {p:P0}")));
        bool ok = await PianoSoundModule.DownloadAsync(progress);
        if (!ok)
            status(T("Stažení zvuku klavíru se nepovedlo - zkontroluj připojení k internetu.", "Downloading the piano sound failed - check your internet connection."));
        return ok;
    }

    /// <summary>Asks before deleting the pack.</summary>
    public static bool ConfirmRemove(IWin32Window owner)
    {
        var answer = MessageBox.Show(owner,
            T("Odebrat stažený zvuk klavíru? Přehrávání pak použije výchozí zvuk Windows. Kdykoli ho můžeš stáhnout znovu.",
              "Remove the downloaded piano sound? Playback will then use Windows' default sound. You can download it again any time."),
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return answer == DialogResult.Yes;
    }
}
