using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean.Tests;

public sealed class PathRulesTests
{
    // Vaste, fictieve Windows-omgeving zodat deze tests op elk besturingssysteem hetzelfde werken.
    private static readonly Dictionary<string, string> EnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windir"] = @"C:\Windows", ["SystemRoot"] = @"C:\Windows", ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)", ["ProgramData"] = @"C:\ProgramData",
        ["APPDATA"] = @"C:\Users\bart\AppData\Roaming", ["LOCALAPPDATA"] = @"C:\Users\bart\AppData\Local",
        ["SystemDrive"] = "C:", ["USERPROFILE"] = @"C:\Users\bart", ["PUBLIC"] = @"C:\Users\Public",
    };
    private static string? Env(string n) => EnvVars.TryGetValue(n, out var v) ? v : null;
    private const string Install = @"C:\Users\bart\AppData\Local\PraktijkClean";

    private static PathCheck Check(string p, params string[] syncRoots) =>
        PathRules.Check(p, Env, Install, syncRoots, _ => false);

    [Test]
    public void SysteemEnProfielmappenGeblokkeerd()
    {
        foreach (var p in new[] { @"C:\", @"D:\", @"C:\Windows", @"C:\Windows\Temp", @"C:\Program Files\X", @"C:\ProgramData\Y",
                                  @"C:\Users", @"C:\Users\bart", @"C:\Users\bart\AppData\Local\Temp", Install, @"C:\Users\Public",
                                  @"C:\Users\bart\..\jan", @"\\?\C:\Users\bart\Desktop", @"C:\PROGRA~1\X" })
            Assert.True(Check(p).Blocked, "geblokkeerd: " + p);
    }

    [Test]
    public void GewoneMappenToegestaan()
    {
        foreach (var p in new[] { @"C:\Users\bart\Desktop", @"C:\Users\bart\Downloads", @"D:\Scans\Tijdelijk" })
            Assert.False(Check(p).Blocked, "toegestaan: " + p);
        Assert.True(Check(@"C:\Users\bart\Documents\Brieven").Warnings.Count > 0, "waarschuwing bij Documenten");
    }

    [Test]
    public void OneDriveEnNetwerkHerkend()
    {
        var a = Check(@"C:\Users\bart\OneDrive - Praktijk\Bureaublad");
        Assert.True(a.IsSync && !a.Blocked && a.Warnings.Count > 0, "OneDrive-naam");
        var b = Check(@"D:\Sync\Bureaublad", @"D:\Sync");
        Assert.True(b.IsSync, "via synchronisatiemap");
        var c = Check(@"\\server\home$\bart\Bureaublad");
        Assert.True(c.IsNetwork, "UNC");
        var d = PathRules.Check(@"H:\Bureaublad", Env, Install, Array.Empty<string>(), _ => true);
        Assert.True(d.IsNetwork, "netwerkstation");
    }
}

public sealed class ScriptIntegrationTests
{
    [Test]
    public void ScriptIsAsciiVoorPowerShell51()
    {
        var bytes = File.ReadAllBytes(Repo.ScriptPath);
        Assert.True(bytes.All(b => b < 128), "cleanup.ps1 bevat alleen ASCII-tekens");
    }

    [Test]
    public void JsonRegelUitUitvoerHalen()
    {
        Assert.Equal("{\"a\":1}", ScriptRunner.ExtractJson("waarschuwing\r\n{\"a\":1}\r\n"), "json");
        Assert.Equal("", ScriptRunner.ExtractJson("geen json"), "geen json");
    }

