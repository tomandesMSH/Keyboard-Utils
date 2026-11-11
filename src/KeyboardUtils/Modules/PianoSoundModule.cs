using System.IO.Compression;

namespace KeyboardUtils;

/// <summary>
/// The optional piano sound pack: FreePats' "Upright Piano KW" (a Kawai upright, CC0 - public domain), a SoundFont
/// of ~55 MB, downloaded on demand into %APPDATA%\KeyboardUtils\modules\piano-sound - the same idea as
/// <see cref="BasicPitchModule"/>. Chosen as the MIDI output, it replaces Windows' GS Wavetable synth with a real
/// piano sound (<see cref="SoundFontOutputDevice"/>).
/// </summary>
static class PianoSoundModule
{
    const string DownloadUrl = "https://github.com/tomandesMSH/Keyboard-Utils/releases/download/piano-sound-v1/PianoSound-module.zip";

    /// <summary>Shown before downloading.</summary>
    public const int ApproximateDownloadMb = 45;

    /// <summary>The name the pack goes by among the MIDI outputs (saved as <see cref="AppConfig.MidiOutputDevice"/>).</summary>
    public const string DeviceName = "Keyboard Utils: piano (SoundFont)";

    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "modules", "piano-sound");

    public static string SoundFontPath => Path.Combine(Dir, "UprightPianoKW-20220221.sf2");

    public static bool IsInstalled => File.Exists(SoundFontPath);

    /// <summary>Downloads and unpacks the pack. Returns false (never throws) on failure. <paramref name="progress"/> gets 0-1.</summary>
    public static async Task<bool> DownloadAsync(IProgress<double>? progress, CancellationToken cancel = default)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "KeyboardUtils-PianoSound-module.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var target = File.Create(zipPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    if (total > 0)
                        progress?.Report((double)done / total.Value);
                }
            }
            Directory.CreateDirectory(Dir);
            ZipFile.ExtractToDirectory(zipPath, Dir, overwriteFiles: true);
            return IsInstalled;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(zipPath); } catch (IOException) { }
        }
    }

    /// <summary>Deletes the pack (the SoundFont is read into memory when used, so its file is never locked).</summary>
    public static bool Remove()
    {
        try
        {
            if (Directory.Exists(Dir))
                Directory.Delete(Dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
