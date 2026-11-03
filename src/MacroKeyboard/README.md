# MacroKeyboard

Identifies and intercepts input from a second ("macro") keyboard via the [Interception](https://github.com/oblitum/Interception)
driver. This way, you can use one of your keyboards as a dedicated controller for your app - triggering actions
instead of typing - while your other keyboard keeps working normally in Windows.

Extracted from [SoundpadTranslator](https://github.com/tomandesMSH/SoundpadTranslator) and
[Keyboard Utils](https://github.com/tomandesMSH/Keyboard-Utils) so the same code can be reused by both
(and any future app).

## Usage

```csharp
using MacroKeyboard;

KeyboardInterceptor interceptor;
try
{
    interceptor = KeyboardInterceptor.Create();
}
catch (InterceptionUnavailableException)
{
    // The Interception driver isn't installed/loaded - offer to install it (see tools/install-interception.exe
    // in SoundpadTranslator/Keyboard Utils for a reference install dialog).
    return;
}

interceptor.MacroKeyDown += key => Console.WriteLine($"Pressed: {KeyNames.Describe(key)}");

// "Press a key on the keyboard you want to use as the macro keyboard":
interceptor.DeviceLearned += target => interceptor.Target = target;
interceptor.StartLearning();
```

## Notes

- Interception is a kernel-level driver. Anti-cheats like Vanguard (Valorant) or FACEIT may block or flag it.
- Interception has 10 keyboard slots. After enough USB unplug/replug cycles, it can stop picking up new
  devices until you restart the computer.
- Interception (oblitum) is LGPL 3.0, non-commercial use only - see `Interception-LGPL-3.0.txt`. This package
  (`MacroKeyboard`) itself is MIT, but depends on `interception.dll`, which carries that LGPL license.
