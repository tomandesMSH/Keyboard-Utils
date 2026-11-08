using System.Net;
using System.Security.Cryptography;

namespace KeyboardUtils.Tests;

public class SelfUpdaterTests
{
    const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    static string Release(string tag = "v1.1.0", string? digest = "sha256:" + Hash,
        string url = "https://github.com/tomandesMSH/Keyboard-Utils/releases/download/v1.1.0/KeyboardUtils.exe") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/tomandesMSH/Keyboard-Utils/releases/tag/{{tag}}",
          "assets": [
            { "name": "basic-pitch.zip", "size": 10, "browser_download_url": "https://github.com/tomandesMSH/Keyboard-Utils/releases/download/{{tag}}/basic-pitch.zip", "digest": "sha256:{{Hash}}" },
            { "name": "KeyboardUtils.exe", "size": 123456, "browser_download_url": "{{url}}", "digest": {{(digest == null ? "null" : $"\"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void Finds_the_new_exe_in_the_release()
    {
        var update = UpdateChecker.ParseRelease(Release(), new Version(1, 0, 2));
        Assert.NotNull(update);
        Assert.Equal(new Version(1, 1, 0), update.Version);
        Assert.Equal("https://github.com/tomandesMSH/Keyboard-Utils/releases/tag/v1.1.0", update.PageUrl);
        Assert.Equal(new ReleaseAsset("https://github.com/tomandesMSH/Keyboard-Utils/releases/download/v1.1.0/KeyboardUtils.exe", 123456, Hash), update.Exe);
    }

    [Fact]
    public void Nothing_when_the_release_is_not_newer()
    {
        Assert.Null(UpdateChecker.ParseRelease(Release("v1.0.2"), new Version(1, 0, 2)));
    }

    [Theory]
    [InlineData(null)]                    // GitHub gave no digest
    [InlineData("md5:abc")]
    [InlineData("sha256:1234")]           // too short
    public void Without_a_usable_digest_the_update_goes_through_the_page(string? digest)
    {
        var update = UpdateChecker.ParseRelease(Release(digest: digest), new Version(1, 0, 2));
        Assert.NotNull(update);
        Assert.Null(update.Exe);
    }

    [Theory]
    [InlineData("https://evil.example.com/KeyboardUtils.exe")]
    [InlineData("https://github.com/someone/else/releases/download/v1.1.0/KeyboardUtils.exe")]
    public void Only_downloads_from_the_projects_releases(string url)
    {
        Assert.Null(UpdateChecker.ParseRelease(Release(url: url), new Version(1, 0, 2))!.Exe);
    }

    sealed class FakeHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kbu-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static ReleaseAsset AssetFor(byte[] body) =>
        new("https://github.com/tomandesMSH/Keyboard-Utils/releases/download/v9/KeyboardUtils.exe", body.Length, Convert.ToHexStringLower(SHA256.HashData(body)));

    [Fact]
    public async Task Downloads_and_checks_the_new_exe()
    {
        string dir = TempDir();
        try
        {
            byte[] body = [.. Enumerable.Range(0, 200_000).Select(i => (byte)i)];
            string target = Path.Combine(dir, "KeyboardUtils.exe.new");
            var reported = new List<double>();
            await SelfUpdater.DownloadAsync(AssetFor(body), target, new SyncProgress(reported.Add), new FakeHandler(body));
            Assert.Equal(body, File.ReadAllBytes(target));
            Assert.Equal(1.0, reported[^1], 6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [Fact]
    public async Task A_tampered_download_is_thrown_away()
    {
        string dir = TempDir();
        try
        {
            byte[] good = [1, 2, 3, 4];
            byte[] bad = [1, 2, 3, 5];
            string target = Path.Combine(dir, "KeyboardUtils.exe.new");
            await Assert.ThrowsAsync<InvalidDataException>(() => SelfUpdater.DownloadAsync(AssetFor(good), target, handler: new FakeHandler(bad)));
            Assert.False(File.Exists(target));
            // Bigger than announced: stopped while downloading.
            await Assert.ThrowsAsync<InvalidDataException>(() => SelfUpdater.DownloadAsync(AssetFor(good), target, handler: new FakeHandler([1, 2, 3, 4, 5])));
            Assert.False(File.Exists(target));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Swaps_the_exe_and_can_roll_back()
    {
        string dir = TempDir();
        try
        {
            string exe = Path.Combine(dir, "KeyboardUtils.exe");
            File.WriteAllText(exe, "old");
            File.WriteAllText(SelfUpdater.NewPath(exe), "new");
            SelfUpdater.Swap(exe, SelfUpdater.NewPath(exe));
            Assert.Equal("new", File.ReadAllText(exe));
            Assert.Equal("old", File.ReadAllText(SelfUpdater.OldPath(exe)));
            Assert.False(File.Exists(SelfUpdater.NewPath(exe)));

            SelfUpdater.RollBack(exe);
            Assert.Equal("old", File.ReadAllText(exe));
            Assert.False(File.Exists(SelfUpdater.OldPath(exe)));

            SelfUpdater.CleanUp(exe);
            Assert.Equal(["KeyboardUtils.exe"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_failed_swap_leaves_the_old_exe_in_place()
    {
        string dir = TempDir();
        try
        {
            string exe = Path.Combine(dir, "KeyboardUtils.exe");
            File.WriteAllText(exe, "old");
            // No .new: the second move fails.
            Assert.ThrowsAny<IOException>(() => SelfUpdater.Swap(exe, SelfUpdater.NewPath(exe)));
            Assert.Equal("old", File.ReadAllText(exe));
            Assert.False(File.Exists(SelfUpdater.OldPath(exe)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Knows_a_start_that_finishes_an_update()
    {
        Assert.False(SelfUpdater.WaitForPreviousInstance([]));
        Assert.False(SelfUpdater.WaitForPreviousInstance(["keyboardutils://open?url=x"]));
        // A process id that is long gone: no waiting.
        Assert.True(SelfUpdater.WaitForPreviousInstance([SelfUpdater.UpdatedFlag, "999999"]));
    }
}
