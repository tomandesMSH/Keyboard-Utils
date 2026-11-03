using System.IO.Compression;
using System.Reflection;

namespace KeyboardUtils;

/// <summary>
/// Downloads and dynamically loads the MacroKeyboard assembly (+ its native interception.dll) on demand,
/// rather than bundling it in every Keyboard Utils build - most people won't use a macro keyboard at all.
///
/// Must never reference MacroKeyboard types - not even indirectly through a type whose property/field type
/// comes from that assembly (confirmed by testing: the .NET JIT resolves an entire method's locals/fields
/// eagerly when that method is first called, regardless of which branch runs, so merely declaring a local
/// variable of a type from a missing assembly crashes the method it's declared in - not just the code path
/// that would use it). Everything that needs to touch MacroKeyboard types for real lives behind
/// <see cref="MacroKeyboardFactory"/>, which is called only after <see cref="IsInstalled"/> is true.
/// </summary>
static class MacroKeyboardModule
{
    const string DownloadUrl = "https://github.com/tomandesMSH/mk-pkg/releases/download/v1.0.2/MacroKeyboard-module.zip";

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardUtils", "modules");

    static readonly string DllPath = Path.Combine(Dir, "MacroKeyboard.dll");

    public static bool IsInstalled => File.Exists(DllPath);

    /// <summary>Call once, as early as possible in Main() - before anything might ask for MacroKeyboard types.</summary>
    public static void RegisterAssemblyResolve() =>
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            new AssemblyName(args.Name).Name == "MacroKeyboard" && File.Exists(DllPath)
                ? Assembly.LoadFrom(DllPath)
                : null;

    /// <summary>Downloads and unpacks the module. Returns false (never throws) on any failure.</summary>
    public static bool Download()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var zipPath = Path.Combine(Dir, "download.zip");
            using (var http = new HttpClient())
                File.WriteAllBytes(zipPath, http.GetByteArrayAsync(DownloadUrl).GetAwaiter().GetResult());
            ZipFile.ExtractToDirectory(zipPath, Dir, overwriteFiles: true);
            File.Delete(zipPath);
            return IsInstalled;
        }
        catch
        {
            return false;
        }
    }
}
