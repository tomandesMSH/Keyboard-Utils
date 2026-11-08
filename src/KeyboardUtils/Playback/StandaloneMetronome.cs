using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// The metronome on its own, with no song: clicks at a set tempo and time signature until stopped, the first click
/// of each bar accented. Timed on a background thread with a stopwatch like <see cref="CountIn"/>. Tempo and time
/// signature can change while it runs; the next click follows the new ones.
/// </summary>
sealed class StandaloneMetronome
{
    readonly Action<bool> click;
    CancellationTokenSource? running;
    volatile int bpm = 100;
    volatile int clicksPerBar = 4;
    volatile int clicksPerBeat = 1;
    string meter = "4/4";

    /// <summary>The time signatures offered.</summary>
    public static readonly string[] Meters = ["2/4", "3/4", "4/4", "5/4", "6/8"];

    /// <summary>
    /// Clicks in a bar of <paramref name="meter"/>, and clicks per beat the BPM counts. x/4: a click per quarter,
    /// BPM in quarters. 6/8: a click per eighth, BPM in dotted quarters (how 6/8 is counted), so 3 clicks per beat.
    /// Anything else is 4/4.
    /// </summary>
    public static (int ClicksPerBar, int ClicksPerBeat) Pattern(string meter) => meter switch
    {
        "2/4" => (2, 1),
        "3/4" => (3, 1),
        "5/4" => (5, 1),
        "6/8" => (6, 3),
        _ => (4, 1),
    };

    /// <param name="click">Plays one click (true = accented); called on the background thread.</param>
    public StandaloneMetronome(Action<bool> click) => this.click = click;

    public bool IsRunning => running != null;

    public int Bpm
    {
        get => bpm;
        set => bpm = Math.Clamp(value, MinBpm, MaxBpm);
    }

    /// <summary>One of <see cref="Meters"/> (anything else counts as 4/4).</summary>
    public string Meter
    {
        get => meter;
        set
        {
            meter = Meters.Contains(value) ? value : "4/4";
            var (perBar, perBeat) = Pattern(meter);
            clicksPerBeat = perBeat;
            clicksPerBar = perBar;
        }
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
            for (long tick = 0; !token.IsCancellationRequested; tick++)
            {
                if (!WaitUntil(clock, next, token))
                    return;
                click(tick % clicksPerBar == 0);
                next += 60.0 / bpm / clicksPerBeat;
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
