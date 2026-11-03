namespace KeyboardUtils;

/// <summary>
/// The ONLY place in Keyboard Utils allowed to reference MacroKeyboard types directly. Never call anything
/// here unless MacroKeyboardModule.IsInstalled is true - that's the caller's job to check, since by the time
/// a method here is actually invoked (JIT-compiled), the assembly must already resolve successfully.
/// </summary>
static class MacroKeyboardFactory
{
    /// <summary>Returns null if the Interception driver isn't installed/loaded - the caller should offer to install it.</summary>
    public static IMacroKeyboardInterceptor? TryCreate()
    {
        try
        {
            return new InterceptorAdapter(MacroKeyboard.KeyboardInterceptor.Create());
        }
        catch (MacroKeyboard.InterceptionUnavailableException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    sealed class InterceptorAdapter : IMacroKeyboardInterceptor
    {
        readonly MacroKeyboard.KeyboardInterceptor inner;

        public InterceptorAdapter(MacroKeyboard.KeyboardInterceptor inner)
        {
            this.inner = inner;
            inner.DeviceLearned += t => DeviceLearned?.Invoke(new MacroTarget(t.HardwareId, t.Device));
            inner.DeviceMoved += t => DeviceMoved?.Invoke(new MacroTarget(t.HardwareId, t.Device));
        }

        public bool Enabled
        {
            get => inner.Enabled;
            set => inner.Enabled = value;
        }

        public MacroTarget? Target
        {
            get => inner.Target is { } t ? new MacroTarget(t.HardwareId, t.Device) : null;
            set => inner.Target = value is { } t ? new MacroKeyboard.MacroKeyboardTarget(t.HardwareId, t.Device) : null;
        }

        public event Action<int>? MacroKeyDown
        {
            add => inner.MacroKeyDown += value;
            remove => inner.MacroKeyDown -= value;
        }

        public event Action<MacroTarget>? DeviceLearned;
        public event Action<MacroTarget>? DeviceMoved;

        public void StartLearning() => inner.StartLearning();
        public void CancelLearning() => inner.CancelLearning();
        public void Dispose() => inner.Dispose();
    }
}
