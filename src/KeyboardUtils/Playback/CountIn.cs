using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// A bar of metronome clicks before playing starts. Timed on a background thread with a stopwatch: the UI timer
/// (33 ms) would make the clicks audibly uneven.
/// </summary>
static class CountIn
{
    /// <summary>
    /// Clicks <paramref name="clicks"/> times, <paramref name="intervalSeconds"/> apart (the first one accented), then
    /// waits out the last beat. True when it ran to the end, false when cancelled.
    /// </summary>
    public static Task<bool> Run(int clicks, double intervalSeconds, Action<bool> click, CancellationToken token) => Task.Run(() =>
    {
        var clock = Stopwatch.StartNew();
        for (int i = 0; i <= clicks; i++)
        {
            if (!WaitUntil(clock, i * intervalSeconds, token))
                return false;
            if (i < clicks)
                click(i == 0);
        }
        return true;
    });

    static bool WaitUntil(Stopwatch clock, double seconds, CancellationToken token)
    {
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (token.IsCancellationRequested)
                return false;
            // Sleep while there's time, spin for the last few milliseconds (Sleep is only ~1-15 ms accurate).
            if (seconds - clock.Elapsed.TotalSeconds > 0.004)
                Thread.Sleep(1);
            else
                Thread.SpinWait(100);
        }
        return !token.IsCancellationRequested;
    }
}
