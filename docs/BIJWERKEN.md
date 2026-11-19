# Bijwerken met één klik

Vanaf versie 1.4.0 kan PraktijkClean zichzelf bijwerken naar een nieuwe versie van GitHub, **maar alleen na een
klik van de gebruiker** en alleen als de nieuwe versie is ondertekend met de releasesleutel van PraktijkClean.

## Wat de gebruiker ziet

1. Hooguit eens per 20 uur vraagt de app (of het icoon in het systeemvak) aan GitHub wat de nieuwste release is.
2. Is er een nieuwere versie, dan verschijnt bovenin het venster een blauwe balk: *"Er is een nieuwe versie van
   PraktijkClean: 1.4.1 (u heeft 1.4.0)"* met de knoppen **Bijwerken…** en **Later**. Het icoon in het systeemvak
   geeft één keer per versie een melding; klikken opent de app.
3. **Bijwerken…** toont wat er nieuw is, met de knoppen **Bijwerken**, **Op GitHub bekijken** en **Annuleren**.
   Zijn er nog niet-opgeslagen wijzigingen, dan vraagt de app eerst op **Opslaan** (of **Ongedaan maken**) te klikken.
4. Na **Bijwerken** wordt de nieuwe versie gedownload en gecontroleerd. PraktijkClean sluit even, zet de nieuwe
   versie op zijn plek, werkt het opschoonscript en de geplande taak bij en gaat weer open met de melding
   *"✓ Bijgewerkt naar versie 1.4.1. Uw instellingen zijn behouden."*

Instellingen, pauze, de lijst verwijderde bestanden en het logboek blijven behouden. Er wordt bij het bijwerken
niets opgeruimd.

Handmatig controleren: **☰ Meer → Controleren op nieuwe versie…**. Uitzetten: **☰ Meer → Automatisch controleren
op nieuwe versies** (vinkje weg).

## Veiligheid

- **Niets zonder klik.** De automatische controle downloadt alleen een klein overzicht (de "latest release" van
  `bduineveld/PraktijkClean`). De .exe wordt pas gedownload na **Bijwerken**.
