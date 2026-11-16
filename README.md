# ProgramTabs v0.1

Toont open vensters van **elk** Windows-programma als tabs, met een tabbalk boven het venster in de stijl van Groupy en TidyTabs. Achtergrond en vergelijking met bestaande tools: [ONDERZOEK.md](ONDERZOEK.md).

## Installeren op een andere pc (bv. werklaptop)
1. Download `ProgramTabs.exe` van de [laatste release](https://github.com/KurtSchaut/program-tabs/releases/latest).
2. Start het. Windows SmartScreen kan waarschuwen omdat het programma niet digitaal ondertekend is. Kies dan "Meer informatie" → "Toch uitvoeren". Op een bedrijfslaptop kan IT dit blokkeren.
3. Rechtsklik op het tray-icoon → **Installeren voor deze gebruiker…**. Het programma wordt gekopieerd naar `%LOCALAPPDATA%\Programs\ProgramTabs`, start voortaan met Windows, en kan zichzelf bijwerken zonder admin-rechten.

## Updates
- ProgramTabs controleert een minuut na het starten en daarna dagelijks of er op GitHub een nieuwere release is. Is die er, dan verschijnt een melding. Klik erop om bij te werken.
- Zelf controleren: tray-menu → **Controleren op updates**. De huidige versie staat in hetzelfde menu.
- Bijwerken = de nieuwe exe downloaden, de SHA-256-checksum controleren, de oude exe vervangen en herstarten. Je vensters blijven open.
- Een nieuwe versie publiceren (op de ontwikkel-pc): `.\release.ps1 -Version 0.3.0 -Notes "Wat er nieuw is"`. Dat zet het versienummer, bouwt, commit, tagt, pusht en maakt de GitHub-release met de exe en de checksum.

## Bouwen
Een SDK installeren is niet nodig: het script gebruikt de C#-compiler die in Windows zit (.NET Framework 4.x).

```bat
build.cmd
```

Resultaat: `bin\ProgramTabs.exe`.

## Starten
```bat
bin\ProgramTabs.exe                 :: groepeert meteen alle vensters (één groep per scherm)
bin\ProgramTabs.exe --no-group      :: start passief, groeperen met Ctrl+Alt+G
bin\ProgramTabs.exe --split         :: groeperen en meteen in twee helften splitsen
bin\ProgramTabs.exe --only notepad,mspaint   :: alleen deze processen (handig om te testen)
bin\ProgramTabs.exe --log pt.log    :: logbestand
bin\ProgramTabs.exe --list          :: toont alleen welke vensters tabs zouden worden (verandert niets)
```
Er draait altijd maar één instantie. Het programma zit in het systeemvak (tray). Rechtsklik daar voor het menu en om af te sluiten.

## Bediening
| Actie | Wat het doet |
|---|---|
| Klik op een tab | Naar dat venster wisselen |
| × op een tab | Venster sluiten (zoals het kruisje van het programma zelf) |
| Middelklik op een tab | Venster losmaken uit de groep |
| Tab slepen binnen de balk | Volgorde aanpassen |
| Tab naar buiten slepen | Losmaken. Laat je los op een andere tabbalk, dan verhuist het venster naar die groep. |
| Venster (titelbalk) op een tabbalk slepen | Toevoegen aan die groep (de balk licht op) |
| Scrollwiel op de balk | Volgende/vorige tab |
| Dubbelklik op een leeg deel van de balk | Groep maximaliseren / terugzetten |
| Rechtsklik op een tab | Vastzetten, hernoemen, alleen icoon, losmaken, sluiten, groep opheffen |
| Rechtsklik → "Alle vensters van … sluiten (n)" | Sluit in één keer alle vensters van dat programma (bv. alle Word-documenten). Vaste tabs blijven open, en het programma vraagt zelf nog om niet-opgeslagen werk op te slaan. |
| **Ctrl+Alt+G** | Alle vensters groeperen |
| **Ctrl+Alt+S** | Gesplitst beeld aan/uit |
| **Ctrl+Alt+T** | Actief venster toevoegen aan of losmaken uit een groep |
| **Ctrl+Alt+PgDn / PgUp** | Volgende/vorige tab |
| **Ctrl+Alt+U** | Alles losmaken |

## Gesplitst beeld (twee vensters naast elkaar)
Vergelijkbaar met *Nieuw venster* in Excel: de groep wordt in twee helften verdeeld, en **beide helften tonen dezelfde tabs**.

- Aan/uit met de knop rechts op de tabbalk (◫), met **Ctrl+Alt+S**, via het tray-menu of met `--split` bij het starten.
- In elke helft kies je zelf welke tab je ziet. Een stipje onder een tab betekent dat die tab in de andere helft getoond wordt.
- Kies je in beide helften **hetzelfde venster**, dan staat het echte venster in de helft waar je het laatst koos. De andere helft toont een **live spiegelbeeld** (DWM-thumbnail, zoals de taakbalkvoorbeelden). De tab heet dan "Spiegel · …" en heeft een gestippelde accentstreep. Klik op het spiegelbeeld en het echte venster verhuist naar die helft.
- Een tab naar de tabbalk van de andere helft slepen toont hem daar.
- Een venster maximaliseren in gesplitst beeld zet het terug in zijn helft.
- Een programma kan zijn eigen venster niet echt dupliceren via ProgramTabs. Alleen programma's die dat zelf ondersteunen (Excel, Word, Verkenner) kunnen een tweede, *bewerkbaar* venster op hetzelfde document openen. Het spiegelbeeld kun je alleen bekijken.

## Vastgezette tabs (bv. Outlook-mail en -agenda)
- **Rechtsklik op een tab → "Tab vastzetten".** Vastgezette tabs staan altijd vooraan, zijn compact (alleen het eerste deel van de titel, bv. "Postvak IN" of "Agenda") en hebben een speldje in plaats van ×.
- Ze zijn beschermd tegen ongelukjes in de tabbalk: er is geen ×, "Venster sluiten" staat uit, en middelklikken, naar buiten slepen of Ctrl+Alt+T maken ze niet los.
- **Ze worden onthouden**, ook na het herstarten van ProgramTabs. Opent het programma later zo'n venster, dan wordt het automatisch weer de vaste tab.
- **Gesloten (bv. met het kruisje van Outlook zelf)?** De tab blijft staan, gedimd met een stippelrand. Klik erop om het programma opnieuw te openen. Bij klassieke Outlook opent dat meteen de juiste map (`/select outlook:calendar`, `/select outlook:inbox`).
- **Browsers** (Chrome, Edge, Brave, Firefox, …): bij het vastzetten wordt het webadres uit de adresbalk bewaard. Heropenen opent die pagina in een eigen venster (`--new-window "https://…"`). De tab wordt herkend aan de sitenaam (bv. "Gmail"), niet aan de wisselende paginatitel.
- **Verkenner**: bij het vastzetten wordt de map bewaard (ook bij Windows 11-tabbladen). Heropenen opent die map in plaats van Start. Bekende mappen vallen terug op `shell:Downloads`, `shell:Personal`, ….
- Tellers zoals "(28)" in titels worden genegeerd bij het herkennen.
- **Rechtsklik → "Startopdracht wijzigen…"** laat je zelf instellen wat een gesloten vaste tab opent (programma + startargumenten).
- Losmaken: rechtsklik → "Niet meer vastzetten".
- Herkenning gebeurt op programmanaam + het begin van de titel (vóór " - "). De lijst staat in `%APPDATA%\ProgramTabs\pinned.txt` (proces, titel-begin, programma, startargumenten, gescheiden door een tab) en kun je met de hand aanpassen.
- Beperking: ProgramTabs kan het kruisje van het programma zelf niet uitschakelen. Het vangt dat op met de gedimde tab en heropenen met één klik.

## Tabs hernoemen of alleen het icoon tonen
Rechtsklik op een tab → **Naam en weergave**:
- **Naam wijzigen (alleen dit venster)…**: een eigen naam voor deze tab. Laat je het veld **leeg**, dan toont de tab **alleen het icoontje**, als een smal tabje.
- **Naam wijzigen (altijd voor *programma*)…**: geldt voor **elk venster van dat programma, ook in de toekomst** (bv. elk Claude-venster heet voortaan "Claude", of is altijd een icoontje). Herkenning gebeurt op programmanaam, niet op venstertitel.
- **Alleen icoon (dit venster / altijd voor *programma*)**: icoon-weergave aan/uit.
- **Standaardnaam herstellen** wist de instelling van deze tab. **Naam voor *programma* vergeten** wist de programma-instelling.
- Zweef over een tab om de volledige venstertitel te zien (handig bij icoon-tabs).

Wat waar bewaard wordt (de meest specifieke instelling wint):
1. **Vastgezette tab**: in `pinned.txt`, blijft bewaard.
2. **Alleen dit venster**: zolang het venster open is.
3. **Altijd voor dit programma**: in `%APPDATA%\ProgramTabs\labels.txt` (proces, naam, icoon), blijft bewaard.

## Maximaliseren
Een venster maximaliseren maximaliseert de hele groep: alle vensters vullen dan het scherm onder de tabbalk. Nieuwe vensters worden automatisch als tab toegevoegd zodra "alle vensters groeperen" actief is. Dat kun je uitzetten in het tray-menu.

## Hoe het werkt (kort)
- `WindowUtil.IsTabbable` past dezelfde regels toe als Alt+Tab om te bepalen welke vensters meedoen.
- Alle vensters van een groep krijgen exact dezelfde **zichtbare** rand (DWM extended frame bounds). Het actieve venster ligt bovenaan.
- `TabStrip` is een randloos venster dat nooit de focus neemt (`WS_EX_NOACTIVATE`) en direct boven het actieve venster staat.
- `Manager` luistert met `SetWinEventHook` naar foreground-, verplaats-, minimaliseer-, show/hide/destroy- en titel-events. Na het verplaatsen of schalen volgen de andere vensters met een kleine vertraging.
- Er wordt niets verborgen of "ingebed". Sluit of crasht ProgramTabs, dan blijven alle vensters gewoon staan.

## Bekende beperkingen van v0.1
- Vensters van programma's die als administrator draaien kunnen niet verplaatst worden, tenzij ProgramTabs zelf ook als administrator draait.
- Programma's met een minimumgrootte (bv. Paint) kunnen iets groter blijven dan de groep.
- Groepen worden niet opgeslagen tussen sessies.
- Met veel tabs worden ze smal. Er is nog geen overloopmenu.
- Er is geen instellingenscherm en geen uitsluitlijst per programma, behalve `--only`.
- Een naam voor "alleen dit venster" verdwijnt als het venster sluit. Kies "altijd voor *programma*" of zet de tab vast om hem te bewaren.
