namespace KeyboardUtils.Tests;

public class PracticeGoalTests
{
    static readonly DateOnly Today = new(2026, 11, 12);

    static PracticeEntry At(DateTime when, double minutes) =>
        new(when, 90, 9, 10, 1, 0, minutes * 60, true, LearningMode.InTempo, 100, ["Pravá ruka"], null, null);

    static DateTime Day(int daysAgo, int hour = 12) => Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0));

    [Fact]
    public void Adds_up_the_minutes_of_a_day()
    {
        var entries = new[] { At(Day(0, 8), 5), At(Day(0, 23), 7.5), At(Day(1, 23), 30), At(Day(0, 0), 2) };
        Assert.Equal(14.5, PracticeGoal.MinutesOn(entries, Today), 6);
        Assert.Equal(30, PracticeGoal.MinutesOn(entries, Today.AddDays(-1)), 6);
        Assert.Equal(0, PracticeGoal.MinutesOn(entries, Today.AddDays(-2)), 6);
    }

    [Fact]
    public void Streak_counts_days_in_a_row_with_the_goal_met()
    {
        var entries = new[]
        {
            At(Day(0), 20), At(Day(1), 25), At(Day(2), 10), At(Day(2, 18), 10), // today, yesterday, two days ago (2 × 10)
            At(Day(3), 5),                                                        // not enough: the streak ends here
            At(Day(4), 60),
        };
        Assert.Equal(3, PracticeGoal.Streak(entries, 20, Today));
    }

    [Fact]
    public void Today_not_met_yet_keeps_yesterdays_streak()
    {
        var entries = new[] { At(Day(0), 3), At(Day(1), 20), At(Day(2), 20) };
        Assert.Equal(2, PracticeGoal.Streak(entries, 20, Today));
    }

    [Fact]
    public void A_missed_day_breaks_the_streak()
    {
        var entries = new[] { At(Day(2), 20), At(Day(3), 20) };
        Assert.Equal(0, PracticeGoal.Streak(entries, 20, Today));
    }

    [Fact]
    public void No_goal_no_streak()
    {
        Assert.Equal(0, PracticeGoal.Streak([At(Day(0), 60)], 0, Today));
        Assert.Equal(0, PracticeGoal.Streak([], 20, Today));
    }

    [Fact]
    public void Last_days_oldest_first_with_empty_days()
    {
        var days = PracticeGoal.LastDays([At(Day(0), 4), At(Day(2), 6)], Today, 3);
        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1), Today], days.Select(d => d.Day));
        Assert.Equal([6.0, 0.0, 4.0], days.Select(d => Math.Round(d.Minutes, 6)));
    }

    [Fact]
    public void All_entries_span_every_song_and_are_not_saved_as_a_field()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kbu-goal-{Guid.NewGuid():N}.json");
        try
        {
            var history = new PracticeHistory();
            history.Add("a", At(Day(0), 5));
            history.Add("b", At(Day(0), 6));
            Assert.Equal(11, PracticeGoal.MinutesOn(history.AllEntries(), Today), 6);
            history.Save(path);
            Assert.DoesNotContain("AllEntries", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
