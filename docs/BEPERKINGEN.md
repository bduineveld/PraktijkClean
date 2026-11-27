# Bekende beperkingen en nog niet geteste onderdelen

## Getest op Windows 11 (versie 1.4.2)

In dezelfde Parallels-VM (Windows 11 ARM64, build 26300):

- `build.ps1` volledig: alle scripttests en 66 C#-tests geslaagd; `PraktijkClean-1.4.2.exe` (603 kB).
- Startwizard opnieuw doorlopen (config.json tijdelijk weggezet, daarna teruggezet): *Prullenbak daarna legen* staat
  standaard aan; op het overzicht de twee nieuwe vinkjes (bureaublad, taakbalk), allebei aan.
- Na *Opslaan en simulatie bekijken* vanuit de gedownloade exe: Startmenu- en bureaubladsnelkoppeling met
  AppUserModelID `PraktijkClean.App` (ook zichtbaar in `shell:AppsFolder`), en PraktijkClean vastgemaakt aan de
  taakbalk (`PraktijkClean.lnk` in *User Pinned\TaskBar*). Tabblad *Voorkeuren* toont beide als aanwezig;
  de bureaubladknop haalt de snelkoppeling weg en zet hem terug.
- Nieuwe teksten voor submappen gecontroleerd in het hoofdvenster (keuzelijst, link *Uitleg en voorbeelden…* eronder),
  in het venster *Map instellen* (kaarttitels op één regel), in stap 2 van de wizard en in de waarschuwing
  "Ook lege submappen verwijderen?".
- **Niet** gezien: de bevestigingsvraag van Windows bij vastmaken (de VM werd op dat moment ook door de gebruiker
  bediend), de uitleg voor oudere Windows-versies zonder deze functie, en het verwijderen van de snelkoppelingen
  via *PraktijkClean verwijderen…*.

## Getest op Windows 11 (versie 1.4.1)

Getest in een nieuwe Parallels-VM (Windows 11 ARM64, build 26300, schermschaal 200 %):

- `build.ps1` volledig: alle scripttests en 66 C#-tests geslaagd; `PraktijkClean-1.4.1.exe` (582 kB).
- 1.4.0 eerst schoon geïnstalleerd via de startwizard (standaardkeuzes), daarna 1.4.1 eroverheen geopend en opgeslagen:
  geïnstalleerde exe en script naar 1.4.1, instellingen behouden.
- Tabblad *Mappen*: compacte keuzelijst voor submappen met de link *Uitleg en voorbeelden…* en de tip over
  dubbelklikken. Tabblad *Voorkeuren*: standaard voor nieuwe mappen, vinkje voor het icoon met het plaatje (aan en
  uit); na *Opslaan* stond het icoon in het opstartitem en draaide het.
- **Niet** in de VM bekeken: het venster dat opent bij dubbelklikken op een map (zelfde venster als na *Map
  toevoegen…*, maar met OK) en de bevestiging bij een zwaardere keuze in de compacte keuzelijst; een nieuwe
  installatie waarbij het icoon vanzelf aan gaat.

## Getest op Windows 11 (versie 1.4.0)

Getest op dezelfde Windows 11-computer (ARM64, build 26300, schermschaal 200 %):

- `build.ps1` volledig: 72 scripttests en 66 C#-tests geslaagd; `PraktijkClean-1.4.0.exe` (578 kB) is gebouwd.
- Submappen Licht/Midden/Zwaar met een eigen testmap (geneste lege mappen, een te nieuwe lege map, een map met een
  nieuw bestand, een map met een oud bestand): eerst met een keuzelijst, daarna opnieuw met de drie kaarten (klik op
  een zwaardere kaart vraagt om bevestiging; Annuleren laat de oude kaart staan), simulatie toont de lege submappen die weg zouden gaan, rechtermuisknop
  → "altijd laten staan" slaat de submap op als uitzondering. Echte opruiming (definitief, met een kopie van de
  instellingen): precies de verwachte drie lege mappen weg, uitgesloten, te nieuwe en niet-lege mappen bleven staan;
  logboek en lijst verwijderde bestanden kloppen. **Niet** getest: lege mappen naar de Prullenbak (zelfde aanroep als
  voor bestanden, maar niet getest om de echte Prullenbak niet te raken; alleen definitief verwijderen is getest).
- *Map toevoegen…* met een lege testmap: venster met bewaartermijn (30 dagen) en kaarten, *Ook submappen* gekozen en
  "ook voor mappen die ik hierna toevoeg" aangevinkt; de map kwam in de lijst met 30 dagen en Midden. Het venster
  *Standaard voor nieuwe mappen…* geopend. Het opslaan van de standaard zit in de C#-tests.
