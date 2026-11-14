using System.Diagnostics;

namespace KeyboardUtils;

/// <summary>Help > About: the logo, name, version, licence and author on the left, links on the right.</summary>
sealed class AboutForm : AntdUI.Window
{
    public const string SourceUrl = "https://github.com/tomandesMSH/Keyboard-Utils";
    /// <summary>
    /// Help > Report a bug: the report form on tomandes.eu (no GitHub account needed), with the app and its version
    /// filled in. It files the report as an issue in this repository.
    /// </summary>
    public static string IssuesUrl => $"https://tomandes.eu/report/?app=keyboard-utils&version={UpdateChecker.CurrentVersion.ToString(3)}";
    const string AuthorUrl = "https://tomandes.eu";
    const string WebUrl = "https://tomandes.eu/Keyboard-Utils/";
    const string DocsUrl = "https://tomandes.eu/Keyboard-Utils/docs/";

    public AboutForm()
    {
        Text = T("O aplikaci", "About");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 330);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        // Left: who and what.
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 18, 12, 12) };
        var logo = new PictureBox { Size = new Size(72, 72), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 10) };
        using (var large = new Icon(AppIcon.Icon, 128, 128))
            logo.Image = large.ToBitmap();
        var name = new Label { Text = "Keyboard Utils", AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var version = new Label { Text = T("Verze ", "Version ") + UpdateChecker.CurrentVersion.ToString(3), AutoSize = true, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 0, 0, 10) };
        var license = new Label
        {
            Text = T("© 2026 tomandesMSH · licence MIT", "© 2026 tomandesMSH · MIT licence"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 4),
        };
        var made = new MadeWith { Margin = new Padding(3, 0, 0, 0) }; // lines up with the labels above (their text sits 3 px in)
        info.Controls.AddRange([logo, name, version, license, made]);

        // Right: OK and the links, one under the other.
        var links = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 210, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 18, 20, 12) };
        var ok = new AntdUI.Button { Text = "OK", Size = new Size(186, 36), Type = AntdUI.TTypeMini.Primary, Radius = 8, Margin = new Padding(0, 0, 0, 10) };
        ok.Click += (_, _) => Close();
        links.Controls.Add(ok);
        foreach (var (text, url) in new[]
        {
            (T("Zdrojový kód aplikace", "Source code"), SourceUrl),
            (T("O autorovi", "About the author"), AuthorUrl),
            (T("Web", "Website"), WebUrl),
            (T("Dokumentace", "Documentation"), DocsUrl),
        })
        {
            var button = new AntdUI.Button { Text = text, Size = new Size(186, 34), Radius = 8, Margin = new Padding(0, 0, 0, 6) };
            button.Click += (_, _) => Open(url);
            links.Controls.Add(button);
        }

        Controls.Add(info);
        Controls.Add(links);
        Controls.Add(new TitleBar(this));
        AcceptButton = ok;
        CancelButton = ok;
    }

    /// <summary>
    /// "made with ♥ by tomandesmsh", the heart red - drawn as one line (separate labels leave gaps around the heart;
    /// an emoji ❤️ comes out as a box in WinForms text).
    /// </summary>
    sealed class MadeWith : Control
    {
        static readonly (string Text, Color? Color)[] Parts = [("made with ", null), ("♥", Color.FromArgb(225, 68, 50)), (" by tomandesmsh", null)];
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public MadeWith()
        {
            Font = new Font("Segoe UI", 9f);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            ForeColor = SystemColors.GrayText;
            Size = new Size(Parts.Sum(p => TextRenderer.MeasureText(p.Text, Font, Size.Empty, Flags).Width) + 2,
                TextRenderer.MeasureText("Mg", Font, Size.Empty, Flags).Height + 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int x = 0;
            foreach (var (text, color) in Parts)
            {
                TextRenderer.DrawText(e.Graphics, text, Font, new Point(x, 1), color ?? ForeColor, Flags);
                x += TextRenderer.MeasureText(e.Graphics, text, Font, Size.Empty, Flags).Width;
            }
        }
    }

    /// <summary>Opens <paramref name="url"/> in the default browser.</summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(url, "Keyboard Utils");
        }
    }
}
