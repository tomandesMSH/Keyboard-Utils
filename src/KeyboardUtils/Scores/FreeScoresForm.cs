using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// "Find free sheet music": searches Mutopia, lists the pieces and downloads the chosen one's MIDI. The caller opens
/// <see cref="Files"/> (several for a piece with movements) like any opened songs.
/// </summary>
sealed class FreeScoresForm : AntdUI.Window
{
    readonly AntdUI.Input queryBox = new() { Size = new Size(420, 38), PlaceholderText = "" };
    readonly AntdUI.Button searchButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Margin = new Padding(8, 0, 0, 0) };
    readonly AntdUI.Button moreButton = new() { AutoSize = true, Radius = 8, Visible = false };
    readonly AntdUI.Button openButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8, Enabled = false };
    readonly Label statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 8, 0, 0) };
    readonly ListView list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Size = new Size(700, 320),
        Font = new Font("Segoe UI", 9.5f),
    };
    readonly List<FreeScoreResult> results = [];
    string lastQuery = "";
    CancellationTokenSource? running;

    /// <summary>The downloaded MIDI files of the chosen piece, once the dialog closed with OK.</summary>
    public List<string> Files { get; } = [];

    /// <summary>The chosen piece (for its license and attribution).</summary>
    public FreeScoreResult? Piece { get; private set; }

    public FreeScoresForm()
    {
        Text = T("Hledat noty zdarma", "Find free sheet music");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 700);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 10, 22, 4) };
        layout.Controls.Add(Paragraph(T(
            "Hledá v Mutopia Project - přes 2000 skladeb (hlavně klasika) jako volné dílo nebo pod licencí Creative Commons. "
            + "Hledej podle skladatele, názvu nebo nástroje, např. \"Bach\", \"Chopin\", \"Piano\".",
            "Searches the Mutopia Project - over 2000 pieces (mostly classical), public domain or Creative Commons. "
            + "Search by composer, title or instrument, e.g. \"Bach\", \"Chopin\", \"Piano\".")));

        queryBox.PlaceholderText = T("Skladatel, název, nástroj...", "Composer, title, instrument...");
        searchButton.Text = T("Hledat", "Search");
        var searchRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 8) };
        searchRow.Controls.AddRange([queryBox, searchButton]);
        layout.Controls.Add(searchRow);

        list.Columns.Add(T("Název", "Title"), 250);
        list.Columns.Add(T("Skladatel", "Composer"), 140);
        list.Columns.Add(T("Nástroj", "Instrument"), 150);
        list.Columns.Add(T("Licence", "License"), 135);
        layout.Controls.Add(list);

        moreButton.Text = T("Další výsledky", "More results");
        moreButton.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(moreButton);
        layout.Controls.Add(statusLabel);

        var source = new LinkLabel
        {
            Text = T("Zdroj: Mutopia Project - u licencí CC BY uveď při sdílení autora a zdroj.",
                "Source: Mutopia Project - for CC BY licenses, credit the author and source when sharing."),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 10, 0, 0),
            LinkArea = new LinkArea(T("Zdroj: ", "Source: ").Length, "Mutopia Project".Length),
        };
        source.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://www.mutopiaproject.org/") { UseShellExecute = true });
        layout.Controls.Add(source);

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        openButton.Text = T("Otevřít", "Open");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, openButton]);

        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        searchButton.Click += async (_, _) => await Search(queryBox.Text.Trim(), append: false);
        queryBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            await Search(queryBox.Text.Trim(), append: false);
        };
        moreButton.Click += async (_, _) => await Search(lastQuery, append: true);
        list.SelectedIndexChanged += (_, _) => openButton.Enabled = running == null && list.SelectedIndices.Count == 1;
        list.DoubleClick += async (_, _) => await OpenSelected();
        openButton.Click += async (_, _) => await OpenSelected();
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Shown += (_, _) => queryBox.Focus();
    }

    async Task Search(string query, bool append)
    {
        if (query.Length == 0 || running != null)
            return;
        lastQuery = query;
        if (!append)
        {
            results.Clear();
            list.Items.Clear();
        }
        SetBusy(true, T("Hledám...", "Searching..."));
        try
        {
            var (found, hasMore) = await MutopiaSearch.SearchAsync(query, results.Count, running!.Token);
            results.AddRange(found);
            list.BeginUpdate();
            foreach (var piece in found)
                list.Items.Add(new ListViewItem([piece.Title, piece.Composer, piece.Instrument, piece.License]));
            list.EndUpdate();
            moreButton.Visible = hasMore;
            SetBusy(false, results.Count == 0
                ? T("Nic nenalezeno. Zkus jiný výraz (anglicky nebo jméno skladatele).", "Nothing found. Try another term (English or a composer's name).")
                : T($"Nalezeno: {results.Count}{(hasMore ? "+" : "")}. Vyber skladbu a klikni Otevřít.", $"Found: {results.Count}{(hasMore ? "+" : "")}. Pick a piece and click Open."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetBusy(false, T("Hledání se nepovedlo - zkontroluj připojení k internetu. ", "Search failed - check your internet connection. ") + ex.Message);
        }
    }

    async Task OpenSelected()
    {
        if (list.SelectedIndices.Count != 1 || running != null)
            return;
        var piece = results[list.SelectedIndices[0]];
        SetBusy(true, T($"Stahuji {piece.Title}...", $"Downloading {piece.Title}..."));
        try
        {
            var files = await MutopiaSearch.DownloadAsync(piece, running!.Token);
            if (files.Count == 0)
            {
                SetBusy(false, T("Ke skladbě se nepodařilo najít MIDI soubor.", "No MIDI file found for this piece."));
                return;
            }
            Files.AddRange(files);
            Piece = piece;
            running = null;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            SetBusy(false, T("Stažení se nepovedlo: ", "Download failed: ") + ex.Message);
        }
    }

    void SetBusy(bool busy, string status)
    {
        if (IsDisposed)
            return;
        running = busy ? new CancellationTokenSource() : null;
        searchButton.Enabled = moreButton.Enabled = !busy;
        openButton.Enabled = !busy && list.SelectedIndices.Count == 1;
        statusLabel.Text = status;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        running?.Cancel();
        base.OnFormClosing(e);
    }

    static Label Paragraph(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(700, 0), Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 4, 0, 2),
    };
}
