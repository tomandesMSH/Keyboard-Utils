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
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 210);
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
        var buttons = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(16, 0, 16, 20), Margin = new Padding(0) };
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

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        layout.Controls.AddRange([prompt, buttons]);
        Controls.Add(layout);
        Controls.Add(new TitleBar(this));
    }
}
