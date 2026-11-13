namespace PraktijkClean;

/// <summary>
/// Alle langere teksten van de interface op één plek, zodat ze zonder programmeerkennis
/// aangepast kunnen worden. Korte knopteksten staan direct in MainForm.cs.
/// </summary>
internal static class Texts
{
    public const string AppTitle = "PraktijkClean";

    public const string LogoffExplanation =
        "Waarom is 'Bij afmelden' niet beschikbaar?\n\n" +
        "Windows Taakplanner heeft geen betrouwbare trigger voor afmelden. Werkende alternatieven vereisen " +
        "beheerdersrechten of groepsbeleid:\n\n" +
        "• Een afmeldscript via Groepsbeleid (Gebruikersconfiguratie > Windows-instellingen > Scripts > Afmelden). " +
        "Vereist een beheerder en Windows Pro/Enterprise of een domein. Het script draait niet bij stroomuitval, " +
        "slaapstand of als de pc hard wordt uitgezet.\n" +
        "• Een taak op gebeurtenis 4647 (afmelden) in het beveiligingslogboek. Dat vereist het inschakelen van " +
        "controlebeleid door een beheerder, en Windows kan de taak afbreken omdat de sessie op dat moment sluit.\n\n" +
        "PraktijkClean gebruikt daarom 'Dagelijks' en 'Bij aanmelden'. Een gemiste dagelijkse controle (pc stond uit) " +
        "wordt automatisch ingehaald zodra de pc weer aan staat. In de README staat hoe een systeembeheerder " +
        "eventueel zelf een afmeldscript via Groepsbeleid instelt.";

    public const string SyncWarning =
        "Deze map wordt gesynchroniseerd met OneDrive (of een andere clouddienst).\n\n" +
        "Als PraktijkClean hier een bestand verwijdert, verdwijnt het ook uit de cloud en van alle andere " +
        "computers en telefoons waarop deze map wordt gesynchroniseerd. Het bestand staat daarna mogelijk nog " +
        "enige tijd in de online prullenbak van OneDrive.\n\n" +
        "Opschonen is op zulke locaties daarom standaard geblokkeerd.";

    public const string NetworkWarning =
        "Deze map staat op een netwerklocatie of verwisselbaar station.\n\n" +
        "Bestanden die hier worden verwijderd kunnen ook voor andere gebruikers verdwijnen en gaan meestal " +
        "niet naar de Prullenbak (ze zijn dan direct weg).\n\n" +
        "Opschonen is op zulke locaties daarom standaard geblokkeerd.";

    public const string AllowSyncConfirm =
        "\n\nWeet u zeker dat u opschonen op deze locatie wilt toestaan? Doe dit alleen als u de gevolgen begrijpt " +
        "en dit heeft afgestemd met uw systeembeheerder.";

    public const string RecurseWarning =
        "Met deze optie worden ook bestanden in alle submappen gecontroleerd en eventueel verwijderd. " +
        "De submappen zelf blijven altijd bestaan.\n\n" +
        "Dit vergroot het risico dat u onbedoeld bestanden kwijtraakt. Gebruik eerst 'Simulatie uitvoeren'.\n\n" +
        "Wilt u submappen toch laten doorzoeken?";

    public const string AddFolderConfirm =
        "U voegt deze map toe:\n\n{0}\n\n" +
        "Bestanden in deze map die ouder zijn dan de ingestelde bewaartermijn (standaard 30 dagen) komen in " +
        "aanmerking voor verwijdering zodra automatische opschoning actief is. Submappen worden niet doorzocht.\n\n" +
        "Controleer altijd eerst met 'Simulatie uitvoeren'.{1}\n\nMap toevoegen?";

