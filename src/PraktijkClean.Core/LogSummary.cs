using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PraktijkClean.Core;

/// <summary>
/// Telt uit het logboek (praktijkclean.log) hoeveel er in de afgelopen dagen is opgeruimd.
/// Het logboek bevat geen bestandsnamen; elke uitvoering is één regel, bijvoorbeeld:
/// "2026-11-13 12:30:05 [Taakplanner] opgeschoond: 3 in aanmerking (1.2 MB), 3 verwijderd (1.2 MB), ..."
/// </summary>
public sealed class LogSummary
{
    public int Runs { get; private set; }
    public int CleanRuns { get; private set; }
    public int DeletedFiles { get; private set; }
    public double DeletedMegabytes { get; private set; }
    public int BinItemsDeleted { get; private set; }
    public double BinMegabytes { get; private set; }
    public int Errors { get; private set; }
    public DateTime? LastClean { get; private set; }

    private static readonly Regex LineRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[(?<trigger>[^\]]*)\] (?<action>[^:]+): " +
        @".*?, (?<del>\d+) verwijderd(?: \((?<delmb>[\d.]+) MB\))?, .*?, (?<err>\d+) fouten\.",
        RegexOptions.CultureInvariant);

    private static readonly Regex BinRegex = new(
        @"\| Prullenbak: .*?, (?<del>\d+) geleegd(?: \((?<mb>[\d.]+) MB\))?",
        RegexOptions.CultureInvariant);

    /// <summary>Vat de regels samen vanaf <paramref name="sinceLocal"/> (lokale tijd, zoals in het logboek).</summary>
    public static LogSummary Parse(IEnumerable<string> lines, DateTime sinceLocal)
    {
        var s = new LogSummary();
        foreach (string line in lines)
        {
            var m = LineRegex.Match(line);
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) continue;
            if (ts < sinceLocal) continue;

            s.Runs++;
            s.Errors += int.Parse(m.Groups["err"].Value, CultureInfo.InvariantCulture);
            int deleted = int.Parse(m.Groups["del"].Value, CultureInfo.InvariantCulture);
            s.DeletedFiles += deleted;
            s.DeletedMegabytes += ParseMb(m.Groups["delmb"].Value);

            int binDeleted = 0;
            var b = BinRegex.Match(line);
            if (b.Success)
            {
                binDeleted = int.Parse(b.Groups["del"].Value, CultureInfo.InvariantCulture);
                s.BinItemsDeleted += binDeleted;
                s.BinMegabytes += ParseMb(b.Groups["mb"].Value);
            }

            if (m.Groups["action"].Value.Trim() == "opgeschoond")
            {
                s.CleanRuns++;
                if (s.LastClean is null || ts > s.LastClean) s.LastClean = ts;
            }
        }
        return s;
    }

    /// <summary>Leest het logboek en de vorige (geroteerde) versie. Bij een leesfout: een lege samenvatting.</summary>
    public static LogSummary Load(string logPath, DateTime sinceLocal)
    {
        var lines = new List<string>();
        try
        {
            string old = Path.Combine(Path.GetDirectoryName(logPath) ?? "", "praktijkclean.1.log");
            foreach (string p in new[] { old, logPath })
                if (File.Exists(p)) lines.AddRange(ReadShared(p));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Parse(lines, sinceLocal);
    }

    /// <summary>Leest ook als het script het bestand op dat moment open heeft.</summary>
    private static IEnumerable<string> ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var result = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null) result.Add(line);
        return result;
    }

    private static double ParseMb(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}
