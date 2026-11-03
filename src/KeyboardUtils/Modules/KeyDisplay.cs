using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardUtils;

/// <summary>
/// Turns a key id (same scan-code-plus-flags encoding MacroKeyboard uses) into a human-readable name.
/// Duplicated here rather than referencing the MacroKeyboard package: the bindings list must display key
/// names even before that module is downloaded, and this is pure Win32 formatting - nothing to do with
/// capturing keys, so it doesn't belong behind the optional-module boundary.
/// </summary>
static class KeyDisplay
{
    const int E0 = 0x100;
    const int E1 = 0x200;

    public static string Describe(int key)
    {
        int scanCode = key & 0xFF;
        if ((key & E1) != 0)
            return scanCode == 0x1D ? "Pause" : $"E1 {scanCode:X2}";

        int lParam = (scanCode << 16) | ((key & E0) != 0 ? 1 << 24 : 0);
        var sb = new StringBuilder(64);
        int length = GetKeyNameTextW(lParam, sb, sb.Capacity);
        return length > 0 ? sb.ToString() : $"Scan {((key & E0) != 0 ? "E0 " : "")}{scanCode:X2}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetKeyNameTextW(int lParam, StringBuilder buffer, int size);
}
