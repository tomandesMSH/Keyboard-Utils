using System.IO.Pipes;
using Microsoft.Win32;

namespace KeyboardUtils;

/// <summary>
/// Ties the app into Windows, per user in HKCU\Software\Classes (no admin rights needed):
/// <list type="bullet">
/// <item><c>keyboardutils://</c> links (Muzio's "Open in Keyboard Utils") start it;</item>
/// <item>MIDI and MusicXML files list it under "Open with" - without taking over the default app, which stays the
/// user's choice.</item>
/// </list>
/// When the app already runs, the new process hands the link or file over a named pipe and quits, so there is only
/// ever one window.
/// </summary>
static class ProtocolHandler
{
    const string ClassesRoot = @"Software\Classes";
    const string PipeName = "KeyboardUtils-open";
    public const string ProgId = "KeyboardUtils.Score";
    public static readonly string[] FileExtensions = [".mid", ".midi", ".musicxml", ".mxl"];

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// Points the link type and "Open with" at <paramref name="exePath"/>. The published single file always does it
    /// (it may have been moved); a development build only when nothing working is registered, so it never takes over
    /// from the real app.
    /// </summary>
    /// <param name="classesRoot">HKCU\Software\Classes; the tests use a key of their own.</param>
    public static void Register(string exePath, bool isPublished, string classesRoot = ClassesRoot)
    {
        try
        {
            string command = $"\"{exePath}\" \"%1\"";
            string linkKey = $@"{classesRoot}\{RemoteScore.Scheme}";
            string fileKey = $@"{classesRoot}\{ProgId}";
            string? linkCommand = CommandOf(linkKey), fileCommand = CommandOf(fileKey);
            if (linkCommand == command && fileCommand == command)
                return;
            if (!isPublished && linkCommand != null && File.Exists(ExeOf(linkCommand)))
                return;

            string icon = $"\"{exePath}\",0";
            using (var key = Registry.CurrentUser.CreateSubKey(linkKey))
            {
                key.SetValue(null, "URL:Keyboard Utils");
                key.SetValue("URL Protocol", "");
                SetCommand(key, icon, command);
            }

            // "Open with": a file type of the app's own, offered for each score extension.
            using (var key = Registry.CurrentUser.CreateSubKey(fileKey))
            {
                key.SetValue(null, T("Noty (Keyboard Utils)", "Score (Keyboard Utils)"));
                SetCommand(key, icon, command);
            }
            using (var app = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\Applications\{Path.GetFileName(exePath)}"))
            {
                app.SetValue("FriendlyAppName", "Keyboard Utils");
                SetCommand(app, icon, command);
                using var supported = app.CreateSubKey("SupportedTypes");
                foreach (string extension in FileExtensions)
                    supported.SetValue(extension, "");
            }
            foreach (string extension in FileExtensions)
            {
                using var openWith = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{extension}\OpenWithProgids");
                openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }
            if (classesRoot == ClassesRoot)
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Links from Muzio and "Open with" just won't offer the app; everything else works.
        }
    }

    static string? CommandOf(string key)
    {
        using var command = Registry.CurrentUser.OpenSubKey(key + @"\shell\open\command");
        return command?.GetValue(null) as string;
    }

    static void SetCommand(RegistryKey key, string icon, string command)
    {
        using (var defaultIcon = key.CreateSubKey("DefaultIcon"))
            defaultIcon.SetValue(null, icon);
        using var open = key.CreateSubKey(@"shell\open\command");
        open.SetValue(null, command);
    }

    /// <summary>The exe in a registered command line (<c>"C:\...\KeyboardUtils.exe" "%1"</c>).</summary>
    public static string ExeOf(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    /// <summary>
    /// Hands <paramref name="argument"/> (a link, a file, or "" to just bring the window up) to the running app.
    /// False when it doesn't answer.
    /// </summary>
    public static bool SendToRunningInstance(string argument, string pipeName = PipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(argument.ReplaceLineEndings(" "));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Listens for <see cref="SendToRunningInstance"/> until <paramref name="token"/> is cancelled, calling
    /// <paramref name="received"/> (on a background thread) with each argument.
    /// </summary>
    public static async Task ListenAsync(Action<string> received, CancellationToken token, string pipeName = PipeName)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe);
                // One short line; a client that never sends is dropped.
                var line = await reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
                if (line != null && line.Length <= 8192)
                    received(line);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // A broken connection or a second listener: try again shortly.
                try { await Task.Delay(500, token); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
