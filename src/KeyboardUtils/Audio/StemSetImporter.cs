namespace KeyboardUtils;

/// <summary>
/// Loads a folder of .wav files as a <see cref="StemSet"/>. Matches two common sources: FL Studio's
/// Export > WAV File > Miscellaneous > "Split mixer tracks" (one .wav per mixer track), and downloaded
/// stem/remix packs (e.g. from sites like RemixPacks) that split a song into vocals/drums/bass/etc.
/// </summary>
static class StemSetImporter
{
    public static StemSet ImportFolder(string folderPath)
    {
        var files = Directory.GetFiles(folderPath, "*.wav", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            throw new InvalidDataException(T("Ve složce nejsou žádné .wav soubory.", "No .wav files found in the folder."));

        var set = new StemSet { Title = new DirectoryInfo(folderPath).Name };
        foreach (var file in files)
            set.Tracks.Add(new StemTrack { Name = Path.GetFileNameWithoutExtension(file), FilePath = file });
        return set;
    }
}
