# Architectuur van PraktijkClean

Dit document beschrijft de opbouw en de belangrijkste technische keuzes. Het is bedoeld voor wie
het project later onderhoudt.

## Overzicht

```
 PraktijkClean.exe (beheerapp, WinForms)          Windows Taakplanner
 ├─ schrijft  ─────────────► cleanup.ps1  ◄──────── start dagelijks / bij aanmelden
 ├─ schrijft  ─────────────► config.json  ◄──────── leest bij elke uitvoering
 ├─ maakt/wijzigt/verwijdert taak ──────────────►  taak "PraktijkClean - <gebruiker>"
 ├─ start simulatie / "Nu opruimen" ─► cleanup.ps1 (JSON-resultaat via stdout)
 └─ leest   ◄──────────────── status.json, logs\praktijkclean.log  ◄── geschreven door cleanup.ps1
```

Alles staat per gebruiker in `%LOCALAPPDATA%\PraktijkClean\`:

| Bestand | Inhoud |
|---|---|
| `cleanup.ps1` | Het vaste, generieke opschoonscript (ingebakken in de .exe en bij opslaan weggeschreven). |
| `config.json` | Alle instellingen. Zie [CONFIGURATIE.md](CONFIGURATIE.md). |
| `status.json` | Resultaat van de laatste uitvoering (aantallen, geen bestandsnamen). |
| `logs\praktijkclean.log` | Eén regel per uitvoering, zonder bestandsnamen of paden. Rouleert bij 512 kB. |

De beheerapp hoeft na het opslaan niet open te blijven: de geplande taak start het script zelfstandig.

## Projectstructuur

```
script/cleanup.ps1               Opschoonscript (Windows PowerShell 5.1)
script/config.example.json       Voorbeeldconfiguratie
src/PraktijkClean.Core/          Logica zonder venster: configuratie, validatie, taakdefinitie, installatie
   AppConfig.cs                  Het instellingenmodel (= config.json)
   ConfigValidator.cs            Controle van instellingen vóór opslaan
   ConfigStore.cs                Lezen/schrijven/standaardwaarden
   TaskXmlBuilder.cs             Definitie van de geplande taak
   Installer.cs                  Installeren, herstellen, pauzeren, activeren, deïnstalleren
   ScriptRunner.cs               Script starten vanuit de app (simulatie / nu opruimen)
   PathRules.cs                  Controle van gekozen mappen in de app
   Exclusions.cs                 Uitzonderingen per map toevoegen/verwijderen (zelfde regels als het script)
   LogSummary.cs                 Telt uit het logboek wat er in de afgelopen dagen is opgeruimd
   Compat.cs                     JSON lezen/schrijven en hulpmiddelen voor .NET Framework 4.8
   Windows/                      Windows-specifiek: Known Folders, Taakplanner (COM), gebruiker
src/PraktijkClean/               De beheerapp (vensters en teksten)
   Texts.cs                      Alle langere teksten van de interface
   MainForm.cs                   Hoofdvenster
   SimulationResultForm.cs       Venster met simulatieresultaat (met 'Uitsluiten' via rechtermuisknop)
   InputDialog.cs                Klein invoervenster (extensie of patroon)
   StatusInfo.cs                 Bepaalt de status (aan/simulatie/pauze/probleem) voor balk en icoon
   TrayContext.cs                Het icoon in het systeemvak (PraktijkClean-<versie>.exe /tray)
   TrayIntegration.cs            Icoon aan/uit: kopie van de exe, opstartitem, afsluiten