- Startwizard: `config.json` tijdelijk weggehaald (met een kopie van de hele programmamap). Wizard verscheen bij de
  start; "Kies minstens één map" bij geen enkele map; andere map via het mapvenster; 1 week en *Ook lege submappen*;
  overzicht klopte; *Opslaan en simulatie bekijken* sloeg op in simulatiemodus en opende de simulatie, de melding
  "PraktijkClean is ingesteld" kwam pas daarna. Om geen echte bestandsnamen te tonen zijn Bureaublad en Downloads
  daarbij uitgevinkt. **Niet** getest: *Overslaan* (dan blijven de standaardinstellingen gewoon staan, net als zonder
  wizard). Daarna is de oude programmamap teruggezet.
- Bijwerken met één klik, met een tijdelijke testversie 1.4.1 die met de echte releasesleutel is ondertekend en via
  `UpdateSource` vanaf een eigen testadres werd aangeboden (het echte GitHub-adres is alleen in de tests gebruikt):
  blauwe balk na het openen, vraagvenster met de releasetekst, download met voortgang onderin, controle van de
  handtekening, app sluit en gaat als 1.4.1 weer open met de melding "Bijgewerkt", icoon herstart, script bijgewerkt
  naar 1.4.1, pauze en instellingen behouden, updatemap opgeruimd. Ook 1.3.0 → 1.4.0 via `/update-install`.
