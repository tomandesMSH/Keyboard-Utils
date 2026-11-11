namespace KeyboardUtils;

/// <summary>The daily practice goal in the bottom bar: "Dnes 12 / 20 min · 🔥 3", from learning mode's history.</summary>
sealed partial class MainForm
{
    readonly AntdUI.Label goalLabel = new() { Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(6, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight };
    readonly System.Windows.Forms.Timer goalTimer = new() { Interval = 30_000 };

    /// <summary>The day the "goal met" message was shown, so it comes once a day.</summary>
    DateOnly? goalCelebrated;

    /// <summary>Called from the constructor (before the layout is built): the indicator and its refresh.</summary>
    void InitGoal()
    {
        // Twice a minute: the running session's time, and a new day at midnight.
        goalTimer.Tick += (_, _) => UpdateGoal();
        goalTimer.Start();
        AppTheme.Changed += UpdateGoal;
        goalLabel.Click += (_, _) => SetDailyGoal();
        goalLabel.Cursor = Cursors.Hand;
        Shown += (_, _) =>
        {
            // Met before this start (earlier today): no message about it now.
            if (config.DailyGoalMinutes > 0 && TodayMinutes() >= config.DailyGoalMinutes)
                goalCelebrated = Today;
            UpdateGoal();
        };
        FormClosed += (_, _) =>
        {
            AppTheme.Changed -= UpdateGoal;
            goalTimer.Dispose();
        };
    }

    static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Saved attempts today plus the learning session running now.</summary>
    double TodayMinutes() =>
        PracticeGoal.MinutesOn(practiceHistory.AllEntries(), Today) + (learning?.Elapsed.TotalMinutes ?? 0);

    void UpdateGoal()
    {
        int goal = config.DailyGoalMinutes;
        goalLabel.Visible = goal > 0;
        if (goal <= 0)
            return;
        var entries = practiceHistory.AllEntries().ToList();
        double today = TodayMinutes();
        // The streak counts saved attempts; today joins it once the goal is met.
        int streak = PracticeGoal.Streak(entries, goal, Today);
        if (today >= goal && PracticeGoal.MinutesOn(entries, Today) < goal)
            streak = PracticeGoal.Streak(entries, goal, Today.AddDays(-1)) + 1;

        string minutes = $"{Math.Floor(today):0} / {goal} min";
        goalLabel.Text = T("Dnes ", "Today ") + minutes + (today >= goal ? " ✓" : "") + (streak > 0 ? $" · 🔥 {streak}" : "");
        goalLabel.ForeColor = today >= goal ? Color.FromArgb(22, 163, 74) : AppTheme.SubtleText;

        var days = PracticeGoal.LastDays(entries, Today, 7);
        toolTip.SetToolTip(goalLabel,
            T($"Denní cíl: {goal} min v režimu učení. Série: {streak} dní v řadě.", $"Daily goal: {goal} min of learning mode. Streak: {streak} days in a row.")
            + "\n\n" + string.Join("\n", days.Select(d =>
                $"{d.Day.ToDateTime(TimeOnly.MinValue):ddd d. M.}  {Math.Floor(d.Minutes):0} min" + (d.Minutes >= goal ? " ✓" : "")))
            + "\n\n" + T("Klikni pro změnu cíle.", "Click to change the goal."));

        if (today >= goal && goalCelebrated != Today)
        {
            goalCelebrated = Today;
            SetStatus(T($"Denní cíl splněn: {goal} min! 🎉", $"Daily goal met: {goal} min! 🎉")
                + (streak > 1 ? T($" Série: {streak} dní v řadě.", $" Streak: {streak} days in a row.") : ""));
        }
    }

    void SetDailyGoal()
    {
        using var dialog = new DailyGoalForm(config.DailyGoalMinutes);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        config.DailyGoalMinutes = dialog.Minutes;
        config.Save();
        if (config.DailyGoalMinutes > 0 && TodayMinutes() >= config.DailyGoalMinutes)
            goalCelebrated = Today; // already met under the new goal: no message for that
        UpdateGoal();
        SetStatus(config.DailyGoalMinutes > 0
            ? T($"Denní cíl: {config.DailyGoalMinutes} min.", $"Daily goal: {config.DailyGoalMinutes} min.")
            : T("Denní cíl vypnutý.", "Daily goal off."));
    }
}
