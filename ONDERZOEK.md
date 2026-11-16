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

## Featurevergelijking en aanbevelingen (16 nov 2026)

### Wat de drie programma's bieden

| Thema | Groupy 2 | TidyTabs | WindowTabs (beta nov 2026) | ProgramTabs 0.2.1 |
|---|---|---|---|---|
| Tabs voor elk programma, slepen om te groeperen | ✔ | ✔ | ✔ | ✔ |
| Automatisch groeperen | zelfde programma, regels | regels | per programma | alles (per scherm) |
| Uitsluiten / whitelist per programma | beperkt | ✔ zwart/witte lijst | ✔ app-regels | alleen `--only` |
| Opgeslagen groepen / werkruimtes | ✔ "Groupings" op de taakbalk | ✔ groepspositie | ✔ werkruimtes (start ook apps) | ✘ (wel vaste tabs) |
| Vaste tabs | ✘ | ✘ | ✔ | ✔ + heropenen + logo |
| Naast elkaar binnen een groep | ✘ (klacht) | via AquaSnap | ✔ split view | ✔ + live spiegelbeeld |
| Tabs hernoemen | ? | ✔ | ? | ✔ per venster/programma |
| Alleen icoon | ? | ? | ? | ✔ |
| Voorbeeld bij zweven | ? | ✔ | ✔ (optioneel) | ✘ |
| Kleuren per tab/groep | ✔ accenten | aanpasbaar uiterlijk | ✔ per tab/app/groep | ✘ (volgt Windows-thema) |
| Tabbalk verbergen als niet nodig | ? | ✔ auto-hide en vervagen | ✔ bij gemaximaliseerd | ✘ |
| Pauze bij volledig scherm | ? | ? | ✔ | ✘ |
| Sneltoetsen | ? | ✔ instelbaar | ✔ instelbaar, Ctrl+1–9, zoeken, palet | vast, beperkt |
| Opdrachtenpalet / tabs zoeken | ✘ | ✘ | ✔ | ✘ |
| "+" nieuw venster van zelfde programma | ✘ | ✘ | ✔ (sneltoets) | ✘ |
| Alle vensters van een programma sluiten | ? | ? | ✔ (bulk sluiten) | ✔ |
| Taakbalk: één knop per groep | ✔ | ✘ | ✔ (optioneel) | ✘ |
| Instellingenscherm, export/import | ✔ | ✔ | ✔ JSON, portable | ✘ (tekstbestanden) |
| Automatische updates | ✔ | ✔ | via releases | ✔ |
| Multi-monitor | ✔ | ✔ (+DisplayFusion/UltraMon) | ✔ | per-monitor DPI, niet getest |

