# Keyboard Utils

Pomocník pro hraní na klávesy (piano). Načteš skladbu (MIDI, MusicXML, nebo vícestopé WAV nahrávky),
necháš si ji zobrazit v piano rollu, zapínáš/vypínáš jednotlivé party nebo nástroje a volitelně si appku
ovládáš i druhou ("makro") klávesnicí, aniž by psala do Windows.

## Instalace

1. Sestavení: `dotnet build -c Release src\KeyboardUtils`
   (výstup najdeš v `src\KeyboardUtils\bin\Release\net10.0-windows\win-x64\`).
2. Spusť `KeyboardUtils.exe`. Appka běží normálně bez dalšího nastavování.

## Použití

Při prvním spuštění se appka zeptá na jazyk (čeština / English). Později ho změníš v menu **Nastavení → Jazyk**.

### Skladba

- **Otevřít skladbu...** – naimportuje `.mid`/`.midi`/`.musicxml`/`.mxl` z MuseScore (nebo odkudkoli jinud).
  Noty se zobrazí jako padající piano roll, part/nástroj jde kdykoli ztlumit v panelu vpravo.
- **Importovat stopy (WAV)...** – naimportuje složku se samostatnými `.wav` stopami jedné skladby:
  - export z **FL Studia**: Export → WAV File → Miscellaneous → "Split mixer tracks" vyexportuje každou
    stopu zvlášť,
  - nebo stažený stem/remix pack (např. z RemixPacks a podobných stránek).

  Všechny stopy ve složce musí mít stejnou vzorkovací frekvenci a počet kanálů. U tohoto typu importu není
  piano roll (nejsou tam notová data), jen mute jednotlivých stop a přehrávání/pauza/stop.
- Přehrávání: Přehrát/Pauza, Stop, Další/Předchozí (jen u skladeb otevřených najednou přes "Otevřít skladbu...").
- MIDI výstupní zařízení jde vybrat v **Nastavení → MIDI výstup** (výchozí je systémový syntezátor).

### Makro klávesnice (volitelné)

Pokud máš doma druhou klávesnici (programovatelnou typu SayoDevice, starou klávesnici, numpad...), můžeš si
ji nastavit jako makro klávesnici a ovládat jí přehrávání, mute nástrojů a další akce - aniž by rušila psaní
na tvé hlavní klávesnici. Appka funguje v pohodě i bez toho, nic se nevynucuje.

1. **Nastavení → Makro klávesnice** (nebo tlačítko v záložce Makro klávesnice). Pokud chybí ovladač
   Interception, appka nabídne **Nainstalovat ovladač** a pak restart počítače.
2. Stiskni klávesu na druhé klávesnici a potvrď **Použít tuto klávesnici**.
3. V záložce **Makro klávesnice** → **Přidat klávesu...**, stiskni klávesu na makro klávesnici a vyber akci
   (přehrát/pauza, stop, další/předchozí skladba, ztlumit/zapnout konkrétní part).

Přiřazení kláves jde uložit tlačítkem **Exportovat...** a načíst **Importovat...** (nahradit současné, nebo
přidat ke stávajícím). Volba klávesnice se neexportuje, protože platí jen pro daný počítač.

Nastavení se ukládá do `%APPDATA%\KeyboardUtils\config.json`.

## Poznámky

- Zachytávání makro klávesnice (Interception driver) je ve vlastním projektu `src/MacroKeyboard`, publikovaném
  i jako samostatný NuGet balíček (sdílený se SoundpadTranslatorem). Stejné poznámky k Interception jako
  u SoundpadTranslatoru platí i tady: ovladač je na úrovni jádra, anticheaty ho můžou blokovat/hlásit,
  má 10 slotů pro klávesnice a po mnoha odpojeních/připojeních USB může přestat nové zařízení zachytávat
  až do restartu.
- **Zatím nehotovo**: detekce zahraných not z mikrofonu, režim učení (barevné zvýrazňování správných/špatných
  not), create mode (zápis hraných not do noty). Piano roll zatím podporuje jen styl "padající noty" (ne
  klasickou notovou osnovu).
