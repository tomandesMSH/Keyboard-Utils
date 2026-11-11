using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace KeyboardUtils;

/// <summary>
/// A newer release on GitHub: its version, the page to download it from and - when the release has it - the new
/// KeyboardUtils.exe for <see cref="SelfUpdater"/>.
/// </summary>
sealed record AvailableUpdate(Version Version, string PageUrl, ReleaseAsset? Exe = null);

/// <summary>A release's file: where to get it, its size and SHA-256 (hex) as GitHub reports them.</summary>
sealed record ReleaseAsset(string Url, long Size, string Sha256);

/// <summary>
/// Asks GitHub for the latest release of the app. Sends nothing about the user - only a User-Agent naming the app
/// and its version, which GitHub's API requires.
/// </summary>
static class UpdateChecker
{
    const string LatestReleaseUrl = "https://api.github.com/repos/tomandesMSH/Keyboard-Utils/releases/latest";
    const string DownloadPrefix = "https://github.com/tomandesMSH/Keyboard-Utils/releases/download/";
    const string ExeName = "KeyboardUtils.exe";
    public const string ReleasesPage = "https://github.com/tomandesMSH/Keyboard-Utils/releases/latest";

    /// <summary>This build's version (from the project's &lt;Version&gt;).</summary>
    public static Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    /// <summary>
    /// The release <paramref name="tag"/> ("v0.4.1") as a version, if it's a newer one than <paramref name="current"/>.
    /// Tags that aren't a plain version (the add-on's "basic-pitch-module-v1") are never newer.
    /// </summary>
    public static Version? NewerVersion(string? tag, Version current)
    {
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version))
            return null;
        return Normalize(version) > Normalize(current) ? version : null;
    }

    /// <summary>0.4.1 and 0.4.1.0 are the same version (missing parts count as 0).</summary>
    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>
    /// Whether GitHub answered (not offline, not rate-limited...) and, if so, the newer release - null when this
    /// is the latest.
    /// </summary>
    public static async Task<(bool Reached, AvailableUpdate? Update)> CheckAsync(Version current, CancellationToken token = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KeyboardUtils", CurrentVersion.ToString(3)));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync(LatestReleaseUrl, token);
            if (!response.IsSuccessStatusCode)
                return (false, null);
            return (true, ParseRelease(await response.Content.ReadAsStringAsync(token), current));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (false, null);
        }
    }

    /// <summary>The page of release <paramref name="version"/> (the project's own, always).</summary>
    public static string ReleasePage(Version version) =>
        $"https://github.com/tomandesMSH/Keyboard-Utils/releases/tag/v{version.ToString(3)}";

    /// <summary>The notes (Markdown) of release <paramref name="version"/>; null when GitHub can't be reached or there are none.</summary>
    public static async Task<string?> ReleaseNotesAsync(Version version, CancellationToken token = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KeyboardUtils", CurrentVersion.ToString(3)));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync(
                $"https://api.github.com/repos/tomandesMSH/Keyboard-Utils/releases/tags/v{version.ToString(3)}", token);
            if (!response.IsSuccessStatusCode)
                return null;
            return NotesOf(await response.Content.ReadAsStringAsync(token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A release's notes from GitHub's JSON: its <c>body</c>, null when empty.</summary>
    public static string? NotesOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            && body.GetString() is { Length: > 0 } text ? text : null;
    }

    /// <summary>The release GitHub describes in <paramref name="json"/>, if it's newer than <paramref name="current"/>.</summary>
    public static AvailableUpdate? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (NewerVersion(tag, current) is not { } newer)
            return null;
        // The page is opened in the browser: only ever the project's own releases.
        string page = root.TryGetProperty("html_url", out var u) && u.GetString() is { } url
            && url.StartsWith("https://github.com/tomandesMSH/Keyboard-Utils/releases/", StringComparison.Ordinal) ? url : ReleasesPage;
        return new AvailableUpdate(newer, page, FindExe(root));
    }

    /// <summary>
    /// The release's KeyboardUtils.exe - only from the project's own release downloads, and only with a SHA-256
    /// digest to check it against (without one, the update goes the old way, through the release page).
    /// </summary>
    static ReleaseAsset? FindExe(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.GetString() != ExeName)
                continue;
            string? url = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            long size = asset.TryGetProperty("size", out var z) && z.TryGetInt64(out long n) ? n : 0;
            string? digest = asset.TryGetProperty("digest", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
            if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !url.EndsWith("/" + ExeName, StringComparison.Ordinal)
                || size <= 0 || digest == null || !digest.StartsWith("sha256:", StringComparison.Ordinal))
                return null;
            string hex = digest["sha256:".Length..].ToLowerInvariant();
            if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
                return null;
            return new ReleaseAsset(url, size, hex);
        }
        return null;
    }
}
