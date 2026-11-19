using System;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

public sealed class SubfolderLevelTests
{
    [Test]
    public void NiveausZettenRecurseEnLegeMappen()
    {
        var f = new FolderSettings();
        Assert.Equal(SubfolderLevels.Light, SubfolderLevels.Of(f), "nieuwe map is Licht");

        SubfolderLevels.Apply(f, SubfolderLevels.Medium);
        Assert.True(f.Recurse && !f.RemoveEmptyFolders, "Midden = submappen doorzoeken, mappen laten staan");
        Assert.Equal(SubfolderLevels.Medium, SubfolderLevels.Of(f), "terug te lezen als Midden");

        SubfolderLevels.Apply(f, SubfolderLevels.Heavy);
        Assert.True(f.Recurse && f.RemoveEmptyFolders, "Zwaar = ook lege mappen weghalen");
        Assert.Equal(SubfolderLevels.Heavy, SubfolderLevels.Of(f), "terug te lezen als Zwaar");

        SubfolderLevels.Apply(f, SubfolderLevels.Light);
        Assert.True(!f.Recurse && !f.RemoveEmptyFolders, "Licht zet beide uit");

        bool threw = false;
        try { SubfolderLevels.Apply(f, "extra-zwaar"); } catch (ArgumentException) { threw = true; }
        Assert.True(threw, "onbekend niveau geweigerd");
    }

    [Test]
    public void NieuweMapKrijgtHetStandaardniveau()
    {
        var heavy = ConfigStore.CreateCustomFolder("map-1", @"C:\Praktijk\Scans", SubfolderLevels.Heavy);
        Assert.Equal(SubfolderLevels.Heavy, SubfolderLevels.Of(heavy), "standaard Zwaar");
        var light = ConfigStore.CreateCustomFolder("map-2", @"C:\Praktijk\Export");
        Assert.Equal(SubfolderLevels.Light, SubfolderLevels.Of(light), "zonder opgave Licht");
        var odd = ConfigStore.CreateCustomFolder("map-3", @"C:\Praktijk\Oud", "onzin");
        Assert.Equal(SubfolderLevels.Light, SubfolderLevels.Of(odd), "onbekend standaardniveau = Licht");
    }

    [Test]
    public void ValidatieLegeMappenAlleenMetSubmappenEnGeldigStandaardniveau()
    {
        var config = ConfigStore.CreateDefault(@"C:\Users\test\Desktop", @"C:\Users\test\Downloads");
        Assert.Equal(0, ConfigValidator.Validate(config).Count, "standaard geldig");
        Assert.Equal(SubfolderLevels.Light, config.DefaultSubfolderLevel, "standaardniveau Licht");

        config.Folders[0].RemoveEmptyFolders = true;   // zonder Recurse
        Assert.Equal(1, ConfigValidator.Validate(config).Count, "lege mappen zonder submappen ongeldig");
        config.Folders[0].Recurse = true;
        Assert.Equal(0, ConfigValidator.Validate(config).Count, "Zwaar geldig");

        config.DefaultSubfolderLevel = "extra";
        Assert.Equal(1, ConfigValidator.Validate(config).Count, "onbekend standaardniveau ongeldig");
    }

    [Test]
    public void OudeConfigZonderNieuweVeldenBlijftLichtEnVoorzichtig()
    {
        const string oldJson = "{\"schemaVersion\":1,\"simulationMode\":true,\"automaticCleanupConfirmed\":false,\"paused\":false," +
            "\"maxFilesPerRun\":200,\"minimumAgeMinutes\":15,\"deleteMode\":\"permanent\"," +
            "\"schedule\":{\"daily\":true,\"dailyTime\":\"12:30\",\"atLogon\":true}," +
            "\"folders\":[{\"id\":\"map-1\",\"displayName\":\"Map\",\"enabled\":true,\"type\":\"custom\",\"knownFolder\":null," +
            "\"path\":\"C:\\\\Map\",\"expectedPath\":\"C:\\\\Map\",\"retentionDays\":7,\"recurse\":true,\"allowNetworkOrSync\":false," +
            "\"exclusions\":{\"files\":[],\"subfolders\":[],\"patterns\":[],\"extensions\":[]}}]}";
        var config = ConfigStore.Deserialize(oldJson);
        Assert.Equal(SubfolderLevels.Light, config.DefaultSubfolderLevel, "standaardniveau ontbreekt = Licht");
        Assert.True(!config.Folders[0].RemoveEmptyFolders, "lege mappen ontbreekt = uit");
        Assert.Equal(SubfolderLevels.Medium, SubfolderLevels.Of(config.Folders[0]), "bestaand vinkje aan = Midden");

        config.DefaultSubfolderLevel = SubfolderLevels.Heavy;
        SubfolderLevels.Apply(config.Folders[0], SubfolderLevels.Heavy);
        string json = ConfigStore.Serialize(config);
        Assert.True(json.Contains("removeEmptyFolders") && json.Contains("defaultSubfolderLevel"), "nieuwe velden worden opgeslagen");
        var back = ConfigStore.Deserialize(json);
        Assert.Equal(SubfolderLevels.Heavy, back.DefaultSubfolderLevel, "standaardniveau na opslaan");
        Assert.Equal(SubfolderLevels.Heavy, SubfolderLevels.Of(back.Folders[0]), "niveau van de map na opslaan");
    }

    [Test]
    public void WeggehaaldeLegeMapInLijstVerwijderdeBestanden()
    {
        var line = DeletedFilesList.FormatLine(new DeletedFilesList.Entry(DateTime.UtcNow, DeletedFilesList.KindFolderRecycleBin, 0, "map-1",
            @"C:\Praktijk\Scans\2024"));
        var e = DeletedFilesList.ParseLine(line)!;
        Assert.True(e.IsFolder && e.InRecycleBin, "map in de Prullenbak");
        Assert.Equal(@"2024\", e.FileName, "naam met \\ erachter");
        Assert.Equal(@"C:\Praktijk\Scans", e.Directory, "bovenliggende map");
        Assert.Equal("Lege map naar de Prullenbak", e.KindText, "omschrijving");
    }

    [Test]
    public void SimulatieMetLegeMappenWordtGelezen()
    {
        const string json = "{\"candidates\":1,\"emptyFolders\":2,\"emptyFoldersDeleted\":0,\"messages\":[],\"folders\":[{\"id\":\"map-1\",\"status\":\"OK\"," +
            "\"message\":\"\",\"emptyFolders\":2,\"skippedByReason\":{}}],\"fileDetails\":[{\"id\":\"map-1\",\"candidates\":[{\"Name\":\"a\\\\oud.pdf\",\"Size\":10}]," +
            "\"skipped\":[],\"emptyFolders\":[{\"Name\":\"a\",\"Size\":0},{\"Name\":\"a\\\\b\",\"Size\":0}]}]}";
        var r = RunReport.Parse(json);
        Assert.Equal(2, r.EmptyFolders, "totaal");
        Assert.Equal(2, r.Folders[0].EmptyFolders, "per map");
        Assert.Equal(2, r.FileDetails![0].EmptyFolders.Count, "namen");
        Assert.Equal(@"a\b", r.FileDetails[0].EmptyFolders[1].Name, "geneste naam");
    }
}
