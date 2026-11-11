namespace KeyboardUtils;

/// <summary>
/// "My songs folder" in one place: which folder it is and how many songs it has, play through them (next / previous
/// song then go through the folder), pick another folder, or open it in Explorer.
/// </summary>
sealed class SongsFolderForm : AntdUI.Window
{
    readonly AntdUI.Label folderLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2) };
    readonly AntdUI.Label countLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 8) };
    readonly AntdUI.Button play = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button explore = new() { AutoSize = true, Radius = 8 };
    readonly Func<string?> currentFolder;
    readonly Func<string, int> countSongs;

    /// <summary>What was chosen: play the folder's songs.</summary>
    public bool PlayRequested { get; private set; }

    /// <param name="currentFolder">The folder now (null = none chosen yet).</param>
    /// <param name="pickFolder">Asks for another folder; true when one was picked.</param>
    /// <param name="countSongs">How many songs a folder holds (with its subfolders).</param>
    public SongsFolderForm(Func<string?> currentFolder, Func<bool> pickFolder, Func<string, int> countSongs)
    {
        this.currentFolder = currentFolder;
        this.countSongs = countSongs;
        Text = T("Moje složka se skladbami", "My songs folder");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(520, 280);

        var intro = new AntdUI.Label
        {
            Text = T("Všechny skladby z této složky (i z podsložek) si můžeš přehrávat jednu po druhé – tlačítky Další / Předchozí skladba.",
                "Play all the songs in this folder (subfolders too) one after another - with Next / Previous song."),
            AutoSize = false,
            Size = new Size(470, 44),
            ForeColor = SystemColors.GrayText,
        };
        play.Text = T("Přehrát skladby", "Play the songs");
        play.Click += (_, _) => { PlayRequested = true; DialogResult = DialogResult.OK; Close(); };
        var change = new AntdUI.Button { Text = T("Změnit složku…", "Change folder…"), AutoSize = true, Radius = 8 };
        change.Click += (_, _) =>
        {
            if (pickFolder())
                Refresh();
        };
        explore.Text = T("Otevřít ve Windows", "Open in Explorer");
        explore.Click += (_, _) =>
        {
            if (currentFolder() is { } folder && Directory.Exists(folder))
                AboutForm.Open(folder);
        };
        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        close.Click += (_, _) => Close();

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 10, 20, 4) };
        layout.Controls.AddRange([intro, folderLabel, countLabel]);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, explore, change, play]);
        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(titleBar);
        CancelButton = close;
        AcceptButton = play;
        Load += (_, _) =>
        {
            ClientSize = new Size(Math.Max(ClientSize.Width, buttons.GetPreferredSize(Size.Empty).Width + 8),
                titleBar.Height + layout.GetPreferredSize(Size.Empty).Height + buttons.GetPreferredSize(Size.Empty).Height);
            CenterToParent();
        };
        Refresh();
    }

    /// <summary>A long path as its drive and its last folders: C:\…\Noty\Klavír.</summary>
    static string Shorten(string path, int max = 60)
    {
        if (path.Length <= max)
            return path;
        var parts = path.TrimEnd('\\', '/').Split('\\', '/');
        string tail = parts[^1];
        for (int i = parts.Length - 2; i > 0 && parts[0].Length + tail.Length + parts[i].Length + 4 <= max; i--)
            tail = parts[i] + "\\" + tail;
        return parts[0] + "\\…\\" + tail;
    }

    /// <summary>Shows the folder now chosen; without one, only "Change folder" makes sense.</summary>
    new void Refresh()
    {
        if (currentFolder() is { } folder && Directory.Exists(folder))
        {
            int count = countSongs(folder);
            folderLabel.Text = Shorten(folder);
            countLabel.Text = count switch
            {
                0 => T("Žádné skladby (MIDI, MusicXML).", "No songs (MIDI, MusicXML)."),
                1 => T("1 skladba", "1 song"),
                < 5 => T($"{count} skladby", $"{count} songs"),
                _ => T($"{count} skladeb", $"{count} songs"),
            };
            play.Enabled = count > 0;
            explore.Enabled = true;
        }
        else
        {
            folderLabel.Text = T("Zatím žádná složka není vybraná.", "No folder chosen yet.");
            countLabel.Text = T("Klikni na Změnit složku a vyber tu, kde máš noty.", "Click Change folder and pick the one with your music.");
            play.Enabled = false;
            explore.Enabled = false;
        }
    }
}