### Wat gebruikers het meest waarderen (en waarom)
1. **Opgeslagen groepen die je met één klik opent** (Groupy's meest geprezen functie). Gebruikers werken in vaste "sets": mail + agenda + projectmap. Wat ze missen: het openen bij het aanmelden, en betrouwbaarheid (groepen die niet goed terugkomen).
2. **Automatisch groeperen met regels, plus uitzonderingen.** Niemand wil alles handmatig slepen, maar ook niet dat pop-ups, dialoogvensters of bepaalde apps tabs krijgen. Voor TidyTabs en WindowTabs is dit een kernfunctie, en het staat hoog op de wensenlijst van WindowTabs (#102).
3. **Rust op het scherm.** TidyTabs verkoopt zich expliciet met "geen visuele ruis": tabs verbergen, vervagen, geen hapering of flikkering. WindowTabs verbergt de tabs bij gemaximaliseerde vensters en pauzeert bij volledig scherm.
4. **Toetsenbord.** Instelbare sneltoetsen, direct naar tab 1–9, tabs zoeken en het opdrachtenpalet zijn bij WindowTabs de recentste toevoegingen.
5. **Overzicht via kleur en voorbeeld.** Groupy's accentkleuren en een voorbeeld bij zweven (TidyTabs, WindowTabs) helpen om veel tabs snel uit elkaar te houden.
6. **Naast elkaar werken binnen een groep.** Bij Groupy een expliciete klacht. WindowTabs voegde het in oktober 2026 toe. ProgramTabs heeft het al, met spiegelbeeld.
7. **Klikdoel en stabiliteit.** Een TidyTabs-gebruiker vraagt bredere tabs (misklikken). Problemen met multi-monitor, DPI en FancyZones zijn terugkerende klachten bij alle drie.

### Aanbevelingen voor ProgramTabs, in volgorde
1. **Uitzonderingen en veilig gedrag op het werk.**
   - Vensters op volledig scherm (presentaties, video's, gedeeld scherm) automatisch overslaan en de tabbalk dan verbergen.
   - Rechtsklik → "Nooit als tab tonen (*programma*)" en een uitsluitlijst.
   - *Waarom eerst:* nu kan ProgramTabs een PowerPoint-presentatie in een groep trekken. Op een werklaptop is dat het grootste risico, en het is relatief weinig werk.
2. **Werkruimtes.** Een set vensters (inclusief gesplitst beeld en vaste tabs) opslaan onder een naam, en met één klik of bij het aanmelden weer openen. Programma's die niet draaien worden dan gestart.
   - *Waarom:* dit is de meest gewaardeerde functie van de concurrentie, en ProgramTabs heeft er met vaste tabs en heropenen al de helft van.
3. **Sneltoetsen uitbreiden.** Ctrl+Alt+1–9 voor een tab, tabs zoeken (typ een deel van de naam), en sneltoetsen instelbaar maken.
   - *Waarom:* weinig werk, en veel winst bij veel tabs.
4. **Instellingenvenster met export/import.** Eén scherm voor uitzonderingen, sneltoetsen, uiterlijk en updates. Daarbij het exporteren en importeren van vaste tabs, namen en werkruimtes.
   - *Waarom:* zo kun je je thuisinstellingen naar de werklaptop overzetten. Nu moet je daarvoor tekstbestanden in `%APPDATA%` kopiëren.
5. **Voorbeeld bij zweven.** Een live miniatuur van het venster als je over een tab zweeft.
   - *Waarom:* de DWM-thumbnailtechniek van het spiegelbeeld is er al, dus het is weinig werk. Vooral nuttig bij icoon-tabs.
6. **"+"-knop of sneltoets voor een nieuw venster van hetzelfde programma**, zoals Chrome of Verkenner, dat meteen in de groep komt. Dit staat open op de wensenlijst van WindowTabs.
7. **Kleuren per programma of groep.** Een kleuraccent op de tab, bv. werk-apps blauw en communicatie groen. Groupy-gebruikers noemen dit een snel hulpmiddel om te sorteren.
8. **Rustiger tabbalk.** Een optie om de balk te verbergen bij één tab, en om hem minder fel of smaller te tonen. Daarbij een instelbare tabbreedte, als antwoord op de misklik-klacht.
9. **Robuustheid en uitrol.** Testen met meerdere schermen, FancyZones, Teams en Office. Een code-signing-certificaat overwegen, zodat SmartScreen en bedrijfsbeveiliging de exe niet blokkeren. Dat kost een jaarlijks bedrag.
10. **Taakbalk: één knop per groep.** Laagste prioriteit: technisch riskant (de taakbalkknoppen van andere programma's aanpassen) en weinig gevraagd.

Bronnen: [Stardock Groupy](https://www.stardock.com/products/groupy/), [Groupy 2 op Steam](https://store.steampowered.com/sub/823992), [Stardock-forum: Groupy 2-ervaringen](https://forums.deadmansdrawgame.com/526125/reply/3921462), [XDA-review](https://www.xda-developers.com/stardock-groupy-2-multitask-productivity-boost-windows-11/), [TidyTabs](https://www.nurgo-software.com/products/tidytabs), [TidyTabs-prijzen](https://www.nurgo-software.com/pricing/tidytabs), [TidyTabs-gedrag (auto-hide)](https://help.nurgo-software.com/article/59-tidytabs-configuration-behavior), [SnapFiles-reviews TidyTabs](https://www.snapfiles.com/userreviews/113919/tidytabs.html), [WindowTabs README](https://github.com/leafOfTree/WindowTabs), [WindowTabs releases](https://github.com/leafOfTree/WindowTabs/releases).

## Wat deze eerste versie (ProgramTabs v0.1) doet
Zie [README.md](README.md).