    public const string DeleteVersusDestroy =
        "Let op: verwijderen is niet hetzelfde als veilig vernietigen.\n\n" +
        "PraktijkClean verwijdert bestanden op de gewone Windows-manier. Met de optie 'Prullenbak' staan ze daarna " +
        "nog in de Prullenbak. Ook na definitief verwijderen kunnen gegevens met speciale software soms nog worden " +
        "teruggehaald, en er kunnen kopieën bestaan elders (back-ups, OneDrive, e-mail, het HIS, " +
        "schaduwkopieën).\n\n" +
        "PraktijkClean is een hulpmiddel om te voorkomen dat bestanden onbedoeld blijven staan. Het is geen " +
        "garantie voor AVG-conforme of onherstelbare vernietiging. Volledige-schijfversleuteling (BitLocker) is " +
        "daarvoor een belangrijke aanvullende maatregel.";

    // Korte notities onder de keuze (één regel); de volledige uitleg staat achter de links.
    public const string RecycleBinNote = "Aanbevolen: nog even terug te halen. Laat PraktijkClean de Prullenbak daarna legen (hieronder).";

    public const string PermanentNote = "Direct weg: niet via Windows terug te zetten.";

    public const string EmptyBinInfo =
        "Prullenbak automatisch legen\n\n" +
        "PraktijkClean leegt dan ook de Prullenbak van deze gebruiker, op alle lokale schijven. Alleen items die " +
        "langer dan het gekozen aantal dagen in de Prullenbak staan, worden definitief verwijderd (0 = alles). " +
        "Iets wat minder dan 15 minuten geleden is weggegooid, blijft altijd staan.\n\n" +
        "Dit geldt ook voor bestanden die u zelf heeft weggegooid, uit welke map dan ook.\n\n" +
        "Het legen gebeurt alleen bij automatische opschoning (dus niet in simulatiemodus of tijdens een pauze) en " +
        "bij 'Nu opruimen'. 'Simulatie uitvoeren' laat zien welke items in aanmerking komen.";

    public const string EmptyBinConfirm =
        "Met deze optie leegt PraktijkClean ook de Prullenbak van deze gebruiker.\n\n" +
        "Alles wat langer dan het gekozen aantal dagen in de Prullenbak staat, wordt definitief verwijderd, ook " +
        "bestanden die u of een collega zelf heeft weggegooid, van welke map of schijf dan ook. Daarna is het niet " +
        "meer via Windows terug te halen.\n\n" +
        "Net als bij de mappen gebeurt dit pas als automatische opschoning actief is; in simulatiemodus wordt " +
        "alleen geteld.\n\nPrullenbak automatisch legen inschakelen?";

    public const string TrayEnabled =
        "Het icoon staat nu rechtsonder in het systeemvak en start voortaan bij elke aanmelding.\n\n" +
        "Groen = aan, blauw = simulatie, oranje = gepauzeerd, rood = probleem, grijs = nog niet ingesteld. " +
        "Ga met de muis over het icoon om te zien wat er vandaag is opgeruimd; klik erop om PraktijkClean te openen.\n\n" +
        "Ziet u het icoon niet? Windows verbergt nieuwe icoontjes achter het pijltje (^) rechtsonder. Sleep het " +
        "icoon van daar naar de taakbalk om het altijd te zien.";

    public const string UninstallConfirm =
        "PraktijkClean volledig verwijderen?\n\n" +
        "Dit verwijdert de geplande taak, het opschoonscript, de instellingen, het logboek en het icoon in het systeemvak van PraktijkClean " +
        "voor deze gebruiker.\n\n" +
        "Uw eigen bestanden (Bureaublad, Downloads en andere mappen) worden NIET aangeraakt.\n\n" +
        "Het programmabestand dat u heeft gedownload (PraktijkClean-<versie>.exe) blijft staan; dat kunt u daarna zelf verwijderen.";

    public const string ElevatedWarning =
        "PraktijkClean draait nu met administratorrechten. Dat is niet nodig.\n\n" +
        "De instellingen en de geplande taak gelden voor het account waarmee u nu werkt. Start PraktijkClean " +
        "bij voorkeur gewoon (niet 'Als administrator uitvoeren') onder het account van de gebruiker van deze werkplek.";

    public const string NotWindows11 =
        "Deze eerste versie van PraktijkClean is gemaakt en getest voor Windows 11. Op deze Windows-versie werkt " +
        "het mogelijk niet goed. Toch doorgaan?";
}
