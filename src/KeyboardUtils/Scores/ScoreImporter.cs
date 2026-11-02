namespace KeyboardUtils;

static class ScoreImporter
{
    public static string FileFilter =>
        T("Skladby", "Songs") + " (*.mid;*.midi;*.musicxml;*.mxl;*.xml)|*.mid;*.midi;*.musicxml;*.mxl;*.xml|" +
        T("Všechny soubory", "All files") + " (*.*)|*.*";

    public static Score Import(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".mid" or ".midi" => MidiImporter.Import(path),
            ".musicxml" or ".xml" or ".mxl" => MusicXmlImporter.Import(path),
            _ => throw new NotSupportedException(T($"Nepodporovaný formát souboru: {extension}", $"Unsupported file format: {extension}")),
        };
    }
}
