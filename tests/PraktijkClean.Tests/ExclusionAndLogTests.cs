using System;
using System.Linq;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

public sealed class ExclusionTests
{
    private static FolderSettings Folder() => ConfigStore.CreateKnownFolder("desktop", "Bureaublad", KnownFolderNames.Desktop, @"C:\Users\test\Desktop");

    [Test]
    public void InvoerWordtHerkendAlsExtensieBestandOfPatroon()
    {
        Assert.Equal(new ExclusionItem(ExclusionKind.Extension, ".docx"), Exclusions.Classify(" .docx "), "extensie");
        Assert.Equal(new ExclusionItem(ExclusionKind.Pattern, "protocol-*"), Exclusions.Classify("protocol-*"), "patroon");
        Assert.Equal(new ExclusionItem(ExclusionKind.Pattern, "*spoed?.pdf"), Exclusions.Classify("*spoed?.pdf"), "patroon met ?");
        Assert.Equal(new ExclusionItem(ExclusionKind.File, "Bewaren.pdf"), Exclusions.Classify("\"Bewaren.pdf\""), "bestand (aanhalingstekens weg)");
        Assert.Equal(new ExclusionItem(ExclusionKind.File, ".gitignore.bak"), Exclusions.Classify(".gitignore.bak"), "naam met punt vooraan en extensie is een bestand");
        Assert.Equal(new ExclusionItem(ExclusionKind.File, @"Scans\Oud.pdf"), Exclusions.Classify("Scans/Oud.pdf"), "relatief pad");
        Assert.True(Exclusions.Classify("   ") is null, "lege invoer");
    }

