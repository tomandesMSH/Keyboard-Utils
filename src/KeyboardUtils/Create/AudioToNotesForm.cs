using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>
/// "Convert audio to notes": pick an audio file, transcribe it in the background with progress, and hand back the
/// resulting <see cref="Score"/>. Also the place for tips on getting single-instrument tracks (stems) to convert.
/// </summary>
sealed class AudioToNotesForm : AntdUI.Window
{
    readonly AntdUI.Button pickButton = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Progress progress = new() { Size = new Size(560, 14), Margin = new Padding(0, 10, 0, 0), Visible = false };
    readonly Label statusLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = SystemColors.GrayText, Font = new Font("Segoe UI", 9f) };
    readonly string? initialDirectory;
    CancellationTokenSource? running;

    /// <summary>The converted notes, once the dialog closed with OK.</summary>
    public Score? Result { get; private set; }

    /// <summary>The audio file <see cref="Result"/> came from.</summary>
    public string? FilePath { get; private set; }

    /// <param name="initialDirectory">Where the file picker starts, e.g. the folder of the loaded stems.</param>
    public AudioToNotesForm(string? initialDirectory)
    {
        this.initialDirectory = initialDirectory;
        Text = T("Převést zvuk na noty", "Convert audio to notes");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 640);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(22, 14, 22, 8),
        };
        layout.Controls.Add(Paragraph(T(
            "Vyber zvukový soubor a appka z něj rozpozná noty. Výsledek se otevře jako nová skladba - můžeš si ji přehrát, "
            + "procvičit v režimu učení nebo uložit jako MIDI / MusicXML (Soubor > Uložit skladbu jako...).",
            "Pick an audio file and the app finds the notes in it. The result opens as a new song - play it, practise it in "
            + "learning mode or save it as MIDI / MusicXML (File > Save song as...).")));
        layout.Controls.Add(Paragraph(T(
            "Rozpozná jednu notu naráz: nejlíp funguje stopa s jedním nástrojem a melodií (zpěv, sólo, basa). "
            + "Akordy jen částečně, celý mix skladby ne.",
            "It hears one note at a time: a track with a single instrument playing a melody works best (vocals, a solo, "
            + "bass). Chords only partly, a full song mix not at all."), gray: true));

        pickButton.Text = T("Vybrat zvukový soubor...", "Choose audio file...");
        pickButton.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(pickButton);
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

        pickButton.Click += async (_, _) => await PickAndConvert();
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
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
        pickButton.Enabled = false;
        progress.Value = 0;
        progress.Visible = true;
        statusLabel.Text = T($"Převádím {Path.GetFileName(path)}...", $"Converting {Path.GetFileName(path)}...");
        var report = new Progress<double>(p => progress.Value = (float)p);
        try
        {
            var score = await Task.Run(() => AudioTranscriber.Transcribe(path, report, cancel), cancel);
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
                pickButton.Enabled = true;
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
