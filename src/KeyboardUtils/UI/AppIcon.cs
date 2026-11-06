namespace KeyboardUtils;

/// <summary>The app's piano-keys icon (Resources/app.ico), for every window's title bar and taskbar button.</summary>
static class AppIcon
{
    static Icon? icon;

    /// <summary>Loaded once and shared; windows must not dispose it.</summary>
    public static Icon Icon => icon ??= Load();

    static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("KeyboardUtils.app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }
}
