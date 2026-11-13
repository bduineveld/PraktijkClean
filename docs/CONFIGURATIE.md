# config.json

`config.json` staat in `%LOCALAPPDATA%\PraktijkClean\` en wordt normaal via de app beheerd. Handmatig
aanpassen kan (bijvoorbeeld met Kladblok), maar **elke fout maakt de hele configuratie ongeldig** en dan
wordt er niets verwijderd. Controleer na een handmatige wijziging altijd met "Simulatie uitvoeren".

Een voorbeeld staat in [`script/config.example.json`](../script/config.example.json).

## Algemene velden

| Veld | Type | Standaard | Betekenis |
|---|---|---|---|
| `schemaVersion` | getal | `1` | Versie van dit formaat. Moet 1 zijn. |
| `simulationMode` | true/false | `true` | Bij `true` wordt bij geplande uitvoeringen alleen geteld. |
| `automaticCleanupConfirmed` | true/false | `false` | Wordt pas `true` na de bevestiging in de app. Automatisch verwijderen gebeurt alleen als dit `true` is **en** `simulationMode` `false` is. |
| `paused` | true/false | `false` | Tijdelijk pauzeren: geplande uitvoeringen doen niets. |
| `maxFilesPerRun` | getal 1–5000 | `200` | Komen er méér bestanden in aanmerking, dan wordt er **niets** verwijderd. |
| `minimumAgeMinutes` | getal 1–1440 | `15` | Bestanden jonger dan dit worden nooit verwijderd, ook niet bij "Direct opruimen". |
| `deleteMode` | `"permanent"` / `"recycleBin"` | `"recycleBin"` | Naar de Prullenbak of definitief verwijderen. |
| `emptyRecycleBin` | true/false | `true` | De Prullenbak van deze gebruiker (alle lokale schijven) automatisch legen. Alleen wanneer er ook verwijderd mag worden (niet in simulatiemodus, niet bij pauze). Mag ontbreken (= uit). |
| `recycleBinRetentionDays` | getal 0–3650 | `1` | Alleen items die langer dan dit aantal dagen in de Prullenbak staan. `0` = alles (met inachtneming van `minimumAgeMinutes`). **Verplicht** als `emptyRecycleBin` `true` is. |
| `schedule.daily` | true/false | `true` | Dagelijks uitvoeren. |
| `schedule.dailyTime` | `"UU:MM"` | `"12:30"` | Tijdstip (24-uursnotatie, lokale tijd). |
| `schedule.atLogon` | true/false | `true` | Uitvoeren bij aanmelden (2 minuten na aanmelden). |
| `folders` | lijst | Bureaublad, Downloads | Maximaal 20 mappen. |

De standaardwaarden zijn die van een nieuwe installatie (Bureaublad en Downloads met `retentionDays` 1). Ontbreekt
in een bestaande `config.json` een veld, dan geldt de voorzichtige waarde: `deleteMode` `"permanent"`,
`emptyRecycleBin` `false`, `recycleBinRetentionDays` `7`, `retentionDays` `7`.

`schedule` wordt alleen door de app gebruikt (voor de geplande taak). Wijzigt u `schedule` met de hand,
klik dan in de app op "Instellingen opslaan" of "Installatie herstellen" om de taak bij te werken.

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
| `recurse` | true/false | Ook submappen doorzoeken (standaard `false`). Mappen zelf worden nooit verwijderd. |
| `allowNetworkOrSync` | true/false | Opschonen toestaan op OneDrive-/cloud- of netwerklocatie (standaard `false`). |
| `exclusions.files` | lijst | Bestandsnamen (of relatieve paden) die nooit worden verwijderd, bijv. `"Bewaren.pdf"`. |
| `exclusions.subfolders` | lijst | Relatieve submappen die nooit worden doorzocht (alleen relevant bij `recurse`). |
| `exclusions.patterns` | lijst | Patronen met `*` (willekeurige tekst) en `?` (één teken), bijv. `"protocol-*"`. |
| `exclusions.extensions` | lijst | Extensies met punt, bijv. `".lnk"`, `".url"`. |

Hoofdletters maken bij namen, patronen en extensies niet uit.

## Altijd overgeslagen (niet instelbaar)

Verborgen bestanden, systeembestanden (zoals `desktop.ini`), alleen-lezenbestanden, symbolische links en
cloud-placeholders, bestanden die in gebruik zijn, lopende downloads (`.crdownload`, `.part`, `.partial`,
`.download`, `.opdownload`) en Office-tijdelijke bestanden (`~$…`).

## Bestandsleeftijd

Leeftijd = nu − de **nieuwste** van aanmaakdatum en wijzigingsdatum. Zie
[ARCHITECTUUR.md](ARCHITECTUUR.md#bestandsleeftijd).