- **Handtekening verplicht.** Elke release heeft naast `PraktijkClean-x.y.z.exe` een bestand
  `PraktijkClean-x.y.z.exe.sig`: een RSA-3072-handtekening (SHA-256, PKCS#1 v1.5, base64). De openbare sleutel zit
  in de app (`UpdateSignature.ReleaseKeyModulus` in `src/PraktijkClean.Core/UpdateCheck.cs`). Klopt de handtekening
  niet, dan wordt de download weggegooid en niet geïnstalleerd. Een `.sha256`-bestand op GitHub alleen is niet
  genoeg: wie de release kan vervangen, kan ook dat bestand vervangen.
- **Juiste versie.** Ook met een geldige handtekening moet de productnaam "PraktijkClean" zijn en de bestandsversie
  gelijk aan de aangeboden versie. Zo kan niemand een oudere (geldig ondertekende) versie als "nieuw" aanbieden.
- **Alleen GitHub via https**, alleen releases die geen concept of testversie zijn, maximaal 50 MB.
- **Nooit terug naar een oudere versie**: een oudere exe overschrijft nooit een nieuwere installatie.
- **Geen gegevens van de pc.** Alleen een gewoon https-verzoek met `PraktijkClean/<versie>` als programmanaam. Een
  proxyserver die om de Windows-aanmelding vraagt, wordt ondersteund.
- De download staat tijdelijk in `%LOCALAPPDATA%\PraktijkClean\update\` en wordt na het bijwerken opgeruimd (ook
  bij het verwijderen van PraktijkClean).

## Uitzetten voor een hele organisatie

Een systeembeheerder kan bijwerken voor alle gebruikers uitzetten (bijvoorbeeld via Groepsbeleid of Intune):

```
HKLM\SOFTWARE\Policies\PraktijkClean
  DisableUpdates  (DWORD) = 1
```

De app en het icoon controleren dan niet meer; de twee menu-items voor bijwerken zijn grijs (met als uitleg
*"Uitgezet door uw systeembeheerder."*).

## Instellingen in het register (per gebruiker)

`HKCU\Software\PraktijkClean` (wordt bij het verwijderen van PraktijkClean helemaal weggehaald):

| Waarde | Betekenis |
|---|---|
| `CheckForUpdates` (DWORD) | `0` = niet automatisch controleren. Ontbreekt = aan. |
| `UpdateLastCheck` | Tijdstip van de laatste controle (UTC). App en icoon delen dit, zodat er niet vaker dan eens per 20 uur wordt gevraagd. |
| `UpdateLatest` | Nieuwste versie bij de laatste controle. |
| `UpdateNotified` | Versie waarover het icoon al een melding heeft gegeven. |
| `UpdateSource` | Alleen voor testen: ander adres voor het release-overzicht. De handtekening blijft verplicht. |

## Een nieuwe versie uitbrengen (voor de ontwikkelaar)

1. Versie ophogen (`<Version>` in `src/PraktijkClean/PraktijkClean.csproj` en `$script:PcVersion` in
   `script/cleanup.ps1`) en `build.ps1` uitvoeren.
2. **Precies het bestand dat u uploadt** ondertekenen, op de computer waar de sleutel staat:

   ```bash
   tools/sign-release.sh dist/PraktijkClean-1.4.1.exe
   ```

   Dat maakt `dist/PraktijkClean-1.4.1.exe.sig` en controleert de handtekening meteen. Bouw daarna niet opnieuw:
   een nieuwe build geeft een ander bestand en dan klopt de handtekening niet meer.
3. Release maken met tag `v1.4.1` en deze drie bestanden: `PraktijkClean-1.4.1.exe`,
   `PraktijkClean-1.4.1.exe.sha256` en `PraktijkClean-1.4.1.exe.sig`. Bijvoorbeeld:

   ```bash
   gh release create v1.4.1 dist/PraktijkClean-1.4.1.exe dist/PraktijkClean-1.4.1.exe.sha256 \
     dist/PraktijkClean-1.4.1.exe.sig --title "PraktijkClean 1.4.1" --notes-file notities.md
   ```

   De releasetekst verschijnt (kort, zonder opmaak) in het vraagvenster van de app. Een release zonder `.sig`
   wordt door de app genegeerd.

## De sleutel

- De geheime sleutel staat **alleen** op de computer van de ontwikkelaar:
  `~/.config/praktijkclean/update-signing-key.pem` (alleen leesbaar voor de eigenaar). Andere plek:
  omgevingsvariabele `PRAKTIJKCLEAN_SIGNING_KEY`. De sleutel staat niet in de repository en niet op GitHub.
- **Maak een reservekopie** (bijvoorbeeld in een wachtwoordbeheerder). Is de sleutel kwijt, dan kunnen bestaande
  installaties niet meer met één klik bijwerken; dan moet iedereen één keer handmatig een nieuwe versie (met een
  nieuwe sleutel) downloaden.
- Is de sleutel mogelijk uitgelekt: maak een nieuwe sleutel, zet de nieuwe openbare sleutel in
  `UpdateSignature.ReleaseKeyModulus`, breng een versie uit en vraag gebruikers die handmatig te installeren.
- Nieuwe sleutel maken (alleen bij verlies of uitlekken):

  ```bash
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out update-signing-key.pem
  openssl rsa -in update-signing-key.pem -noout -modulus   # hexadecimaal; omzetten naar base64 voor de app
  ```

## Overgang vanaf 1.3.0

Versie 1.3.0 en ouder kunnen nog niet zelf bijwerken. Wie 1.3.0 gebruikt, downloadt 1.4.0 één keer zelf van
GitHub en opent die; de instellingen blijven behouden. Daarna gaat bijwerken met één klik.
