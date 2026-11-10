using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

public sealed class ConfigTests
{
    private static AppConfig Valid() => ConfigStore.CreateDefault(@"C:\Users\test\Desktop", @"C:\Users\test\Downloads");

    [Test]
    public void StandaardConfiguratieIsGeldigEnVeilig()
    {
        var c = Valid();
        Assert.Equal(0, ConfigValidator.Validate(c).Count, "fouten");
        Assert.True(c.SimulationMode, "simulatiemodus staat standaard aan");
        Assert.False(c.AutomaticCleanupConfirmed, "automatisch verwijderen standaard bevestigd");
        Assert.Equal(2, c.Folders.Count, "aantal mappen");
        Assert.True(c.Folders.All(f => f.RetentionDays == 7), "standaard 7 dagen");
        Assert.True(c.Folders.All(f => !f.Recurse), "submappen standaard uit");
        Assert.True(c.Folders.All(f => !f.AllowNetworkOrSync), "OneDrive/netwerk standaard geblokkeerd");
        Assert.True(c.Folders.All(f => f.Exclusions.Extensions.Contains(".lnk")), ".lnk standaard uitgezonderd");
        Assert.Equal(DeleteModes.Permanent, c.DeleteMode, "standaard definitief verwijderen");
        Assert.False(c.EmptyRecycleBin, "Prullenbak legen staat standaard uit");
        Assert.Equal(7, c.RecycleBinRetentionDays, "standaard termijn Prullenbak");
    }

    [Test]
    public void OngeldigeWaardenWordenGeweigerd()
    {
        void Expect(Action<AppConfig> change, string what)
        {
            var c = Valid();
            change(c);
            Assert.True(ConfigValidator.Validate(c).Count > 0, what);
        }
        Expect(c => c.MaxFilesPerRun = 0, "max 0");
        Expect(c => c.MaxFilesPerRun = 6000, "max te groot");
        Expect(c => c.MinimumAgeMinutes = 0, "minimumleeftijd 0");
        Expect(c => c.DeleteMode = "shred", "onbekende verwijdermodus");
        Expect(c => c.RecycleBinRetentionDays = -1, "negatieve termijn Prullenbak");
        Expect(c => c.RecycleBinRetentionDays = 3651, "te lange termijn Prullenbak");
        Expect(c => c.Schedule.DailyTime = "25:00", "ongeldig tijdstip");
        Expect(c => c.Schedule.DailyTime = "9:30", "tijdstip zonder voorloopnul");
        Expect(c => c.Folders.Clear(), "geen mappen");
        Expect(c => c.Folders[0].Id = "Bureau Blad", "ongeldige id");
        Expect(c => c.Folders[1].Id = c.Folders[0].Id, "dubbele id");
        Expect(c => c.Folders[0].RetentionDays = -1, "negatieve termijn");
        Expect(c => c.Folders[0].KnownFolder = "Documents", "onbekende standaardmap");
        Expect(c => c.Folders[0].Type = "alles", "onbekend type");
        Expect(c => c.Folders[0].ExpectedPath = "", "geen verwacht pad");
        Expect(c => c.Folders[0].Exclusions.Extensions.Add("pdf"), "extensie zonder punt");
        Expect(c => c.Folders[0].Exclusions.Files.Add(@"..\geheim.pdf"), "bestand met ..");
        Expect(c => c.Folders[0].Exclusions.Subfolders.Add(@"C:\Windows"), "absolute submap");
        Expect(c => c.Folders[0].Exclusions.Patterns.Add(@"a\*"), "patroon met pad");
        Expect(c => c.Folders.Add(ConfigStore.CreateCustomFolder("map-1", "")), "eigen map zonder pad");
    }

    [Test]
    public void TijdstipControle()
    {
        Assert.True(ConfigValidator.TryParseTime("00:00", out _), "00:00");
        Assert.True(ConfigValidator.TryParseTime("23:59", out var t) && t == new TimeSpan(23, 59, 0), "23:59");
        Assert.False(ConfigValidator.TryParseTime("24:00", out _), "24:00");
        Assert.False(ConfigValidator.TryParseTime("12.30", out _), "12.30");
        Assert.False(ConfigValidator.TryParseTime(null, out _), "null");
    }

    [Test]
    public void JsonVeldnamenKomenOvereenMetHetScript()
    {
        // JSON als XML lezen (ingebouwd in .NET Framework): elk veld wordt een element met type="number" enz.
        var root = ParseJson(ConfigStore.Serialize(Valid()));
        foreach (var name in new[] { "schemaVersion", "simulationMode", "automaticCleanupConfirmed", "paused", "maxFilesPerRun", "minimumAgeMinutes", "deleteMode", "emptyRecycleBin", "recycleBinRetentionDays", "folders", "schedule" })
            Assert.True(root.Element(name) is not null, "veld " + name);
        var f = root.Element("folders")!.Elements("item").First();
        foreach (var name in new[] { "id", "enabled", "type", "knownFolder", "expectedPath", "retentionDays", "recurse", "allowNetworkOrSync", "exclusions" })
            Assert.True(f.Element(name) is not null, "mapveld " + name);
        var ex = f.Element("exclusions")!;
        foreach (var name in new[] { "files", "subfolders", "patterns", "extensions" })
            Assert.Equal("array", (string?)ex.Element(name)!.Attribute("type"), "lijst " + name);
        Assert.Equal("number", (string?)f.Element("retentionDays")!.Attribute("type"), "retentionDays is een getal");
        Assert.Equal("boolean", (string?)root.Element("simulationMode")!.Attribute("type"), "simulationMode is true/false");
    }

