# config.json

`config.json` staat in `%LOCALAPPDATA%\PraktijkClean\` en wordt normaal via de app beheerd. Handmatig
aanpassen kan (bijvoorbeeld met Kladblok), maar **elke fout maakt de hele configuratie ongeldig** en dan
wordt er niets verwijderd. Controleer na een handmatige wijziging altijd met "Simulatie bekijken…".

Een voorbeeld staat in [`script/config.example.json`](../script/config.example.json).

## Algemene velden

| Veld | Type | Standaard | Betekenis |
|---|---|---|---|
| `schemaVersion` | getal | `1` | Versie van dit formaat. Moet 1 zijn. |
| `simulationMode` | true/false | `true` | Bij `true` wordt bij geplande uitvoeringen alleen geteld. |
| `automaticCleanupConfirmed` | true/false | `false` | Wordt pas `true` na de bevestiging in de app. Automatisch verwijderen gebeurt alleen als dit `true` is **en** `simulationMode` `false` is. |
| `paused` | true/false | `false` | Tijdelijk pauzeren: geplande uitvoeringen doen niets. |
| `maxFilesPerRun` | getal 1–5000 | `200` | Komen er méér bestanden (plus lege submappen bij `removeEmptyFolders`) in aanmerking, dan wordt er **niets** verwijderd. |
| `minimumAgeMinutes` | getal 1–1440 | `15` | Bestanden jonger dan dit worden nooit verwijderd, ook niet bij "Direct opruimen". |
| `deleteMode` | `"permanent"` / `"recycleBin"` | `"recycleBin"` | Naar de Prullenbak of definitief verwijderen. |
| `emptyRecycleBin` | true/false | `true` | De Prullenbak van deze gebruiker (alle lokale schijven) automatisch legen. Alleen wanneer er ook verwijderd mag worden (niet in simulatiemodus, niet bij pauze). Mag ontbreken (= uit). |
| `recycleBinRetentionDays` | getal 0–3650 | `1` | Alleen items die langer dan dit aantal dagen in de Prullenbak staan. `0` = alles (met inachtneming van `minimumAgeMinutes`). **Verplicht** als `emptyRecycleBin` `true` is. |
| `deletedFilesListDays` | getal 0–365 | `7` | Hoe lang de namen van verwijderde bestanden in `logs\verwijderde-bestanden.log` blijven staan (in de app: *Namen bewaren*). `0` = geen lijst bijhouden; een bestaande lijst wordt dan gewist. Mag ontbreken (= `0`). |
| `schedule.daily` | true/false | `true` | Dagelijks uitvoeren. |
| `schedule.dailyTime` | `"UU:MM"` | `"12:30"` | Tijdstip (24-uursnotatie, lokale tijd). |
| `schedule.atLogon` | true/false | `true` | Uitvoeren bij aanmelden (2 minuten na aanmelden). |
| `folders` | lijst | Bureaublad, Downloads | Maximaal 20 mappen. |
| `defaultSubfolderLevel` | `"light"` / `"medium"` / `"heavy"` | `"light"` | Alleen voor de app: het niveau voor submappen dat een **nieuw toegevoegde** map krijgt (tabblad *Voorkeuren* of de startwizard). Bestaande mappen veranderen er niet door. Mag ontbreken (= `"light"`); het script negeert dit veld. |
| `defaultRetentionDays` | getal 0–3650 | `30` | Alleen voor de app: de bewaartermijn die een **nieuw toegevoegde** map krijgt (tabblad *Voorkeuren* of de startwizard). Mag ontbreken (= `30`); het script negeert dit veld. |

De standaardwaarden zijn die van een nieuwe installatie (Bureaublad en Downloads met `retentionDays` 1). Ontbreekt
in een bestaande `config.json` een veld, dan geldt de voorzichtige waarde: `deleteMode` `"permanent"`,
`emptyRecycleBin` `false`, `recycleBinRetentionDays` `7`, `retentionDays` `7`. Ontbreekt `deletedFilesListDays`,
dan houdt het script geen lijst bij; de app vult bij de eerstvolgende *Opslaan* `7` in.

`schedule` wordt alleen door de app gebruikt (voor de geplande taak). Wijzigt u `schedule` met de hand,
klik dan in de app op "Opslaan" of kies "☰ Meer → Problemen oplossen" om de taak bij te werken.

## Per map (`folders[]`)

| Veld | Type | Betekenis |
|---|---|---|
| `id` | tekst | Unieke korte naam: kleine letters, cijfers en `-` (bijv. `desktop`, `map-1`). |
| `displayName` | tekst | Naam in de app (wordt door het script genegeerd). |
| `enabled` | true/false | Deze map opschonen. |
| `type` | `"knownFolder"` / `"custom"` | Standaardmap van Windows of zelf gekozen map. |
| `knownFolder` | `"Desktop"` / `"Downloads"` | Alleen bij `knownFolder`. De werkelijke locatie wordt via Windows bepaald (geen vaste paden). |
| `path` | tekst | Alleen bij `custom`: volledig pad. |
| `expectedPath` | tekst | Het pad zoals bij het opslaan bevestigd. Wijkt de werkelijke locatie af, dan wordt de map overgeslagen. |
| `retentionDays` | getal 0–3650 | Bewaartermijn in dagen. `0` = direct (met inachtneming van `minimumAgeMinutes`). |
| `recurse` | true/false | Ook bestanden in submappen opruimen (standaard `false`). |
| `removeEmptyFolders` | true/false | Submappen die na het opruimen leeg zijn (of al leeg waren) en ouder zijn dan de bewaartermijn ook weghalen. Alleen toegestaan samen met `recurse` `true`. Mag ontbreken (= `false`). Zie *Submappen* hieronder. |
| `allowNetworkOrSync` | true/false | Opschonen toestaan op OneDrive-/cloud- of netwerklocatie (standaard `false`). |
| `exclusions.files` | lijst | Bestandsnamen (of relatieve paden) die nooit worden verwijderd, bijv. `"Bewaren.pdf"`. |
| `exclusions.subfolders` | lijst | Relatieve submappen die nooit worden doorzocht en nooit worden weggehaald (alleen relevant bij `recurse`). |
| `exclusions.patterns` | lijst | Patronen met `*` (willekeurige tekst) en `?` (één teken), bijv. `"protocol-*"`. |
| `exclusions.extensions` | lijst | Extensies met punt, bijv. `".lnk"`, `".url"`. |

Hoofdletters maken bij namen, patronen en extensies niet uit.

## Submappen: Licht, Midden en Zwaar

In de app kiest u per map een niveau (keuzelijst *Submappen*); in `config.json` zijn dat twee velden:

| Niveau in de app | `recurse` | `removeEmptyFolders` | Wat er gebeurt |
|---|---|---|---|
| **Licht** – submappen overslaan | `false` | `false` | Alleen bestanden direct in de map. Submappen en alles daarin blijven staan. |
| **Midden** – ook bestanden in submappen | `true` | `false` | Ook oude bestanden in submappen. De submappen zelf blijven altijd staan. |
| **Zwaar** – daarna ook lege submappen | `true` | `true` | Als Midden, en daarna worden submappen die leeg zijn ook verwijderd. |

Bij **Zwaar** wordt een submap alleen weggehaald als hij helemaal leeg is (ook geen verborgen of uitgesloten
bestanden, geen te nieuwe bestanden, geen submappen die blijven) én de map zelf (aanmaak- en wijzigingsdatum)
ouder is dan de bewaartermijn. Nooit weggehaald: de ingestelde map zelf, submappen onder `exclusions.subfolders`,
submappen waarvan de naam onder `exclusions.files` of `exclusions.patterns` valt, verborgen, systeem- en
alleen-lezenmappen, koppelingen (junctions, symbolische links; die worden ook niet gevolgd) en mappen die
niet volledig gelezen konden worden. Vlak voor het weghalen wordt opnieuw gecontroleerd of de map leeg is.
Lege mappen gaan net als bestanden naar de Prullenbak of worden definitief verwijderd (`deleteMode`), tellen mee
voor `maxFilesPerRun` en staan in de lijst verwijderde bestanden (met een `\` achter de naam).

Een bestaande `config.json` zonder `removeEmptyFolders` werkt zoals voorheen: `recurse` `false` = Licht,
`recurse` `true` = Midden.

## Altijd overgeslagen (niet instelbaar)

Verborgen bestanden, systeembestanden (zoals `desktop.ini`), alleen-lezenbestanden, symbolische links en
cloud-placeholders, bestanden die in gebruik zijn, lopende downloads (`.crdownload`, `.part`, `.partial`,
`.download`, `.opdownload`) en Office-tijdelijke bestanden (`~$…`).

## Instellingen buiten config.json

Bijwerken met één klik gebruikt het register (`HKCU\Software\PraktijkClean`, bijv. `CheckForUpdates` = 0 om niet
automatisch te controleren) en kan door een beheerder worden uitgezet met
`HKLM\SOFTWARE\Policies\PraktijkClean\DisableUpdates` = 1. Zie [BIJWERKEN.md](BIJWERKEN.md).

## Bestandsleeftijd

Leeftijd = nu − de **nieuwste** van aanmaakdatum en wijzigingsdatum. Zie
[ARCHITECTUUR.md](ARCHITECTUUR.md#bestandsleeftijd).
