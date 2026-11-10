# Windows SmartScreen, Defender en digitale ondertekening

## Wat de gebruiker ziet

Een nieuw, niet-ondertekend programma dat van internet komt, krijgt bij het starten meestal een blauw
SmartScreen-venster ("Windows heeft uw pc beschermd"). Via **Meer info → Toch uitvoeren** kan het
programma worden gestart. Dit is geen virusmelding, maar betekent dat Windows het bestand (nog) niet kent.

Belangrijk om te weten (bron: Microsoft-documentatie over SmartScreen-reputatie, 2024–2026):

- SmartScreen-reputatie hoort bij **het bestand (de hash)** en de **uitgever**. Elke nieuwe versie begint
  opnieuw met weinig reputatie.
- Ook met een geldig handtekeningcertificaat (OV of EV) kan de waarschuwing in het begin verschijnen; een
  ondertekend bestand toont dan wél een **geverifieerde uitgeversnaam** en bouwt reputatie op die naar
  volgende versies kan meegaan.
- **EV-certificaten geven sinds 2024 geen directe vrijstelling meer.** Een duur EV-certificaat kopen alleen
  om SmartScreen te vermijden is daarom niet zinvol.
- In beheerde omgevingen kan een systeembeheerder een uitgever of bestandshash vooraf toestaan (bijv. via
  Intune/Defender-beleid).

## Wat in dit project al is gedaan om onterechte meldingen te beperken

- Geen compressie, versleuteling of "packing" van de .exe (`build.ps1` zet compressie bewust niet aan).
- Geen code die zichzelf of andere code downloadt; het script is een vaste versie die in de .exe zit.
- Geen `Invoke-Expression`, geen verborgen PowerShell-opdrachten (geen `-EncodedCommand`).
- Geen omzeiling van beveiliging: de app vraagt geen administratorrechten (`asInvoker`), en de geplande taak
  draait nooit verhoogd. `-ExecutionPolicy Bypass` wordt niet gebruikt in de geplande taak.
- Versie-informatie (product, omschrijving, versie) in de .exe.
- Bij elke build een SHA-256-controlegetal (`PraktijkClean.exe.sha256`), zodat ontvangers kunnen controleren
  dat het bestand ongewijzigd is.
- Volledig open broncode en reproduceerbare build via GitHub Actions.

Er is **geen** antivirusontwijking ingebouwd en dat moet zo blijven.

## Opties voor digitale ondertekening

| Optie | Kosten | Voor wie | Opmerkingen |
|---|---|---|---|
| **SignPath Foundation** | gratis voor open source | Openbaar GitHub-project met OSI-licentie (MIT is goed) | Certificaat staat op naam van "SignPath Foundation"; de build moet via CI (GitHub Actions) lopen en alle teamleden gebruiken MFA. Vermelding in de README vereist. Waarschijnlijk de beste eerste stap voor PraktijkClean. |
| **Azure Artifact Signing** (voorheen Trusted Signing) | ca. 10 USD per maand (Basic) | Organisaties in o.a. de EU (bijv. een praktijk-bv of zorggroep) | Volledig beheerd door Microsoft, werkt met GitHub Actions. De organisatie moet een identiteitsvalidatie doorlopen. |
| OV-certificaat van een certificaatautoriteit | ca. 200–400 euro per jaar | Organisatie of persoon | Sleutel moet op een hardwaretoken of HSM staan. Zelfde SmartScreen-gedrag als EV. |
| Microsoft Store | eenmalige ontwikkelaarsaccount | — | Geen SmartScreen-waarschuwing, maar vereist MSIX-verpakking; past minder bij een "portable" .exe. |

### Ook het script ondertekenen

Wordt in een organisatie het PowerShell-uitvoeringsbeleid `AllSigned` afgedwongen, dan moet ook
`script/cleanup.ps1` ondertekend zijn. Onderteken het script dan **vóór** het bouwen (met
`Set-AuthenticodeSignature`); de handtekening is onderdeel van de tekst en gaat zo mee in de .exe.
Let op: na elke wijziging aan het script moet opnieuw worden ondertekend.

## Bij een onterechte melding van Microsoft Defender

Bestand indienen voor analyse via de officiële Microsoft-pagina voor het indienen van bestanden
("Submit a file for malware analysis", als softwareontwikkelaar). Voeg de link naar de broncode toe.
