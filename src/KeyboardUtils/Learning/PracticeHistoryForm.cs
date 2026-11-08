namespace KeyboardUtils;

/// <summary>All learning attempts at a song: a chart of the latest and the full list, newest first.</summary>
sealed class PracticeHistoryForm : AntdUI.Window
{
    public PracticeHistoryForm(string songTitle, IReadOnlyList<PracticeEntry> history, IReadOnlyList<HardSpot> hardSpots)
    {
        Text = T($"Historie cvičení - {songTitle}", $"Practice history - {songTitle}");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 560);
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        var summary = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Text = history.Count == 0
                ? T("U téhle skladby zatím nejsou žádné pokusy.", "No attempts at this song yet.")
                : T($"Pokusů: {history.Count}  ·  Nejlepší: {history.Max(e => e.Percent):0} %  ·  Průměr posledních 10: {history.TakeLast(10).Average(e => e.Percent):0} %",
                    $"Attempts: {history.Count}  ·  Best: {history.Max(e => e.Percent):0} %  ·  Average of the last 10: {history.TakeLast(10).Average(e => e.Percent):0} %"),
        };
        var hard = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = hardSpots.Count > 0 ? 24 : 0,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = PianoRollControl.WrongColor,
            Text = hardSpots.Count > 0
                ? T("Nejvíc chyb: takty ", "Most mistakes: bars ") + string.Join(", ", hardSpots.Select(s => s.Bars))
                : "",
        };

        var chart = new PracticeChart { Dock = DockStyle.Fill, MaxBars = 40 };
        chart.SetEntries(history);
        var chartBox = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(16, 4, 16, 8) };
        chartBox.Controls.Add(chart);

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        list.Columns.Add(T("Kdy", "When"), 130);
        list.Columns.Add("%", 55, HorizontalAlignment.Right);
        list.Columns.Add(T("Správně", "Correct"), 80, HorizontalAlignment.Right);
        list.Columns.Add(T("Rychlost", "Speed"), 70, HorizontalAlignment.Right);
        list.Columns.Add(T("Režim", "Mode"), 105);
        list.Columns.Add(T("Části", "Parts"), 120);
        list.Columns.Add(T("Úsek", "Section"), 95);
        foreach (var entry in history.Reverse())
            list.Items.Add(new ListViewItem(
            [
                entry.When.ToString("d. M. yyyy  H:mm"),
                $"{entry.Percent:0}" + (entry.Completed ? "" : "*"),
                $"{entry.Correct}/{entry.Total}",
                $"{entry.SpeedPercent} %",
                entry.Mode == LearningMode.InTempo ? T("v tempu", "in tempo") : T("čekat na notu", "wait for note"),
                string.Join(", ", entry.Parts),
                entry.LoopStart is { } start && entry.LoopEnd is { } end
                    ? $"{TimeSpan.FromSeconds(start):m\\:ss}–{TimeSpan.FromSeconds(end):m\\:ss}"
                    : T("celá skladba", "whole song"),
            ]));

        var note = new AntdUI.Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            ForeColor = SystemColors.GrayText,
            Text = T("* nedohráno", "* not finished"),
            Padding = new Padding(16, 0, 0, 0),
        };
        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), Size = new Size(100, 36), Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 4, 12, 12) };
        buttons.Controls.Add(close);
        var listBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0) };
        listBox.Controls.Add(list);

        Controls.Add(listBox);
        Controls.Add(chartBox);
        Controls.Add(hard);
        Controls.Add(summary);
        Controls.Add(note);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        // Enter or Esc closes (also from the macro keyboard).
        AcceptButton = close;
        CancelButton = close;
    }
}
