using System.Diagnostics;
using System.Security.Cryptography;

namespace KeyboardUtils;

/// <summary>
/// One-click update of the single KeyboardUtils.exe:
/// <list type="number">
/// <item>the new exe is downloaded next to it as KeyboardUtils.exe.new and checked against the size and SHA-256
/// GitHub gives for it - anything else is thrown away;</item>
/// <item>the running exe is renamed to .old (Windows allows renaming a running exe, not overwriting it) and .new
/// takes its name; if that fails halfway, the old one is put back;</item>
/// <item>the new exe starts with <c>--updated &lt;pid&gt;</c>, waits for this one to quit and deletes the .old.</item>
/// </list>
/// </summary>
static class SelfUpdater
{
    public const string UpdatedFlag = "--updated";

    /// <summary>The published single KeyboardUtils.exe (not a development build from bin\).</summary>
#pragma warning disable IL3000 // Location is empty in a single file - exactly what this asks.
    public static bool IsSingleFile => string.IsNullOrEmpty(typeof(SelfUpdater).Assembly.Location);
#pragma warning restore IL3000

    /// <summary>The exe to replace, when this is the published single file in a folder the user can write to.</summary>
    public static string? UpdatableExe()
    {
        if (!IsSingleFile || Environment.ProcessPath is not { } exe)
            return null;
        try
        {
            // Program Files without admin rights: no.
            string probe = exe + ".write-test";
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return exe;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> to <paramref name="target"/> and checks it. Throws
    /// <see cref="InvalidDataException"/> when it isn't the file GitHub described (and deletes it).
    /// </summary>
    public static async Task DownloadAsync(ReleaseAsset asset, string target, IProgress<double>? progress = null,
        HttpMessageHandler? handler = null, CancellationToken token = default)
    {
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KeyboardUtils");
        try
        {
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > asset.Size)
                        throw new InvalidDataException("The download is bigger than the release says.");
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report((double)total / asset.Size);
                }
            }
            await VerifyAsync(target, asset, token);
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Throws <see cref="InvalidDataException"/> unless <paramref name="path"/> has the asset's size and SHA-256.</summary>
    public static async Task VerifyAsync(string path, ReleaseAsset asset, CancellationToken token = default)
    {
        var info = new FileInfo(path);
        if (info.Length != asset.Size)
            throw new InvalidDataException($"Size {info.Length}, expected {asset.Size}.");
        await using var stream = File.OpenRead(path);
        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 doesn't match.");
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in place of <paramref name="exe"/>, keeping the old one as
    /// <see cref="OldPath"/>. All or nothing: on failure the old exe is back where it was.
    /// </summary>
    public static void Swap(string exe, string newExe)
    {
        string old = OldPath(exe);
        TryDelete(old);
        File.Move(exe, old);
        try
        {
            File.Move(newExe, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    /// <summary>Undoes <see cref="Swap"/>: the old exe back, the new one aside as .new (deleted on the next start).</summary>
    public static void RollBack(string exe)
    {
        string old = OldPath(exe);
        if (!File.Exists(old))
            return;
        File.Move(exe, NewPath(exe), overwrite: true);
        File.Move(old, exe);
    }

    public static string OldPath(string exe) => exe + ".old";
    public static string NewPath(string exe) => exe + ".new";

    /// <summary>Starts the new exe, telling it to wait for this process.</summary>
    public static void StartNew(string exe) =>
        Process.Start(new ProcessStartInfo(exe) { ArgumentList = { UpdatedFlag, Environment.ProcessId.ToString() }, UseShellExecute = false });

    /// <summary>
    /// In the new exe, before anything else: waits for the old process (<c>--updated &lt;pid&gt;</c>) to quit. True
    /// when this start is the end of an update.
    /// </summary>
    public static bool WaitForPreviousInstance(string[] args)
    {
        int index = Array.IndexOf(args, UpdatedFlag);
        if (index < 0)
            return false;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out int pid))
        {
            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(30_000);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
        return true;
    }

    /// <summary>Removes what an update left behind (the old exe, a broken download). Retries briefly: the old exe may still be closing.</summary>
    public static void CleanUp(string exe)
    {
        foreach (string leftover in new[] { OldPath(exe), NewPath(exe) })
            for (int attempt = 0; attempt < 10 && File.Exists(leftover); attempt++)
            {
                if (TryDelete(leftover))
                    break;
                Thread.Sleep(300);
            }
    }

    static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
