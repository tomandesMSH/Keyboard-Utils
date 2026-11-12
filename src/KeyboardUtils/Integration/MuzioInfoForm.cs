namespace KeyboardUtils;

/// <summary>Muzio > Muzio...: what Muzio is, with Open Muzio, Cancel - and Hide, for those who don't use it.</summary>
sealed class MuzioInfoForm : AntdUI.Window
{
    public enum Choice { None, Open, Hide }

    public Choice Result { get; private set; }

    public MuzioInfoForm()
    {
        Text = "Muzio";
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(520, 250);

        var text = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 12, 24, 8),
            TextAlign = ContentAlignment.TopLeft,
            Text = T("Muzio je webová aplikace ZUŠ Moravský Krumlov pro žáky a učitele: učitel zadává úkoly a noty, žák cvičí, "
                     + "nahrává se a sbírá body a odznaky za každodenní cvičení.\n\n"
                     + "Noty z úkolu v Muziu otevřeš v Keyboard Utils tlačítkem „Otevřít v Keyboard Utils“.",
                     "Muzio is the web app of ZUŠ Moravský Krumlov for students and teachers: teachers set assignments and scores, students "
                     + "practise, record themselves and earn points and badges for practising every day.\n\n"
                     + "Open an assignment's score in Keyboard Utils with its \"Open in Keyboard Utils\" button."),
        };

        var open = new AntdUI.Button { Text = T("Otevřít Muzio", "Open Muzio"), AutoSize = true, Radius = 8, Type = AntdUI.TTypeMini.Primary };
        var cancel = new AntdUI.Button { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };
        var hide = new AntdUI.Button { Text = T("Skrýt menu Muzio", "Hide the Muzio menu"), AutoSize = true, Radius = 8, Ghost = true };
        open.Click += (_, _) => Finish(Choice.Open);
        cancel.Click += (_, _) => Finish(Choice.None);
        hide.Click += (_, _) => Finish(Choice.Hide);

        // Open and Cancel on the right, Hide on its own on the left; the row as tall as the buttons need.
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        right.Controls.AddRange([open, cancel]);
        hide.Anchor = AnchorStyles.Left;
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(12, 6, 12, 12) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(hide, 0, 0);
        buttons.Controls.Add(right, 1, 0);

        Controls.Add(text);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        AcceptButton = open;
        CancelButton = cancel;
    }

    void Finish(Choice choice)
    {
        Result = choice;
        DialogResult = choice == Choice.None ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}
