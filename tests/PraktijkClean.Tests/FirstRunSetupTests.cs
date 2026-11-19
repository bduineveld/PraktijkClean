using System;
using System.Linq;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

public sealed class FirstRunSetupTests
{
    private static AppConfig Fresh() => ConfigStore.CreateDefault(@"C:\Users\test\Desktop", @"C:\Users\test\Downloads");

    [Test]
    public void WizardZetMappenTermijnEnNiveauMaarBlijftInSimulatie()
    {
        var config = Fresh();
        var c = FirstRunChoices.From(config);
        Assert.True(c.Desktop && c.Downloads, "standaard beide mappen aan");
        c.Downloads = false;
        c.OtherFolders.Add(@"C:\Praktijk\Scans");
        c.OtherFolders.Add(@"c:\praktijk\scans\");          // dubbel, wordt overgeslagen
        c.RetentionDays = 7;
        c.SubfolderLevel = SubfolderLevels.Medium;
        c.RecycleBin = false;
        c.EmptyRecycleBin = true;
        c.Daily = false;
        c.AtLogon = true;
        config.SimulationMode = false;                      // ook als het ergens anders is uitgezet
        config.AutomaticCleanupConfirmed = true;

        FirstRunSetup.Apply(config, c);

        Assert.Equal(3, config.Folders.Count, "bureaublad, downloads en één eigen map");
        var desktop = config.Folders.Single(f => f.KnownFolder == KnownFolderNames.Desktop);
        var downloads = config.Folders.Single(f => f.KnownFolder == KnownFolderNames.Downloads);
        var scans = config.Folders.Single(f => f.Type == FolderTypes.Custom);
        Assert.True(desktop.Enabled && !downloads.Enabled && scans.Enabled, "aan/uit zoals gekozen");
        Assert.True(config.Folders.All(f => f.RetentionDays == 7), "termijn op alle mappen");
        Assert.True(config.Folders.All(f => SubfolderLevels.Of(f) == SubfolderLevels.Medium), "niveau op alle mappen");
        Assert.Equal(7, config.DefaultRetentionDays, "standaardtermijn voor later");
        Assert.Equal(SubfolderLevels.Medium, config.DefaultSubfolderLevel, "standaardniveau voor later");
        Assert.Equal(DeleteModes.Permanent, config.DeleteMode, "definitief");
        Assert.True(!config.EmptyRecycleBin, "Prullenbak legen alleen bij Prullenbak");
        Assert.True(!config.Schedule.Daily && config.Schedule.AtLogon, "planning");
        Assert.True(config.SimulationMode && !config.AutomaticCleanupConfirmed, "simulatie blijft aan");
        Assert.Equal(0, ConfigValidator.Validate(config).Count, "geldige instellingen");
    }

    [Test]
    public void WizardWeigertOngeldigeKeuzes()
    {
        var config = Fresh();
        var c = FirstRunChoices.From(config);
        c.RetentionDays = -1;
        bool threw = false;
        try { FirstRunSetup.Apply(config, c); } catch (ArgumentException) { threw = true; }
        Assert.True(threw, "negatieve termijn geweigerd");

        c = FirstRunChoices.From(config);
        c.SubfolderLevel = "extra-zwaar";
        threw = false;
        try { FirstRunSetup.Apply(config, c); } catch (ArgumentException) { threw = true; }
        Assert.True(threw, "onbekend niveau geweigerd");
    }

    [Test]
    public void NieuweMapVolgtDeStandaardUitDeInstellingen()
    {
        var config = Fresh();
        Assert.Equal(30, config.DefaultRetentionDays, "standaardtermijn nieuwe map is 30 dagen");
        config.DefaultRetentionDays = 14;
        config.DefaultSubfolderLevel = SubfolderLevels.Heavy;
        var f = ConfigStore.CreateCustomFolder(config, "map-1", @"C:\Praktijk\Export");
        Assert.Equal(14, f.RetentionDays, "termijn uit de standaard");
        Assert.Equal(SubfolderLevels.Heavy, SubfolderLevels.Of(f), "niveau uit de standaard");

        config.DefaultRetentionDays = 99999;
        Assert.Equal(1, ConfigValidator.Validate(config).Count, "te lange standaardtermijn ongeldig");
        var g = ConfigStore.CreateCustomFolder(config, "map-2", @"C:\Praktijk\Oud");
        Assert.Equal(30, g.RetentionDays, "ongeldige standaard = 30 dagen");
    }

    [Test]
    public void OudeConfigZonderStandaardtermijnKrijgtDertigDagen()
    {
        const string oldJson = "{\"schemaVersion\":1,\"simulationMode\":true,\"automaticCleanupConfirmed\":false,\"paused\":false," +
            "\"maxFilesPerRun\":200,\"minimumAgeMinutes\":15,\"deleteMode\":\"recycleBin\"," +
            "\"schedule\":{\"daily\":true,\"dailyTime\":\"12:30\",\"atLogon\":true},\"folders\":[]}";
        var config = ConfigStore.Deserialize(oldJson);
        Assert.Equal(30, config.DefaultRetentionDays, "ontbreekt = 30");
        config.DefaultRetentionDays = 7;
        Assert.Equal(7, ConfigStore.Deserialize(ConfigStore.Serialize(config)).DefaultRetentionDays, "wordt opgeslagen");
    }
}