tests/Script/Run-ScriptTests.ps1 Tests voor het script (zonder externe modules)
tests/PraktijkClean.Tests/       Tests voor de C#-logica (zonder externe pakketten)
```

De app is gebouwd voor **.NET Framework 4.8**, dat in Windows 10 (vanaf 1903) en Windows 11 standaard
aanwezig is. Daardoor is `PraktijkClean-<versie>.exe` één klein bestand (ongeveer 250 kB) zonder meegeleverde runtime.
De code van `PraktijkClean.Core` wordt bij het bouwen in de exe zelf opgenomen (geen losse dll), JSON wordt
gelezen en geschreven met het ingebouwde `DataContractJsonSerializer` (`Compat.cs`), en schermschaal (DPI)
staat in `app.manifest` (systeembreed; bij verschillende schermen schaalt Windows het venster bij).

Interface, configuratie, taakregistratie en opschoonlogica zijn gescheiden: de interface roept alleen
`Installer` en `ScriptRunner` aan; de opschoonlogica zit uitsluitend in `cleanup.ps1`.

## Waarom het opschonen in PowerShell gebeurt

- Er is precies één plek waar bestanden worden verwijderd: `cleanup.ps1`. De app verwijdert zelf nooit
  gebruikersbestanden.
- Het script is leesbaar en controleerbaar door een systeembeheerder zonder ontwikkeltools.
- Windows PowerShell 5.1 zit standaard in Windows 11; er hoeft niets extra te worden geïnstalleerd.
- Het script is altijd dezelfde, vaste versie. Instellingen gaan uitsluitend via `config.json` en worden
  als **gegevens** gelezen (nooit als code uitgevoerd: geen `Invoke-Expression`, geen dynamische scriptblokken).
  Een test controleert dit.

## Windows Taakplanner en gebruikersrechten

**Keuze: één taak per gebruiker, onder het account van die gebruiker, zonder verhoogde rechten.**

| Instelling | Waarde | Waarom |
|---|---|---|
| Account | huidige gebruiker | Bureaublad en Downloads zijn per gebruiker. |
| Aanmeldtype | `InteractiveToken` | Draait alleen als de gebruiker is aangemeld; er wordt **geen wachtwoord opgeslagen**. |
| Rechtenniveau | `LeastPrivilege` | Nooit verhoogd. Script en config staan in een map die de gebruiker zelf kan wijzigen; een verhoogde taak zou dan een manier zijn om administratorrechten te krijgen. |
| Triggers | dagelijks op tijdstip en/of bij aanmelden (+2 min. vertraging) | Instelbaar; combinaties mogelijk. |
| `StartWhenAvailable` | aan | Gemiste dagelijkse uitvoering (pc stond uit) wordt ingehaald. |
| Meerdere instanties | `IgnoreNew` | Nooit twee keer tegelijk. Het script heeft daarnaast een eigen mutex. |
| Maximale looptijd | 30 minuten | Vastgelopen uitvoering wordt gestopt. |
| Actie | `conhost.exe --headless powershell.exe -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File …cleanup.ps1 -ConfigPath …config.json -Mode Scheduled` | Geen zichtbaar venster; vaste argumenten. |
| Map in Taakplanner | hoofdmap, naam `PraktijkClean - <gebruikersnaam>` | Aanmaken in de hoofdmap lukt zonder administratorrechten. |

De taak wordt aangemaakt via de officiële COM-interface van Taakplanner (`Schedule.Service`).
Installeren, opslaan, pauzeren en deïnstalleren vragen **geen** administratorrechten; de app vraagt
daar ook nooit om (`asInvoker` in het manifest).

### PowerShell-uitvoeringsbeleid

Het standaardbeleid op Windows 11-clients is `Restricted`, waardoor geen enkel script mag draaien.
PraktijkClean geeft daarom bij het starten `-ExecutionPolicy RemoteSigned` mee. Dat geldt alleen voor dat
ene proces en staat alleen lokale scripts toe (het script wordt lokaal door de app geschreven, dus zonder
internetmarkering). **Een beleid dat via Groepsbeleid of Intune is ingesteld (MachinePolicy/UserPolicy)
gaat altijd voor** en wordt dus gerespecteerd. Als een organisatie `AllSigned` afdwingt, moet het script
digitaal ondertekend zijn (zie [ONDERTEKENING.md](ONDERTEKENING.md)); anders weigert Windows het en wordt
er niets verwijderd. `Bypass` wordt nooit gebruikt in de geplande taak.

## Bij afmelden

Onderzocht en **bewust niet geïmplementeerd** (uitgeschakeld in de interface, met uitleg):

1. **Taakplanner** heeft triggers voor aanmelden, vergrendelen, ontgrendelen en verbinding verbreken,
   maar **geen trigger voor afmelden**.
2. **Gebeurtenis 4647** ("gebruiker heeft afmelden gestart") in het beveiligingslogboek kan als trigger
   dienen, maar alleen als een beheerder het controlebeleid "Aanmelden/afmelden controleren" aanzet. De
   sessie wordt op dat moment afgesloten, waardoor Windows de taak kan afbreken. Niet betrouwbaar.
3. **Afmeldscript via Groepsbeleid** (Gebruikersconfiguratie > Windows-instellingen > Scripts > Afmelden)
   werkt wel bij een normaal afmelden of afsluiten, maar vereist een beheerder en Windows Pro/Enterprise of
   een domein. Het draait niet bij stroomuitval, slaapstand of hard uitzetten. Een beheerder kan dit zelf
   instellen (zie README, "Afmelden via Groepsbeleid"); het script respecteert dan gewoon simulatie en pauze.

Omdat werkplekken vaak worden uitgezet zonder netjes af te melden, combineert PraktijkClean
**"dagelijks" (met inhaalfunctie)** en **"bij aanmelden"**. Daarmee wordt een werkplek die 's avonds
uitgaat de volgende ochtend bij aanmelden alsnog opgeschoond.

## Bestandsleeftijd

**Regel: leeftijd = nu − de nieuwste van (aanmaakdatum, wijzigingsdatum).**

- Een gedownload of gekopieerd bestand krijgt op deze pc een nieuwe **aanmaakdatum**, ook als de
  wijzigingsdatum oud is. Het wordt dus niet direct als oud gezien.
- Een bestand waaraan vandaag is gewerkt, heeft een nieuwe **wijzigingsdatum**.
- Datums in de toekomst (verkeerde klok) gelden als "niet oud".
- Daarnaast geldt altijd een **minimale leeftijd** (standaard 15 minuten), ook bij "Direct opruimen", zodat
  een bestand dat net wordt opgeslagen nooit wordt verwijderd.
- De laatste-toegangsdatum wordt niet gebruikt: Windows werkt die vaak niet bij.

Beperking: een bestand dat binnen dezelfde schijf wordt **verplaatst** (niet gekopieerd), houdt zijn oude
datums. Zie [BEPERKINGEN.md](BEPERKINGEN.md).

## Veiligheidslagen in het script

1. **Configuratie**: strikt gecontroleerd. Bij elke fout (ongeldige JSON, ontbrekend veld, verkeerd type,
   waarde buiten bereik) wordt er **niets** verwijderd. Ontbrekende waarden krijgen nooit een standaardwaarde
   (een ontbrekende bewaartermijn wordt dus niet 0).
2. **Mappen**: alleen expliciet ingestelde mappen; volledig pad verplicht; geen `..`, geen apparaatpaden,
   geen stationswortel, geen korte 8.3-namen (zoals `PROGRA~1`, waarmee de controle anders omzeild zou
   kunnen worden). Geweigerd: Windows, Program Files, ProgramData, AppData, de PraktijkClean-map zelf,
   `C:\Users`, het gebruikersprofiel zelf, Openbaar, en alle bovenliggende mappen daarvan.
3. **Locatie gewijzigd**: per map wordt het pad opgeslagen dat de gebruiker heeft gezien (`expectedPath`).
   Wijst Bureaublad/Downloads later ergens anders heen (bijv. na het aanzetten van OneDrive-back-up), dan
   slaat het script die map over totdat de instellingen opnieuw zijn opgeslagen.
4. **Koppelingen**: symbolische links, junctions en andere reparse points worden nooit gevolgd (map zelf,
   bovenliggende mappen, submappen en bestanden).
5. **OneDrive/cloud en netwerk**: herkend via OneDrive-omgevingsvariabelen en register, de Windows
   Cloud Files-registratie (ook Dropbox, Google Drive, iCloud), cloudbestandsattributen, mapnamen,
   UNC-paden en niet-vaste stations. Standaard geblokkeerd; alleen per map te deblokkeren na waarschuwing.
6. **Bestanden**: overgeslagen als ze verborgen, systeem of alleen-lezen zijn, een koppeling/cloud-placeholder
   zijn, een lopende download zijn (`.crdownload`, `.part`, …), een Office-tijdelijk bestand (`~$…`), of
   in gebruik zijn (exclusief openen mislukt). Vlak vóór het verwijderen wordt alles opnieuw gecontroleerd.
7. **Nooit mappen verwijderen**. Submappen worden standaard niet eens doorzocht.
8. **Limiet**: komen er meer bestanden in aanmerking dan het maximum (standaard 200), dan wordt er in die
   uitvoering **helemaal niets** verwijderd en wordt een fout gemeld.
9. **Bevestiging**: automatische verwijdering gebeurt alleen als `simulationMode` uit staat **én**
   `automaticCleanupConfirmed` aan staat. Die laatste wordt alleen gezet na de bevestigingsdialoog in de app.
   "Nu opruimen" vereist de expliciete schakelaar `-ConfirmedByUser`, die alleen na een bevestigingsdialoog
   wordt meegegeven.
10. **Installatie/configuratie verwijdert nooit iets**; de app zelf verwijdert nooit gebruikersbestanden.
11. **Logboek** bevat nooit bestandsnamen, paden of foutteksten van Windows (die kunnen paden bevatten);
   alleen aantallen en het soort fout.

## Verwijderen: Prullenbak of definitief

Instelbaar. Standaard (bij een nieuwe installatie, sinds versie 1.2.0) gaan bestanden na 1 dag **naar de
Prullenbak** en leegt PraktijkClean de Prullenbak **na nog 1 dag** (`emptyRecycleBin` aan, termijn 1 dag).
Afweging: een vergissing is dan nog een dag terug te halen, terwijl gegevens niet ongemerkt in de Prullenbak
blijven staan. In 1.1.0 was "definitief" de standaard; bestaande instellingen worden bij een update niet
veranderd (een ontbrekend veld krijgt de voorzichtige waarde: Prullenbak legen uit). "Definitief" gebruikt de gewone
Windows-verwijderfunctie. In beide gevallen is er **geen sprake van veilig vernietigen**: gegevens kunnen
met speciale software soms worden teruggehaald en er kunnen elders kopieën zijn. PraktijkClean doet daarover
geen AVG-claims.

## Prullenbak legen

Optioneel (`emptyRecycleBin`; bij een nieuwe installatie aan met een termijn van 1 dag, bij een ontbrekend veld uit). Windows bewaart de Prullenbak per schijf en per gebruiker in
`<schijf>:\$Recycle.Bin\<SID>`. Elk item bestaat uit een `$I`-bestand (oorspronkelijk pad, grootte, tijdstip van
verwijderen) en een `$R`-bestand of -map met de inhoud. Het script leest de `$I`-bestanden zelf (versie 1 en 2),
zodat items op **tijd in de Prullenbak** worden geselecteerd; `SHEmptyRecycleBin` kan alleen alles tegelijk legen.

- Alleen de eigen Prullenbakmap van de huidige gebruiker (eigen SID) op lokale vaste schijven.
- Dezelfde poorten als bij de mappen: alleen bij een geplande uitvoering met actieve automatische opschoning
  of bij "Nu opruimen" met bevestiging. Simulatie telt alleen; bij pauze gebeurt niets; bij overschrijding van
  de limiet wordt ook de Prullenbak niet geleegd. De limiet zelf geldt niet voor Prullenbak-items.
- `minimumAgeMinutes` geldt ook hier: iets wat net is weggegooid blijft altijd staan.
- Verwijderde mappen in de Prullenbak worden recursief verwijderd, maar **koppelingen (junctions/symbolische
  links) worden nooit gevolgd**: alleen de koppeling zelf wordt verwijderd.
- Eerst de `$R`-inhoud, daarna het `$I`-bestand; mislukt de inhoud, dan blijft het item zichtbaar in de Prullenbak.
- Logboek en status bevatten alleen aantallen; de oorspronkelijke paden verschijnen alleen in het
  simulatievenster.

## Icoon in het systeemvak (optioneel)

Standaard uit; aan te zetten met een vinkje in het statusvak van de app. Dan:

- kopieert de app zichzelf naar `%LOCALAPPDATA%\PraktijkClean\PraktijkClean.exe`. Het gedownloade bestand staat
  vaak in Downloads, dat PraktijkClean zelf opschoont; de installatiemap wordt nooit opgeschoond.
- zet de app die kopie met `/tray` in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (per gebruiker,
  geen administratorrechten nodig), zodat het icoon bij elke aanmelding start.
- leest het icoon elke 20 seconden `config.json`, `status.json`, het logboek en de geplande taak, en toont
  groen (aan), blauw (simulatie), oranje (gepauzeerd), rood (aan, maar de taak ontbreekt of staat uit) of grijs
  (niet ingesteld), met een eigen teken per status. Het icoon verwijdert zelf nooit iets; het kan alleen de app
  openen, pauzeren/hervatten en zichzelf verbergen.
- werkt de app bij het openen de kopie bij als die een andere versie is, en sluit hij het icoon af via een
  benoemd signaal (`Local\PraktijkClean-TrayExit`). Bij "Volledig verwijderen" worden opstartitem en kopie weggehaald.

Een tweede start van de app (bijvoorbeeld via het icoon) haalt het al geopende venster naar voren
(`Local\PraktijkClean-Show`). Het venster leest bij activeren de opgeslagen instellingen opnieuw in, zodat een
pauze die via het icoon is gezet niet door 'Instellingen opslaan' ongedaan wordt gemaakt.
