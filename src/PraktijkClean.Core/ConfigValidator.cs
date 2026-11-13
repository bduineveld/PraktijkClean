using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PraktijkClean.Core;

/// <summary>
/// Controleert instellingen voordat ze worden opgeslagen. Gebruikt dezelfde regels als
/// cleanup.ps1 (dat de configuratie bij elke uitvoering nogmaals zelf controleert).
/// </summary>
public static class ConfigValidator
{
    public const int MaxFolders = 20;
    public const int MaxListItems = 200;

    private static readonly Regex IdRegex = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex ExtensionRegex = new(@"^\.[A-Za-z0-9_\-]{1,20}$", RegexOptions.CultureInvariant);
    private static readonly char[] PatternForbidden = { '\\', '/', ':', '[', ']', '"', '<', '>', '|' };
    private static readonly char[] RelativeForbidden = { ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Retourneert een lijst met foutmeldingen in begrijpelijk Nederlands. Leeg = geldig.</summary>
    public static List<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (config.SchemaVersion != AppConfig.CurrentSchemaVersion)
            errors.Add($"Onbekende configuratieversie ({config.SchemaVersion}).");
        if (config.MaxFilesPerRun is < 1 or > 5000)
            errors.Add("Het maximum aantal bestanden per keer moet tussen 1 en 5000 liggen.");
        if (config.MinimumAgeMinutes is < 1 or > 1440)
            errors.Add("De minimale leeftijd moet tussen 1 en 1440 minuten liggen.");
        if (config.DeleteMode != DeleteModes.RecycleBin && config.DeleteMode != DeleteModes.Permanent)
            errors.Add("Kies of bestanden naar de Prullenbak gaan of definitief worden verwijderd.");
        if (config.RecycleBinRetentionDays is < 0 or > 3650)
            errors.Add("De termijn voor het legen van de Prullenbak moet tussen 0 en 3650 dagen liggen.");

        if (config.Schedule is null)
            errors.Add("Planning ontbreekt.");
        else if (config.Schedule.Daily && !TryParseTime(config.Schedule.DailyTime, out _))
            errors.Add("Het dagelijkse tijdstip moet de vorm UU:MM hebben (bijv. 12:30).");

        if (config.Folders is null || config.Folders.Count == 0)
        {
            errors.Add("Er is geen map ingesteld.");
            return errors;
        }
        if (config.Folders.Count > MaxFolders)
            errors.Add($"Er kunnen maximaal {MaxFolders} mappen worden ingesteld.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in config.Folders)
        {
            string label = string.IsNullOrWhiteSpace(f.DisplayName) ? $"Map '{f.Id}'" : $"Map '{f.DisplayName}'";
            if (f.Id is null || !IdRegex.IsMatch(f.Id))
                errors.Add($"{label}: ongeldige interne naam (id).");
            else if (!ids.Add(f.Id))
                errors.Add($"{label}: komt dubbel voor.");

            if (f.Type == FolderTypes.KnownFolder)
            {
                if (f.KnownFolder != KnownFolderNames.Desktop && f.KnownFolder != KnownFolderNames.Downloads)
                    errors.Add($"{label}: onbekende standaardmap.");
            }
            else if (f.Type == FolderTypes.Custom)
            {
                if (string.IsNullOrWhiteSpace(f.Path))
                    errors.Add($"{label}: pad ontbreekt.");
            }
            else
            {
                errors.Add($"{label}: onbekend maptype.");
            }

            if (string.IsNullOrWhiteSpace(f.ExpectedPath))
                errors.Add($"{label}: de locatie van de map kon niet worden bepaald.");
            if (f.RetentionDays is < 0 or > 3650)
                errors.Add($"{label}: de bewaartermijn moet tussen 0 en 3650 dagen liggen.");

            var ex = f.Exclusions;
            if (ex is null)
            {
                errors.Add($"{label}: uitzonderingen ontbreken.");
                continue;
            }
            ValidateList(ex.Files, $"{label}: uitgesloten bestand", IsValidRelative, errors);
            ValidateList(ex.Subfolders, $"{label}: uitgesloten submap", IsValidRelative, errors);
            ValidateList(ex.Patterns, $"{label}: patroon", IsValidPattern, errors);
            ValidateList(ex.Extensions, $"{label}: extensie", IsValidExtension, errors);
        }
        return errors;
    }

    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (text is null) return false;
        if (!Regex.IsMatch(text, "^[0-2][0-9]:[0-5][0-9]$")) return false;
        return TimeSpan.TryParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromHours(24);
    }

    public static bool IsValidExtension(string s) => s is not null && ExtensionRegex.IsMatch(s);

    public static bool IsValidPattern(string s) =>
        !string.IsNullOrWhiteSpace(s) && s.Length <= 260 && s.IndexOfAny(PatternForbidden) < 0 && !s.Any(char.IsControl);

    public static bool IsValidRelative(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 260 || s.Any(char.IsControl)) return false;
        if (s.IndexOfAny(RelativeForbidden) >= 0) return false;
        if (s.StartsWith('\\') || s.StartsWith('/')) return false;
        foreach (var seg in s.Split('\\', '/'))
            if (seg is "" or "." or "..") return false;
        return true;
    }

    private static void ValidateList(List<string>? list, string label, Func<string, bool> isValid, List<string> errors)
    {
        if (list is null) { errors.Add($"{label}: lijst ontbreekt."); return; }
        if (list.Count > MaxListItems) errors.Add($"{label}: te veel items (maximaal {MaxListItems}).");
        foreach (var item in list)
            if (item is null || !isValid(item.Trim()))
                errors.Add($"{label} '{item}' is ongeldig.");
    }
}
