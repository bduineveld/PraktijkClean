using System.Collections.Generic;

namespace PraktijkClean.Core;

/// <summary>De keuzes voor bewaartermijn zoals getoond in de interface.</summary>
public static class RetentionOptions
{
    public sealed record Option(int? Days, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Days == null betekent: zelf een aantal dagen opgeven.</summary>
    public static readonly IReadOnlyList<Option> All = new[]
    {
        new Option(0, "Direct opruimen"),
        new Option(1, "Ouder dan 1 dag"),
        new Option(3, "Ouder dan 3 dagen"),
        new Option(7, "Ouder dan 7 dagen"),
        new Option(14, "Ouder dan 14 dagen"),
        new Option(30, "Ouder dan 30 dagen"),
        new Option(null, "Zelf aantal dagen opgeven"),
    };

    /// <summary>Index in <see cref="All"/> voor een aantal dagen; onbekende waarden geven "zelf opgeven".</summary>
    public static int IndexFor(int days)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Days == days) return i;
        return All.Count - 1;
    }
}
