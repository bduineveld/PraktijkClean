using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PraktijkClean.Core;

/// <summary>Uitkomst van de controle van een map in de beheerapp.</summary>
public sealed record PathCheck(bool Blocked, string? BlockReason, bool IsSync, bool IsNetwork, IReadOnlyList<string> Warnings);

/// <summary>
/// Controles in de beheerapp bij het kiezen of tonen van een map. Dit is een eerste, gebruiksvriendelijke
/// controle; cleanup.ps1 controleert bij elke uitvoering zelf opnieuw en is leidend.
/// </summary>
public static class PathRules
{
    private static readonly Regex SyncName = new(@"^(OneDrive( - .*)?|Dropbox( \(.*\))?|Google Drive|iCloud ?Drive.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Mappen waarin/waarboven nooit mag worden opgeruimd (Windows, Program Files, AppData, ...).
    /// </summary>
    public static IEnumerable<string> ProtectedTrees(Func<string, string?> env, string installDir)
    {
        foreach (var name in new[] { "windir", "SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
                                     "ProgramData", "APPDATA", "LOCALAPPDATA", "CommonProgramFiles" })
        {
            var v = env(name);
            if (!string.IsNullOrWhiteSpace(v)) yield return v!;
        }
        yield return installDir;
    }

    /// <summary>Mappen die zelf (of een bovenliggende map ervan) niet gekozen mogen worden.</summary>
    public static IEnumerable<string> ProtectedExact(Func<string, string?> env)
    {
        var sd = env("SystemDrive");
        if (!string.IsNullOrWhiteSpace(sd)) yield return sd + "\\Users";
        foreach (var name in new[] { "USERPROFILE", "PUBLIC" })
        {
            var v = env(name);
            if (!string.IsNullOrWhiteSpace(v)) yield return v!;
        }
    }

    public static PathCheck Check(string path, Func<string, string?> env, string installDir,
                                  IEnumerable<string> syncRoots, Func<string, bool> isNetworkDrive)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(path)) return Block("Geen map gekozen.");
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")) return Block("Apparaatpaden zijn niet toegestaan.");
        if (path.Split('\\', '/').Any(s => s is "." or "..")) return Block("Pad bevat \"..\".");
        if (Regex.IsMatch(path, "~[0-9]")) return Block("Korte padnamen (zoals PROGRA~1) zijn niet toegestaan; kies de map opnieuw.");

        string root = WindowsRoot(path);
        if (root.Length == 0) return Block("Kies een volledig pad, bijvoorbeeld C:\\Users\\naam\\Scans.");
        if (PathEquals(path, root)) return Block("Een volledig station (bijv. C:\\) kan niet worden gekozen.");

        foreach (var t in ProtectedTrees(env, installDir))
            if (IsInside(path, t) || IsInside(t, path))
                return Block("Systeemmappen, programmamappen en AppData kunnen niet worden gekozen.");
        foreach (var e in ProtectedExact(env))
            if (IsInside(e, path))
                return Block("Deze map (of een bovenliggende map van het gebruikersprofiel) kan niet worden gekozen.");

        bool isNetwork = path.StartsWith(@"\\") || isNetworkDrive(path);
        bool isSync = syncRoots.Any(r => !string.IsNullOrWhiteSpace(r) && IsInside(path, r))
                      || path.Split('\\', '/').Any(s => SyncName.IsMatch(s));

        if (isSync)
            warnings.Add("Deze map wordt gesynchroniseerd met OneDrive of een andere clouddienst. Een verwijderd bestand " +
                         "verdwijnt dan ook uit de cloud en van andere apparaten. Opschonen is hier standaard geblokkeerd.");
        if (isNetwork)
            warnings.Add("Deze map staat op een netwerklocatie of verwisselbaar station. Bestanden kunnen ook voor " +
                         "andere gebruikers verdwijnen en gaan meestal niet naar de Prullenbak. Opschonen is hier standaard geblokkeerd.");

        var docs = env("USERPROFILE");
        if (docs is not null && IsInside(path, Path.Combine(docs, "Documents")))
            warnings.Add("Dit lijkt (een deel van) de map Documenten. Wees hier extra voorzichtig.");

        return new PathCheck(false, null, isSync, isNetwork, warnings);

        static PathCheck Block(string reason) => new(true, reason, false, false, Array.Empty<string>());
    }

    /// <summary>
    /// Stationswortel volgens Windows-regels ("C:\\" of "\\\\server\\share"), ook als de tests op een
    /// ander besturingssysteem draaien. Leeg = geen volledig pad.
    /// </summary>
    public static string WindowsRoot(string path)
    {
        var m = Regex.Match(path, @"^([A-Za-z]:\\|\\\\[^\\/]+[\\/][^\\/]+)");
        return m.Success ? m.Value : "";
    }

    public static bool PathEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsInside(string child, string parent)
    {
        string c = Normalize(child), p = Normalize(parent);
        if (c.Length == 0 || p.Length == 0) return false;
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;
        if (!p.EndsWith('\\')) p += "\\";
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
    {
        string p = path.Replace('/', '\\');
        while (p.Length > 3 && p.EndsWith('\\')) p = p.Substring(0, p.Length - 1);
        return p;
    }
}
