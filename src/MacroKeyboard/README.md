# MacroKeyboard

Identifikuje a zachytává vstup z druhé ("makro") klávesnice přes ovladač [Interception](https://github.com/oblitum/Interception),
takže její klávesy jdou ovládat appku místo psaní do Windows. První klávesnice funguje normálně.

Vytaženo ze [SoundpadTranslatoru](https://github.com/tomandesMSH/SoundpadTranslator) a [Keyboard Utils](https://github.com/tomandesMSH/Keyboard-Utils),
aby šlo stejný kód použít v obou (a dalších budoucích appkách).

## Použití

```csharp
using MacroKeyboard;

KeyboardInterceptor interceptor;
try
{
    interceptor = KeyboardInterceptor.Create();
}
catch (InterceptionUnavailableException)
{
    // Ovladač Interception není nainstalovaný/načtený - nabídni instalaci (viz tools/install-interception.exe
    // ve SoundpadTranslatoru/Keyboard Utils pro referenční implementaci instalačního dialogu).
    return;
}

interceptor.MacroKeyDown += key => Console.WriteLine($"Stisknuto: {KeyNames.Describe(key)}");

// "Stiskni klávesu na klávesnici, kterou chceš použít jako makro klávesnici":
interceptor.DeviceLearned += target => interceptor.Target = target;
interceptor.StartLearning();
```

## Poznámky

- Interception je ovladač na úrovni jádra. Anticheaty jako Vanguard (Valorant) nebo FACEIT ho můžou blokovat nebo hlásit.
- Interception má 10 slotů pro klávesnice. Po mnoha odpojeních a připojeních USB přestane nové zařízení
  zachytávat, dokud nerestartuješ počítač.
- Interception (oblitum) je pro nekomerční použití pod LGPL 3.0, viz `Interception-LGPL-3.0.txt`. Tenhle balíček
  (`MacroKeyboard`) je MIT, ale závisí na `interception.dll`, které tou LGPL licencí vázané je.
