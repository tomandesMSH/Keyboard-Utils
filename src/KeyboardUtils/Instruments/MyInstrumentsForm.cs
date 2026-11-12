namespace KeyboardUtils;

/// <summary>Settings > My instruments: tick what you play - each gets a tab at the top. At least one stays.</summary>
sealed class MyInstrumentsForm : AntdUI.Window
{
    readonly List<(Instrument Instrument, AntdUI.Checkbox Box)> boxes = [];
    readonly AntdUI.Button ok = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };

    /// <summary>The instruments ticked, in the app's order.</summary>
    public List<Instrument> Chosen => boxes.Where(b => b.Box.Checked).Select(b => b.Instrument).ToList();

    public MyInstrumentsForm(IReadOnlyCollection<Instrument> mine)
    {
        Text = T("Moje nástroje", "My instruments");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(360, 480);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 10, 20, 4) };
        layout.Controls.Add(new AntdUI.Label
        {
            Text = T("Na co hraješ? Každý nástroj má nahoře svou záložku.", "What do you play? Each instrument gets a tab at the top."),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        });
        foreach (var instrument in Instrument.All)
        {
            var box = new AntdUI.Checkbox { Text = instrument.Name, Checked = mine.Contains(instrument), AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            box.CheckedChanged += (_, _) => ok.Enabled = Chosen.Count > 0;
            boxes.Add((instrument, box));
            layout.Controls.Add(box);
        }

        ok.Text = "OK";
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);
        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(titleBar);
        AcceptButton = ok;
        CancelButton = cancel;
        Load += (_, _) =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(ClientSize.Width, content.Width), titleBar.Height + content.Height + buttons.GetPreferredSize(Size.Empty).Height);
            CenterToParent();
        };
    }
}
