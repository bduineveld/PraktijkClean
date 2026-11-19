using System;
using System.Collections.Generic;

namespace PraktijkClean.Core;

/// <summary>
/// Hoe PraktijkClean met submappen omgaat, in drie niveaus:
/// <list type="bullet">
/// <item><b>Licht</b>: alleen bestanden direct in de map; submappen blijven ongemoeid (Recurse uit).</item>
/// <item><b>Midden</b>: ook oude bestanden in submappen opruimen; de submappen zelf blijven staan (Recurse aan).</item>
/// <item><b>Zwaar</b>: als Midden, en submappen die daarna leeg zijn (en ouder dan de bewaartermijn) worden weggehaald
/// (Recurse en RemoveEmptyFolders aan). Een map met inhoud wordt nooit verwijderd.</item>
/// </list>
/// </summary>
public static class SubfolderLevels
{
    public const string Light = "light";
    public const string Medium = "medium";
    public const string Heavy = "heavy";

    public sealed record Option(string Level, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    public static readonly IReadOnlyList<Option> All = new[]
    {
        new Option(Light, "Licht – submappen overslaan", "Alleen bestanden direct in deze map. Submappen en alles daarin blijven staan."),
        new Option(Medium, "Midden – ook bestanden in submappen", "Ook oude bestanden in submappen worden opgeruimd. De submappen zelf blijven altijd staan."),
        new Option(Heavy, "Zwaar – ook lege submappen weghalen", "Ook oude bestanden in submappen, en submappen die daarna leeg zijn worden weggehaald. Een map waar nog iets in staat, blijft altijd staan."),
    };

    public static bool IsValid(string? level) => level is Light or Medium or Heavy;

    public static string Of(FolderSettings f) => !f.Recurse ? Light : f.RemoveEmptyFolders ? Heavy : Medium;

    public static void Apply(FolderSettings f, string level)
    {
        if (!IsValid(level)) throw new ArgumentException("Onbekend niveau: " + level, nameof(level));
        f.Recurse = level != Light;
        f.RemoveEmptyFolders = level == Heavy;
    }

    public static int IndexOf(string? level)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Level == level) return i;
        return 0;
    }

    /// <summary>Kort, voor onder de mapnaam in de lijst ("" bij Licht).</summary>
    public static string ShortText(FolderSettings f) => Of(f) switch
    {
        Medium => "met submappen",
        Heavy => "lege submappen weg",
        _ => "",
    };
}
