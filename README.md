# PraktijkClean

Gratis, open-source hulpmiddel voor huisartsenpraktijken dat oude bestanden automatisch opruimt van het
**Bureaublad** en uit **Downloads** (en eventueel andere gekozen mappen), zodat patiëntbrieven, labuitslagen
en PDF's daar niet onbedoeld blijven staan.

- Kleine Windows-app om in te stellen; daarna doet **Windows Taakplanner** het werk. De app hoeft niet open
  te blijven en er draait geen achtergronddienst.
- Geen administratorrechten nodig, alles per gebruiker.
- Na installatie staat alles in **simulatiemodus**: er wordt pas iets verwijderd nadat u dat expliciet
  bevestigt.

> **Status: versie 1.3.0, getest op twee Windows 11-testcomputers, nog niet in de praktijk.** Lees eerst
> [docs/BEPERKINGEN.md](docs/BEPERKINGEN.md) en probeer PraktijkClean op een testcomputer voordat u het
> op werkplekken gebruikt.

> **Verwijderen is niet hetzelfde als veilig vernietigen.** PraktijkClean verwijdert bestanden op de gewone
> Windows-manier (standaard via de Prullenbak, die PraktijkClean daarna zelf leegt; instelbaar: direct definitief). Het is een hulpmiddel tegen onbedoeld achterblijvende
> bestanden, geen garantie voor AVG-conforme of onherstelbare vernietiging.

---

## Inhoud

