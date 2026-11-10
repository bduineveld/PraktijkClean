using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace PraktijkClean.Core;

// JSON-namen zoals cleanup.ps1 ze schrijft (camelCase; de bestandsregels in fileDetails gebruiken Name/Size/Reason).
// Standaardwaarden staan in Init() omdat de DataContractJsonSerializer geen constructor aanroept.

/// <summary>
/// Resultaat van een uitvoering van cleanup.ps1 (uit status.json of uit de JSON-uitvoer).
/// FileDetails is alleen gevuld bij een handmatige simulatie en wordt nooit opgeslagen.
/// </summary>
[DataContract]
public sealed class RunReport
{
    public RunReport() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Messages = new List<string>();
        Folders = new List<FolderReport>();
    }

    [DataMember(Name = "scriptVersion")] public string? ScriptVersion { get; set; }

    /// <summary>Tijdstip in UTC; in JSON als ISO 8601-tekst ("o").</summary>
    public DateTime? LastRunUtc { get; set; }

    [DataMember(Name = "lastRunUtc")]
    private string? LastRunUtcText
    {
        get => LastRunUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        set => LastRunUtc = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    [DataMember(Name = "mode")] public string? Mode { get; set; }
    [DataMember(Name = "trigger")] public string? Trigger { get; set; }
    [DataMember(Name = "action")] public string? Action { get; set; }
    [DataMember(Name = "exitCode")] public int ExitCode { get; set; }
    [DataMember(Name = "candidates")] public int Candidates { get; set; }
    [DataMember(Name = "candidateBytes")] public long CandidateBytes { get; set; }
    [DataMember(Name = "deleted")] public int Deleted { get; set; }
    [DataMember(Name = "deletedBytes")] public long DeletedBytes { get; set; }
    [DataMember(Name = "skipped")] public int Skipped { get; set; }
    [DataMember(Name = "tooYoung")] public int TooYoung { get; set; }
    [DataMember(Name = "errors")] public int Errors { get; set; }
    [DataMember(Name = "messages")] public List<string> Messages { get; set; } = null!;
    [DataMember(Name = "folders")] public List<FolderReport> Folders { get; set; } = null!;
    [DataMember(Name = "fileDetails")] public List<FolderFileDetails>? FileDetails { get; set; }

    /// <summary>Alleen aanwezig als 'Prullenbak legen' aan staat.</summary>
    [DataMember(Name = "recycleBin")] public RecycleBinReport? RecycleBin { get; set; }

    /// <summary>Oorspronkelijke paden van items in de Prullenbak; alleen bij een handmatige simulatie.</summary>
    [DataMember(Name = "recycleBinDetails")] public List<FileEntry>? RecycleBinDetails { get; set; }

    /// <summary>Gooit <see cref="JsonFormatException"/> als de tekst geen geldig resultaat is.</summary>
    public static RunReport Parse(string json)
    {
        var report = Json.Deserialize<RunReport>(json);
        // "null" in JSON levert lege lijsten op, zodat de rest van de app daar niet op hoeft te controleren.
        report.Messages ??= new List<string>();
        report.Folders ??= new List<FolderReport>();
        foreach (var f in report.Folders)
        {
            f.SkippedByReason ??= new Dictionary<string, int>();
            f.Message ??= "";
            f.Status ??= "";
        }
        if (report.RecycleBin is { } bin) { bin.Message ??= ""; bin.Status ??= ""; }
        foreach (var d in report.FileDetails ?? new List<FolderFileDetails>())
        {
            d.Candidates ??= new List<FileEntry>();
            d.Skipped ??= new List<FileEntry>();
        }
        return report;
    }

    /// <summary>Leest status.json; null als er nog geen uitvoering is geweest of het bestand onleesbaar is.</summary>
    public static RunReport? TryLoadStatus(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonFormatException)
        {
            return null;
        }
    }
}

[DataContract]
public sealed class FolderReport
{
    public FolderReport() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Id = "";
        Status = "";
        Message = "";
        SkippedByReason = new Dictionary<string, int>();
    }

    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "status")] public string Status { get; set; } = "";
    [DataMember(Name = "message")] public string Message { get; set; } = "";
    [DataMember(Name = "candidates")] public int Candidates { get; set; }
    [DataMember(Name = "candidateBytes")] public long CandidateBytes { get; set; }
    [DataMember(Name = "deleted")] public int Deleted { get; set; }
    [DataMember(Name = "deletedBytes")] public long DeletedBytes { get; set; }
    [DataMember(Name = "tooYoung")] public int TooYoung { get; set; }
    [DataMember(Name = "skipped")] public int Skipped { get; set; }
    [DataMember(Name = "skippedByReason")] public Dictionary<string, int> SkippedByReason { get; set; } = null!;
    [DataMember(Name = "errors")] public int Errors { get; set; }
}

[DataContract]
public sealed class RecycleBinReport
{
    public RecycleBinReport() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Status = "";
        Message = "";
    }

    [DataMember(Name = "retentionDays")] public int RetentionDays { get; set; }
    [DataMember(Name = "status")] public string Status { get; set; } = "";
    [DataMember(Name = "message")] public string Message { get; set; } = "";
    [DataMember(Name = "candidates")] public int Candidates { get; set; }
    [DataMember(Name = "candidateBytes")] public long CandidateBytes { get; set; }
    [DataMember(Name = "deleted")] public int Deleted { get; set; }
    [DataMember(Name = "deletedBytes")] public long DeletedBytes { get; set; }
    [DataMember(Name = "tooYoung")] public int TooYoung { get; set; }
    [DataMember(Name = "errors")] public int Errors { get; set; }
}

[DataContract]
public sealed class FolderFileDetails
{
    public FolderFileDetails() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init()
    {
        Id = "";
        Candidates = new List<FileEntry>();
        Skipped = new List<FileEntry>();
    }

    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "candidates")] public List<FileEntry> Candidates { get; set; } = null!;
    [DataMember(Name = "skipped")] public List<FileEntry> Skipped { get; set; } = null!;
}

[DataContract]
public sealed class FileEntry
{
    public FileEntry() => Init();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext _) => Init();

    private void Init() => Name = "";

    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "Size")] public long Size { get; set; }
    [DataMember(Name = "Reason")] public string? Reason { get; set; }
}

public static class Format
{
    /// <summary>Bytes leesbaar weergeven, bijv. "4,2 MB".</summary>
    public static string Bytes(long bytes)
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        if (bytes < 1024) return bytes.ToString(nl) + " bytes";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.#", nl) + " kB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.#", nl) + " MB";
        return (mb / 1024.0).ToString("0.##", nl) + " GB";
    }
}
