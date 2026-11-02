namespace KeyboardUtils;

/// <summary>Picks what a macro keyboard key should do.</summary>
sealed class ActionPickerForm : AntdUI.Window
{
    sealed record Choice(string Text, MacroAction Action, int? PartIndex = null, string? PartName = null)
    {
        public override string ToString() => Text;
    }

    static Choice[] FixedChoices =>
    [
        new(new Binding { Action = MacroAction.PlayPause }.Describe(), MacroAction.PlayPause),
        new(new Binding { Action = MacroAction.Stop }.Describe(), MacroAction.Stop),
        new(new Binding { Action = MacroAction.NextTrack }.Describe(), MacroAction.NextTrack),
        new(new Binding { Action = MacroAction.PreviousTrack }.Describe(), MacroAction.PreviousTrack),
        new(new Binding { Action = MacroAction.ToggleLearningMode }.Describe(), MacroAction.ToggleLearningMode),
        new(new Binding { Action = MacroAction.ToggleCreateMode }.Describe(), MacroAction.ToggleCreateMode),
    ];

    readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public Binding? Result { get; private set; }

    public ActionPickerForm(string keyName, IReadOnlyList<string>? currentSongParts = null)
    {
        Text = T($"Klávesa {keyName} - vyber akci", $"Key {keyName} - choose an action");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(420, 360);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var ok = new AntdUI.Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Width = 90, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(list);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        var titleBar = new TitleBar(this);
        Controls.Add(titleBar);
        titleBar.BringToFront();

        foreach (var choice in FixedChoices)
            list.Items.Add(choice);
        if (currentSongParts != null)
            for (int i = 0; i < currentSongParts.Count; i++)
                list.Items.Add(new Choice(
                    new Binding { Action = MacroAction.ToggleMutePart, PartName = currentSongParts[i] }.Describe(),
                    MacroAction.ToggleMutePart, i, currentSongParts[i]));
        list.SelectedIndex = 0;

        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) { DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
                return;
            if (list.SelectedItem is not Choice choice)
            {
                e.Cancel = true;
                return;
            }
            Result = new Binding { Action = choice.Action, PartIndex = choice.PartIndex, PartName = choice.PartName };
        };

        Shown += (_, _) => list.Focus();
    }
}