    [Test]
    public void ToevoegenControleertEnVoorkomtDubbele()
    {
        var f = Folder();
        int before = Exclusions.All(f).Count;
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Extension, "pdf")) is null, "extensie zonder punt");
        Assert.True(f.Exclusions.Extensions.Contains(".pdf"), "punt toegevoegd");
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Extension, ".PDF")) is null, "dubbel geeft geen fout");
        Assert.Equal(before + 1, Exclusions.All(f).Count, "dubbel niet opnieuw toegevoegd");

        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Extension, ".do cx")) is not null, "ongeldige extensie");
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Pattern, @"map\*.pdf")) is not null, "patroon met map");
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.File, @"..\buiten.pdf")) is not null, "pad buiten de map");
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Subfolder, @"C:\Windows")) is not null, "volledig pad als submap");
        Assert.True(Exclusions.TryAdd(f, new ExclusionItem(ExclusionKind.Subfolder, @"Scans\Oud\")) is null, "submap");
        Assert.True(f.Exclusions.Subfolders.Contains(@"Scans\Oud"), "afsluitende backslash weg");

        var config = ConfigStore.CreateDefault(@"C:\Users\test\Desktop", @"C:\Users\test\Downloads");
        config.Folders[0] = f;
        Assert.Equal(0, ConfigValidator.Validate(config).Count, "na toevoegen nog steeds geldig");

        Assert.True(Exclusions.Remove(f, new ExclusionItem(ExclusionKind.Extension, ".pdf")), "verwijderen");
        Assert.False(f.Exclusions.Extensions.Contains(".pdf"), "weg na verwijderen");
        Assert.False(Exclusions.Remove(f, new ExclusionItem(ExclusionKind.Extension, ".pdf")), "tweede keer niets te verwijderen");
    }

    [Test]
    public void PadenBinnenDeMapWordenRelatief()
    {
        const string root = @"C:\Users\test\Desktop";
        Assert.Equal("Brief.pdf", Exclusions.RelativeTo(root, @"C:\Users\test\Desktop\Brief.pdf"), "direct in de map");
        Assert.Equal(@"Scans\Brief.pdf", Exclusions.RelativeTo(root + @"\", @"c:\users\TEST\desktop\Scans\Brief.pdf"), "submap, hoofdletterongevoelig");
        Assert.True(Exclusions.RelativeTo(root, @"C:\Users\test\Desktop2\Brief.pdf") is null, "map met langere naam is niet binnen");
        Assert.True(Exclusions.RelativeTo(root, @"C:\Users\test\Downloads\Brief.pdf") is null, "andere map");
        Assert.True(Exclusions.RelativeTo(root, root) is null, "de map zelf");

        Assert.Equal(@"Scans\Oud", Exclusions.ParentOf(@"Scans\Oud\a.pdf"), "bovenliggende submap");
        Assert.True(Exclusions.ParentOf("a.pdf") is null, "geen submap");
        Assert.Equal(".pdf", Exclusions.ExtensionOf("Brief.PDF"), "extensie in kleine letters");
        Assert.True(Exclusions.ExtensionOf("README") is null, "geen extensie");
    }
}

public sealed class LogSummaryTests
{
    private static readonly string[] Lines =
    {
        "2026-11-01 12:30:00 [Taakplanner] opgeschoond: 9 in aanmerking (9 MB), 9 verwijderd (9 MB), 0 overgeslagen, 0 te recent, 0 fouten.",
        "2026-11-10 12:30:01 [Taakplanner] opgeschoond: 3 in aanmerking (1.5 MB), 3 verwijderd (1.5 MB), 1 overgeslagen, 2 te recent, 0 fouten. | Prullenbak: 4 in aanmerking (2 MB), 4 geleegd (2 MB), 0 te recent, 0 fouten.",
        "2026-11-11 08:01:12 [Aanmelden] simulatie: 5 in aanmerking (0.5 MB), 0 verwijderd (0 MB), 0 overgeslagen, 0 te recent, 0 fouten.",
        "2026-11-12 12:30:00 [Taakplanner] opgeschoond: 2 in aanmerking (0.2 MB), 2 verwijderd, 0 overgeslagen, 0 te recent, 1 fouten. | map 'desktop': iets",
        "dit is geen geldige regel",
    };

    [Test]
    public void TeltAlleenRegelsVanafDeGekozenDatum()
    {
        var s = LogSummary.Parse(Lines, new DateTime(2026, 11, 6));
        Assert.Equal(3, s.Runs, "controles");
        Assert.Equal(2, s.CleanRuns, "opschoningen");
        Assert.Equal(5, s.DeletedFiles, "verwijderde bestanden (ook oud formaat zonder MB)");
        Assert.True(Math.Abs(s.DeletedMegabytes - 1.5) < 0.001, "megabytes");
        Assert.Equal(4, s.BinItemsDeleted, "Prullenbak geleegd");
        Assert.Equal(1, s.Errors, "fouten");
        Assert.Equal(new DateTime(2026, 11, 12, 12, 30, 0), s.LastClean, "laatste opschoning");
    }

    [Test]
    public void OntbrekendLogboekGeeftLegeSamenvatting()
    {
        using var dir = new TempDir();
        var s = LogSummary.Load(System.IO.Path.Combine(dir.Path, "bestaat-niet.log"), DateTime.MinValue);
        Assert.Equal(0, s.Runs, "geen controles");
    }
}

public sealed class DeletedFilesListTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    private static string Line(double daysAgo, string path, string kind = DeletedFilesList.KindRecycleBin) =>
        DeletedFilesList.FormatLine(new DeletedFilesList.Entry(Now.AddDays(-daysAgo), kind, 1234, "desktop", path));

    [Test]
    public void RegelHeenEnTerug()
    {
        var e = DeletedFilesList.ParseLine(Line(1, @"C:\Users\test\Desktop\Scans\Uitslag Jansen.pdf"))!;
        Assert.Equal(Now.AddDays(-1), e.DeletedUtc, "tijdstip");
        Assert.Equal(DateTimeKind.Utc, e.DeletedUtc.Kind, "UTC");
        Assert.Equal("Uitslag Jansen.pdf", e.FileName, "naam");
        Assert.Equal(@"C:\Users\test\Desktop\Scans", e.Directory, "map");
        Assert.Equal(1234L, e.Size, "grootte");
        Assert.Equal("Naar de Prullenbak", e.KindText, "soort");
        Assert.True(DeletedFilesList.ParseLine("kapot") is null, "kapotte regel");
        Assert.True(DeletedFilesList.ParseLine("2026-13-45T00:00:00Z\tx\t1\td\tC:\\a") is null, "ongeldige datum");
    }

    [Test]
    public void LadenToontAlleenBinnenTermijnNieuwsteEerst()
    {
        using var dir = new TempDir();
        string path = System.IO.Path.Combine(dir.Path, "verwijderde-bestanden.log");
        System.IO.File.WriteAllLines(path, new[] { Line(40, @"C:\a\oud.pdf"), Line(5, @"C:\a\midden.pdf"), "rommel", Line(1, @"C:\a\nieuw.pdf") });
        var week = DeletedFilesList.Load(path, 7, Now);
        Assert.Equal(2, week.Count, "binnen een week");
        Assert.Equal("nieuw.pdf", week[0].FileName, "nieuwste eerst");
        Assert.Equal(3, DeletedFilesList.Load(path, 90, Now).Count, "binnen 3 maanden");
        Assert.Equal(0, DeletedFilesList.Load(path, 0, Now).Count, "niet bijhouden = leeg");
        Assert.Equal(0, DeletedFilesList.Load(System.IO.Path.Combine(dir.Path, "bestaat-niet.log"), 7, Now).Count, "geen bestand");
    }

    [Test]
    public void InkortenEnWissen()
    {
        using var dir = new TempDir();
        string path = System.IO.Path.Combine(dir.Path, "verwijderde-bestanden.log");
        System.IO.File.WriteAllLines(path, new[] { Line(40, @"C:\a\oud.pdf"), Line(1, @"C:\a\nieuw.pdf") });
        DeletedFilesList.Prune(path, 30, Now);
        var left = System.IO.File.ReadAllLines(path);
        Assert.Equal(1, left.Length, "oude regel weg");
        Assert.True(left[0].EndsWith("nieuw.pdf"), "nieuwe regel blijft");
        DeletedFilesList.Prune(path, 0, Now);
        Assert.False(System.IO.File.Exists(path), "termijn 0 wist de lijst");
        System.IO.File.WriteAllLines(path, new[] { Line(40, @"C:\a\oud.pdf") });
        DeletedFilesList.Prune(path, 7, Now);
        Assert.False(System.IO.File.Exists(path), "alles verlopen: lijst weg");
        System.IO.File.WriteAllLines(path, new[] { Line(1, @"C:\a\x.pdf") });
        DeletedFilesList.Clear(path);
        Assert.False(System.IO.File.Exists(path), "wissen");
    }

    [Test]
    public void KeuzesEnValidatie()
    {
        Assert.Equal(0, DeletedFilesList.OptionIndexFor(0), "niet bijhouden");
        Assert.Equal("1 maand", DeletedFilesList.Options[DeletedFilesList.OptionIndexFor(30)].Label, "1 maand");
        Assert.Equal("2 weken", DeletedFilesList.Options[DeletedFilesList.OptionIndexFor(10)].Label, "afwijkende waarde: naar boven afgerond");
        Assert.Equal("de afgelopen maand", DeletedFilesList.PeriodText(30), "periode in een zin");
        var c = ConfigStore.CreateDefault(@"C:\Users\test\Desktop", @"C:\Users\test\Downloads");
        c.DeletedFilesListDays = 366;
        Assert.True(ConfigValidator.Validate(c).Count > 0, "te lang");
        c.DeletedFilesListDays = -1;
        Assert.True(ConfigValidator.Validate(c).Count > 0, "negatief");
        c.DeletedFilesListDays = 30;
        Assert.Equal(0, ConfigValidator.Validate(c).Count, "geldig");
        Assert.True(new InstallLayout(@"C:\x").OwnedFiles.Any(f => f.EndsWith("verwijderde-bestanden.log")), "wordt bij verwijderen opgeruimd");
    }
}
