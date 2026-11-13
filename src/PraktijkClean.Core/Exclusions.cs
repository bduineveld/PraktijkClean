using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PraktijkClean.Core;

public enum ExclusionKind
{
    File,
    Pattern,
    Extension,
    Subfolder,
}

/// <summary>Eén uitzondering van een map, zoals getoond in de lijst in de app.</summary>
public sealed record ExclusionItem(ExclusionKind Kind, string Value)
{
    public string KindLabel => Exclusions.Label(Kind);
}

/// <summary>
/// Toevoegen, verwijderen en tonen van uitzonderingen per map. Controleert met dezelfde regels als
/// <see cref="ConfigValidator"/> (en cleanup.ps1), zodat de instellingen daarna altijd opgeslagen kunnen worden.
/// </summary>
public static class Exclusions
{
    public static string Label(ExclusionKind kind) => kind switch
    {
        ExclusionKind.File => "Bestand",
        ExclusionKind.Pattern => "Patroon",
        ExclusionKind.Extension => "Extensie",
        ExclusionKind.Subfolder => "Submap",
        _ => kind.ToString(),
    };

    /// <summary>Alle uitzonderingen van een map, in een vaste volgorde (extensies, bestanden, patronen, submappen).</summary>
    public static List<ExclusionItem> All(FolderSettings folder)
    {
        var ex = folder.Exclusions;
        return ex.Extensions.Select(v => new ExclusionItem(ExclusionKind.Extension, v))
            .Concat(ex.Files.Select(v => new ExclusionItem(ExclusionKind.File, v)))
            .Concat(ex.Patterns.Select(v => new ExclusionItem(ExclusionKind.Pattern, v)))
            .Concat(ex.Subfolders.Select(v => new ExclusionItem(ExclusionKind.Subfolder, v)))
            .ToList();
    }

    /// <summary>
    /// Bepaalt wat de gebruiker heeft getypt: ".pdf" of "pdf"-achtige extensie, een patroon met * of ?,
    /// of anders een bestandsnaam. Geeft null bij lege invoer.
    /// </summary>
    public static ExclusionItem? Classify(string? input)
    {
        string s = (input ?? "").Trim().Trim('"').Trim();
        if (s.Length == 0) return null;
        if (s.IndexOfAny(new[] { '*', '?' }) >= 0) return new ExclusionItem(ExclusionKind.Pattern, s);
        if (s.StartsWith(".") && s.IndexOfAny(new[] { '\\', '/' }) < 0 && s.IndexOf('.', 1) < 0)
            return new ExclusionItem(ExclusionKind.Extension, s);
        return new ExclusionItem(ExclusionKind.File, s.Replace('/', '\\'));
    }

    /// <summary>
    /// Voegt een uitzondering toe. Geeft null bij succes (ook als hij er al stond), anders een uitleg voor de gebruiker.
    /// </summary>
    public static string? TryAdd(FolderSettings folder, ExclusionItem item)
    {
        string value = item.Value.Trim();
        if (item.Kind == ExclusionKind.Extension && !value.StartsWith(".")) value = "." + value;
        if (item.Kind is ExclusionKind.File or ExclusionKind.Subfolder) value = value.Replace('/', '\\').TrimEnd('\\');

        bool valid = item.Kind switch
        {
            ExclusionKind.Extension => ConfigValidator.IsValidExtension(value),
            ExclusionKind.Pattern => ConfigValidator.IsValidPattern(value),
            _ => ConfigValidator.IsValidRelative(value),
        };
        if (!valid)
        {
            return item.Kind switch
            {
                ExclusionKind.Extension => $"'{value}' is geen geldige extensie. Gebruik bijvoorbeeld .docx of .pdf.",
                ExclusionKind.Pattern => $"'{value}' is geen geldig patroon. Gebruik alleen een naam met * of ?, zonder mapnamen, bijvoorbeeld protocol-*.",
                ExclusionKind.Subfolder => $"'{value}' is geen geldige submap binnen deze map.",
                _ => $"'{value}' is geen geldige bestandsnaam binnen deze map.",
            };
        }

        var list = ListFor(folder, item.Kind);
        if (list.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))) return null;
        if (list.Count >= ConfigValidator.MaxListItems)
            return $"Er kunnen maximaal {ConfigValidator.MaxListItems} uitzonderingen van de soort '{Label(item.Kind)}' per map worden opgegeven.";
        list.Add(value);
        return null;
    }

    public static bool Remove(FolderSettings folder, ExclusionItem item)
    {
        var list = ListFor(folder, item.Kind);
        int i = list.FindIndex(v => string.Equals(v, item.Value, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        list.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// Pad van een bestand of map ten opzichte van de opgeschoonde map, bijvoorbeeld "Brief.pdf" of "Scans\Brief.pdf".
    /// Geeft null als het niet binnen die map ligt (of de map zelf is).
    /// </summary>
    public static string? RelativeTo(string folderPath, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fullPath)) return null;
        string root = folderPath.Replace('/', '\\').TrimEnd('\\') + "\\";
        string full = fullPath.Replace('/', '\\').TrimEnd('\\');
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        string rel = full.Substring(root.Length);
        if (rel.Length == 0) return null;
        return ConfigValidator.IsValidRelative(rel) ? rel : null;
    }

    /// <summary>De submap van een relatief pad ("Scans\Oud\a.pdf" geeft "Scans\Oud"), of null als het direct in de map staat.</summary>
    public static string? ParentOf(string relativePath)
    {
        int i = relativePath.Replace('/', '\\').LastIndexOf('\\');
        return i > 0 ? relativePath.Substring(0, i) : null;
    }

    /// <summary>Extensie van een bestandsnaam als die ook als uitzondering geldig is (".pdf"), anders null.</summary>
    public static string? ExtensionOf(string fileName)
    {
        string ext = Path.GetExtension(fileName ?? "");
        return ConfigValidator.IsValidExtension(ext) ? ext.ToLowerInvariant() : null;
    }

    private static List<string> ListFor(FolderSettings folder, ExclusionKind kind) => kind switch
    {
        ExclusionKind.File => folder.Exclusions.Files,
        ExclusionKind.Pattern => folder.Exclusions.Patterns,
        ExclusionKind.Extension => folder.Exclusions.Extensions,
        ExclusionKind.Subfolder => folder.Exclusions.Subfolders,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
