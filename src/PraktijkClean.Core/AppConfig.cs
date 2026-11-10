using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PraktijkClean.Core;

// De JSON-namen (camelCase) en volgorde in config.json liggen vast via [DataMember]; cleanup.ps1 leest dezelfde namen.
// De DataContractJsonSerializer roept geen constructor aan: standaardwaarden staan daarom in Init(),
// dat zowel door de constructor als vóór het inlezen ([OnDeserializing]) wordt aangeroepen.
// Een veld dat in een oudere config.json ontbreekt, krijgt zo dezelfde standaardwaarde als bij 'new'.

/// <summary>
/// Alle instellingen van PraktijkClean. Wordt opgeslagen als config.json en gelezen door cleanup.ps1.
/// Zie docs/CONFIGURATIE.md voor de betekenis van elk veld.
/// </summary>
[DataContract]
public sealed class AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public AppConfig() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        SchemaVersion = CurrentSchemaVersion;
        SimulationMode = true;
        AutomaticCleanupConfirmed = false;
        Paused = false;
        MaxFilesPerRun = 200;
        MinimumAgeMinutes = 15;
        DeleteMode = DeleteModes.Permanent;
        EmptyRecycleBin = false;
        RecycleBinRetentionDays = 7;
        Schedule = new ScheduleSettings();
        Folders = new List<FolderSettings>();
    }

    [DataMember(Name = "schemaVersion", Order = 1)]
    public int SchemaVersion { get; set; }

    /// <summary>Bij true wordt er bij geplande uitvoeringen alleen geteld, nooit verwijderd.</summary>
    [DataMember(Name = "simulationMode", Order = 2)]
    public bool SimulationMode { get; set; }

    /// <summary>Wordt pas true nadat de gebruiker automatische verwijdering expliciet heeft bevestigd.</summary>
    [DataMember(Name = "automaticCleanupConfirmed", Order = 3)]
    public bool AutomaticCleanupConfirmed { get; set; }

    /// <summary>Tijdelijk pauzeren: geplande taak staat uit en het script doet niets.</summary>
    [DataMember(Name = "paused", Order = 4)]
    public bool Paused { get; set; }

    /// <summary>Komen er meer bestanden in aanmerking, dan wordt er niets verwijderd.</summary>
    [DataMember(Name = "maxFilesPerRun", Order = 5)]
    public int MaxFilesPerRun { get; set; }

    /// <summary>Bestanden jonger dan dit aantal minuten worden nooit verwijderd (ook niet bij "Direct").</summary>
    [DataMember(Name = "minimumAgeMinutes", Order = 6)]
    public int MinimumAgeMinutes { get; set; }

    /// <summary>"permanent" (standaard) of "recycleBin" (Prullenbak).</summary>
    [DataMember(Name = "deleteMode", Order = 7)]
    public string DeleteMode { get; set; } = "";

    /// <summary>De Prullenbak van deze gebruiker automatisch legen (standaard uit).</summary>
    [DataMember(Name = "emptyRecycleBin", Order = 8)]
    public bool EmptyRecycleBin { get; set; }

    /// <summary>Alleen items die langer dan dit aantal dagen in de Prullenbak staan. 0 = alles (met inachtneming van MinimumAgeMinutes).</summary>
    [DataMember(Name = "recycleBinRetentionDays", Order = 9)]
    public int RecycleBinRetentionDays { get; set; }

    [DataMember(Name = "schedule", Order = 10)]
    public ScheduleSettings Schedule { get; set; } = null!;

    [DataMember(Name = "folders", Order = 11)]
    public List<FolderSettings> Folders { get; set; } = null!;
}

public static class DeleteModes
{
    public const string RecycleBin = "recycleBin";
    public const string Permanent = "permanent";
}

public static class FolderTypes
{
    public const string KnownFolder = "knownFolder";
    public const string Custom = "custom";
}

public static class KnownFolderNames
{
    public const string Desktop = "Desktop";
    public const string Downloads = "Downloads";
}

