namespace KeyboardUtils;

/// <summary>
/// Cvičení > Zpráva pro učitele: choose the period (and the name on it), see the first page, save it as a PDF to
/// send or print. Built from learning mode's history.
/// </summary>
sealed class TeacherReportForm : AntdUI.Window
{
    readonly AppConfig config;
    readonly PracticeHistory history;
    readonly Instrument instrument;
    readonly AntdUI.Input nameBox = new() { Size = new Size(260, 38), Margin = new Padding(0, 0, 0, 6) };
    readonly AntdUI.Select periodBox = new() { Size = new Size(220, 38), List = true, Margin = new Padding(10, 0, 0, 6) };
    readonly DateTimePicker fromBox = new() { Format = DateTimePickerFormat.Short, Width = 120, Margin = new Padding(10, 8, 0, 0), Visible = false };
    readonly DateTimePicker toBox = new() { Format = DateTimePickerFormat.Short, Width = 120, Margin = new Padding(6, 8, 0, 0), Visible = false };
    readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(200, 202, 208) };
    readonly AntdUI.Label summary = new() { Dock = DockStyle.Bottom, Height = 28, ForeColor = SystemColors.GrayText, Padding = new Padding(16, 0, 16, 0) };
    List<Bitmap> pages = [];
    PracticeReport? report;

    public TeacherReportForm(AppConfig config, PracticeHistory history, Instrument instrument)
    {
        this.config = config;
        this.history = history;
        this.instrument = instrument;
        Text = T("Zpráva pro učitele", "Report for the teacher");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(760, 900);

        nameBox.PlaceholderText = T("Jméno žáka / žákyně", "Student's name");
        nameBox.Text = config.StudentName ?? "";
        periodBox.Items.AddRange([T("Tento týden", "This week"), T("Posledních 7 dní", "Last 7 days"), T("Tento měsíc", "This month"),
            T("Posledních 30 dní", "Last 30 days"), T("Vlastní období...", "Custom period...")]);
        periodBox.SelectedIndex = 1;
        var today = DateTime.Today;
        fromBox.Value = today.AddDays(-13);
        toBox.Value = today;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(16, 10, 16, 6) };
        top.Controls.AddRange([nameBox, periodBox, fromBox, toBox]);
        var save = new AntdUI.Button { Text = T("Uložit PDF...", "Save PDF..."), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        save.Click += (_, _) => Save();
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, save]);
        var previewBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
        previewBox.Controls.Add(preview);

        Controls.Add(previewBox);
        Controls.Add(summary);
        Controls.Add(buttons);
        Controls.Add(top);
        Controls.Add(new TitleBar(this));
        CancelButton = close;

        periodBox.SelectedIndexChanged += (_, _) =>
        {
            bool custom = periodBox.SelectedIndex == 4;
            fromBox.Visible = toBox.Visible = custom;
            Rebuild();
        };
        fromBox.ValueChanged += (_, _) => Rebuild();
        toBox.ValueChanged += (_, _) => Rebuild();
        nameBox.TextChanged += (_, _) => Rebuild();
        FormClosed += (_, _) =>
        {
            config.StudentName = nameBox.Text.Trim();
            config.Save();
            Dispose(pages);
        };
        Rebuild();
    }

    (DateOnly From, DateOnly To) Period()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return periodBox.SelectedIndex switch
        {
            0 => PracticeReport.ThisWeek(today),
            2 => PracticeReport.ThisMonth(today),
            3 => PracticeReport.LastDays(today, 30),
            4 => (DateOnly.FromDateTime(fromBox.Value), DateOnly.FromDateTime(toBox.Value)),
            _ => PracticeReport.LastDays(today, 7),
        };
    }

    void Rebuild()
    {
        var (from, to) = Period();
        report = PracticeReport.Build(history, from, to, config.DailyGoalMinutes, DateOnly.FromDateTime(DateTime.Today));
        var old = pages;
        pages = PracticeReportRenderer.Render(report, nameBox.Text, instrument);
        preview.Image = pages[0];
        Dispose(old);
        summary.Text = T($"{pages.Count} {(pages.Count == 1 ? "strana" : pages.Count < 5 ? "strany" : "stran")} · {Math.Round(report.Minutes)} min · {report.Songs.Count} {(report.Songs.Count == 1 ? "skladba" : report.Songs.Count is >= 2 and <= 4 ? "skladby" : "skladeb")}",
            $"{pages.Count} page{(pages.Count == 1 ? "" : "s")} · {Math.Round(report.Minutes)} min · {report.Songs.Count} song{(report.Songs.Count == 1 ? "" : "s")}");
    }

    static void Dispose(List<Bitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps)
            bitmap.Dispose();
    }

    void Save()
    {
        if (report == null)
            return;
        string name = string.IsNullOrWhiteSpace(nameBox.Text) ? "" : " – " + nameBox.Text.Trim();
        string file = $"{T("Cvičení", "Practice")}{name} – {report.From:d.M.yyyy}–{report.To:d.M.yyyy}.pdf";
        using var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = string.Concat(file.Split(Path.GetInvalidFileNameChars())),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            PdfWriter.Write(dialog.FileName, pages, T("Zpráva o cvičení", "Practice report") + name);
            AboutForm.Open(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowModal(this, Text, T("PDF se nepodařilo uložit: ", "Couldn't save the PDF: ") + ex.Message, AntdUI.TType.Error);
        }
    }
}
