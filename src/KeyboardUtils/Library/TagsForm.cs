namespace KeyboardUtils;

/// <summary>Edits a song's tags as a comma-separated list - simple on purpose, no autocomplete/picker.</summary>
sealed class TagsForm : AntdUI.Window
{
    readonly AntdUI.Input tagsBox = new() { Size = new Size(360, 38), PlaceholderText = "jazz, těžké, oblíbené" };

    public List<string> Tags => tagsBox.Text
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public TagsForm(string songTitle, List<string> currentTags)
    {
        Text = T("Štítky", "Tags");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 220);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        tagsBox.Text = string.Join(", ", currentTags);
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 16, 20, 0) };
        row.Controls.Add(new AntdUI.Label { Text = T($"Štítky pro „{songTitle}“ (oddělené čárkou):", $"Tags for “{songTitle}” (comma-separated):"), AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        row.Controls.Add(tagsBox);

        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var ok = new AntdUI.Button { Text = T("Uložit", "Save"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(row);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}
