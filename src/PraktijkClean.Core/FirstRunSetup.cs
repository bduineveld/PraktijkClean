using System;
using System.Collections.Generic;
using System.Linq;

namespace PraktijkClean.Core;

/// <summary>De keuzes uit de startwizard (eerste start van PraktijkClean).</summary>
public sealed class FirstRunChoices
{
    public bool Desktop { get; set; } = true;
    public bool Downloads { get; set; } = true;
    /// <summary>Zelf gekozen mappen (volledige paden), al gecontroleerd door de app.</summary>
    public List<string> OtherFolders { get; } = new();
    public int RetentionDays { get; set; } = ConfigStore.DefaultRetentionDays;
    public string SubfolderLevel { get; set; } = SubfolderLevels.Light;
    public bool RecycleBin { get; set; } = true;
    public bool EmptyRecycleBin { get; set; } = true;
    public bool Daily { get; set; } = true;
    public string DailyTime { get; set; } = "12:30";
    public bool AtLogon { get; set; } = true;

    /// <summary>Vooraf ingevuld met wat er nu in de instellingen staat.</summary>
    public static FirstRunChoices From(AppConfig config)
    {
        var known = config.Folders.Where(f => f.Type == FolderTypes.KnownFolder).ToList();
        var first = config.Folders.FirstOrDefault();
        return new FirstRunChoices
        {
            Desktop = known.FirstOrDefault(f => f.KnownFolder == KnownFolderNames.Desktop)?.Enabled ?? true,
            Downloads = known.FirstOrDefault(f => f.KnownFolder == KnownFolderNames.Downloads)?.Enabled ?? true,
            RetentionDays = first?.RetentionDays ?? ConfigStore.DefaultRetentionDays,
            SubfolderLevel = first is null ? SubfolderLevels.Light : SubfolderLevels.Of(first),
            RecycleBin = config.DeleteMode != DeleteModes.Permanent,
            EmptyRecycleBin = config.EmptyRecycleBin,
            Daily = config.Schedule.Daily,
            DailyTime = config.Schedule.DailyTime,
            AtLogon = config.Schedule.AtLogon,
        };
    }
}

/// <summary>
/// Zet de keuzes uit de startwizard in de instellingen. De simulatiemodus blijft aan: er wordt pas iets verwijderd
/// nadat de gebruiker de simulatie heeft gezien en automatisch opruimen zelf aanzet.
/// </summary>
public static class FirstRunSetup
{
    public static void Apply(AppConfig config, FirstRunChoices c)
    {
        if (c.RetentionDays is < 0 or > 3650) throw new ArgumentOutOfRangeException(nameof(c), "Bewaartermijn buiten bereik.");
        if (!SubfolderLevels.IsValid(c.SubfolderLevel)) throw new ArgumentException("Onbekend niveau voor submappen.", nameof(c));

        foreach (var f in config.Folders.Where(f => f.Type == FolderTypes.KnownFolder))
        {
            if (f.KnownFolder == KnownFolderNames.Desktop) f.Enabled = c.Desktop;
            if (f.KnownFolder == KnownFolderNames.Downloads) f.Enabled = c.Downloads;
        }
        foreach (string path in c.OtherFolders)
        {
            if (config.Folders.Any(f => PathRules.PathEquals(f.ExpectedPath, path))) continue;
            if (config.Folders.Count >= ConfigValidator.MaxFolders) break;
            config.Folders.Add(ConfigStore.CreateCustomFolder(ConfigStore.NextCustomId(config), path));
        }
        // Bewaartermijn en submappen gelden voor alle mappen, en worden de standaard voor mappen die later bijkomen.
        foreach (var f in config.Folders)
        {
            f.RetentionDays = c.RetentionDays;
            SubfolderLevels.Apply(f, c.SubfolderLevel);
        }
        config.DefaultRetentionDays = c.RetentionDays;
        config.DefaultSubfolderLevel = c.SubfolderLevel;

        config.DeleteMode = c.RecycleBin ? DeleteModes.RecycleBin : DeleteModes.Permanent;
        config.EmptyRecycleBin = c.RecycleBin && c.EmptyRecycleBin;
        config.Schedule.Daily = c.Daily;
        config.Schedule.DailyTime = c.DailyTime;
        config.Schedule.AtLogon = c.AtLogon;
        config.SimulationMode = true;
        config.AutomaticCleanupConfirmed = false;
    }
}
