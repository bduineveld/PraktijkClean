using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PraktijkClean.Core;

/// <summary>
/// De lijst met verwijderde bestanden (logs\verwijderde-bestanden.log). cleanup.ps1 schrijft er na elke opruimbeurt
/// regels bij en haalt regels weg die ouder zijn dan de ingestelde termijn. Anders dan het logboek bevat deze lijst
/// WEL bestandsnamen, zodat de gebruiker kan terugzien wat er is opgeruimd.
/// Opbouw per regel (tab-gescheiden): tijdstip UTC (yyyy-MM-ddTHH:mm:ssZ), soort, grootte, map-id, volledig pad.
/// </summary>
public static class DeletedFilesList
{
    public const int MaxDays = 365;

    public const string KindRecycleBin = "prullenbak";
    public const string KindPermanent = "definitief";
    public const string KindBinEmptied = "prullenbak-geleegd";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public sealed record Entry(DateTime DeletedUtc, string Kind, long Size, string FolderId, string FullPath)
    {
        public string FileName => Path.GetFileName(FullPath);
        public string Directory => Path.GetDirectoryName(FullPath) ?? "";

        public string KindText => Kind switch
        {
            KindRecycleBin => "Naar de Prullenbak",
            KindPermanent => "Definitief verwijderd",
            KindBinEmptied => "Prullenbak geleegd",
            _ => Kind,
        };
    }

    /// <summary>De keuzes in de interface.</summary>
    public static readonly IReadOnlyList<(int Days, string Label)> Options = new[]
    {
        (0, "Niet bijhouden"),
        (7, "1 week"),
        (14, "2 weken"),
        (30, "1 maand"),
        (90, "3 maanden"),
    };

    /// <summary>"de afgelopen week", "de afgelopen maand", ... voor in een zin.</summary>
    public static string PeriodText(int days) => days switch
    {
        7 => "de afgelopen week",
        14 => "de afgelopen 2 weken",
        30 => "de afgelopen maand",
        90 => "de afgelopen 3 maanden",
        1 => "de afgelopen dag",
        _ => $"de afgelopen {days} dagen",
    };

    public static int OptionIndexFor(int days)
    {
        for (int i = 0; i < Options.Count; i++) if (Options[i].Days == days) return i;
        // Een (handmatig ingestelde) andere waarde: de dichtstbijzijnde langere keuze, zodat er nooit iets onverwacht wegvalt.
        for (int i = 0; i < Options.Count; i++) if (Options[i].Days > days) return i;
        return Options.Count - 1;
    }

    public static Entry? ParseLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var parts = line.Split(new[] { '\t' }, 5);
        if (parts.Length < 5) return null;
        if (!DateTime.TryParseExact(parts[0], TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts)) return null;
        long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);
        if (parts[4].Length == 0) return null;
        return new Entry(ts, parts[1], size, parts[3], parts[4]);
    }

    public static string FormatLine(Entry e) =>
        string.Join("\t", e.DeletedUtc.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture), e.Kind,
            e.Size.ToString(CultureInfo.InvariantCulture), e.FolderId, e.FullPath);

    /// <summary>
    /// Leest de lijst, nieuwste eerst. Regels ouder dan <paramref name="days"/> dagen worden niet getoond
    /// (ook als het script ze nog niet heeft weggehaald); bij 0 dagen is de lijst leeg.
    /// </summary>
    public static List<Entry> Load(string path, int days, DateTime nowUtc)
    {
        var result = new List<Entry>();
        if (days <= 0) return result;
        DateTime cutoff = nowUtc.AddDays(-days);
        try
        {
            if (!File.Exists(path)) return result;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
                if (ParseLine(line) is { } e && e.DeletedUtc >= cutoff) result.Add(e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// Haalt regels ouder dan <paramref name="days"/> dagen weg; bij 0 dagen wordt de hele lijst gewist.
    /// Wordt aangeroepen na het opslaan, zodat een kortere termijn direct geldt en niet pas bij de volgende controle.
    /// </summary>
    public static void Prune(string path, int days, DateTime nowUtc)
    {
        if (days <= 0) { Clear(path); return; }
        if (!File.Exists(path)) return;
        DateTime cutoff = nowUtc.AddDays(-days);
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var keep = lines.Where(l => ParseLine(l) is { } e && e.DeletedUtc >= cutoff).ToArray();
        if (keep.Length == lines.Length) return;
        if (keep.Length == 0) { Clear(path); return; }
        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, keep, new UTF8Encoding(false));
        FileCompat.MoveOverwrite(tmp, path);
    }

    /// <summary>Wist de lijst (en een eventueel achtergebleven tijdelijk bestand).</summary>
    public static void Clear(string path)
    {
        foreach (string p in new[] { path, path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }
}
