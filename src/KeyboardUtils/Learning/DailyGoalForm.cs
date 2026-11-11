namespace KeyboardUtils;

/// <summary>How many minutes of learning a day to aim for; 0 turns the goal (and its indicator) off.</summary>
sealed class DailyGoalForm : AntdUI.Window
{
    readonly AntdUI.InputNumber minutesBox = new() { Size = new Size(120, 36), Minimum = 0, Maximum = 240, SuffixText = "min", Margin = new Padding(0, 0, 0, 0) };

    public int Minutes => (int)minutesBox.Value;

    public DailyGoalForm(int minutes)
    {
        Text = T("Denní cíl", "Daily goal");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 250);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        minutesBox.Value = Math.Clamp(minutes, 0, 240);
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 16, 20, 0) };
        fields.Controls.Add(new AntdUI.Label
        {
            Text = T("Kolik minut denně chceš cvičit v režimu učení:", "Minutes of learning mode a day to aim for:"),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        });
        fields.Controls.Add(minutesBox);
        fields.Controls.Add(new AntdUI.Label
        {
            Text = T("0 = bez cíle. Dole vpravo uvidíš, kolik máš dnes, a sérii dní v řadě se splněným cílem.",
                "0 = no goal. Bottom right shows today's minutes and the streak of days in a row with the goal met."),
            AutoSize = false,
            Size = new Size(410, 40),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
        });

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var ok = new AntdUI.Button { Text = T("Uložit", "Save"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(fields);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}
