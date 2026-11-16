# Onderzoek: alle vensters als tabs, ongeacht het programma

## Bestaande programma's

| Programma | Platform | Licentie | Werkwijze | Status (nov 2026) |
|---|---|---|---|---|
| **Stardock Groupy 2** | Windows 10/11 | Betalend (~€10) | Tabbalk "zweeft" boven de titelbalk van het venster. Sleep een venster op een ander om te groeperen. Inactieve tabs worden verborgen achter het actieve venster. Groepen kunnen opgeslagen en hersteld worden. | Actief onderhouden |
| **TidyTabs** (Nurgo Software) | Windows 7–11 | Gratis (persoonlijk, max 3 tabs per groep) / Pro | Hetzelfde overlay-principe. De tabbalk verschijnt pas als je over de bovenrand van een venster zweeft. Groeperen door tabs op elkaar te slepen. Regels per programma (whitelist/blacklist). | Actief |
| **WindowTabs** | Windows 7–11 | Open source (MIT) | In 2009 gemaakt door Maurice Flanagan en later open source gemaakt. De actieve fork is `leafOfTree/WindowTabs` (F#/C#). Elke groep krijgt een eigen tabbalk die het actieve venster volgt. Er is een optie om vensters van hetzelfde programma automatisch te groeperen. Instellen via het tray-icoon. | Fork actief |
| **TaskSpace / WinTabber / United Sets** | Windows | Gratis / klein | *Container*-aanpak: één eigen hoofdvenster waarin vreemde vensters met `SetParent` als kindvenster worden "ingebed". | Weinig onderhoud |
| **Clover, QTTabBar, Windows 11 Verkenner-tabs** | Windows | Divers | Alleen tabs voor Verkenner, niet voor willekeurige programma's. | Buiten scope |
| **Microsoft "Sets"** | Windows 10 Insider | – | Ingebouwde tabs voor elk programma (2017–2018). | Geschrapt |
| **suckless `tabbed`, i3/sway tabbed layout, KWin window tabs** | Linux | Open source | `tabbed` gebruikt XEmbed (het programma moet meewerken). Tegelende vensterbeheerders tonen vensters als tabs in hun layout. | Actief |

## De twee technische benaderingen op Windows

### 1. Overlay (Groupy, TidyTabs, WindowTabs): gekozen voor deze versie
- Een eigen, randloos venster (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) wordt **net boven** het actieve venster getekend en toont de tabs.
- Alle vensters van een groep krijgen **dezelfde positie en grootte**. Er is altijd één zichtbaar bovenaan, de rest ligt er recht achter.
- Gebeurtenissen komen binnen via `SetWinEventHook`: foreground-wissel, verplaatsen/schalen, minimaliseren, nieuwe en vernietigde vensters, titelwijziging. Daarmee volgt de tabbalk het venster en blijven de andere vensters op dezelfde positie.
- Verplaatsen gebeurt met `SetWindowPos(... SWP_ASYNCWINDOWPOS)`, zodat een programma dat vastloopt ons niet blokkeert.
- **Voordelen:** het programma zelf blijft ongewijzigd. Dat is robuust: als de tab-tool crasht, blijven alle vensters gewoon bestaan.
- **Nadelen:** de tabbalk moet het venster nauwkeurig volgen (DPI, onzichtbare randen van Windows 10/11, gemaximaliseerde vensters, virtuele bureaubladen).

### 2. Container / reparenting (TaskSpace, WinTabber)
- `SetParent(vreemdVenster, eigenContainer)` maakt het vreemde venster een kind van een eigen venster met een echte tabcontrol.
- **Voordelen:** het voelt als een echt tabbed venster.
- **Nadelen:** Microsoft ondersteunt dit officieel niet over processen heen. De invoerwachtrijen worden gekoppeld (hangt het ene programma, dan hangt de ander mee), en veel moderne apps (UWP, Chromium/Electron, DirectX) renderen of reageren dan verkeerd. Crasht de container, dan sleurt die vensters mee.

### Details die alle goede tools oplossen
- **Welke vensters tellen mee?** Dezelfde regels als Alt+Tab: zichtbaar, geen eigenaar (`GW_OWNER`), geen `WS_EX_TOOLWINDOW`, niet *cloaked* (DWM: UWP-spookvensters en vensters op andere virtuele bureaubladen), heeft een titel, en geen shellvenster (`Progman`, `Shell_TrayWnd`, …).
- **Onzichtbare randen:** `GetWindowRect` bevat op Windows 10/11 ~7 px onzichtbare resize-rand. De zichtbare rand komt uit `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`.
- **DPI:** het proces is *per-monitor DPI aware (v2)*, zodat alle coördinaten fysieke pixels zijn.
- **Foreground-beperkingen:** `SetForegroundWindow` mag alleen als jouw proces de laatste invoer kreeg. Een klik op de tabbalk telt daarvoor mee.
- **Rechten (UIPI):** een niet-verhoogd proces kan vensters van programma's die als administrator draaien niet verplaatsen.

## Wat gebruikers waarderen en missen (onderzoek nov 2026)
Bronnen: Stardock-fora (2024–2026), XDA-review (jul 2025), AlternativeTo-reviews, GitHub-issues en -PR's van WindowTabs (2025–2026). Op Reddit vond ik weinig bruikbaars.

**Stardock Groupy 2**
- Gewaardeerd: tabs aan elkaar slepen en eruit slepen, vensters van hetzelfde programma automatisch groeperen, opgeslagen groepen die je aan de taakbalk kunt vastmaken, kleuren per groep, Windows 11-uiterlijk, regels om automatisch samen te voegen.
- Gemist / klachten:
  - Gegroepeerde vensters kun je niet naast elkaar zetten.
  - Een tweede exemplaar van hetzelfde programma valt buiten de groep.
  - Opgeslagen groepen herstellen niet alles (bv. Acrobat).
  - UWP-apps worden niet betrouwbaar gegroepeerd.
  - Een kier van 2 px naast FancyZones.
  - Sommige apps (WPS Office, QQ Browser) vallen buiten de regels.
  - Een regel "alles in één groep" werd in 2.3 geschrapt, en gebruikers vragen hem terug.

**TidyTabs**
- Gewaardeerd: licht, werkt met bijna elk venster, gratis voor persoonlijk gebruik, Pro is goedkoop.
- Gemist / klachten:
  - De gratis versie is beperkt (max 3 tabs, geen multi-monitor, geen herschikken of hernoemen).
  - Oudere reviews melden bugs met meerdere schermen of resoluties en lange periodes zonder updates.
  - De balk die "altijd bovenaan" staat, stoorde een gebruiker.
  - Er is geen manier om bugs te melden.

**WindowTabs (open source, leafOfTree)**
- Recent toegevoegd (okt–nov 2026):
  - Gesplitste tabs.
  - Vastgezette tabs (blijven vooraan, speldje in plaats van ×, blijven bewaard na herstart).
  - Opdrachtenpalet (Ctrl+Alt+P).
  - Voorbeeld bij zweven, opdrachtregelopties (`--workspace`, `--group`).
  - "Nieuwe tab" (Ctrl+Alt+N).
- Nog gevraagd:
  - Een zichtbare "+"-knop.
  - Een uitsluitlijst (blacklist).
  - Automatisch groeperen op basis van opdrachtregel.
  - Installeren via winget.
  - Problemen met schermschaal (DPI).

**Wat ProgramTabs daaruit leert**
- Al aanwezig: naast elkaar in één groep (gesplitst beeld, met spiegelbeeld), vastgezette tabs met heropenen, alles automatisch in één groep, per-monitor DPI, een tabbalk die niet "altijd bovenaan" staat.
- Kandidaten voor later:
  - Uitsluitlijst en regels per programma.
  - Groepen/werkruimtes opslaan en herstellen.
  - "+"-knop voor een nieuw venster van hetzelfde programma.
  - Tabs hernoemen.
  - Voorbeeld bij zweven (de DWM-thumbnail-techniek is er al).
  - Zoeken in tabs of een opdrachtenpalet.
  - Testen met FancyZones, UWP-apps en meerdere schermen.

## Wat deze eerste versie (ProgramTabs v0.1) doet
Zie [README.md](README.md).
