namespace KeyboardUtils;

/// <summary>One song in the report: how much and how well it was practised in the period.</summary>
sealed record ReportSong(
    string Title,
    int Attempts,
    double Minutes,
    double BestPercent,
    double LastPercent,
    int SlowestSpeed,
    int FastestSpeed,
    IReadOnlyList<HardSpot> HardSpots,
    DateTime LastPractised);

/// <summary>
/// The teacher's report: learning mode over a period, from the practice history - the minutes, the days, the goal,
/// and each song practised (best and last result, speed, hard spots). Only learning mode leaves entries, so free
/// playing isn't in it.
/// </summary>
sealed record PracticeReport(
    DateOnly From,
    DateOnly To,
    double Minutes,
    int Attempts,
    int DaysPractised,
    int GoalMinutes,
    int GoalDaysMet,
    int Streak,
    IReadOnlyList<(DateOnly Day, double Minutes)> PerDay,
    IReadOnlyList<ReportSong> Songs)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public static PracticeReport Build(PracticeHistory history, DateOnly from, DateOnly to, int goalMinutes, DateOnly today)
    {
        if (to < from)
            (from, to) = (to, from);
        bool InPeriod(PracticeEntry e) => DateOnly.FromDateTime(e.When) is var day && day >= from && day <= to;

        var all = history.AllEntries().ToList();
        var inPeriod = all.Where(InPeriod).ToList();
        int days = to.DayNumber - from.DayNumber + 1;
        var perDay = PracticeGoal.LastDays(inPeriod, to, days);

        var songs = history.Songs
            .Select(song => (song.Key, Entries: song.Value.Where(InPeriod).ToList(), All: song.Value))
            .Where(s => s.Entries.Count > 0)
            .Select(s => new ReportSong(
                TitleOf(s.Key),
                s.Entries.Count,
                s.Entries.Sum(e => e.Seconds) / 60,
                s.Entries.Max(e => e.Percent),
                s.Entries[^1].Percent,
                s.Entries.Min(e => e.SpeedPercent),
                s.Entries.Max(e => e.SpeedPercent),
                // Hard spots as they stood at the end of the period (the last attempts up to then).
                HardSpots.For(s.All.Where(e => DateOnly.FromDateTime(e.When) <= to).ToList()),
                s.Entries[^1].When))
            .OrderByDescending(s => s.Minutes)
            .ToList();

        return new PracticeReport(
            from, to,
            inPeriod.Sum(e => e.Seconds) / 60,
            inPeriod.Count,
            perDay.Count(d => d.Minutes > 0),
            goalMinutes,
            goalMinutes > 0 ? perDay.Count(d => d.Minutes >= goalMinutes) : 0,
            // The streak as of the period's end (today's when the period runs up to today).
            PracticeGoal.Streak(all.Where(e => DateOnly.FromDateTime(e.When) <= to), goalMinutes, to < today ? to : today),
            perDay,
            songs);
    }

    /// <summary>A song's name from its history key: the file's name, or the title of a song without a file.</summary>
    public static string TitleOf(string key) =>
        key.StartsWith("title:", StringComparison.Ordinal) ? key["title:".Length..] : Path.GetFileNameWithoutExtension(key);

    // --- The usual periods -----------------------------------------------------------------------------

    /// <summary>Monday to today.</summary>
    public static (DateOnly From, DateOnly To) ThisWeek(DateOnly today) =>
        (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today);

    public static (DateOnly From, DateOnly To) LastDays(DateOnly today, int days) => (today.AddDays(1 - days), today);

    public static (DateOnly From, DateOnly To) ThisMonth(DateOnly today) => (new DateOnly(today.Year, today.Month, 1), today);
}
