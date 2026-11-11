namespace KeyboardUtils;

/// <summary>
/// "What's new": the release notes of this version, from GitHub - shown once after a one-click update, and any time
/// from Help > What's new. Shown as read-only text (<see cref="ReleaseNotes"/>); nothing in it can be clicked or run.
/// </summary>
sealed class WhatsNewForm : AntdUI.Window
{
    /// <summary>Takes its colours from <see cref="AppTheme"/> itself - recolouring would flip the dark ones back to light.</summary>
    sealed class NotesBox : RichTextBox, IThemed;

    sealed class ThemedPanel : Panel, IThemed;

    readonly NotesBox text = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        DetectUrls = false,
        TabStop = false,
        ShortcutsEnabled = true,
    };

    /// <param name="markdown">The notes; null when they couldn't be loaded (offline).</param>
    public WhatsNewForm(Version version, string? markdown)
    {
        Text = T($"Co je nového ve verzi {version.ToString(3)}", $"What's new in version {version.ToString(3)}");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 520);
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        text.BackColor = AppTheme.Window;
        text.ForeColor = AppTheme.Text;
        Fill(ReleaseNotes.ToBlocks(markdown));

        var textBox = new ThemedPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 16, 8), BackColor = AppTheme.Window };
        textBox.Controls.Add(text);

        var ok = new AntdUI.Button { Text = "OK", Size = new Size(110, 36), Type = AntdUI.TTypeMini.Primary, Radius = 8 };
        ok.Click += (_, _) => Close();
        var page = new AntdUI.Button { Text = T("Celé poznámky na GitHubu", "Full notes on GitHub"), AutoSize = true, Radius = 8 };
        page.Click += (_, _) => AboutForm.Open(UpdateChecker.ReleasePage(version));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([ok, page]);

        Controls.Add(textBox);
        Controls.Add(buttons);
        Controls.Add(new TitleBar(this));
        AcceptButton = ok;
        CancelButton = ok;
    }

    void Fill(List<NoteBlock> blocks)
    {
        var regular = new Font("Segoe UI", 10.5f);
        var bold = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        var heading = new Font("Segoe UI", 13f, FontStyle.Bold);
        if (blocks.Count == 0)
        {
            Append(T("Novinky se nepodařilo načíst - nejsi offline? Celé poznámky jsou na GitHubu (tlačítko dole).",
                "Couldn't load what's new - are you offline? The full notes are on GitHub (button below)."), regular, AppTheme.SubtleText);
            return;
        }
        bool first = true;
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case NoteBlockKind.Heading:
                    if (!first)
                        Append("\n", regular);
                    Append(block.Text + "\n", heading);
                    break;
                case NoteBlockKind.Bullet:
                    Append("  •  ", regular, AppTheme.SubtleText);
                    foreach (var span in block.Spans)
                        Append(span.Text, span.Bold ? bold : regular);
                    Append("\n", regular);
                    break;
                default:
                    foreach (var span in block.Spans)
                        Append(span.Text, span.Bold ? bold : regular);
                    Append("\n", regular);
                    break;
            }
            first = false;
        }
        text.SelectionStart = 0;
        text.ScrollToCaret();
    }

    void Append(string value, Font font, Color? color = null)
    {
        text.SelectionStart = text.TextLength;
        text.SelectionLength = 0;
        text.SelectionFont = font;
        text.SelectionColor = color ?? AppTheme.Text;
        text.AppendText(value);
    }

    /// <summary>Loads this version's notes and shows them; offline it says so (with the GitHub button).</summary>
    /// <param name="onlyWithNotes">Right after an update: if the notes can't be loaded, show nothing at all.</param>
    public static async Task ShowAsync(Form owner, bool onlyWithNotes = false)
    {
        var version = UpdateChecker.CurrentVersion;
        string? notes = await UpdateChecker.ReleaseNotesAsync(version);
        if (owner.IsDisposed || (onlyWithNotes && notes == null))
            return;
        using var form = new WhatsNewForm(version, notes);
        form.ShowDialog(owner);
    }
}
