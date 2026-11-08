namespace KeyboardUtils;

/// <summary>A score to download, from a <c>keyboardutils://open?url=...&amp;name=...</c> link (Muzio's "Open in Keyboard Utils").</summary>
sealed record RemoteScoreLink(Uri Download, string FileName);

/// <summary>
/// Opening a score straight from Muzio: the browser hands the app a <c>keyboardutils://</c> link with a short-lived
/// signed download link. Anyone can make such a link, so only what Muzio sends is accepted: an https link to Supabase
/// storage, a MIDI or MusicXML file, at most <see cref="MaxBytes"/>.
/// </summary>
static class RemoteScore
{
    public const string Scheme = "keyboardutils";
    public const long MaxBytes = 5 * 1024 * 1024;
    static readonly string[] Extensions = [".mid", ".midi", ".musicxml", ".mxl", ".xml"];

    /// <summary>Where downloaded scores are kept (they stay openable from "recent songs").</summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardUtils", "Muzio");

    public static bool IsLink(string argument) =>
        argument.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The link's download, or null with <paramref name="error"/> saying why it can't be used.</summary>
    public static RemoteScoreLink? Parse(string argument, out string? error)
    {
        error = T("Odkaz na noty je neplatný.", "The link to the score is invalid.");
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var link)
            || !link.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !link.Host.Equals("open", StringComparison.OrdinalIgnoreCase))
            return null;
        var query = ParseQuery(link.Query);
        if (!query.TryGetValue("url", out var url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var download)
            || download.Scheme != Uri.UriSchemeHttps
            || !download.IsDefaultPort
            || !IsAllowedHost(download.Host)
            || !string.IsNullOrEmpty(download.UserInfo))
        {
            error = T("Noty jde otevřít jen z Muzia.", "Scores can only be opened from Muzio.");
            return null;
        }
        string name = SafeFileName(query.GetValueOrDefault("name") ?? Uri.UnescapeDataString(download.Segments[^1]));
        if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            error = T("Tenhle soubor není MIDI ani MusicXML.", "This file is neither MIDI nor MusicXML.");
            return null;
        }
        error = null;
        return new RemoteScoreLink(download, name);
    }

    /// <summary>Supabase storage (Muzio's attachments live there).</summary>
    public static bool IsAllowedHost(string host) =>
        host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase) && host.Length > ".supabase.co".Length;

    /// <summary>A plain file name: no folders, no characters Windows refuses, not too long.</summary>
    public static string SafeFileName(string name)
    {
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).TrimStart(' ').TrimEnd(' ', '.');
        string extension = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length > 80)
            stem = stem[..80];
        if (stem.Length == 0)
            stem = "noty";
        // Reserved device names (CON, NUL, COM1...) can't be file names.
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(con|prn|aux|nul|com\d|lpt\d)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            stem = "_" + stem;
        return stem + extension;
    }

    static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            string value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }
        return result;
    }

    /// <summary>
    /// Downloads the score into <paramref name="folder"/> (default <see cref="Folder"/>) and returns its path.
    /// Throws <see cref="HttpRequestException"/> (also for a file that's too big) or
    /// <see cref="TaskCanceledException"/> (timeout).
    /// </summary>
    public static async Task<string> DownloadAsync(RemoteScoreLink link, HttpMessageHandler? handler = null, string? folder = null)
    {
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KeyboardUtils");
        using var response = await client.GetAsync(link.Download, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new HttpRequestException(T("Soubor je moc velký.", "The file is too big."));

        // Read it whole into memory (at most MaxBytes), so a broken download never leaves a half-written file.
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new HttpRequestException(T("Soubor je moc velký.", "The file is too big."));
            buffer.Write(chunk, 0, read);
        }

        folder ??= Folder;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, link.FileName);
        await File.WriteAllBytesAsync(path, buffer.ToArray());
        return path;
    }
}
