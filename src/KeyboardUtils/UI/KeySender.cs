using System.Runtime.InteropServices;

namespace KeyboardUtils;

/// <summary>
/// Computer keys sent from macro keys (Enter, Esc, Tab, arrows, Space), so dialogs can be answered without touching
/// the keyboard or the mouse. When the app is the foreground app, a real key press is simulated (SendInput) - that
/// reaches everything, Windows' own file dialogs and message boxes too. Otherwise (the app is behind another window,
/// and Windows won't let it take the focus) the key goes straight to the app's topmost window.
/// </summary>
static class KeySender
{
    /// <param name="key">A key code, optionally with <see cref="Keys.Shift"/>.</param>
    public static void Send(Form main, Keys key)
    {
        if (OwnsForeground())
            SendInputKey(key);
        else
            SendInProcess(TopWindow(main), key);
    }

    /// <summary>The dialog open over the main window (the last one opened), or the main window.</summary>
    public static Form TopWindow(Form main) =>
        Application.OpenForms.Cast<Form>().LastOrDefault(f => f != main && f.Visible && f.Modal) ?? main;

    static bool OwnsForeground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(window, out uint process);
        return process == Environment.ProcessId;
    }

    static void SendInProcess(Form form, Keys key)
    {
        var code = key & Keys.KeyCode;
        bool shift = (key & Keys.Shift) != 0;
        var focused = Focused(form);
        switch (code)
        {
            case Keys.Tab:
                form.SelectNextControl(focused ?? form, forward: !shift, tabStopOnly: true, nested: true, wrap: true);
                return;
            case Keys.Enter when focused is IButtonControl button:
                button.PerformClick();
                return;
            case Keys.Enter when form.AcceptButton is { } accept:
                accept.PerformClick();
                return;
            case Keys.Escape when form.CancelButton is { } cancel:
                cancel.PerformClick();
                return;
        }
        // The rest as key messages to the focused control. Posted, they go through the message loop like real keys,
        // so the window's shortcuts (ProcessCmdKey) see them too.
        var handle = (focused ?? form).Handle;
        PostMessage(handle, WM_KEYDOWN, (IntPtr)(int)code, IntPtr.Zero);
        PostMessage(handle, WM_KEYUP, (IntPtr)(int)code, (IntPtr)unchecked((int)0xC0000001));
    }

    static Control? Focused(Form form)
    {
        Control? control = form.ActiveControl;
        while (control is ContainerControl { ActiveControl: { } inner })
            control = inner;
        return control;
    }

    static void SendInputKey(Keys key)
    {
        var inputs = new List<INPUT>();
        bool shift = (key & Keys.Shift) != 0;
        if (shift)
            inputs.Add(KeyInput(Keys.ShiftKey, up: false));
        inputs.Add(KeyInput(key & Keys.KeyCode, up: false));
        inputs.Add(KeyInput(key & Keys.KeyCode, up: true));
        if (shift)
            inputs.Add(KeyInput(Keys.ShiftKey, up: true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    static INPUT KeyInput(Keys key, bool up)
    {
        // Arrows are "extended" keys: without the flag they'd come out as the numpad's 8 / 2 / 4 / 6.
        bool extended = key is Keys.Up or Keys.Down or Keys.Left or Keys.Right;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key,
                    dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                },
            },
        };
    }

    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Sized like the largest member (MOUSEINPUT), as SendInput checks the struct size.
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
