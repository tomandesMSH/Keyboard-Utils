namespace KeyboardUtils;

/// <summary>
/// Edit > Macro keyboard: what used to be its tab - the keyboard to listen to and its keys' actions. The controls
/// stay the main window's (it keeps driving them); this window only shows them. Closing hides it.
/// </summary>
sealed class MacroKeyboardForm : AntdUI.Window
{
    public MacroKeyboardForm(Control content)
    {
        Text = T("Makro klávesnice", "Macro keyboard");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 480);
        MinimumSize = new Size(520, 320);
        content.Dock = DockStyle.Fill;
        Controls.Add(content);
        Controls.Add(new TitleBar(this));
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }
}
