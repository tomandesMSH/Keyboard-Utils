using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// "Import stems": either load a folder of tracks as the accompaniment (the original recording, played as audio),
/// or detect the notes in an audio file (transcribed in the background with progress). Also the place for tips on
/// getting a song's single-instrument tracks (stems).
/// </summary>
sealed class ImportAudioForm : AntdUI.Window
{
    readonly AntdUI.Button accompanimentButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button notesButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Progress progress = new() { Size = new Size(560, 14), Margin = new Padding(0, 10, 0, 0), Visible = false };
    readonly Label statusLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = SystemColors.GrayText, Font = new Font("Segoe UI", 9f) };
    readonly string? initialDirectory;
    readonly AppConfig config;
    CancellationTokenSource? running;

    /// <summary>"As accompaniment": the chosen folder of tracks, once the dialog closed with OK.</summary>
    public string? StemsFolder { get; private set; }

    /// <summary>"Detect notes": the converted notes, once the dialog closed with OK.</summary>
    public Score? Result { get; private set; }

    /// <summary>The audio file <see cref="Result"/> came from.</summary>
    public string? FilePath { get; private set; }

    /// <param name="initialDirectory">Where the pickers start, e.g. the folder of the loaded stems.</param>
    /// <param name="config">For offering the chord detection add-on (and remembering a "no").</param>
    public ImportAudioForm(string? initialDirectory, AppConfig config)
    {
        this.initialDirectory = initialDirectory;
        this.config = config;
        Text = T("Importovat stopy", "Import stems");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 790);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(22, 6, 22, 8),
        };

        layout.Controls.Add(Heading(T("Jako doprovod", "As accompaniment")));
        layout.Controls.Add(Paragraph(T(
            "Vyber složku se stopami (.wav) - přehraje se originální nahrávka a v panelu vpravo jde každou stopu ztlumit "
            + "nebo zeslabit. Tlačítkem \"Noty\" u stopy pak přidáš její noty do piano rollu a můžeš ji hrát sám.",
            "Choose a folder of tracks (.wav) - the original recording plays, and each track can be muted or turned down "
            + "in the panel on the right. The \"Notes\" button next to a track adds its notes to the piano roll, so you can play it yourself.")));
        accompanimentButton.Text = T("Vybrat složku se stopami...", "Choose folder with tracks...");
        accompanimentButton.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(accompanimentButton);

        layout.Controls.Add(Heading(T("Detekovat noty z hudby", "Detect notes from music")));
        layout.Controls.Add(Paragraph(T(
            "Vyber zvukový soubor a appka z něj rozpozná noty. Výsledek se otevře jako nová skladba - můžeš si ji přehrát, "
            + "procvičit v režimu učení nebo uložit jako MIDI / MusicXML (Soubor > Uložit skladbu jako...).",
            "Pick an audio file and the app finds the notes in it. The result opens as a new song - play it, practise it in "
            + "learning mode or save it as MIDI / MusicXML (File > Save song as...).")));
        layout.Controls.Add(Paragraph(T(
            "Bez doplňku rozpozná jednu notu naráz (nejlíp stopa s melodií - zpěv, sólo, basa). S doplňkem pro rozpoznávání "
            + "akordů (nabídne se ke stažení) pozná i víc not najednou, třeba piano s akordy. Celý mix skladby ale spolehlivě ne.",
            "Without the add-on it hears one note at a time (best: a track with a melody - vocals, a solo, bass). With the "
            + "chord detection add-on (offered for download) it hears several notes at once, e.g. piano with chords. A full song mix still not reliably."), gray: true));
        notesButton.Text = T("Vybrat zvukový soubor...", "Choose audio file...");
        notesButton.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(notesButton);
        layout.Controls.Add(progress);
        layout.Controls.Add(statusLabel);

        layout.Controls.Add(Heading(T("Jak dostat stopu z FL Studia", "Getting a track out of FL Studio")));
        layout.Controls.Add(Paragraph(T(
            "Export > WAV File > Miscellaneous > \"Split mixer tracks\" vyexportuje každou stopu mixéru jako samostatný .wav.",
            "Export > WAV File > Miscellaneous > \"Split mixer tracks\" exports every mixer track as its own .wav.")));

        layout.Controls.Add(Heading(T("Kde sehnat jednotlivé stopy skladeb (stems)", "Where to get a song's separate tracks (stems)")));
        layout.Controls.Add(Paragraph(T("Oficiální a legální:", "Official and legal:"), gray: true));
        layout.Controls.Add(Link("SKIO Music", "https://skiomusic.com/",
            T("oficiální remixové soutěže s labely (Sony, Universal), stopy v plné kvalitě", "official remix contests with labels (Sony, Universal), full-quality stems")));
        layout.Controls.Add(Link("Beatport", "https://www.beatport.com/",
            T("remixové soutěže elektronických labelů s oficiálními stopami", "electronic labels' remix contests with official stems")));
        layout.Controls.Add(Link("Cambridge Music Technology", "https://www.cambridge-mt.com/ms/mtk/",
            T("600+ skladeb rozložených na stopy, zdarma na cvičení", "600+ songs split into tracks, free for practice")));
        layout.Controls.Add(Paragraph(T("Komunitní (často neoficiální - pozor na autorská práva):", "Community (often unofficial - mind the copyright):"), gray: true));
        layout.Controls.Add(Link("Remix Packs", "https://remixpacks.net/",
            T("velký katalog sdílených stopových balíčků", "a large catalogue of shared stem packs")));
        layout.Controls.Add(Link("SongStems", "https://songstems.net/",
            T("komunitní databáze stop a instrumentálů", "a community database of stems and instrumentals")));

        var close = new AntdUI.Button { Text = T("Zavřít", "Close"), AutoSize = true, Radius = 8 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.Add(close);

        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));

        accompanimentButton.Click += (_, _) => PickStemsFolder();
        notesButton.Click += async (_, _) => await PickAndConvert();
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    void PickStemsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T("Vyber složku se stopami (.wav)", "Choose the folder with the stems (.wav)"),
            InitialDirectory = initialDirectory ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        StemsFolder = dialog.SelectedPath;
        DialogResult = DialogResult.OK;
        Close();
    }

    async Task PickAndConvert()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = AudioTranscriber.FileFilter,
            Title = T("Vybrat zvukový soubor", "Choose audio file"),
            InitialDirectory = initialDirectory ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string path = dialog.FileName;
        running = new CancellationTokenSource();
        var cancel = running.Token;
        accompanimentButton.Enabled = notesButton.Enabled = false;
        await ChordAddonPrompt.OfferAsync(this, config, s => statusLabel.Text = s);
        progress.Value = 0;
        progress.Visible = true;
        statusLabel.Text = T($"Převádím {Path.GetFileName(path)}...", $"Converting {Path.GetFileName(path)}...");
        var report = new Progress<double>(p => progress.Value = (float)p);
        try
        {
            var score = await Task.Run(() => AudioTranscriber.TranscribeBest(path, report, cancel), cancel);
            int count = score.Parts.Sum(p => p.Notes.Count);
            if (count == 0)
            {
                statusLabel.Text = T("V souboru jsem nenašel žádné noty.", "No notes found in the file.");
                return;
            }
            Result = score;
            FilePath = path;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            // Closed while converting.
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            statusLabel.Text = T("Soubor nejde přečíst: ", "Could not read the file: ") + ex.Message;
        }
        finally
        {
            running = null;
            if (!IsDisposed)
            {
                accompanimentButton.Enabled = notesButton.Enabled = true;
                progress.Visible = false;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        running?.Cancel();
        base.OnFormClosing(e);
    }

    static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 18, 0, 4),
    };

    static Label Paragraph(string text, bool gray = false) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(570, 0),
        Font = new Font("Segoe UI", 9f),
        ForeColor = gray ? SystemColors.GrayText : SystemColors.ControlText,
        Margin = new Padding(0, 4, 0, 2),
    };

    /// <summary>"Name - what it is", where the name opens the site in the browser.</summary>
    static Control Link(string name, string url, string description)
    {
        var link = new LinkLabel
        {
            Text = $"{name} - {description}",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(12, 2, 0, 2),
            LinkArea = new LinkArea(0, name.Length),
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }
}
