using System.Runtime.InteropServices;

namespace KeyboardUtils;

/// <summary>The bits of Windows' foreground rules the app needs (see <c>MainForm.ComeToFront</c>).</summary>
static class WindowFront
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    public static IntPtr Foreground => GetForegroundWindow();

    /// <summary>Flashes the taskbar button until the window is activated.</summary>
    public static void Flash(IntPtr window)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = window,
            dwFlags = 0x3 /* FLASHW_ALL */ | 0xC /* FLASHW_TIMERNOFG */,
        };
        FlashWindowEx(ref info);
    }

    /// <summary>
    /// Called by a second start (from a JáMuzikant link the user just clicked, so it may take the front) before it hands
    /// the link over: lets the running app bring its window to the front instead of only flashing.
    /// </summary>
    public static void LetOthersComeToFront() => AllowSetForegroundWindow(-1 /* ASFW_ANY */);
}
