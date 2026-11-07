using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// The metronome on its own, with no song: clicks at a set tempo until stopped, the first beat of each bar
/// accented. Timed on a background thread with a stopwatch like <see cref="CountIn"/>. The tempo can change while
/// it runs; the next beat comes at the new one.
/// </summary>
sealed class StandaloneMetronome
{
    readonly Action<bool> click;
    CancellationTokenSource? running;
    volatile int bpm = 100;
    volatile int beatsPerBar = 4;

    /// <param name="click">Plays one click (true = accented); called on the background thread.</param>
    public StandaloneMetronome(Action<bool> click) => this.click = click;

    public bool IsRunning => running != null;

    public int Bpm
    {
        get => bpm;
        set => bpm = Math.Clamp(value, MinBpm, MaxBpm);
    }

    public int BeatsPerBar
    {
        get => beatsPerBar;
        set => beatsPerBar = Math.Max(1, value);
    }

    public const int MinBpm = 20, MaxBpm = 300;

    public void Start()
    {
        if (running != null)
            return;
        var cts = running = new CancellationTokenSource();
        var token = cts.Token;
        var thread = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            double next = 0;
            for (long beat = 0; !token.IsCancellationRequested; beat++)
            {
                if (!WaitUntil(clock, next, token))
                    return;
                click(beat % beatsPerBar == 0);
                next += 60.0 / bpm;
                // Fell far behind (the PC stalled): carry on from now rather than rattling off the missed beats.
                if (clock.Elapsed.TotalSeconds > next + 0.25)
                    next = clock.Elapsed.TotalSeconds;
            }
        })
        { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Metronome" };
        thread.Start();
    }

    public void Stop()
    {
        running?.Cancel();
        running = null;
    }

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
