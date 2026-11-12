using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardUtils;

/// <summary>A pack the catalog offers: where to get it, how big it is, its checksum.</summary>
sealed record PackEntry(string Id, int Version, string Url, long Bytes, string Sha256, string Credits);

/// <summary>An installed pack's pack.json: its sound and which presets play what.</summary>
sealed record PackManifest(string Id, int Version, string Sound, Dictionary<string, int> Presets, string Credits, string License)
{
    public int MainPreset => Presets.GetValueOrDefault("main");

    /// <summary>The plucked-string sound of a bowed instrument; null when the pack has none.</summary>
    public int? PizzicatoPreset => Presets.TryGetValue("pizzicato", out int p) ? p : null;
}

/// <summary>
/// Instruments other than the piano come as downloadable packs (tomandesMSH/KUtils-Addons on GitHub): the
/// instrument's real sound as a SoundFont, CC0-licensed (FreePats, VSCO 2 CE). Installed into
/// %APPDATA%\KeyboardUtils\instruments\&lt;id&gt;. The catalog says what exists; a download is checked against its
/// size and SHA-256 before it's unpacked.
/// </summary>
static class InstrumentPacks
{
    public const string CatalogUrl = "https://github.com/tomandesMSH/KUtils-Addons/releases/latest/download/catalog.json";

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "instruments");

    /// <summary>For tests: where downloads come from instead of the internet.</summary>
    public static HttpMessageHandler? Handler { get; set; }

    static HttpClient Client(HttpMessageHandler? handler) =>
        (handler ?? Handler) is { } h ? new HttpClient(h, disposeHandler: false) : new HttpClient();

    static string CachedCatalog => Path.Combine(Root, "catalog.json");

    /// <summary>The piano is built in; anything else needs its pack.</summary>
    public static bool IsInstalled(Instrument instrument) => instrument.IsPiano || Manifest(instrument.Id) != null;

    public static IEnumerable<Instrument> Installed => Instrument.All.Where(IsInstalled);

    public static string SoundPath(string id) => Path.Combine(Root, id, Manifest(id)?.Sound ?? "sound.sf2");

    public static PackManifest? Manifest(string id)
    {
        string path = Path.Combine(Root, id, "pack.json");
        try
        {
            if (!File.Exists(path))
                return null;
            var manifest = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(path), Json);
            return manifest != null && manifest.Id == id && File.Exists(Path.Combine(Root, id, manifest.Sound)) ? manifest : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The catalog from the internet (kept for when it's offline); the last one seen if it can't be reached.</summary>
    public static async Task<List<PackEntry>> CatalogAsync(HttpMessageHandler? handler = null, string? url = null)
    {
        try
        {
            using var http = Client(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            string text = await http.GetStringAsync(url ?? CatalogUrl);
            var list = Parse(text);
            Directory.CreateDirectory(Root);
            await File.WriteAllTextAsync(CachedCatalog, text);
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            return CachedCatalogEntries();
        }
    }

    /// <summary>What the catalog said last time (sizes for the "+" menu without waiting), or nothing.</summary>
    public static List<PackEntry> CachedCatalogEntries()
    {
        try
        {
            return File.Exists(CachedCatalog) ? Parse(File.ReadAllText(CachedCatalog)) : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    sealed record CatalogFile(int Version, List<PackEntry> Packs);

    static List<PackEntry> Parse(string text) => JsonSerializer.Deserialize<CatalogFile>(text, Json)?.Packs ?? [];

    /// <summary>
    /// Downloads, checks and installs a pack; null when it worked, otherwise why not (for the user). A failed
    /// download leaves any installed version as it was.
    /// </summary>
    public static async Task<string?> InstallAsync(PackEntry entry, IProgress<double>? progress = null, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        // Unpacked beside its final place, so the last step is a move on the same drive.
        string temp = Path.Combine(Root, $"{entry.Id}.new-{Guid.NewGuid():N}");
        string zip = temp + ".zip";
        try
        {
            Directory.CreateDirectory(Root);
            using (var http = Client(handler))
            {
                http.Timeout = TimeSpan.FromMinutes(10);
                using var response = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var target = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    if (done > entry.Bytes + 1024)
                        return T("Stažený soubor je větší, než má být.", "The download is larger than it should be.");
                    if (entry.Bytes > 0)
                        progress?.Report((double)done / entry.Bytes);
                }
            }
            if (new FileInfo(zip).Length != entry.Bytes)
                return T("Stažení se nedokončilo - zkus to znovu.", "The download didn't finish - try again.");
            string hash;
            await using (var file = File.OpenRead(zip))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return T("Stažený balíček je poškozený (nesedí kontrolní součet).", "The downloaded pack is damaged (checksum mismatch).");

            ZipFile.ExtractToDirectory(zip, temp);
            var manifest = JsonSerializer.Deserialize<PackManifest>(await File.ReadAllTextAsync(Path.Combine(temp, "pack.json"), cancel), Json);
            if (manifest == null || manifest.Id != entry.Id || !File.Exists(Path.Combine(temp, manifest.Sound)))
                return T("Balíček nemá správný obsah.", "The pack doesn't have the right contents.");

            // Swap it in: the old version out of the way first, back if the move fails.
            string dir = Path.Combine(Root, entry.Id);
            Directory.CreateDirectory(Root);
            string old = dir + ".old";
            if (Directory.Exists(old))
                Directory.Delete(old, recursive: true);
            if (Directory.Exists(dir))
                Directory.Move(dir, old);
            try
            {
                Directory.Move(temp, dir);
            }
            catch (IOException)
            {
                if (Directory.Exists(old))
                    Directory.Move(old, dir);
                throw;
            }
            if (Directory.Exists(old))
                TryDelete(old);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return T("Stažení se nepovedlo - zkontroluj připojení k internetu.", "Downloading failed - check your internet connection.") + $" ({ex.Message})";
        }
        finally
        {
            TryDeleteFile(zip);
            if (Directory.Exists(temp))
                TryDelete(temp);
        }
    }

    /// <summary>Deletes an installed pack; false when it's in use (its sound is playing - try again after switching).</summary>
    public static bool Remove(string id)
    {
        string dir = Path.Combine(Root, id);
        if (!Directory.Exists(dir))
            return true;
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void TryDeleteFile(string file)
    {
        try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>"5,7 MB" for the menus.</summary>
    public static string SizeText(long bytes) => $"{bytes / 1048576.0:0.0} MB";
}
