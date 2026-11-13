using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PraktijkClean.Core;

/// <summary>Lezen, schrijven en standaardwaarden van config.json.</summary>
public static class ConfigStore
{
    /// <summary>Standaard bewaartermijn voor Bureaublad en Downloads (dagen).</summary>
    public const int DefaultRetentionDays = 1;

    /// <summary>Standaard: zo lang blijft iets in de Prullenbak staan voordat PraktijkClean het definitief verwijdert (dagen).</summary>
    public const int DefaultRecycleBinDays = 1;

    /// <summary>
    /// Standaardinstellingen bij eerste gebruik: simulatiemodus; Bureaublad en Downloads na 1 dag naar de Prullenbak,
    /// en wat daarna nog 1 dag in de Prullenbak staat, wordt definitief verwijderd.
    /// (Alleen voor nieuwe installaties. Een bestaande config.json zonder een van deze velden krijgt de
    /// voorzichtiger waarden uit AppConfig.Init, zodat er na een update niet ineens meer wordt verwijderd.)
    /// </summary>
    public static AppConfig CreateDefault(string desktopPath, string downloadsPath)
    {
        return new AppConfig
        {
            SimulationMode = true,
            AutomaticCleanupConfirmed = false,
            DeleteMode = DeleteModes.RecycleBin,
            EmptyRecycleBin = true,
            RecycleBinRetentionDays = DefaultRecycleBinDays,
            Folders =
            {
                CreateKnownFolder("desktop", "Bureaublad", KnownFolderNames.Desktop, desktopPath),
                CreateKnownFolder("downloads", "Downloads", KnownFolderNames.Downloads, downloadsPath),
            },
        };
    }

    public static FolderSettings CreateKnownFolder(string id, string displayName, string knownFolder, string path) => new()
    {
        Id = id,
        DisplayName = displayName,
        Type = FolderTypes.KnownFolder,
        KnownFolder = knownFolder,
        ExpectedPath = path,
        RetentionDays = DefaultRetentionDays,
        Exclusions = DefaultExclusions(),
    };

    public static FolderSettings CreateCustomFolder(string id, string path) => new()
    {
        Id = id,
        DisplayName = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,
        Type = FolderTypes.Custom,
        Path = path,
        ExpectedPath = path,
        RetentionDays = 30,
        Exclusions = DefaultExclusions(),
    };

    /// <summary>Snelkoppelingen (.lnk) en internetkoppelingen (.url) blijven standaard behouden.</summary>
    public static ExclusionSettings DefaultExclusions() => new() { Extensions = { ".lnk", ".url" } };

    /// <summary>Kiest een vrije id voor een nieuwe, zelf gekozen map.</summary>
    public static string NextCustomId(AppConfig config)
    {
        for (int i = 1; ; i++)
        {
            string id = "map-" + i;
            if (config.Folders.All(f => f.Id != id)) return id;
        }
    }

    public static string Serialize(AppConfig config) => Json.Serialize(config);

    public static AppConfig Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? throw new InvalidDataException("config.json is leeg.") : Json.Deserialize<AppConfig>(json);

    /// <summary>Leest config.json. Geeft null en een foutmelding als het bestand ontbreekt of ongeldig is.</summary>
    public static AppConfig? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) { error = "Er is nog geen configuratie opgeslagen."; return null; }
            var config = Deserialize(File.ReadAllText(path, Encoding.UTF8));
            var problems = ConfigValidator.Validate(config);
            if (problems.Count > 0) { error = "De opgeslagen configuratie is ongeldig: " + string.Join(" ", problems); return null; }
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonFormatException or InvalidDataException or NotSupportedException)
        {
            error = "De configuratie kon niet worden gelezen (" + ex.GetType().Name + ").";
            return null;
        }
    }

    /// <summary>Valideert en schrijft config.json via een tijdelijk bestand (geen half geschreven bestand bij een storing).</summary>
    public static void Save(string path, AppConfig config)
    {
        var problems = ConfigValidator.Validate(config);
        if (problems.Count > 0)
            throw new InvalidOperationException("De instellingen zijn niet opgeslagen:\n- " + string.Join("\n- ", problems));
        WriteAtomic(path, Serialize(config), new UTF8Encoding(false));
    }

    public static void WriteAtomic(string path, string content, Encoding encoding)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content, encoding);
        FileCompat.MoveOverwrite(tmp, path);
    }

    /// <summary>Zet tekst als "a; b; c" om naar een opgeschoonde lijst (lege items vervallen, dubbele weg).</summary>
    public static List<string> SplitList(string? text) =>
        (text ?? "")
            .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string JoinList(IEnumerable<string> items) => string.Join("; ", items);

    /// <summary>
    /// Extensies uit de interface normaliseren: "pdf" wordt ".pdf".
    /// </summary>
    public static List<string> NormalizeExtensions(IEnumerable<string> items) =>
        items.Select(e => e.StartsWith('.') ? e : "." + e).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Splitst de invoer "bestanden en patronen" uit de interface: items met * of ? worden patronen.
    /// </summary>
    public static (List<string> Files, List<string> Patterns) SplitFilesAndPatterns(IEnumerable<string> items)
    {
        var files = new List<string>();
        var patterns = new List<string>();
        foreach (var item in items)
        {
            if (item.IndexOfAny(new[] { '*', '?' }) >= 0) patterns.Add(item); else files.Add(item);
        }
        return (files, patterns);
    }
}
