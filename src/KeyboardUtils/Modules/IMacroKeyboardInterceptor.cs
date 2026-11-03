namespace KeyboardUtils;

/// <summary>
/// Mirrors MacroKeyboard.MacroKeyboardTarget without referencing that assembly - MainForm, AppConfig and
/// KeyboardPickerForm must never touch MacroKeyboard types directly, since that assembly might not be
/// downloaded yet. See Modules/MacroKeyboardFactory.cs for why.
/// </summary>
sealed record MacroTarget(string HardwareId, int Device);

/// <summary>Local mirror of MacroKeyboard.KeyboardInterceptor's public surface - same reasoning as <see cref="MacroTarget"/>.</summary>
interface IMacroKeyboardInterceptor : IDisposable
{
    bool Enabled { get; set; }
    MacroTarget? Target { get; set; }

    event Action<int>? MacroKeyDown;
    event Action<MacroTarget>? DeviceLearned;
    event Action<MacroTarget>? DeviceMoved;

    void StartLearning();
    void CancelLearning();
}
