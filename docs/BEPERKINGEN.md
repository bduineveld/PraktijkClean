# Bekende beperkingen en nog niet geteste onderdelen

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
  bestanden die moeten blijven in een **submap** zetten (submappen worden standaard niet opgeschoond) of een
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
