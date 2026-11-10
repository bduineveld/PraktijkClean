# Bekende beperkingen en nog niet geteste onderdelen

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
