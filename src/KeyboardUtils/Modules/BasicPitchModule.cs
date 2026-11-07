using System.IO.Compression;
using System.Reflection;

namespace KeyboardUtils;

/// <summary>
/// The optional chord detection add-on: Spotify's Basic Pitch model + ONNX Runtime (~12 MB unpacked), downloaded
/// on demand into %APPDATA%\KeyboardUtils\modules\basic-pitch and loaded from there - the same idea as
/// <see cref="MacroKeyboardModule"/>. Without it, audio-to-notes stays one note at a time.
///
/// Like MacroKeyboardModule, this must never reference BasicPitch types (a method that merely mentions a type from
/// a missing assembly fails as a whole when JIT-compiled); everything that does lives in <see cref="BasicPitchFactory"/>,
/// called only once <see cref="IsInstalled"/> is true.
/// </summary>
static class BasicPitchModule
{
    const string DownloadUrl = "https://github.com/tomandesMSH/Keyboard-Utils/releases/download/basic-pitch-module-v1/BasicPitch-module.zip";

    /// <summary>Shown before downloading; the zip is about half of the unpacked size.</summary>
    public const int ApproximateDownloadMb = 5;

    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "modules", "basic-pitch");

    public static string ModelPath => Path.Combine(Dir, "nmp.onnx");

    static readonly string[] RequiredFiles = ["BasicPitch.dll", "Microsoft.ML.OnnxRuntime.dll", "onnxruntime.dll", "nmp.onnx"];

    public static bool IsInstalled => RequiredFiles.All(f => File.Exists(Path.Combine(Dir, f)));

    /// <summary>Set once the add-on's assemblies are loaded: Windows then keeps its files locked until the app exits.</summary>
    public static bool IsLoaded { get; private set; }

    /// <summary>Call once, early in Main() - before anything might ask for BasicPitch types.</summary>
    public static void RegisterAssemblyResolve() =>
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string? name = new AssemblyName(args.Name).Name;
            if (name is not ("BasicPitch" or "Microsoft.ML.OnnxRuntime"))
                return null;
            string path = Path.Combine(Dir, name + ".dll");
            if (!File.Exists(path))
                return null;
            IsLoaded = true;
            // onnxruntime.dll (native) is found next to its managed wrapper: P/Invoke searches the assembly's folder.
            return Assembly.LoadFrom(path);
        };

    /// <summary>Downloads and unpacks the add-on. Returns false (never throws) on failure. <paramref name="progress"/> gets 0-1.</summary>
    public static async Task<bool> DownloadAsync(IProgress<double>? progress, CancellationToken cancel)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "KeyboardUtils-BasicPitch-module.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
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

    /// <summary>Deletes the add-on. False when it's in use this session (its files are locked until the app restarts).</summary>
    public static bool Remove()
    {
        if (IsLoaded)
            return false;
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
