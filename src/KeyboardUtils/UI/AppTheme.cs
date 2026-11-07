using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KeyboardUtils;

/// <summary>
/// Light / dark colours for the whole app. AntdUI's own controls follow <see cref="AntdUI.Config.Mode"/>; plain
/// WinForms controls and our custom-drawn ones are recoloured here. Windows built with a <see cref="TitleBar"/>
/// are themed automatically (it calls <see cref="Attach"/>).
/// </summary>
static class AppTheme
{
    public static bool Dark { get; private set; }

    /// <summary>Raised on the UI thread after the mode changed; custom-drawn controls repaint in it.</summary>
    public static event Action? Changed;

    public static void SetDark(bool dark)
    {
        if (Dark == dark && AntdUI.Config.Mode == (dark ? AntdUI.TMode.Dark : AntdUI.TMode.Light))
            return;
        Dark = dark;
        AntdUI.Config.Mode = dark ? AntdUI.TMode.Dark : AntdUI.TMode.Light;
        foreach (var form in Application.OpenForms.Cast<Form>().ToList())
            ApplyTo(form);
        Changed?.Invoke();
    }

    // --- Palette --------------------------------------------------------------------------------

    public static Color Window => Dark ? Color.FromArgb(24, 24, 27) : Color.White;
    public static Color Surface => Dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(248, 248, 248);
    public static Color Text => Dark ? Color.FromArgb(228, 228, 231) : Color.FromArgb(31, 31, 31);
    public static Color SubtleText => Dark ? Color.FromArgb(150, 150, 158) : SystemColors.GrayText;
    public static Color Hover => Dark ? Color.FromArgb(52, 52, 58) : Color.FromArgb(232, 232, 232);
    public static Color Pressed => Dark ? Color.FromArgb(66, 66, 74) : Color.FromArgb(218, 218, 218);

    /// <summary>The thick line under the toolbar that sets the song view apart from it.</summary>
    public static Color Separator => Dark ? Color.FromArgb(110, 110, 120) : Color.Black;

    /// <summary>Sheet music: paper and ink.</summary>
    public static Color Paper => Dark ? Color.FromArgb(28, 28, 32) : Color.White;
    public static Color Ink => Dark ? Color.FromArgb(225, 225, 230) : Color.FromArgb(20, 20, 20);
    public static Color StaffLine => Dark ? Color.FromArgb(120, 120, 128) : Color.FromArgb(70, 70, 70);

    // --- Recolouring plain WinForms controls ------------------------------------------------------

    /// <summary>The light-mode colours a control was built with, captured the first time it's themed.</summary>
    sealed record Original(Color Back, bool OwnBack, Color Fore, bool OwnFore);

    static readonly ConditionalWeakTable<Control, Original> originals = new();

    /// <summary>Themes <paramref name="form"/> now and again whenever it gains controls (rebuilt panels, dialogs filled late).</summary>
    public static void Attach(Form form)
    {
        if (form.IsHandleCreated)
            ApplyTo(form);
        else
            form.HandleCreated += (_, _) => ApplyTo(form);
        WatchForNewControls(form);
    }

    static void WatchForNewControls(Control control)
    {
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is not { } added)
                return;
            WatchForNewControls(added);
            // In light mode there's nothing to do: the light colours are what controls are built with.
            if (Dark)
                ApplyTo(added);
        };
        foreach (Control child in control.Controls)
            WatchForNewControls(child);
    }

    /// <summary>
    /// Recolours <paramref name="root"/> and everything in it. Only colours a control sets itself are touched (the
    /// rest come from its parent anyway): light backgrounds get dark, dark text gets light, colourful accents stay.
    /// </summary>
    public static void ApplyTo(Control root)
    {
        Remember(root);
        Recolour(root);
    }

    static void Remember(Control control)
    {
        if (!originals.TryGetValue(control, out _))
        {
            var parent = control.Parent;
            bool ownBack = parent == null || control.BackColor != parent.BackColor || control is ListView or TextBoxBase or ListBox;
            bool ownFore = parent == null || control.ForeColor != parent.ForeColor;
            originals.Add(control, new Original(control.BackColor, ownBack, control.ForeColor, ownFore));
        }
        foreach (Control child in control.Controls)
            Remember(child);
    }

    static void Recolour(Control control)
    {
        // Custom-drawn controls pick their colours from the palette themselves.
        if (control is IThemed)
            return;
        bool antd = control.GetType().Namespace?.StartsWith("AntdUI") == true;
        if (originals.TryGetValue(control, out var original))
        {
            if (control is Form form)
            {
                form.BackColor = Dark ? Window : original.Back;
                form.ForeColor = Dark ? Text : original.Fore;
            }
            else
            {
                // AntdUI draws its controls from its own theme; only its labels' explicit text colours need help.
                if (!antd && original.OwnBack && original.Back.A > 0)
                    control.BackColor = Dark ? ToDark(original.Back) : original.Back;
                if ((!antd || control is AntdUI.Label) && original.OwnFore)
                    control.ForeColor = Dark ? ToDark(original.Fore) : original.Fore;
                if (control is LinkLabel link)
                    link.LinkColor = Dark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(0, 0, 255);
                if (control is ListView list)
                    ThemeNative(list);
            }
        }
        foreach (Control child in control.Controls)
            Recolour(child);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    const int LVM_GETHEADER = 0x101F;

    /// <summary>A list view's scrollbars and column headers are drawn by Windows: switch them to its dark style.</summary>
    static void ThemeNative(ListView list)
    {
        void Apply()
        {
            // Null, null drops the association again: back to the default (light) look.
            SetWindowTheme(list.Handle, Dark ? "DarkMode_Explorer" : null, null);
            var header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero)
                SetWindowTheme(header, Dark ? "DarkMode_ItemsView" : null, null);
        }
        if (list.IsHandleCreated)
            Apply();
        else if (Dark)
            list.HandleCreated += (_, _) => Apply();
    }

    /// <summary>
    /// Flips a greyish colour's lightness (white -> near black, black -> near white); coloured ones stay. System
    /// colours (Window, ControlText, GrayText...) are fixed rather than flipped: switching AntdUI to dark switches
    /// WinForms' system colours to their dark versions too, so flipping those would turn them light again.
    /// </summary>
    public static Color ToDark(Color color)
    {
        if (color.IsSystemColor)
            return color.ToKnownColor() switch
            {
                KnownColor.Window or KnownColor.Control or KnownColor.ControlLight or KnownColor.ButtonFace => Window,
                KnownColor.GrayText => SubtleText,
                _ => Text,
            };
        if (color.A == 0 ||color.GetSaturation() > 0.35f && color.GetBrightness() is > 0.15f and < 0.92f)
            return color;
        float lightness = 1 - color.GetBrightness();
        int value = (int)Math.Round(255 * (0.09 + lightness * 0.83));
        return Color.FromArgb(color.A, value, value, Math.Min(255, value + 3));
    }
}

/// <summary>A custom-drawn control that reads <see cref="AppTheme"/>'s palette itself and must not be recoloured.</summary>
interface IThemed;
