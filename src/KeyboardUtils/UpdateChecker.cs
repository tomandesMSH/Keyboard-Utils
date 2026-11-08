using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace KeyboardUtils;

/// <summary>A newer release on GitHub: its version and the page to download it from.</summary>
sealed record AvailableUpdate(Version Version, string PageUrl);

/// <summary>
/// Asks GitHub for the latest release of the app. Sends nothing about the user - only a User-Agent naming the app
/// and its version, which GitHub's API requires.
/// </summary>
static class UpdateChecker
{
    const string LatestReleaseUrl = "https://api.github.com/repos/tomandesMSH/Keyboard-Utils/releases/latest";
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
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            // The page is opened in the browser: only ever the project's own releases.
            string page = root.TryGetProperty("html_url", out var u) && u.GetString() is { } url
                && url.StartsWith("https://github.com/tomandesMSH/Keyboard-Utils/releases/", StringComparison.Ordinal) ? url : ReleasesPage;
            return (true, NewerVersion(tag, current) is { } newer ? new AvailableUpdate(newer, page) : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (false, null);
        }
    }
}
