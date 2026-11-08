using System.Net;
using Microsoft.Win32;

namespace KeyboardUtils.Tests;

public class IntegrationTests
{
    const string Signed = "https://abcdefgh.supabase.co/storage/v1/object/sign/assignment-files/x/y/Pisen.mid?token=eyJ.a.b&download=Pisen.mid";

    static string Link(string url, string? name = null) =>
        "keyboardutils://open?url=" + Uri.EscapeDataString(url) + (name == null ? "" : "&name=" + Uri.EscapeDataString(name));

    [Fact]
    public void Parses_a_link_from_muzio()
    {
        var link = RemoteScore.Parse(Link(Signed, "Píseň o klavíru.mid"), out var error);
        Assert.Null(error);
        Assert.NotNull(link);
        Assert.Equal(Signed, link.Download.AbsoluteUri);
        Assert.Equal("Píseň o klavíru.mid", link.FileName);
    }

    [Fact]
    public void Without_a_name_the_file_is_named_from_the_url()
    {
        var link = RemoteScore.Parse(Link(Signed), out _);
        Assert.Equal("Pisen.mid", link!.FileName);
    }

    [Theory]
    [InlineData("http://abcdefgh.supabase.co/a.mid")]           // not https
    [InlineData("https://evil.example.com/a.mid")]              // not Supabase
    [InlineData("https://supabase.co.evil.com/a.mid")]
    [InlineData("https://abc.supabase.co:8443/a.mid")]          // odd port
    [InlineData("https://user:pw@abc.supabase.co/a.mid")]
    [InlineData("file:///C:/Windows/win.ini")]
    public void Refuses_downloads_from_elsewhere(string url)
    {
        Assert.Null(RemoteScore.Parse(Link(url, "a.mid"), out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("noty.exe")]
    [InlineData("noty.mid.bat")]
    [InlineData("noty")]
    public void Refuses_files_that_are_not_scores(string name)
    {
        Assert.Null(RemoteScore.Parse(Link(Signed, name), out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("keyboardutils://evil?url=x")]
    [InlineData("keyboardutils:open")]
    [InlineData("https://abcdefgh.supabase.co/a.mid")]
    public void Refuses_malformed_links(string argument)
    {
        Assert.Null(RemoteScore.Parse(argument, out _));
    }

    [Theory]
    [InlineData(@"..\..\Windows\evil.mid", "evil.mid")]
    [InlineData("../../x/evil.musicxml", "evil.musicxml")]
    [InlineData("a:b*c?.mid", "a_b_c_.mid")]
    [InlineData("CON.mid", "_CON.mid")]
    [InlineData(".mid", "noty.mid")]
    public void File_names_are_made_safe(string name, string expected)
    {
        Assert.Equal(expected, RemoteScore.SafeFileName(name));
    }

    sealed class FakeHandler(HttpStatusCode status, byte[] body, bool sendLength = true) : HttpMessageHandler
    {
        public Uri? Requested;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requested = request.RequestUri;
            var content = new StreamContent(new MemoryStream(body));
            if (sendLength)
                content.Headers.ContentLength = body.Length;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    static string TempFolder() => Path.Combine(Path.GetTempPath(), "kbu-remote-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Downloads_into_the_folder()
    {
        string folder = TempFolder();
        try
        {
            var handler = new FakeHandler(HttpStatusCode.OK, [1, 2, 3]);
            var link = RemoteScore.Parse(Link(Signed, "a.mid"), out _)!;
            string path = await RemoteScore.DownloadAsync(link, handler, folder);
            Assert.Equal(Path.Combine(folder, "a.mid"), path);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
            Assert.Equal(Signed, handler.Requested!.AbsoluteUri);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // no Content-Length: stopped while reading
    public async Task Refuses_too_big_files(bool sendLength)
    {
        string folder = TempFolder();
        var handler = new FakeHandler(HttpStatusCode.OK, new byte[RemoteScore.MaxBytes + 1], sendLength);
        var link = RemoteScore.Parse(Link(Signed, "a.mid"), out _)!;
        await Assert.ThrowsAsync<HttpRequestException>(() => RemoteScore.DownloadAsync(link, handler, folder));
        Assert.False(File.Exists(Path.Combine(folder, "a.mid")));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task An_expired_link_fails()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, []);
        var link = RemoteScore.Parse(Link(Signed, "a.mid"), out _)!;
        await Assert.ThrowsAsync<HttpRequestException>(() => RemoteScore.DownloadAsync(link, handler, TempFolder()));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\KeyboardUtils.exe\" \"%1\"", @"C:\Apps\KeyboardUtils.exe")]
    [InlineData(@"C:\Apps\KeyboardUtils.exe %1", @"C:\Apps\KeyboardUtils.exe")]
    public void Reads_the_exe_of_a_registered_command(string command, string exe)
    {
        Assert.Equal(exe, ProtocolHandler.ExeOf(command));
    }

    [Fact]
    public void Registers_the_link_type_for_the_user()
    {
        // A classes root of its own, never the real HKCU\Software\Classes.
        string classes = @"Software\KeyboardUtilsTest-" + Guid.NewGuid().ToString("N");
        string key = classes + @"\keyboardutils";
        string exe = Path.Combine(Path.GetTempPath(), "KeyboardUtils.exe");
        try
        {
            ProtocolHandler.Register(exe, isPublished: true, classes);
            using (var root = Registry.CurrentUser.OpenSubKey(key)!)
            {
                Assert.Equal("", root.GetValue("URL Protocol"));
                using var command = root.OpenSubKey(@"shell\open\command")!;
                Assert.Equal($"\"{exe}\" \"%1\"", command.GetValue(null));
            }

            // "Open with" for scores: the app's file type, listed for each extension, the default app left alone.
            using (var progId = Registry.CurrentUser.OpenSubKey($@"{classes}\{ProtocolHandler.ProgId}\shell\open\command")!)
                Assert.Equal($"\"{exe}\" \"%1\"", progId.GetValue(null));
            foreach (string extension in new[] { ".mid", ".midi", ".musicxml", ".mxl" })
            {
                using var ext = Registry.CurrentUser.OpenSubKey($@"{classes}\{extension}")!;
                Assert.Null(ext.GetValue(null));
                using var openWith = ext.OpenSubKey("OpenWithProgids")!;
                Assert.Contains(ProtocolHandler.ProgId, openWith.GetValueNames());
            }
            using (var app = Registry.CurrentUser.OpenSubKey($@"{classes}\Applications\KeyboardUtils.exe")!)
            {
                Assert.Equal("Keyboard Utils", app.GetValue("FriendlyAppName"));
                using var supported = app.OpenSubKey("SupportedTypes")!;
                Assert.Contains(".musicxml", supported.GetValueNames());
            }

            // A development build doesn't take over from a working registration...
            string devExe = @"C:\dev\bin\KeyboardUtils.exe";
            File.WriteAllBytes(exe, []);
            ProtocolHandler.Register(devExe, isPublished: false, classes);
            using (var command = Registry.CurrentUser.OpenSubKey(key + @"\shell\open\command")!)
                Assert.Equal($"\"{exe}\" \"%1\"", command.GetValue(null));
            // ...but replaces one whose exe is gone.
            File.Delete(exe);
            ProtocolHandler.Register(devExe, isPublished: false, classes);
            using (var command = Registry.CurrentUser.OpenSubKey(key + @"\shell\open\command")!)
                Assert.Equal($"\"{devExe}\" \"%1\"", command.GetValue(null));
            using (var progId = Registry.CurrentUser.OpenSubKey($@"{classes}\{ProtocolHandler.ProgId}\shell\open\command")!)
                Assert.Equal($"\"{devExe}\" \"%1\"", progId.GetValue(null));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(classes, throwOnMissingSubKey: false);
            File.Delete(exe);
        }
    }

    [Fact]
    public async Task A_second_start_hands_its_link_to_the_running_app()
    {
        string pipe = "KeyboardUtils-test-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var listening = ProtocolHandler.ListenAsync(arg => received.TrySetResult(arg), cts.Token, pipe);

        string link = Link(Signed, "a.mid");
        Assert.True(await Task.Run(() => ProtocolHandler.SendToRunningInstance(link, pipe)));
        Assert.Equal(link, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        cts.Cancel();
        await listening.WaitAsync(TimeSpan.FromSeconds(5));
        // Nobody listening: the second start says so (and would just quit).
        Assert.False(ProtocolHandler.SendToRunningInstance("x", pipe + "-nobody"));
    }
}