[DataContract]
public sealed class ScheduleSettings
{
    public ScheduleSettings() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Daily = true;
        DailyTime = "12:30";
        AtLogon = true;
    }

    [DataMember(Name = "daily", Order = 1)]
    public bool Daily { get; set; }

    /// <summary>Tijdstip in 24-uursnotatie, "UU:MM".</summary>
    [DataMember(Name = "dailyTime", Order = 2)]
    public string DailyTime { get; set; } = "";

    [DataMember(Name = "atLogon", Order = 3)]
    public bool AtLogon { get; set; }
}

[DataContract]
public sealed class FolderSettings
{
    public FolderSettings() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Id = "";
        DisplayName = "";
        Enabled = true;
        Type = FolderTypes.KnownFolder;
        KnownFolder = null;
        Path = null;
        ExpectedPath = "";
        RetentionDays = 7;
        Recurse = false;
        AllowNetworkOrSync = false;
        Exclusions = new ExclusionSettings();
    }

    /// <summary>Unieke korte naam: kleine letters, cijfers en streepjes.</summary>
    [DataMember(Name = "id", Order = 1)]
    public string Id { get; set; } = "";

    /// <summary>Naam zoals getoond in de app (wordt door het script genegeerd).</summary>
    [DataMember(Name = "displayName", Order = 2)]
    public string DisplayName { get; set; } = "";

    [DataMember(Name = "enabled", Order = 3)]
    public bool Enabled { get; set; }

    /// <summary>"knownFolder" (Bureaublad/Downloads) of "custom" (zelf gekozen map).</summary>
    [DataMember(Name = "type", Order = 4)]
    public string Type { get; set; } = "";

    /// <summary>"Desktop" of "Downloads" (alleen bij type knownFolder).</summary>
    [DataMember(Name = "knownFolder", Order = 5)]
    public string? KnownFolder { get; set; }

    /// <summary>Volledig pad (alleen bij type custom).</summary>
    [DataMember(Name = "path", Order = 6)]
    public string? Path { get; set; }

    /// <summary>
    /// Het pad zoals het bij het opslaan was. Wijkt de werkelijke locatie later af,
    /// dan slaat het script de map over totdat de instellingen opnieuw zijn opgeslagen.
    /// </summary>
    [DataMember(Name = "expectedPath", Order = 7)]
    public string ExpectedPath { get; set; } = "";

    /// <summary>0 = direct (met inachtneming van MinimumAgeMinutes).</summary>
    [DataMember(Name = "retentionDays", Order = 8)]
    public int RetentionDays { get; set; }

    /// <summary>Submappen doorzoeken. Standaard uit.</summary>
    [DataMember(Name = "recurse", Order = 9)]
    public bool Recurse { get; set; }

    /// <summary>Opschonen op OneDrive-/cloud- of netwerklocatie toestaan. Standaard uit.</summary>
    [DataMember(Name = "allowNetworkOrSync", Order = 10)]
    public bool AllowNetworkOrSync { get; set; }

    [DataMember(Name = "exclusions", Order = 11)]
    public ExclusionSettings Exclusions { get; set; } = null!;
}

[DataContract]
public sealed class ExclusionSettings
{
    public ExclusionSettings() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Files = new List<string>();
        Subfolders = new List<string>();
        Patterns = new List<string>();
        Extensions = new List<string>();
    }

    /// <summary>Bestandsnamen (of relatieve paden) die nooit worden verwijderd.</summary>
    [DataMember(Name = "files", Order = 1)]
    public List<string> Files { get; set; } = null!;

    /// <summary>Submappen (relatief) die nooit worden doorzocht.</summary>
    [DataMember(Name = "subfolders", Order = 2)]
    public List<string> Subfolders { get; set; } = null!;

    /// <summary>Eenvoudige patronen met * en ?, bijv. "protocol-*".</summary>
    [DataMember(Name = "patterns", Order = 3)]
    public List<string> Patterns { get; set; } = null!;

    /// <summary>Extensies met punt, bijv. ".lnk".</summary>
    [DataMember(Name = "extensions", Order = 4)]
    public List<string> Extensions { get; set; } = null!;
}
