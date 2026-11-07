using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace KeyboardUtils;

/// <summary>One piece found on Mutopia: what it is, under which license, and where its MIDI is.</summary>
sealed record FreeScoreResult(int Id, string Title, string Composer, string Instrument, string License, string MidiUrl)
{
    /// <summary>Several movements/parts: the MIDI comes as a zip of .mid files.</summary>
    public bool IsZip => MidiUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public string PageUrl => $"https://www.mutopiaproject.org/cgibin/piece-info.cgi?id={Id}";
}

/// <summary>
/// Searches the Mutopia Project (https://www.mutopiaproject.org) - ~2000 pieces of public domain / Creative Commons
/// sheet music with MIDI files. Mutopia has no API, so this reads its search results page: each result is a
/// "result-table" whose cells come in a fixed order (title, composer, ..., "for Organ", ..., license, info link).
/// Nothing is contacted until the user searches.
/// </summary>
static partial class MutopiaSearch
{
    const string SearchUrl = "https://www.mutopiaproject.org/cgibin/make-table.cgi";

    /// <summary>Mutopia's page size.</summary>
    public const int PageSize = 10;

    static readonly HttpClient Http = CreateClient();

    static readonly string DownloadDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "downloads", "mutopia");

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KeyboardUtils/0.1 (+https://github.com/tomandesMSH/Keyboard-Utils)");
        return http;
    }

    /// <summary>Throws HttpRequestException / TaskCanceledException on network problems.</summary>
    public static async Task<(List<FreeScoreResult> Results, bool HasMore)> SearchAsync(string query, int startAt, CancellationToken cancel)
    {
        string url = $"{SearchUrl}?searchingfor={Uri.EscapeDataString(query)}&startat={startAt}";
        string html = await Http.GetStringAsync(url, cancel);
        return Parse(html);
    }

    /// <summary>Parses a search results page. Public for testing against saved pages.</summary>
    public static (List<FreeScoreResult> Results, bool HasMore) Parse(string html)
    {
        var results = new List<FreeScoreResult>();
        // Each result is its own table; splitting on its class gives one chunk per piece (the first is the page head).
        foreach (string block in html.Split("result-table").Skip(1))
        {
            var cells = Cell().Matches(block).Select(m => Text(m.Groups[1].Value)).ToList();
            var midi = MidiLink().Match(block);
            var id = PieceId().Match(block);
            if (cells.Count < 10 || !midi.Success || !id.Success)
                continue; // a piece without MIDI is no use here
            results.Add(new FreeScoreResult(
                Id: int.Parse(id.Groups[1].Value),
                Title: cells[0],
                Composer: cells[1].StartsWith("by ", StringComparison.OrdinalIgnoreCase) ? cells[1][3..] : cells[1],
                Instrument: StripFor(cells[4]),
                License: cells[9],
                MidiUrl: WebUtility.HtmlDecode(midi.Groups[1].Value)));
        }
        return (results, html.Contains("startat=", StringComparison.Ordinal) && NextLink().IsMatch(html));
    }

    /// <summary>
    /// Downloads the piece's MIDI (unpacking a zip of movements) and returns the .mid files in order.
    /// Cached: a piece downloaded before isn't fetched again.
    /// </summary>
    public static async Task<List<string>> DownloadAsync(FreeScoreResult piece, CancellationToken cancel)
    {
        string dir = Path.Combine(DownloadDir, piece.Id.ToString());
        if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.mid").Length == 0)
        {
            Directory.CreateDirectory(dir);
            byte[] data = await Http.GetByteArrayAsync(piece.MidiUrl, cancel);
            if (piece.IsZip)
            {
                using var zip = new ZipArchive(new MemoryStream(data));
                foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)))
                    entry.ExtractToFile(Path.Combine(dir, entry.Name), overwrite: true);
            }
            else
                await File.WriteAllBytesAsync(Path.Combine(dir, Path.GetFileName(new Uri(piece.MidiUrl).LocalPath)), data, cancel);
        }
        // Movement files are numbered (…-1.mid, …-2.mid, …-10.mid): sort naturally so 10 comes after 9.
        return Directory.GetFiles(dir, "*.mid").OrderBy(f => NaturalKey(Path.GetFileNameWithoutExtension(f))).ToList();
    }

    static string NaturalKey(string name) => Digits().Replace(name, m => m.Value.PadLeft(8, '0'));

    static string Text(string cellHtml)
    {
        string text = WebUtility.HtmlDecode(Tag().Replace(cellHtml, "")).Trim();
        return text is "n/a" ? "" : Whitespace().Replace(text, " ");
    }

    static string StripFor(string instrument) =>
        instrument.StartsWith("for ", StringComparison.OrdinalIgnoreCase) ? instrument[4..] : instrument;

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"href=""([^""]+(?:\.mid|-mids\.zip))""", RegexOptions.IgnoreCase)]
    private static partial Regex MidiLink();

    [GeneratedRegex(@"piece-info\.cgi\?id=(\d+)")]
    private static partial Regex PieceId();

    [GeneratedRegex(@">\s*Next\s+\d+\s*<", RegexOptions.IgnoreCase)]
    private static partial Regex NextLink();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