    private static string? FindPowerShell()
    {
        if (Platform.IsWindows)
            return WindowsSystem.PowerShellPath; // Windows PowerShell 5.1, zoals in productie
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir, "pwsh");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Test]
    public async Task SimulatieViaScriptRunnerEnGeinstalleerdScript()
    {
        string? ps = FindPowerShell();
        if (ps is null) Assert.Skip("PowerShell niet gevonden");
        using var tmp = new TempDir();
        string data = tmp.Sub("Bureaublad");
        string old = Path.Combine(data, "Patiëntbrief.pdf");
        File.WriteAllText(old, "fictief");
        File.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.WriteAllText(Path.Combine(data, "nieuw.pdf"), "fictief");

        var layout = new InstallLayout(tmp.Sub("app"));
        var inst = new Installer(layout, new FakeTaskService(),
            new TaskEnvironment(@"X\test", "S-1-5-21-0", ps!, "conhost.exe"), File.ReadAllText(Repo.ScriptPath, Encoding.UTF8));
        var config = new AppConfig { Folders = { ConfigStore.CreateCustomFolder("map-1", data) } };
        config.Folders[0].RetentionDays = 7;
        config.DeleteMode = DeleteModes.Permanent; // Prullenbak bestaat alleen op Windows
        inst.SaveAndApply(config);

        var runner = new ScriptRunner(ps!, layout, TimeSpan.FromMinutes(2));
        var report = await runner.SimulateAsync();
        Assert.True(File.Exists(old), "simulatie verwijdert niets");
        if (Platform.IsWindows)
        {
            // %TEMP% ligt in %LOCALAPPDATA%: het script hoort deze locatie te weigeren.
            Assert.Equal("Geblokkeerd", report.Folders[0].Status, "status in beschermde map");
            return;
        }
        Assert.Equal("simulatie", report.Action, "actie");
        Assert.Equal(1, report.Candidates, "kandidaten");
        Assert.Equal(1, report.TooYoung, "te recent");
        Assert.Equal("Patiëntbrief.pdf", report.FileDetails![0].Candidates[0].Name, "bestandsnaam met é");

        string log = File.ReadAllText(layout.LogPath);
        Assert.False(log.Contains("Patiëntbrief") || log.Contains(data), "geen namen/paden in logboek");
        var status = RunReport.TryLoadStatus(layout.StatusPath);
        Assert.True(status is not null && status.FileDetails is null, "status zonder bestandsnamen");

        var cleaned = await runner.CleanNowAsync();
        Assert.Equal(1, cleaned.Deleted, "nu opruimen");
        Assert.False(File.Exists(old), "oud bestand weg");
        Assert.True(File.Exists(Path.Combine(data, "nieuw.pdf")), "nieuw bestand blijft");
    }
}

// De Windows-only aanroepen hieronder staan steeds achter een Platform.IsWindows-controle.
#pragma warning disable CA1416
/// <summary>Echte Windows-integratie. Wordt op andere systemen overgeslagen.</summary>
public sealed class WindowsIntegrationTests
{
    [Test]
    public void KnownFoldersBestaan()
    {
        if (!Platform.IsWindows) Assert.Skip("alleen Windows");
        Assert.True(Directory.Exists(KnownFolders.GetPath(KnownFolderNames.Desktop)), "Bureaublad");
        Assert.True(Directory.Exists(KnownFolders.GetPath(KnownFolderNames.Downloads)), "Downloads");
    }

    [Test]
    public void EchteTaakAanmakenUitlezenEnVerwijderen()
    {
        if (!Platform.IsWindows) Assert.Skip("alleen Windows");
        // Testtaak met unieke naam; zonder dagelijkse trigger zodat hij nooit vanzelf start.
        var env = WindowsSystem.CreateTaskEnvironment();
        string name = "PraktijkClean-TEST-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        using var tmp = new TempDir();
        var layout = new InstallLayout(tmp.Path);
        string xml = TaskXmlBuilder.Build(new ScheduleSettings { Daily = false, AtLogon = true }, enabled: false, layout, env);
        var svc = new WindowsTaskSchedulerService();
        try
        {
            svc.Register(name, xml);
            var info = svc.GetInfo(name);
            Assert.True(info is not null, "taak bestaat");
            Assert.False(info!.Enabled, "taak staat uit");
            svc.Register(name, xml); // nogmaals: bijwerken mag geen fout geven
        }
        finally
        {
            svc.Delete(name);
        }
        Assert.True(svc.GetInfo(name) is null, "taak verwijderd");
        svc.Delete(name); // verwijderen van niet-bestaande taak geeft geen fout
    }
}
#pragma warning restore CA1416
