# Keyboard Utils

Pomocník pro hraní na klávesy (piano). Načte skladbu z MIDI nebo MusicXML (třeba export z MuseScore),
ukáže ji jako padající noty (piano roll) a poslouchá, co hraješ – přes mikrofon, MIDI klávesy, zvuk z počítače
nebo klikáním na klaviaturu v appce. Umí režim učení s hodnocením, zápis zahraných not do MIDI / MusicXML
a převod zvukové stopy na noty. Volitelně se dá ovládat druhou klávesnicí (makro klávesnice, např. SayoDevice).

## Instalace

1. Sestavení: `dotnet build -c Release src\KeyboardUtils`
   (výstup najdeš v `src\KeyboardUtils\bin\Release\net10.0-windows\win-x64\`).
   Ke spuštění je potřeba .NET 10 Desktop Runtime (x64), Windows 10 nebo 11.
2. Spusť `KeyboardUtils.exe`. Při prvním spuštění se zeptá na jazyk (čeština / English).
   Později ho změníš v **Nastavení > Jazyk**.

Aplikace běží jen jednou – když ji spustíš podruhé, nová kopie se hned ukončí. Pokud nevidíš novou verzi,
zavři všechna okna Keyboard Utils (případně ve Správci úloh).

Makro klávesnice je volitelná a nic dalšího k běhu aplikace nepotřebuje – viz [Makro klávesnice](#makro-klávesnice).

## Použití

### Skladba

- **Otevřít skladbu...** načte MIDI (`.mid`) nebo MusicXML (`.musicxml`, `.mxl`). Vybrat můžeš víc souborů najednou,
  mezi nimi pak přepínáš **<< Předchozí** / **Další >>**.
- **Přehrát / Pauza**, **Stop**. Zvuk jde na MIDI výstup zvolený v **Nastavení > MIDI výstup**.
- Panel **Části / nástroje** vpravo: zaškrtávátkem část vypneš, posuvníkem změníš její hlasitost.
- V piano rollu **tažením nahoru / dolů** posouváš skladbu v čase. Klávesa se při dopadu noty rozsvítí barvou části.
  Kliknutím na klávesu ji zahraješ.
- **Velikost klávesnice** mění šířku kláves – ve větším okně se ukáže víc oktáv, až celá klaviatura.
- **Odebrat skladbu** ji zavře (soubor na disku zůstane).
- **Soubor > Uložit skladbu jako...** uloží skladbu jako MIDI nebo MusicXML.

### Vstup not (co appka slyší)

V **Nastavení > Vstup not** vybereš, odkud appka bere zahrané noty:

- **Mikrofon (akustické piano)** – rozpoznávání výšky tónu ze zvuku.
  Před prvním použitím otevři **Nastavení > Mikrofon a citlivost...**, chvíli buď potichu a klikni **Změřit šum**,
  pak zahraj pár not – dole by se měly ukázat správně. Když appka reaguje na šum, posuň práh doprava.
- **Zvuk z počítače (YouTube, Spotify...)** – poslouchá, co hraje počítač (bez zvuku samotné appky).
- **Klikání na klaviaturu v appce** – kliknutá klávesa se počítá jako zahraná. Hodí se na vyzkoušení bez piana.
- **MIDI: ...** – digitální piano nebo MIDI klávesy připojené přes USB.

Tlačítkem **Poslouchat** zapneš poslouchání – znějící klávesy svítí oranžově.

### Režim učení

**Režim učení...** – vybereš, které části hraješ (např. jen pravou ruku), režim a rychlost:

- **Čekat na notu** – skladba se zastaví na další notě, dokud ji nezahraješ správně.
- **V tempu** – skladba jede, nota se počítá, když ji zahraješ včas (±0,25 s). Ostatní části můžou hrát jako doprovod.

Klávesy, které máš zahrát, svítí světle modře. Správně zahraná nota zezelená, chyba zčervená.
Na konci (nebo po **Ukončit učení** / **Stop**) se ukáže hodnocení: procento správných not, špatné klávesy,
vynechané noty a čas.

### Zapisování not

**Zapisovat noty** – hraj a appka zapisuje rozpoznané noty. Po **Ukončit nahrávání** nastavíš tempo zápisu (BPM)
a nahrávku uložíš jako:

- **MIDI** – přesně to, co jsi zahrál,
- **MusicXML** (pro MuseScore) – noty zarovnané na šestnáctiny v nastaveném tempu, klavírní dvojosnova.
  Tempo nastav zhruba na to, ve kterém jsi hrál.

### Stopy a převod zvuku na noty

**Importovat stopy...** nabídne dvě možnosti:

- **Jako doprovod** – vybereš složku se stopami (`.wav`) a přehraje se originální nahrávka; každou stopu jde
  vypnout nebo zeslabit. Tlačítko **Noty** u stopy z ní rozpozná noty a ukáže je v piano rollu souběžně s nahrávkou –
  stopu pak vypneš a hraješ ji sám k originálu, i v režimu učení.
- **Detekovat noty z hudby** – vybereš zvukový soubor (WAV, MP3, FLAC, AIFF) a appka z něj udělá noty
  jako novou skladbu.

Stopy z FL Studia: **Export > WAV File > Miscellaneous > "Split mixer tracks"** vyexportuje každou stopu mixéru
zvlášť. Odkazy, kde sehnat stopy hotových skladeb, jsou přímo v dialogu.

### Makro klávesnice

Druhou klávesnici (SayoDevice, starou klávesnici, numerický blok...) můžeš používat jako ovladač appky.
Klávesy z ní se nedostanou do Windows, hlavní klávesnice funguje normálně.

1. **Makro klávesnice > Nastavit makro klávesnici...** Napoprvé appka stáhne malý doplňkový modul (pár desítek KB).
   Když chybí driver Interception, nabídne **instalaci ovladače** (potvrdíš v UAC) a pak restart počítače.
   Ruční odinstalace: `install-interception.exe /uninstall` jako administrátor a restart.
2. Stiskni klávesu na klávesnici, kterou chceš používat, a potvrď **Použít tuto klávesnici**.
3. **Přidat klávesu...**, stiskni klávesu na makro klávesnici a vyber akci: přehrát / pauza, stop, další / předchozí
   skladba, vypnout / zapnout část, zahrát notu, přepnout režim učení, přepnout zapisování not.

Přiřazení kláves uložíš do souboru tlačítkem **Exportovat...** a načteš tlačítkem **Importovat...**
(nahradit současné, nebo přidat ke stávajícím). Volba klávesnice se neexportuje, platí jen pro daný počítač.
**Ovládání zapnuté** dočasně vypne zachytávání – makro klávesnice pak píše normálně.

Nastavení se ukládá do `%APPDATA%\KeyboardUtils\config.json`.

## Poznámky

- Rozpoznávání ze zvuku (mikrofon, zvuk z počítače, převod stop) slyší **jednu notu naráz**. Nejlíp funguje
  melodie nebo basa jednoho nástroje; u akordu stačí v režimu učení zahrát jednu jeho notu, celý mix skladby nejde.
- Nejhlubší noty pod F1 (asi spodních 8 kláves) ze zvuku nerozpozná. Mikrofon má zpoždění kolem 70 ms –
  v režimu v tempu se odečítá.
- Se stopami jede režim učení v tempu jen na 100 % – originální nahrávka zpomalit nejde.
- Zvuk z počítače vynechává zvuk samotné appky jen na Windows 10 verze 2004 a novějších.
- Interception je ovladač na úrovni jádra. Anticheaty jako Vanguard (Valorant) nebo FACEIT ho můžou blokovat
  nebo hlásit.
- Interception má 10 slotů pro klávesnice. Po mnoha odpojeních a připojeních USB přestane nové zařízení
  zachytávat, dokud nerestartuješ počítač.
- Interception (oblitum) je pro nekomerční použití pod LGPL 3.0, viz `tools/`.
- Zachytávání makro klávesnice je oddělené do knihovny `src/MacroKeyboard`, kterou sdílí i
  [Soundpad Translator](https://github.com/tomandesMSH/SoundpadTranslator).
