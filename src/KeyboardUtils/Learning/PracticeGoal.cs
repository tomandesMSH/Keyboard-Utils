namespace KeyboardUtils;

/// <summary>
/// The daily practice goal: minutes of learning mode a day (from the practice history, all songs) and the streak
/// of days in a row the goal was met. Free playing isn't timed - only learning mode leaves entries.
/// </summary>
static class PracticeGoal
{
    /// <summary>Minutes of learning on <paramref name="day"/> (local date of each attempt).</summary>
    public static double MinutesOn(IEnumerable<PracticeEntry> entries, DateOnly day) =>
        entries.Where(e => DateOnly.FromDateTime(e.When) == day).Sum(e => e.Seconds) / 60;

    /// <summary>Minutes per day, for the last <paramref name="days"/> days ending with <paramref name="today"/> (oldest first).</summary>
    public static List<(DateOnly Day, double Minutes)> LastDays(IEnumerable<PracticeEntry> entries, DateOnly today, int days)
    {
        var perDay = PerDay(entries);
        return Enumerable.Range(0, days).Select(i => today.AddDays(i - days + 1))
            .Select(day => (day, perDay.GetValueOrDefault(day))).ToList();
    }

    /// <summary>
    /// Days in a row the goal was met, ending today - or yesterday while today isn't met yet, so the streak doesn't
    /// vanish in the morning. 0 with no goal.
    /// </summary>
    public static int Streak(IEnumerable<PracticeEntry> entries, int goalMinutes, DateOnly today)
    {
        if (goalMinutes <= 0)
            return 0;
        var perDay = PerDay(entries);
        bool Met(DateOnly day) => perDay.GetValueOrDefault(day) >= goalMinutes;
        var day = Met(today) ? today : today.AddDays(-1);
        int streak = 0;
        while (Met(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    static Dictionary<DateOnly, double> PerDay(IEnumerable<PracticeEntry> entries) =>
        entries.GroupBy(e => DateOnly.FromDateTime(e.When)).ToDictionary(g => g.Key, g => g.Sum(e => e.Seconds) / 60);
}
