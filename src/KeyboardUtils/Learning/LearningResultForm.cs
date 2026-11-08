namespace KeyboardUtils;

/// <summary>
/// Final score after a learning session, with the song's earlier attempts (last time, best, a chart).
/// DialogResult.Retry = play it again; DialogResult.Yes = practise <see cref="HardSpotToPractise"/> in a loop.
/// </summary>
sealed class LearningResultForm : AntdUI.Window
{
    /// <summary>The hard spot whose "Practise" button closed the dialog.</summary>
    public HardSpot? HardSpotToPractise { get; private set; }

    /// <param name="history">The song's attempts, oldest first, this one last.</param>
    /// <param name="hardSpots">Where the most mistakes are (worst first), from the attempts so far.</param>
    public LearningResultForm(LearningResult result, string songTitle, IReadOnlyList<PracticeEntry> history, IReadOnlyList<HardSpot> hardSpots)
    {
        Text = T("Hodnocení", "Results");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(480, 560);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var percent = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 80,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 34f, FontStyle.Bold),
            Text = $"{result.Percent:0} %",
            ForeColor = PracticeChart.ColorFor(result.Percent),
        };
        var title = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = songTitle + (result.Completed ? "" : T("  (nedohráno)", "  (not finished)")),
        };

        var lines = new List<string>
        {
            T($"Správně: {result.CorrectNotes} z {result.TotalNotes} not", $"Correct: {result.CorrectNotes} of {result.TotalNotes} notes"),
            T($"Špatné klávesy: {result.WrongPresses}", $"Wrong keys: {result.WrongPresses}"),
        };
        if (result.MissedNotes > 0)
            lines.Add(T($"Vynechané nebo pozdě: {result.MissedNotes}", $"Missed or late: {result.MissedNotes}"));
        lines.Add(T($"Čas: {result.Duration:m\\:ss}", $"Time: {result.Duration:m\\:ss}"));
        if (hardSpots.Count > 0)
            lines.Add(T("Nejvíc chyb: takty ", "Most mistakes: bars ") + string.Join(", ", hardSpots.Select(s => s.Bars)));
        var details = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 11f),
            Text = string.Join("\n", lines),
            Padding = new Padding(0, 12, 0, 0),
        };

        // Earlier attempts: the previous one, the best one, and whether this beat it.
        var earlier = history.Take(Math.Max(0, history.Count - 1)).ToList();
        var summary = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        };
        if (earlier.Count == 0)
            summary.Text = T("První pokus u téhle skladby", "First attempt at this song");
        else
        {
            var best = earlier.MaxBy(e => e.Percent)!;
            summary.Text = (result.Percent > best.Percent ? T("Nový rekord!  ·  ", "New best!  ·  ") : "")
                + T($"Minule: {earlier[^1].Percent:0} %  ·  Nejlepší: {best.Percent:0} % ({best.When:d. M.})",
                    $"Last time: {earlier[^1].Percent:0} %  ·  Best: {best.Percent:0} % ({best.When:d. M.})");
            if (result.Percent > best.Percent)
                summary.ForeColor = PianoRollControl.CorrectColor;
        }
        var chart = new PracticeChart { Dock = DockStyle.Fill, HighlightLast = true };
        chart.SetEntries(history);
        var chartBox = new Panel { Dock = DockStyle.Bottom, Height = 122, Padding = new Padding(16, 0, 16, 12) };
        chartBox.Controls.Add(chart);

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        var retry = new AntdUI.Button { Text = T("Zkusit znovu", "Try again"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, retry]);
        // The worst spot, straight into a loop.
        if (hardSpots.Count > 0)
        {
            var spot = hardSpots[0];
            var practise = new AntdUI.Button { Text = T($"Procvičit takty {spot.Bars}", $"Practise bars {spot.Bars}"), AutoSize = true, Radius = 8 };
            practise.Click += (_, _) =>
            {
                HardSpotToPractise = spot;
                DialogResult = DialogResult.Yes;
                Close();
            };
            buttons.Controls.Add(practise);
        }

        Controls.Add(details);
        Controls.Add(summary);
        Controls.Add(title);
        Controls.Add(percent);
        Controls.Add(chartBox);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        // Enter = try again, Esc = close (also from the macro keyboard's Confirm / Cancel).
        AcceptButton = retry;
        CancelButton = close;

        retry.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
        close.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }
}