    private static XElement ParseJson(string json)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), XmlDictionaryReaderQuotas.Max);
        return XElement.Load(reader);
    }

    [Test]
    public void OudereConfigZonderNieuweVeldenKrijgtStandaardwaarden()
    {
        // Een config.json van versie 1.0.0: zonder Prullenbak-velden, met ontbrekende mapvelden en een onbekend veld.
        const string json = @"{""schemaVersion"":1,""simulationMode"":false,""automaticCleanupConfirmed"":true,""paused"":false,
            ""maxFilesPerRun"":50,""minimumAgeMinutes"":15,""deleteMode"":""recycleBin"",
            ""schedule"":{""daily"":false,""dailyTime"":""08:15"",""atLogon"":true},
            ""folders"":[{""id"":""desktop"",""displayName"":""Bureaublad"",""enabled"":true,""type"":""knownFolder"",
            ""knownFolder"":""Desktop"",""path"":null,""expectedPath"":""C:\\Users\\test\\Desktop"",""retentionDays"":3,
            ""exclusions"":{""extensions"":["".lnk""]},""onbekendVeld"":123}]}";
        var c = ConfigStore.Deserialize(json);
        Assert.Equal(0, ConfigValidator.Validate(c).Count, "geldig");
        Assert.Equal(DeleteModes.RecycleBin, c.DeleteMode, "eigen keuze blijft behouden");
        Assert.False(c.EmptyRecycleBin, "Prullenbak legen ontbreekt = uit");
        Assert.Equal(7, c.RecycleBinRetentionDays, "standaardtermijn");
        Assert.Equal(50, c.MaxFilesPerRun, "maximum");
        Assert.Equal("08:15", c.Schedule.DailyTime, "tijdstip");
        Assert.Equal(@"C:\Users\test\Desktop", c.Folders[0].ExpectedPath, "pad met backslashes");
        Assert.Equal(3, c.Folders[0].RetentionDays, "bewaartermijn");
        Assert.False(c.Folders[0].Recurse, "ontbrekend recurse = uit");
        Assert.Equal(0, c.Folders[0].Exclusions.Files.Count, "ontbrekende lijst = leeg");
        Assert.Equal(1, c.Folders[0].Exclusions.Extensions.Count, "aanwezige lijst gelezen");
    }

    [Test]
    public void OngeldigeJsonGeeftNetteFout()
    {
        foreach (var json in new[] { "", "{", "[1,2]", @"{""simulationMode"":""ja""}", "niet eens json", "null" })
        {
            bool failed = false;
            try { ConfigStore.Deserialize(json); }
            catch (Exception e) when (e is JsonFormatException or InvalidDataException) { failed = true; }
            Assert.True(failed, "nette fout verwacht voor: " + json);
        }
    }

    [Test]
    public void ResultaatVanHetScriptWordtGelezen()
    {
        const string json = @"{""scriptVersion"":""1.1.0"",""lastRunUtc"":""2026-11-09T23:05:23.5165753Z"",""mode"":""Scheduled"",
            ""exitCode"":0,""candidates"":2,""candidateBytes"":3000,""messages"":[""Simulatiemodus: er wordt niets verwijderd.""],
            ""folders"":[{""id"":""desktop"",""status"":""OK"",""message"":"""",""skippedByReason"":{""Uitzondering: extensie"":4}}],
            ""recycleBin"":{""retentionDays"":7,""status"":""OK"",""message"":"""",""candidates"":1,""candidateBytes"":500},
            ""fileDetails"":[{""id"":""desktop"",""candidates"":[{""Name"":""caf\u00e9.pdf"",""Size"":1000}],""skipped"":[{""Name"":""a.lnk"",""Reason"":""Uitzondering: extensie""}]}],
            ""recycleBinDetails"":[{""Name"":""C:\\x\\brief.pdf"",""Size"":500}]}";
        var r = RunReport.Parse(json);
        Assert.True(r.LastRunUtc is not null, "tijdstip gelezen");
        Assert.Equal(DateTimeKind.Utc, r.LastRunUtc!.Value.Kind, "UTC");
        Assert.Equal(new DateTime(2026, 11, 9, 23, 5, 23, DateTimeKind.Utc), r.LastRunUtc.Value.AddTicks(-(r.LastRunUtc.Value.Ticks % TimeSpan.TicksPerSecond)), "tijdstip");
        Assert.Equal(2, r.Candidates, "kandidaten");
        Assert.Equal(1, r.Messages.Count, "meldingen");
        Assert.Equal(4, r.Folders[0].SkippedByReason["Uitzondering: extensie"], "redenen");
        Assert.Equal(1, r.RecycleBin!.Candidates, "prullenbak");
        Assert.Equal("café.pdf", r.FileDetails![0].Candidates[0].Name, "bestandsnaam met accent");
        Assert.Equal("Uitzondering: extensie", r.FileDetails[0].Skipped[0].Reason, "reden");
        Assert.Equal(@"C:\x\brief.pdf", r.RecycleBinDetails![0].Name, "prullenbakpad");
    }

    [Test]
    public void ArgumentenVoorPowerShellWordenVeiligGeciteerd()
    {
        Assert.Equal("-File", ScriptRunner.QuoteArgument("-File"), "zonder spaties ongewijzigd");
        Assert.Equal("\"C:\\Users\\Jan de Vries\\cleanup.ps1\"", ScriptRunner.QuoteArgument(@"C:\Users\Jan de Vries\cleanup.ps1"), "pad met spaties");
        Assert.Equal("\"C:\\Map met spatie\\\\\"", ScriptRunner.QuoteArgument(@"C:\Map met spatie\"), "afsluitende backslash verdubbeld");
        Assert.Equal("\"a\\\"b\"", ScriptRunner.QuoteArgument("a\"b"), "aanhalingsteken ge-escaped");
        Assert.Equal("\"\"", ScriptRunner.QuoteArgument(""), "leeg argument");
    }

    [Test]
    public void VoorbeeldconfiguratieIsGeldig()
    {
        var c = ConfigStore.Deserialize(File.ReadAllText(Repo.ExampleConfigPath));
        Assert.Equal(0, ConfigValidator.Validate(c).Count, "fouten in config.example.json");
        Assert.True(c.SimulationMode, "voorbeeld staat in simulatiemodus");
    }

    [Test]
    public void OpslaanEnTerugLezen()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");
        var c = Valid();
        c.Folders[0].Exclusions.Patterns.Add("protocol-*");
        c.Folders.Add(ConfigStore.CreateCustomFolder("map-1", @"D:\Scans\Tijdelijk"));
        ConfigStore.Save(path, c);
        var back = ConfigStore.TryLoad(path, out var error);
        Assert.True(back is not null, "teruggelezen: " + error);
        Assert.Equal("protocol-*", back!.Folders[0].Exclusions.Patterns[0], "patroon");
        Assert.Equal(@"D:\Scans\Tijdelijk", back.Folders[2].Path, "eigen pad");
        Assert.False(File.Exists(path + ".tmp"), "geen tijdelijk bestand achtergebleven");
        Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "config zonder BOM");
    }

    [Test]
    public void OngeldigeInstellingenOverschrijvenBestaandeConfigNiet()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");
        ConfigStore.Save(path, Valid());
        string before = File.ReadAllText(path);
        var bad = Valid();
        bad.Folders[0].RetentionDays = -5;
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Save(path, bad), "opslaan ongeldig");
        Assert.Equal(before, File.ReadAllText(path), "bestaande config ongewijzigd");
    }

    [Test]
    public void OntbrekendeOfKapotteConfigGeeftNetteFout()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");
        Assert.True(ConfigStore.TryLoad(path, out var e1) is null && e1 is not null, "ontbrekend");
        File.WriteAllText(path, "{ kapot", Encoding.UTF8);
        Assert.True(ConfigStore.TryLoad(path, out var e2) is null && e2 is not null, "kapot");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"folders\":[]}", Encoding.UTF8);
        Assert.True(ConfigStore.TryLoad(path, out var e3) is null && e3 is not null, "geen mappen");
    }

    [Test]
    public void InvoerUitInterfaceWordtNetjesOmgezet()
    {
        var list = ConfigStore.SplitList(" .lnk; pdf ;; .LNK ;\r\nprotocol-* ");
        Assert.Equal(3, list.Count, "aantal (dubbele weg)");
        var ext = ConfigStore.NormalizeExtensions(new[] { "pdf", ".docx", ".PDF" });
        Assert.Equal(".pdf", ext[0], "punt toegevoegd");
        Assert.Equal(2, ext.Count, "dubbele extensie weg");
        var (files, patterns) = ConfigStore.SplitFilesAndPatterns(new[] { "Bewaren.pdf", "protocol-*", "scan??.tif" });
        Assert.Equal(1, files.Count, "bestanden");
        Assert.Equal(2, patterns.Count, "patronen");
    }

    [Test]
    public void BewaartermijnKeuzes()
    {
        Assert.Equal("Direct opruimen", RetentionOptions.All[RetentionOptions.IndexFor(0)].Label, "0 dagen");
        Assert.Equal("Ouder dan 7 dagen", RetentionOptions.All[RetentionOptions.IndexFor(7)].Label, "7 dagen");
        Assert.Equal(null, RetentionOptions.All[RetentionOptions.IndexFor(45)].Days, "45 dagen = zelf opgeven");
    }

    [Test]
    public void NieuweIdIsUniek()
    {
        var c = Valid();
        c.Folders.Add(ConfigStore.CreateCustomFolder("map-1", @"D:\A"));
        Assert.Equal("map-2", ConfigStore.NextCustomId(c), "volgende id");
    }
}
