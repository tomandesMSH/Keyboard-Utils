namespace KeyboardUtils;

/// <summary>A solid line in <see cref="AppTheme.Separator"/> - the thick rule under the song toolbar.</summary>
sealed class SeparatorLine : Control, IThemed
{
    public SeparatorLine()
    {
        BackColor = AppTheme.Separator;
        TabStop = false;
        AppTheme.Changed += OnThemeChanged;
    }

    void OnThemeChanged() => BackColor = AppTheme.Separator;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

/// <summary>
/// A split container whose divider is drawn in <see cref="AppTheme.Separator"/>. Its two halves hold custom-drawn
/// controls that theme themselves, so the generic recolouring leaves the whole thing alone.
/// </summary>
sealed class ThemedSplitContainer : SplitContainer, IThemed
{
    public ThemedSplitContainer()
    {
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;
    }

    void ApplyTheme()
    {
        BackColor = AppTheme.Separator;
        Panel1.BackColor = Panel2.BackColor = AppTheme.Window;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppTheme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }
}
