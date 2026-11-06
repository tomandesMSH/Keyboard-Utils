namespace KeyboardUtils;

/// <summary>Final score after a learning session. DialogResult.Retry = play it again.</summary>
sealed class LearningResultForm : AntdUI.Window
{
    public LearningResultForm(LearningResult result, string songTitle)
    {
        Text = T("Hodnocení", "Results");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(440, 400);
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
            ForeColor = result.Percent >= 90 ? PianoRollControl.CorrectColor
                : result.Percent >= 60 ? Color.FromArgb(244, 180, 0)
                : PianoRollControl.WrongColor,
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
        var details = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 11f),
            Text = string.Join("\n", lines),
            Padding = new Padding(0, 12, 0, 0),
        };

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        var retry = new AntdUI.Button { Text = T("Zkusit znovu", "Try again"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, retry]);

        Controls.Add(details);
        Controls.Add(title);
        Controls.Add(percent);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        retry.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
        close.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }
}