1. [Gebruik (voor de praktijk)](#1-gebruik-voor-de-praktijk)
2. [Hoe het werkt](#2-hoe-het-werkt)
3. [Ontwikkelen: openen, bouwen, testen](#3-ontwikkelen-openen-bouwen-testen)
4. [Zelf iets aanpassen](#4-zelf-iets-aanpassen)
5. [Beheer en probleemoplossing](#5-beheer-en-probleemoplossing)
6. [Licentie](#6-licentie)

---

## 1. Gebruik (voor de praktijk)

### Starten

1. Download `PraktijkClean-<versie>.exe` (bijvoorbeeld `PraktijkClean-1.3.0.exe`) van de pagina
   [Releases](https://github.com/bduineveld/PraktijkClean/releases/latest) (of uit de map [`dist`](dist)).
   Het is één bestand van ongeveer 500 kB; er hoeft niets bij geïnstalleerd te worden (het gebruikt het
   .NET Framework 4.8 dat standaard in Windows 10 en 11 zit).
2. Start het programma **als de gebruiker van die werkplek** (niet "Als administrator uitvoeren").
3. Ziet u "Windows heeft uw pc beschermd"? Klik op **Meer info → Toch uitvoeren**. Dat komt doordat het
   programma nog niet digitaal is ondertekend (zie [docs/ONDERTEKENING.md](docs/ONDERTEKENING.md)).

Bij de eerste keer **Opslaan** zet PraktijkClean zichzelf voor deze gebruiker neer in
`%LOCALAPPDATA%\PraktijkClean` (zonder administratorrechten), met een snelkoppeling in het **Startmenu** en een
vermelding bij **Instellingen > Apps > Geïnstalleerde apps** (daar kan het ook worden verwijderd). Het
gedownloade bestand is daarna niet meer nodig. Een nieuwere versie installeert u door die gewoon te openen en op
**Opslaan** te klikken; de instellingen blijven behouden.

### De startwizard

Bij de allereerste start (er is nog geen `config.json`) opent een korte wizard in drie stappen:

1. **Welke mappen**: Bureaublad en/of Downloads, en eventueel andere mappen.
2. **Hoe lang bewaren** (1 dag, 1 week of 1 maand) en **wat er met submappen gebeurt** (zie hieronder).
3. **Waarheen en wanneer**: naar de Prullenbak (en die daarna legen) of direct definitief; elke dag op een
   tijdstip en/of bij aanmelden.

Daarna ziet u een overzicht met twee vinkjes onder *PraktijkClean snel openen* (allebei standaard aan):
**Snelkoppeling op het bureaublad** en **Vastmaken aan de taakbalk**. Bij vastmaken toont Windows zelf nog een vraag
ter bevestiging; op oudere Windows-versies die dat niet ondersteunen, legt PraktijkClean uit hoe het met de
rechtermuisknop kan. Beide kan later ook op het tabblad *Voorkeuren*, onder *Snelkoppelingen*.
Dan klikt u op **Opslaan en simulatie bekijken**: de instellingen worden opgeslagen in
simulatiemodus en de simulatie opent meteen. Er wordt niets verwijderd tot u zelf automatisch opruimen aanzet.
De gekozen bewaartermijn en submappen worden ook de standaard voor mappen die u later toevoegt. Met **Overslaan**
gaat u verder met de standaardinstellingen en de stappen hieronder. Bij een beschadigde `config.json` verschijnt
de wizard niet (dan ziet u een melding).

### In vijf stappen instellen

1. **Controleer de mappen** (tabblad *Mappen*). Bureaublad en Downloads staan klaar met "ouder dan 1 dag": wat
   langer dan een dag op het bureaublad of in Downloads staat, gaat naar de Prullenbak, en wat daarna nog een dag
   in de Prullenbak staat, wordt definitief verwijderd. Klik een map aan voor de bewaartermijn, wat er met
   submappen gebeurt (zie hieronder) en uitzonderingen.
2. **Kies het moment** (tabblad *Planning en veiligheid*): dagelijks op een tijdstip en/of bij aanmelden.
3. Klik rechtsonder op **Opslaan**. Het script en de geplande taak worden geïnstalleerd, in simulatiemodus.
   Er wordt niets verwijderd.
4. Klik linksonder op **Simulatie bekijken…** en bekijk welke bestanden in aanmerking komen en welke worden
   overgeslagen. Moet een bestand blijven staan? Klik er met de **rechtermuisknop** op (of gebruik de knop
   **Uitsluiten**) en kies "nooit verwijderen", "alle .pdf-bestanden nooit verwijderen" of "submap overslaan".
   Dat wordt meteen opgeslagen.
5. Tevreden? Klik rechtsboven op **Automatisch opruimen aanzetten…**. U ziet nogmaals een overzicht en moet bevestigen.

### Het venster

- **Bovenaan** staat de status in een gekleurde kop, met rechts één knop voor wat op dat moment het meest voor de
  hand ligt (*Automatisch opruimen aanzetten…*, *Pauzeren*, *Hervatten* of *Herstellen*) en het menu **☰ Meer**.
- **In het midden** drie tabbladen: *Mappen*, *Planning en veiligheid* en *Logboek*.
- **Onderaan** alleen **Simulatie bekijken…** en **Opslaan**. Wijzigingen in de tabbladen gelden pas na *Opslaan*;
  onderaan staat dan "Niet-opgeslagen wijzigingen", met **Ongedaan maken** om terug te gaan naar wat er was opgeslagen.

| Onder ☰ Meer | Wat het doet |
|---|---|
| Nu opruimen… | Direct opruimen, na een overzicht en bevestiging. |
| Tijdelijk pauzeren / Hervatten | Zet de geplande taak tijdelijk uit of weer aan. |
| Terug naar simulatiemodus | Zet het echte verwijderen uit (alleen zichtbaar als het aan staat). |
| Logboek openen | Toont het logboek (zonder bestandsnamen) in Kladblok. Welke bestanden zijn verwijderd, ziet u op het tabblad *Logboek*. |
| Problemen oplossen → Geplande taak en script opnieuw instellen | Zet script en taak opnieuw neer met de huidige instellingen. Dit is **geen** terugzetten naar de standaardinstellingen. |
| Problemen oplossen → Programmamap openen | Opent `%LOCALAPPDATA%\PraktijkClean`. |
| PraktijkClean verwijderen… | Verwijdert taak, script, instellingen, logboek, icoon, de snelkoppelingen (Startmenu, bureaublad, taakbalk) en de vermelding bij Apps. Nooit uw eigen bestanden. |

### Staat het aan?

De kop bovenaan is **groen = aan** (er wordt automatisch opgeruimd), **blauw = simulatie** (alleen tellen),
**oranje = gepauzeerd**, **rood = actie nodig** (aan, maar de geplande taak ontbreekt of staat uit; klik op
*Herstellen*), **grijs** = nog niet ingesteld. Eronder staan wat er de **afgelopen 7 dagen** is opgeruimd, de
laatste en de volgende controle. Het tabblad *Logboek* geeft meer details.

Rechtsonder in het systeemvak staat het icoon van PraktijkClean (bij een nieuwe installatie staat het aan; uit te
zetten op het tabblad *Voorkeuren*, met een plaatje van waar het staat). Het icoon heeft
dezelfde kleuren (met een teken: ✓ aan, ○ simulatie, ‖ gepauzeerd, ! probleem), toont bij aanwijzen wat er
vandaag is opgeruimd en opent PraktijkClean met een klik; via de rechtermuisknop kunt u pauzeren of het icoon
verbergen. Windows zet nieuwe icoontjes eerst achter het pijltje (^); sleep het naar de taakbalk om het altijd
te zien. Het icoon start bij elke aanmelding.

Het **logboek** staat in `%LOCALAPPDATA%\PraktijkClean\logs\praktijkclean.log`: één regel per controle met
de aantallen, nooit bestandsnamen. Boven 512 kB wordt het oude deel bewaard als `praktijkclean.1.log`.

### Welke bestanden zijn verwijderd?

Bovenaan het tabblad **Logboek** staat de lijst **Verwijderde bestanden**: wanneer, welk bestand, uit welke map en
wat er gebeurde (naar de Prullenbak, definitief verwijderd of Prullenbak geleegd). Met **Zoeken** filtert u op
(een deel van) de naam. Dubbelklik op een bestand dat naar de Prullenbak is gegaan om de Prullenbak te openen en
het terug te zetten.

Omdat bestandsnamen patiëntgegevens kunnen bevatten, blijven ze maar een beperkte tijd staan. Onder de lijst kiest
u bij **Namen bewaren** *1 week* (standaard), *2 weken*, *1 maand*, *3 maanden* of *Niet bijhouden*; dit geldt na
*Opslaan*. Oudere regels worden bij elke controle automatisch gewist, en na *Opslaan* direct. Met **Lijst wissen…**
maakt u de lijst meteen leeg (de bestanden zelf veranderen daardoor niet). De lijst staat in
`%LOCALAPPDATA%\PraktijkClean\logs\verwijderde-bestanden.log` en wordt bij het verwijderen van PraktijkClean ook
verwijderd.

### Submappen: Licht, Midden of Zwaar

Per map kiest u op het tabblad *Mappen* bij **Submappen** een van drie mogelijkheden in een keuzelijst. Wilt u
uitleg? **Dubbelklik op de map** in de lijst (of klik op *Uitleg en voorbeelden…*): dan opent een venster met de
bewaartermijn en drie grote kaarten. Elke kaart laat met een klein voorbeeld zien wat er gebeurt: wat **rood
doorgestreept** is, wordt opgeruimd als het ouder is dan de bewaartermijn. De streepjes rechtsboven geven aan hoe
ver het gaat.

- **Alleen deze map** (Licht, standaard): alleen bestanden direct in de map. Submappen en alles daarin
  blijven staan. Handig voor gebruikers: zet wat moet blijven in een submap, bijvoorbeeld `Bureaublad\Bewaren`.
- **Ook bestanden in submappen** (Midden): ook oude bestanden in submappen worden opgeruimd; de submappen zelf
  blijven altijd staan.
- **Daarna ook lege submappen** (Zwaar): eerst worden, net als bij Midden, oude bestanden in alle submappen
  verwijderd; daarna worden submappen die leeg zijn (geworden of al waren) ook verwijderd, als ze ouder zijn dan de bewaartermijn. Een submap waar nog iets in staat (een nieuwer, verborgen of
  uitgesloten bestand) blijft altijd staan.

Naar een zwaarder niveau gaan vraagt eerst om bevestiging. Let bij Zwaar op vaste mappen die leeg mogen zijn,
zoals een map waar de scanner naartoe schrijft: zet die als submap bij *Nooit verwijderen*, of klik in het
simulatievenster met de rechtermuisknop op de lege submap en kies "altijd laten staan".

### Een map toevoegen en de standaard voor nieuwe mappen

Na **Map toevoegen…** en het kiezen van de map opent hetzelfde venster met uitleg (bewaartermijn en de drie
kaarten), al ingevuld met de standaard voor nieuwe mappen. Die standaard stelt u in op het tabblad **Voorkeuren**
(bij een nieuwe installatie 30 dagen en *Alleen deze map*). Mappen die al in de lijst staan, veranderen daardoor
niet. Zoals elke wijziging geldt dit pas na *Opslaan*.

### Uitzonderingen

Per map staat op het tabblad *Mappen* een lijst **Nooit verwijderen (uitzonderingen)**. Met **Toevoegen ▾** kiest u:

- **Bestand(en) uit deze map kiezen…**
- **Submap kiezen…**: een submap overslaan en nooit verwijderen (alleen van belang bij Midden of Zwaar).
- **Extensie, naam of patroon typen…**: een extensie (`.docx`), een bestandsnaam (`Bewaren.pdf`) of een patroon
  met `*` (willekeurige tekst) of `?` (één teken), bijvoorbeeld `protocol-*`.

**Verwijderen** (of de Delete-toets) haalt de geselecteerde uitzondering weg. Sneller: in het simulatievenster
met de rechtermuisknop op een bestand klikken (zie hierboven).

### Wat wordt nooit verwijderd

- Bestanden jonger dan de bewaartermijn. **Leeftijd** = tijd sinds de nieuwste van aanmaak- en
  wijzigingsdatum, dus een net gedownload bestand is altijd "nieuw". Bovendien nooit iets jonger dan 15 minuten.
- Snelkoppelingen (`.lnk`, `.url`), verborgen en systeembestanden, alleen-lezenbestanden, geopende bestanden,
  lopende downloads en tijdelijke Office-bestanden.
- **Mappen.** Submappen worden standaard niet eens bekeken (Licht). Alleen bij Zwaar worden lege submappen
  verwijderd; een map waar nog iets in staat nooit, en de ingestelde map zelf ook nooit.
- Alles op **OneDrive/cloud- of netwerklocaties** (standaard geblokkeerd, zie hieronder).
- Alles in systeemmappen, Program Files, AppData en dergelijke.
- Als er meer bestanden (en lege submappen) in aanmerking komen dan het ingestelde maximum (standaard 200): dan
  wordt er in die ronde helemaal niets verwijderd.

### De Prullenbak en definitief verwijderen

Standaard verplaatst PraktijkClean oude bestanden eerst **naar de Prullenbak** (dan is een vergissing nog terug
te halen) en leegt het **na nog 1 dag** de Prullenbak, zodat patiëntgegevens daar niet blijven staan. Onder
"Veiligheid" kunt u ook kiezen voor "Definitief verwijderen" (direct weg, niet via Windows terug te zetten).

Met **"Prullenbak automatisch legen"** (standaard aan, na 1 dag) leegt PraktijkClean bij elke automatische opschoning
en bij "Nu opruimen" ook de Prullenbak van de gebruiker, op alle lokale schijven. Alleen items die langer
dan het ingestelde aantal dagen in de Prullenbak staan worden verwijderd (0 = alles, maar nooit iets dat
korter dan 15 minuten geleden is weggegooid). Let op: dit geldt ook voor bestanden die de gebruiker zelf heeft
weggegooid. In simulatiemodus wordt alleen geteld; de simulatie toont welke items in aanmerking komen.

### OneDrive en netwerkmappen

Vaak wordt het bureaublad met OneDrive gesynchroniseerd of naar een netwerkschijf omgeleid. PraktijkClean
herkent dit en **blokkeert opschonen daar standaard**. Een bestand dat u in een gesynchroniseerde map
verwijdert, verdwijnt namelijk ook uit de cloud en van alle andere apparaten. Alleen na een duidelijke
waarschuwing kan dit per map worden toegestaan ("Opschonen op deze OneDrive-/netwerklocatie toestaan").
Overleg dit met uw systeembeheerder.

### Nieuwe versies

Hooguit eens per 20 uur kijkt PraktijkClean op GitHub of er een nieuwe versie is. Zo ja, dan verschijnt bovenin een
blauwe balk met **Bijwerken…**. Pas na die klik wordt de nieuwe versie gedownload; die wordt alleen geïnstalleerd
als de digitale handtekening van PraktijkClean klopt. Instellingen, taak en logboek blijven behouden. Uitzetten via
**☰ Meer → Automatisch controleren op nieuwe versies**. Zie [docs/BIJWERKEN.md](docs/BIJWERKEN.md).

### Waarom niet "bij afmelden"?

Windows Taakplanner kent geen betrouwbare afmeldtrigger. PraktijkClean gebruikt daarom **dagelijks** (een
gemiste controle wordt ingehaald zodra de pc weer aan staat) en **bij aanmelden**. Zo wordt ook een pc die
's avonds gewoon wordt uitgezet de volgende ochtend opgeschoond. Details en alternatieven:
[docs/ARCHITECTUUR.md](docs/ARCHITECTUUR.md#bij-afmelden).

---

## 2. Hoe het werkt

```
PraktijkClean.exe  ──schrijft──►  %LOCALAPPDATA%\PraktijkClean\cleanup.ps1   (vast script)
                   ──schrijft──►  %LOCALAPPDATA%\PraktijkClean\config.json   (instellingen)
                   ──maakt────►  Taakplanner: "PraktijkClean - <gebruiker>"
Taakplanner        ──start────►  cleanup.ps1  ──schrijft──► status.json en logs\praktijkclean.log
```

- Het **script** (`script/cleanup.ps1`, Windows PowerShell 5.1) is de enige plek waar bestanden worden
  verwijderd. Het is generiek: bij het wijzigen van instellingen verandert alleen `config.json`.
- De **app** (C# WinForms, .NET Framework 4.8 — standaard aanwezig in Windows 10/11) beheert instellingen, script en geplande taak.
- De **geplande taak** draait als de gebruiker zelf, nooit met verhoogde rechten, en slaat geen wachtwoord op.

Meer: [docs/ARCHITECTUUR.md](docs/ARCHITECTUUR.md) · alle instellingen: [docs/CONFIGURATIE.md](docs/CONFIGURATIE.md)

---

## 3. Ontwikkelen: openen, bouwen, testen

### Benodigd (alles gratis)

- **Windows 11** (om de app te bouwen en te testen).
- **.NET SDK 8 of nieuwer** (alleen om te bouwen) — te downloaden via de officiële .NET-website van Microsoft
  (`dotnet.microsoft.com`). De app zelf gebruikt .NET Framework 4.8; de benodigde referentiebestanden haalt
  de SDK bij de eerste bouw automatisch op via NuGet.
- Een editor naar keuze:
  - **Visual Studio Community**, workload ".NET-desktopontwikkeling"; of
  - **Visual Studio Code** met de extensie "C# Dev Kit".
- Optioneel **Git** en een GitHub-account.

### Openen

- Visual Studio: open `PraktijkClean.sln`.
- VS Code: open de projectmap.

### Bouwen, testen en de .exe maken (aanbevolen)

Open PowerShell in de projectmap en voer uit:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Dit doet achter elkaar:
1. de **scripttests** onder Windows PowerShell 5.1;
2. de **C#-tests**;
3. het maken van **`dist\PraktijkClean-<versie>.exe`** (één los bestand van ongeveer 500 kB; de werkplek heeft
   alleen het standaard .NET Framework 4.8 van Windows nodig) plus een SHA-256-controlegetal. De versie in de
   bestandsnaam komt uit `<Version>` in `src/PraktijkClean/PraktijkClean.csproj`.

(`-ExecutionPolicy Bypass` geldt alleen voor dat ene bouwproces op uw ontwikkelcomputer.)

### Losse commando's

```powershell
# Alleen de scripttests (maken uitsluitend tijdelijke testmappen met fictieve bestanden)
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Script\Run-ScriptTests.ps1

# Alleen de C#-tests (optioneel met filter, bijv. "Installer")
dotnet run --project .\tests\PraktijkClean.Tests -c Release
dotnet run --project .\tests\PraktijkClean.Tests -c Release -- Installer

# De app starten tijdens het ontwikkelen
dotnet run --project .\src\PraktijkClean
```

De tests gebruiken geen externe testpakketten en raken nooit uw echte Bureaublad, Downloads of Documenten:
alles gebeurt in `%TEMP%\PraktijkCleanTest-<willekeurig>` en die map wordt na afloop verwijderd. Eén test
maakt een tijdelijke, uitgeschakelde taak `PraktijkClean-TEST-…` in Taakplanner aan en verwijdert die direct.

### Bouwen zonder eigen Windows-ontwikkelomgeving: GitHub Actions

Zet het project op GitHub. De workflow in `.github/workflows/build.yml` draait dan bij elke wijziging alle
tests op een Windows-machine van GitHub en levert `PraktijkClean-<versie>.exe` op onder **Actions → (laatste run) →
Artifacts**. Voor openbare projecten is dit gratis.

### Wat te doen bij een nieuwe versie

1. Pas `<Version>` aan in `src/PraktijkClean/PraktijkClean.csproj` en, als het script wijzigt,
   `$script:PcVersion` in `script/cleanup.ps1`.
2. Voer `build.ps1` uit.
3. De .exe ondertekenen met `tools/sign-release.sh` en een GitHub-release maken met de .exe, het `.sha256`- en
   het `.sig`-bestand ([docs/BIJWERKEN.md](docs/BIJWERKEN.md)). Installaties vanaf 1.4.0 bieden de nieuwe versie
   dan zelf aan en werken bij het bijwerken ook het script en de taak bij.
4. Wie de nieuwe .exe zelf downloadt en opent, krijgt het nieuwe script zodra men op **Opslaan** klikt of
   **☰ Meer → Problemen oplossen → Geplande taak en script opnieuw instellen** kiest (de app meldt een afwijkend
   script vanzelf).

---

## 4. Zelf iets aanpassen

Kleine aanpassingen kunnen zonder diepe programmeerkennis. Na elke wijziging: `build.ps1` uitvoeren.

| Wat | Waar |
|---|---|
| Langere teksten en uitleg in de app | `src/PraktijkClean/Texts.cs` |
| Knopteksten, opbouw van het scherm | `src/PraktijkClean/MainForm.cs`, methode `BuildLayout` en de velden bovenaan |
| Standaardinstellingen (1 dag, Prullenbak na 1 dag legen, `.lnk`/`.url`, simulatiemodus) | `src/PraktijkClean.Core/ConfigStore.cs` (`DefaultRetentionDays`, `DefaultRecycleBinDays`, `CreateDefault`, `DefaultExclusions`) |
| Keuzes in de lijst "Bewaartermijn" | `src/PraktijkClean.Core/RetentionOptions.cs` |
| Wanneer/hoe de geplande taak draait | `src/PraktijkClean.Core/TaskXmlBuilder.cs` |
| Wat het script altijd overslaat | `script/cleanup.ps1`, bovenaan (`PcAlwaysSkipNames`, `PcInProgressExtensions`) |

**Voorbeeld — standaard bewaartermijn naar 14 dagen:** in `ConfigStore.cs` `DefaultRetentionDays = 1`
wijzigen in `DefaultRetentionDays = 14`. Pas ook de test `StandaardConfiguratieIsGeldigEnVeilig`
in `tests/PraktijkClean.Tests/ConfigTests.cs` aan; die controleert bewust de standaardwaarde.

**Voorbeeld — een tekst in de app wijzigen:** zoek de tekst op in `Texts.cs` (of `MainForm.cs`) en pas hem
aan tussen de aanhalingstekens.

**Let op bij het script:** houd `cleanup.ps1` in gewone ASCII-tekens (geen é, ë, enz. — een test controleert
dit), zodat Windows PowerShell 5.1 het altijd goed leest. Gebruik nooit `Invoke-Expression`.

---

## 5. Beheer en probleemoplossing

### Bestanden van PraktijkClean

`%LOCALAPPDATA%\PraktijkClean\` (bijv. `C:\Users\<naam>\AppData\Local\PraktijkClean`):
`PraktijkClean.exe`, `cleanup.ps1`, `config.json`, `status.json`, `logs\praktijkclean.log`,
`logs\verwijderde-bestanden.log` en tijdelijk `update\` (een download bij het bijwerken). Instellingen voor het
bijwerken staan in het register onder `HKCU\Software\PraktijkClean`.

### Uitvoeringsbeleid (Execution Policy)

De geplande taak start het script met `-ExecutionPolicy RemoteSigned`, alleen voor dat proces. Een beleid
dat de organisatie via Groepsbeleid of Intune afdwingt, gaat altijd voor. Staat dat op `AllSigned`, dan moet
het script digitaal ondertekend zijn ([docs/ONDERTEKENING.md](docs/ONDERTEKENING.md)); anders weigert Windows
het script en wordt er niets verwijderd. De app toont dan een foutmelding bij de simulatie.

### Het script handmatig controleren

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File "$env:LOCALAPPDATA\PraktijkClean\cleanup.ps1" -Mode Simulate -Json
```

`-Mode Simulate` verwijdert nooit iets. Exitcodes: 0 = gelukt, 1 = configuratiefout (of `PraktijkClean.exe` ontbreekt), 2 = fouten,
3 = limiet overschreden (niets verwijderd), 4 = er draaide al een opschoning.

### Afmelden via Groepsbeleid (alleen voor systeembeheerders)

Wie toch bij afmelden wil opschonen, kan als beheerder een **afmeldscript** instellen via
Gebruikersconfiguratie > Windows-instellingen > Scripts (aanmelden/afmelden) > Afmelden > tabblad
PowerShell-scripts:

- Script: `%LOCALAPPDATA%\PraktijkClean\cleanup.ps1`
- Parameters: `-Mode Scheduled -Trigger Afmelden`

Het script respecteert dan gewoon simulatiemodus, pauze, limiet en alle veiligheidsregels. Test dit eerst;
het draait niet bij stroomuitval, slaapstand of hard uitzetten.

### Deïnstalleren

In de app: **☰ Meer → PraktijkClean verwijderen…**, of via **Instellingen > Apps > Geïnstalleerde apps >
PraktijkClean > Verwijderen**. Een eventueel nog aanwezig gedownload `PraktijkClean-<versie>.exe` kan daarna
zelf worden weggegooid.
Daarbij verdwijnen ook de geplande taak, de instellingen, het logboek en de registerinstellingen.

Is alleen `PraktijkClean.exe` (of de hele map) met de hand weggegooid, dan staat de geplande taak er nog. Uit
voorzorg verwijdert die dan **niets** meer: het script ziet dat het programma ontbreekt, stopt met de melding
"programma ontbreekt" en exitcode 1. Ruim de rest op door in Taakplanner de taak `PraktijkClean - <gebruiker>` te
verwijderen en daarna de map `%LOCALAPPDATA%\PraktijkClean` (als die er nog is).

### Meer

- [docs/ARCHITECTUUR.md](docs/ARCHITECTUUR.md) — opbouw, rechten, veiligheidslagen
- [docs/CONFIGURATIE.md](docs/CONFIGURATIE.md) — alle velden van `config.json`
- [docs/BEPERKINGEN.md](docs/BEPERKINGEN.md) — bekende beperkingen en niet-geteste onderdelen
- [docs/BIJWERKEN.md](docs/BIJWERKEN.md) — bijwerken met één klik, releasesleutel, uitzetten via beleid
- [docs/ONDERTEKENING.md](docs/ONDERTEKENING.md) — SmartScreen, Defender en digitale handtekening

---

## 6. Licentie

MIT — zie [LICENSE](LICENSE). Gebruik op eigen risico; zie ook de waarschuwing bovenaan.
