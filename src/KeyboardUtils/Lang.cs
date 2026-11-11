global using static KeyboardUtils.Lang;

namespace KeyboardUtils;

enum AppLanguage
{
    Czech,
    English,
}

/// <summary>UI language. Texts are written inline as <c>T("česky", "English")</c>.</summary>
static class Lang
{
    public static AppLanguage Current { get; set; }

    public static string T(string czech, string english) => Current == AppLanguage.English ? english : czech;

    /// <summary>
    /// AntdUI.Modal.open's OK/Cancel buttons default to AntdUI's built-in Chinese text when not told
    /// otherwise - this wrapper always supplies localized button text.
    /// </summary>
    public static void ShowModal(Form target, string title, string content, AntdUI.TType type) =>
        AntdUI.Modal.open(new AntdUI.Modal.Config(target, title, content, type)
        {
            OkText = T("OK", "OK"),
            CancelText = T("Zrušit", "Cancel"),
        });

    /// <summary>A yes/no question; true when answered OK.</summary>
    public static bool Confirm(Form target, string title, string content, string okText) =>
        AntdUI.Modal.open(new AntdUI.Modal.Config(target, title, content, AntdUI.TType.Warn)
        {
            OkText = okText,
            CancelText = T("Zrušit", "Cancel"),
        }) == DialogResult.OK;

    /// <summary>Display names, deliberately not translated so each is readable to its own speakers.</summary>
    public static string DisplayName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        _ => "Čeština",
    };
}

/// <summary>Asked once on first start, before anything else is shown.</summary>
sealed class LanguagePickerForm : AntdUI.Window
{
    public AppLanguage? Result { get; private set; }

    public LanguagePickerForm()
    {
        Text = "Keyboard Utils";
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 210); // minimum; grown to fit in Load
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var prompt = new AntdUI.Label
        {
            Text = "Vyber jazyk / Choose language",
            AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Margin = new Padding(20, 28, 20, 16),
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(16, 0, 16, 20), Margin = new Padding(0) };
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var button = new AntdUI.Button
            {
                Text = DisplayName(language),
                Size = new Size(150, 44),
                Font = new Font("Segoe UI", 11f),
                Type = AntdUI.TTypeMini.Primary,
                Radius = 8,
                Margin = new Padding(6),
            };
            button.Click += (_, _) => { Result = language; DialogResult = DialogResult.OK; Close(); };
            buttons.Controls.Add(button);
        }

        // Light or dark right away - before anything else is seen (it starts as Windows has it).
        var theme = new AntdUI.Button
        {
            Size = new Size(240, 34),
            Font = new Font("Segoe UI", 10f),
            Radius = 8,
            Margin = new Padding(0, 0, 0, 18),
        };
        void ShowTheme() => theme.Text = AppTheme.Dark ? "☀  Světlý režim / Light mode" : "🌙  Tmavý režim / Dark mode";
        theme.Click += (_, _) =>
        {
            AppTheme.SetDark(!AppTheme.Dark);
            ShowTheme();
        };
        ShowTheme();

        // WrapContents=false: a wrapping TopDown panel that runs out of height moves the buttons into a second
        // column to the right, clipped by the window edge.
        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        layout.Controls.AddRange([prompt, buttons, theme]);
        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(titleBar);

        // Fit the window to its content after DPI scaling rather than guessing a fixed size.
        Load += (_, _) =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(ClientSize.Width, content.Width), titleBar.Height + content.Height);
            // The window can be wider than its content: centre the text and the buttons in it.
            int inner = ClientSize.Width - layout.Padding.Horizontal;
            prompt.Margin = new Padding(Math.Max(0, (inner - prompt.Width) / 2), prompt.Margin.Top, 0, prompt.Margin.Bottom);
            var row = buttons.GetPreferredSize(Size.Empty);
            buttons.Margin = new Padding(Math.Max(0, (inner - row.Width) / 2), 0, 0, 0);
            theme.Margin = new Padding(Math.Max(0, (inner - theme.Width) / 2), 0, 0, theme.Margin.Bottom);
            CenterToScreen();
        };
    }
}
