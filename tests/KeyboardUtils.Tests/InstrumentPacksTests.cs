using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace KeyboardUtils.Tests;

/// <summary>Instrument packs: the catalog, a download checked and unpacked, removal. No network - a fake handler serves the files.</summary>
[Collection(nameof(InstrumentPacksTests))] // InstrumentPacks.Root is shared
public sealed class InstrumentPacksTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "kbu-packs-test-" + Guid.NewGuid().ToString("N"));
    readonly string previousRoot = InstrumentPacks.Root;

    public InstrumentPacksTests() => InstrumentPacks.Root = root;

    public void Dispose()
    {
        InstrumentPacks.Root = previousRoot;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    /// <summary>Serves fixed bytes per URL; anything else is a 404.</summary>
    sealed class FakeHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    static byte[] PackZip(string id, bool withSound = true, string? manifestId = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
            Add("pack.json", $$"""{"id":"{{manifestId ?? id}}","version":1,"sound":"sound.sf2","presets":{"main":0,"pizzicato":1},"credits":"Test","license":"CC0"}""");
            if (withSound)
                Add("sound.sf2", "RIFF fake");
        }
        return memory.ToArray();
    }

    static PackEntry Entry(string id, byte[] zip) =>
        new(id, 1, $"https://example.test/{id}.zip", zip.Length, Convert.ToHexStringLower(SHA256.HashData(zip)), "Test");

    [Fact]
    public async Task Install_UnpacksAndReadsTheManifest()
    {
        var violin = Instrument.ById("violin");
        Assert.False(InstrumentPacks.IsInstalled(violin));
        var zip = PackZip("violin");
        var entry = Entry("violin", zip);
        string? error = await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip }));
        Assert.Null(error);
        Assert.True(InstrumentPacks.IsInstalled(violin));
        var manifest = InstrumentPacks.Manifest("violin")!;
        Assert.Equal(0, manifest.MainPreset);
        Assert.Equal(1, manifest.PizzicatoPreset);
        Assert.True(File.Exists(InstrumentPacks.SoundPath("violin")));
        // Nothing left over beside it.
        Assert.Equal(["violin"], Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Install_RejectsAChecksumMismatch()
    {
        var zip = PackZip("guitar");
        var entry = Entry("guitar", zip) with { Sha256 = new string('0', 64) };
        string? error = await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip }));
        Assert.NotNull(error);
        Assert.False(InstrumentPacks.IsInstalled(Instrument.ById("guitar")));
    }

    [Fact]
    public async Task Install_RejectsAPackForAnotherInstrument()
    {
        var zip = PackZip("guitar", manifestId: "cello");
        var entry = Entry("guitar", zip);
        Assert.NotNull(await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip })));
        Assert.False(InstrumentPacks.IsInstalled(Instrument.ById("guitar")));
        Assert.Null(InstrumentPacks.Manifest("cello"));
    }

    [Fact]
    public async Task Install_FailedDownloadKeepsTheInstalledVersion()
    {
        var zip = PackZip("flute");
        var entry = Entry("flute", zip);
        Assert.Null(await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip })));
        // A second try that 404s.
        Assert.NotNull(await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler([])));
        Assert.True(InstrumentPacks.IsInstalled(Instrument.ById("flute")));
    }

    [Fact]
    public async Task Install_MissingSoundIsNotInstalled()
    {
        var zip = PackZip("trumpet", withSound: false);
        var entry = Entry("trumpet", zip);
        Assert.NotNull(await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip })));
        Assert.False(InstrumentPacks.IsInstalled(Instrument.ById("trumpet")));
    }

    [Fact]
    public async Task Catalog_ParsesAndIsKeptForOffline()
    {
        const string url = "https://example.test/catalog.json";
        string json = """
            {"version":1,"packs":[
              {"id":"guitar","version":1,"url":"https://x/guitar.zip","bytes":14000000,"sha256":"ab","credits":"FreePats (CC0)"},
              {"id":"violin","version":1,"url":"https://x/violin.zip","bytes":6000000,"sha256":"cd","credits":"VSCO 2 CE (CC0)"}]}
            """;
        var list = await InstrumentPacks.CatalogAsync(new FakeHandler(new() { [url] = Encoding.UTF8.GetBytes(json) }), url);
        Assert.Equal(["guitar", "violin"], list.Select(e => e.Id));
        Assert.Equal(14000000, list[0].Bytes);
        // Offline: the last catalog seen.
        var offline = await InstrumentPacks.CatalogAsync(new FakeHandler([]), url);
        Assert.Equal(2, offline.Count);
        Assert.Equal(2, InstrumentPacks.CachedCatalogEntries().Count);
    }

    [Fact]
    public async Task Remove_DeletesThePack_PianoAlwaysInstalled()
    {
        var zip = PackZip("cello");
        var entry = Entry("cello", zip);
        Assert.Null(await InstrumentPacks.InstallAsync(entry, handler: new FakeHandler(new() { [entry.Url] = zip })));
        Assert.True(InstrumentPacks.Remove("cello"));
        Assert.False(InstrumentPacks.IsInstalled(Instrument.ById("cello")));
        Assert.True(InstrumentPacks.IsInstalled(Instrument.Piano));
    }
}