- Geweigerd (en niet geïnstalleerd): een download waarin één byte is veranderd (melding "De handtekening van de
  download klopt niet") en een geldig ondertekende oudere versie die als nieuwere werd aangeboden ("niet de
  verwachte versie").
- Fail-safe: met `PraktijkClean.exe` tijdelijk hernoemd verwijderde het geïnstalleerde script als geplande taak
  niets (exitcode 1, "programma ontbreekt" in logboek en status); met de exe terug werd hetzelfde testbestand wel
  opgeruimd. Getest met een kopie van de instellingen en een eigen testmap.
- Tabbladen worden nu op de achtergrond voorbereid zonder ze even te selecteren; daardoor tekenen na het
  vergroten of verkleinen van het venster geen stukjes van een ander tabblad meer door het huidige heen.
- Tabblad wisselen gemeten (Ctrl+Tab, tijd tot het scherm verandert): was 1,5–2,5 s naar *Planning en veiligheid* of
  *Logboek*, nu 0,2–0,45 s. Oorzaak: tabbladen met de achtergrond van het thema (`UseVisualStyleBackColor`) laten elk
  label die achtergrond opnieuw tekenen; nu een effen achtergrond in dezelfde kleur.
- **Niet** getest: bijwerken vanaf de echte GitHub-release (kan pas bij de eerstvolgende release na 1.4.0); de
  melding en het menu-item van het systeemvak-icoon (alleen de code); groepsbeleid `DisableUpdates` (alleen de
  code); bijwerken achter een proxyserver.

## Getest op Windows 11 (versie 1.3.0)

Getest op een tweede Windows 11-computer (ARM64, build 26300, schermschaal 200 %):

- `build.ps1` volledig: 59 scripttests en 48 C#-tests geslaagd; `PraktijkClean-1.3.0.exe` (496 kB, inclusief iconen) is gebouwd.
- Lijst verwijderde bestanden: met een eigen testmap twee keer opgeruimd (naar de Prullenbak en definitief); de
  namen staan in de lijst op het tabblad Logboek, zoeken filtert, "Niet bijhouden" + Opslaan wist de lijst direct,
  terugzetten naar "1 week" werkt. "Lijst wissen…" en dubbelklikken zijn alleen in de tests/code gecontroleerd.
- Verwijderen naar de Prullenbak: in de gewone Windows-sessie (zoals de geplande taak, `InteractiveToken`) komt
  een bestand in de Prullenbak terecht. Let op: start u het script via een SSH-verbinding, dan verdwijnt het
  bestand direct (er is daar geen Prullenbak); dat geldt niet voor normaal gebruik.
- Vraagvensters: geen Ja/Nee meer maar een vraag als kop en knoppen die zeggen wat er gebeurt ("Activeren",
  "Map toevoegen", "Annuleren"). "Automatisch opruimen activeren?" en "Nu opruimen?" hebben een derde knop
  "Eerst simulatie bekijken…" (standaardknop); na het sluiten van de simulatie komt de vraag opnieuw. Esc en het
  kruisje kiezen altijd de veilige knop. Getest met de activeringsvraag (met een demomap; Bureaublad en Downloads
  uitgeschakeld); de andere vragen gebruiken hetzelfde venster.
- App-icoon (beeldscherm met bezem en schildje, `icon\PraktijkClean.ico`, 16-256 px): zichtbaar in de exe en in de
  titelbalk. Systeemvak-icoon: hetzelfde beeldscherm zonder schildje met linksonder het statusbolletje; alle vijf
  standen zijn in deze VM uit de gebouwde exe getekend op 16, 20, 24 en 32 px. Het icoon bij Instellingen > Apps en
  het echte systeemvak zijn niet apart bekeken (beide gebruiken het icoon in de exe en `NotifyIcon`).
- Snelheid (gemeten in deze VM, die PraktijkClean via x64-emulatie draait op 3730×2008 bij 200 %): pauzeren/hervatten
  van ~2 s naar ~1 s; het eerste bezoek aan een tabblad van 1,5–4,7 s naar ~0,7–1 s (tabbladen worden op de
  achtergrond voorbereid); het nieuwe tabblad of de nieuwe kleur verschijnt in één keer in plaats van stukje voor
  stukje; terugklikken in het venster hertekent niets meer als er niets is veranderd. Op een gewone x64-pc is dit
  allemaal veel sneller.
- Nieuwe indeling: statuskop (grijs → blauw na opslaan), hoofdknop, menu "Meer", tabbladen Mappen /
  Planning en veiligheid / Logboek, onderbalk met "Niet-opgeslagen wijzigingen" en "Ongedaan maken". Knoppen en
  teksten passen bij 200 %; geen tekst valt meer buiten het venster.
- Installatie bij eerste "Opslaan": kopie in `%LOCALAPPDATA%\PraktijkClean`, Startmenu-snelkoppeling,
  vermelding bij Geïnstalleerde apps (naam, versie 1.3.0, grootte), geplande taak. Een nieuwe build van dezelfde
  versie werkt de geïnstalleerde kopie bij (zelfde SHA-256).
- Icoon aanzetten via het menu: op de achtergrond ("Icoon wordt aangezet…"), venster blijft bruikbaar, klaar in
  ongeveer 2 seconden.
- Verwijderen met `PraktijkClean.exe /uninstall` (zoals Instellingen > Apps doet): melding als het venster nog
  open is; daarna taak, opstartregel, snelkoppeling, Apps-vermelding én de map met de exe weg. Verwijderen via
  het menu: idem, venster sluit zichzelf.
- **Niet** getest: de knop Verwijderen in het scherm Instellingen > Apps zelf (wel de opdracht die daar staat);
  daadwerkelijk activeren vanuit de nieuwe vraag (alleen geopend, simulatie bekeken en de vraag opnieuw gekregen);
  bijwerken vanaf een echte 1.2.0-installatie.

## Getest op Windows 11 (versie 1.2.0)

Getest op dezelfde Windows 11-testcomputer (ARM64, schermschaal 200 %):

- `build.ps1` volledig: 55 scripttests en 44 C#-tests geslaagd; `PraktijkClean-1.2.0.exe` (246 kB) is gebouwd.
- In de app doorlopen: nieuwe standaardinstellingen (1 dag, Prullenbak na 1 dag legen), opslaan, gekleurde
  statusbalk (grijs, blauw, oranje), overzicht van 7 dagen, uitzonderingenlijst, simulatie met
  "nooit verwijderen" en "alle .svg-bestanden nooit verwijderen" via de rechtermuisknop (direct opgeslagen).
- Icoon in het systeemvak: aanzetten (kopie + opstartitem), tooltip, menu, pauzeren via het icoon (taak uit,
  app toont daarna GEPAUZEERD), hervatten in de app (icoon volgt), openen via het icoon (venster komt terug,
  geen tweede kopie), bijwerken van de kopie bij een nieuwe versie, uitzetten via vinkje en via
  "Icoon verbergen", volledig verwijderen (map, taak en opstartitem weg).
- **Niet** getest: de rode status (taak ontbreekt) en de melding die het icoon dan geeft; "Bestand kiezen…" en
  "Submap kiezen…" met de dialoogvensters van Windows; het icoon na een echte nieuwe aanmelding.

## Getest op Windows 11 (versie 1.1.0)

Getest op een Windows 11-testcomputer (ARM64, .NET Framework 4.8.1, schermschaal 200 %):

- `build.ps1` volledig: 55 scripttests onder **Windows PowerShell 5.1** en 39 C#-tests geslaagd, niets
  overgeslagen; `PraktijkClean.exe` (.NET Framework 4.8, 206 kB) is gebouwd.
- Na de overstap van .NET 10 naar .NET Framework 4.8 opnieuw doorlopen in de app: opslaan, simulatie (ook met
  Prullenbak), Prullenbak-optie met bevestiging, pauzeren en volledig verwijderen. Twee schermschaalproblemen
  die alleen onder .NET Framework optraden (getalvakken en kolombreedtes) zijn opgelost.
- De eerdere tests hieronder zijn gedaan met de .NET 10-versie; het script is ongewijzigd.
- De vensters zijn bekeken en bediend: instellingen opslaan, simulatie, pauzeren/hervatten, logboek openen,
  Prullenbak-optie met bevestiging, volledig verwijderen. De geplande taak is via Taakplanner gestart
  (simulatie, resultaat 0) en het logboek bevat geen bestandsnamen.
- Prullenbak legen: het lezen van de echte Prullenbak en het verwijderen van één eigen testitem zijn gecontroleerd
  (Verkenner toont het item daarna niet meer). Verder getest met nagebootste `$I`/`$R`-bestanden.

Nog **niet** getest:

| Onderdeel | Status |
|---|---|
| Echt verwijderen via de app ("Nu opruimen", automatische opschoning) | Alleen via de scripttests met fictieve bestanden |
| Verwijderen naar de **Prullenbak** (optie "Naar de Prullenbak") | Niet getest (in de tests wordt "definitief" gebruikt) |
| Prullenbak legen via de geplande taak op een echte, gevulde Prullenbak | Niet getest |
| Schermschaal 100–150 %, Windows 10, x64-hardware | Niet getest (alleen 200 % op ARM64) |
| Meerdere schermen met verschillende schaal | Niet getest; Windows schaalt het venster dan bij (kan iets wazig zijn) |
| Known Folders omgeleid of via OneDrive | Niet getest |
| OneDrive-herkenning via register en Cloud Files-registratie | Alleen via omgevingsvariabele en mapnaam getest |
| Netwerkstations en UNC-paden | Alleen padherkenning getest |

## Functionele beperkingen

- **Geen opschonen bij afmelden.** Niet betrouwbaar mogelijk zonder beheerder/groepsbeleid; zie
  [ARCHITECTUUR.md](ARCHITECTUUR.md#bij-afmelden). Alternatief: dagelijks (met inhaalfunctie) en bij aanmelden.
- **Verplaatste bestanden houden hun oude datums.** Sleept iemand een oud document vanuit Documenten naar het
  bureaublad (verplaatsen op dezelfde schijf, niet kopiëren), dan kan het bij de volgende controle al in
  aanmerking komen. Openen en opslaan maakt het weer "nieuw"; alleen bekijken niet. Advies aan gebruikers:
  bestanden die moeten blijven in een **submap** zetten (bij niveau Licht, de standaard, worden submappen niet opgeschoond) of een
  uitzondering instellen.
- **Limiet is alles-of-niets.** Komen er meer bestanden in aanmerking dan het maximum, dan wordt er niets
  verwijderd. Bij een grote achterstand de eerste keer: simulatie bekijken en het maximum tijdelijk verhogen
  of eerst handmatig opruimen.
- **Prullenbak**: kiest u "Naar de Prullenbak", dan blijven verwijderde bestanden op de pc staan tot de
  Prullenbak wordt geleegd (eventueel automatisch met "Prullenbak automatisch legen"). Op
  netwerk-/cloudlocaties (standaard geblokkeerd) gaan bestanden meestal niet naar de Prullenbak.
- **Prullenbak legen** geldt voor de hele Prullenbak van de gebruiker, dus ook voor zelf weggegooide
  bestanden. Verkenner kan na het legen nog even een verouderde weergave tonen. Prullenbakken op
  verwisselbare schijven en netwerklocaties worden niet geleegd.
- **Geen veilige vernietiging.** Gewoon verwijderen; geen overschrijven. Kopieën elders (back-ups, OneDrive,
  e-mail, HIS, schaduwkopieën) worden niet beïnvloed.
- **Per gebruiker.** Op een pc met meerdere accounts moet PraktijkClean per account worden ingesteld. Centrale
  uitrol (Intune/GPO) is nog niet uitgewerkt.
- **Taak draait alleen als de gebruiker is aangemeld** (er wordt bewust geen wachtwoord opgeslagen).
- **Uitvoeringsbeleid/AppLocker**: bij een afgedwongen `AllSigned`-beleid moet het script ondertekend zijn. In
  omgevingen met AppLocker/WDAC (PowerShell "Constrained Language Mode") werkt het script niet; er wordt dan
  niets verwijderd en de app toont een foutmelding.
- **Zeer lange paden** (> 260 tekens) kunnen onder Windows PowerShell 5.1 tot een leesfout leiden; die
  bestanden worden overgeslagen en als fout geteld.
- **Verborgen bestanden** worden nooit opgeschoond.
- **Geen programma-icoon** en nog geen digitale handtekening (zie [ONDERTEKENING.md](ONDERTEKENING.md)).
- Eerste versie uitsluitend bedoeld voor **Windows 11**; op Windows 10 verschijnt een waarschuwing.
