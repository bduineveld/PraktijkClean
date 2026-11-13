# PraktijkClean

Gratis, open-source hulpmiddel voor huisartsenpraktijken dat oude bestanden automatisch opruimt van het
**Bureaublad** en uit **Downloads** (en eventueel andere gekozen mappen), zodat patiëntbrieven, labuitslagen
en PDF's daar niet onbedoeld blijven staan.

- Kleine Windows-app om in te stellen; daarna doet **Windows Taakplanner** het werk. De app hoeft niet open
  te blijven en er draait geen achtergronddienst.
- Geen administratorrechten nodig, alles per gebruiker.
- Na installatie staat alles in **simulatiemodus**: er wordt pas iets verwijderd nadat u dat expliciet
  bevestigt.

> **Status: versie 1.2.0, getest op een Windows 11-testcomputer, nog niet in de praktijk.** Lees eerst
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

1. Download `PraktijkClean-<versie>.exe` (bijvoorbeeld `PraktijkClean-1.2.0.exe`) van de pagina
   [Releases](https://github.com/bduineveld/PraktijkClean/releases/latest) (of uit de map [`dist`](dist)) en zet
   het op de werkplek (bijvoorbeeld in een map `Programma's` in het gebruikersprofiel).
   Het is één bestand van ongeveer 250 kB; er hoeft niets bij geïnstalleerd te worden (het gebruikt het
   .NET Framework 4.8 dat standaard in Windows 10 en 11 zit).
2. Start het programma **als de gebruiker van die werkplek** (niet "Als administrator uitvoeren").
3. Ziet u "Windows heeft uw pc beschermd"? Klik op **Meer info → Toch uitvoeren**. Dat komt doordat het
   programma nog niet digitaal is ondertekend (zie [docs/ONDERTEKENING.md](docs/ONDERTEKENING.md)).

### In vijf stappen instellen

1. **Controleer de mappen.** Bureaublad en Downloads staan klaar met "ouder dan 1 dag": wat langer dan een dag
   op het bureaublad of in Downloads staat, gaat naar de Prullenbak, en wat daarna nog een dag in de Prullenbak
   staat, wordt definitief verwijderd. Klik een map aan om de bewaartermijn, uitzonderingen en waarschuwingen te zien.
2. **Kies het moment**: dagelijks op een tijdstip en/of bij aanmelden van Windows.
3. Klik **Instellingen opslaan**. Het script en de geplande taak worden geïnstalleerd, in simulatiemodus.
   Er wordt niets verwijderd.
4. Klik **Simulatie uitvoeren** en bekijk welke bestanden in aanmerking komen en welke worden overgeslagen.
   Moet een bestand blijven staan? Klik er met de **rechtermuisknop** op (of gebruik de knop **Uitsluiten**) en
   kies "nooit verwijderen", "alle .pdf-bestanden nooit verwijderen" of "submap overslaan". Dat wordt meteen opgeslagen.
5. Tevreden? Klik **Automatische opschoning activeren…**. U ziet nogmaals een overzicht en moet bevestigen.

### De knoppen

| Knop | Wat het doet |
|---|---|
| Instellingen opslaan | Slaat op en installeert/werkt script en geplande taak bij. Verwijdert niets. |
| Simulatie uitvoeren | Toont wat er zou gebeuren, met bestandsnamen (alleen op het scherm). |
| Automatische opschoning activeren… / Terug naar simulatiemodus | Zet het echte verwijderen aan of uit. |
| Tijdelijk pauzeren / Hervatten | Zet de geplande taak tijdelijk uit. |
| Nu opruimen… | Direct opruimen, na een overzicht en bevestiging. |
| Installatie herstellen | Schrijft script, instellingen en taak opnieuw weg. |
| PraktijkClean volledig verwijderen… | Verwijdert taak, script, instellingen en logboek. Nooit uw eigen bestanden. |
| Logboek openen | Toont het logboek (zonder bestandsnamen). |

### Staat het aan?

Bovenin het venster staat een gekleurde balk: **groen = AAN** (er wordt automatisch opgeruimd), **blauw =
SIMULATIE** (alleen tellen), **oranje = GEPAUZEERD**, **rood** = aan, maar de geplande taak ontbreekt of staat
uit (gebruik "Installatie herstellen"), **grijs** = nog niet ingesteld. Daaronder staan de laatste controle,
de volgende geplande controle en een overzicht van de **afgelopen 7 dagen** (aantal controles, opgeruimde
bestanden en wat uit de Prullenbak is geleegd).

Wilt u de status altijd zien? Zet dan **"Icoon rechtsonder in het systeemvak tonen"** aan. Het icoon heeft
dezelfde kleuren (met een teken: ✓ aan, ○ simulatie, ‖ gepauzeerd, ! probleem), toont bij aanwijzen wat er
vandaag is opgeruimd en opent PraktijkClean met een klik; via de rechtermuisknop kunt u pauzeren of het icoon
verbergen. Windows zet nieuwe icoontjes eerst achter het pijltje (^); sleep het naar de taakbalk om het altijd
te zien. Het icoon start bij elke aanmelding en staat standaard uit.

Het **logboek** staat in `%LOCALAPPDATA%\PraktijkClean\logs\praktijkclean.log`: één regel per controle met
de aantallen, nooit bestandsnamen. Boven 512 kB wordt het oude deel bewaard als `praktijkclean.1.log`.

### Uitzonderingen

Per map staat rechts een lijst met **uitzonderingen**: wat daarin staat, wordt nooit verwijderd.

- **Bestand kiezen…**: kies een of meer bestanden uit die map.
- **Submap kiezen…**: een submap overslaan (alleen van belang als "Ook submappen doorzoeken" aan staat).
- **Extensie of patroon…**: typ een extensie (`.docx`), een bestandsnaam (`Bewaren.pdf`) of een patroon met
  `*` (willekeurige tekst) of `?` (één teken), bijvoorbeeld `protocol-*`.
- **Verwijderen** (of de Delete-toets): haalt de geselecteerde uitzondering weg.

Sneller: in het simulatievenster met de rechtermuisknop op een bestand klikken (zie hierboven).

### Wat wordt nooit verwijderd

- Bestanden jonger dan de bewaartermijn. **Leeftijd** = tijd sinds de nieuwste van aanmaak- en
  wijzigingsdatum, dus een net gedownload bestand is altijd "nieuw". Bovendien nooit iets jonger dan 15 minuten.
- Snelkoppelingen (`.lnk`, `.url`), verborgen en systeembestanden, alleen-lezenbestanden, geopende bestanden,
  lopende downloads en tijdelijke Office-bestanden.
- **Mappen.** Submappen worden standaard niet eens bekeken. Tip voor gebruikers: zet wat moet blijven in een
  submap, bijvoorbeeld `Bureaublad\Bewaren`.
- Alles op **OneDrive/cloud- of netwerklocaties** (standaard geblokkeerd, zie hieronder).
- Alles in systeemmappen, Program Files, AppData en dergelijke.
- Als er meer bestanden in aanmerking komen dan het ingestelde maximum (standaard 200): dan wordt er in die
  ronde helemaal niets verwijderd.

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
3. het maken van **`dist\PraktijkClean-<versie>.exe`** (één los bestand van ongeveer 250 kB; de werkplek heeft
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
3. Bestaande installaties krijgen het nieuwe script zodra men in de nieuwe app op **Instellingen opslaan**
   of **Installatie herstellen** klikt (de app meldt een afwijkend script vanzelf).

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
`cleanup.ps1`, `config.json`, `status.json`, `logs\praktijkclean.log`.

### Uitvoeringsbeleid (Execution Policy)

De geplande taak start het script met `-ExecutionPolicy RemoteSigned`, alleen voor dat proces. Een beleid
dat de organisatie via Groepsbeleid of Intune afdwingt, gaat altijd voor. Staat dat op `AllSigned`, dan moet
het script digitaal ondertekend zijn ([docs/ONDERTEKENING.md](docs/ONDERTEKENING.md)); anders weigert Windows
het script en wordt er niets verwijderd. De app toont dan een foutmelding bij de simulatie.

### Het script handmatig controleren

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File "$env:LOCALAPPDATA\PraktijkClean\cleanup.ps1" -Mode Simulate -Json
```

`-Mode Simulate` verwijdert nooit iets. Exitcodes: 0 = gelukt, 1 = configuratiefout, 2 = fouten,
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

In de app: **PraktijkClean volledig verwijderen…**. Daarna kan `PraktijkClean-<versie>.exe` zelf worden weggegooid.
Lukt dat niet meer (bijv. app kwijt): verwijder in Taakplanner de taak `PraktijkClean - <gebruiker>` en
daarna de map `%LOCALAPPDATA%\PraktijkClean`.

### Meer

- [docs/ARCHITECTUUR.md](docs/ARCHITECTUUR.md) — opbouw, rechten, veiligheidslagen
- [docs/CONFIGURATIE.md](docs/CONFIGURATIE.md) — alle velden van `config.json`
- [docs/BEPERKINGEN.md](docs/BEPERKINGEN.md) — bekende beperkingen en niet-geteste onderdelen
- [docs/ONDERTEKENING.md](docs/ONDERTEKENING.md) — SmartScreen, Defender en digitale handtekening

---

## 6. Licentie

MIT — zie [LICENSE](LICENSE). Gebruik op eigen risico; zie ook de waarschuwing bovenaan.
