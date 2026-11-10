using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

/// <summary>Nep-Taakplanner voor tests: houdt taken in het geheugen bij en kan fouten simuleren.</summary>
public sealed class FakeTaskService : IScheduledTaskService
{
    public Dictionary<string, string> Tasks { get; } = new();
    public bool FailRegister { get; set; }
    public bool FailDelete { get; set; }
    public int RegisterCalls { get; private set; }

    public void Register(string taskName, string xml)
    {
        RegisterCalls++;
        if (FailRegister) throw new ScheduledTaskException("Gesimuleerde fout: taak kon niet worden aangemaakt (0x80070005).");
        XDocument.Parse(xml); // moet geldige XML zijn
        Tasks[taskName] = xml;
    }

    public void Delete(string taskName)
    {
        if (FailDelete) throw new ScheduledTaskException("Gesimuleerde fout bij verwijderen.");
        Tasks.Remove(taskName);
    }

    public ScheduledTaskInfo? GetInfo(string taskName)
    {
        if (!Tasks.TryGetValue(taskName, out var xml)) return null;
        var settings = XDocument.Parse(xml).Descendants().First(e => e.Name.LocalName == "Settings");
        bool enabled = settings.Elements().First(e => e.Name.LocalName == "Enabled").Value == "true";
        return new ScheduledTaskInfo(enabled, null, null, null);
    }
}

public sealed class TaskXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly TaskEnvironment Env = new(@"PRAKTIJK\assistent", "S-1-5-21-1-2-3-1001",
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", @"C:\Windows\System32\conhost.exe");
    private static readonly InstallLayout Layout = new(Path.Combine(Path.GetTempPath(), "PraktijkClean Test"));

    [Test]
    public void TaakDraaitNooitVerhoogdEnZonderOpgeslagenWachtwoord()
    {
        var doc = XDocument.Parse(TaskXmlBuilder.Build(new ScheduleSettings(), true, Layout, Env));
        var principal = doc.Descendants(Ns + "Principal").Single();
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")!.Value, "RunLevel");
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value, "LogonType");
        Assert.Equal(Env.UserSid, principal.Element(Ns + "UserId")!.Value, "gebruiker");
        Assert.False(doc.ToString().Contains("HighestAvailable"), "HighestAvailable");
    }

    [Test]
    public void TriggersVolgenDeInstellingen()
    {
        var both = XDocument.Parse(TaskXmlBuilder.Build(new ScheduleSettings { Daily = true, DailyTime = "07:45", AtLogon = true }, true, Layout, Env));
        Assert.Equal(1, both.Descendants(Ns + "CalendarTrigger").Count(), "dagelijks");
        Assert.Equal(1, both.Descendants(Ns + "LogonTrigger").Count(), "aanmelden");
        Assert.True(both.Descendants(Ns + "StartBoundary").Single().Value.EndsWith("T07:45:00"), "tijdstip lokaal 07:45");
        Assert.Equal(Env.UserName, both.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value, "aanmelden van deze gebruiker");
        Assert.Equal("true", both.Descendants(Ns + "StartWhenAvailable").Single().Value, "gemiste uitvoering inhalen");

        var logonOnly = XDocument.Parse(TaskXmlBuilder.Build(new ScheduleSettings { Daily = false, AtLogon = true }, true, Layout, Env));
        Assert.Equal(0, logonOnly.Descendants(Ns + "CalendarTrigger").Count(), "geen dagelijkse trigger");

        Assert.Throws<InvalidOperationException>(() => TaskXmlBuilder.Build(new ScheduleSettings { Daily = false, AtLogon = false }, true, Layout, Env), "geen triggers");
    }

    [Test]
    public void ActieStartVastScriptMetJuisteArgumenten()
    {
        var doc = XDocument.Parse(TaskXmlBuilder.Build(new ScheduleSettings(), false, Layout, Env));
        var exec = doc.Descendants(Ns + "Exec").Single();
        Assert.Equal(Env.ConhostPath, exec.Element(Ns + "Command")!.Value, "command");
        string args = exec.Element(Ns + "Arguments")!.Value;
        Assert.True(args.StartsWith("--headless \"" + Env.PowerShellPath + "\""), "headless powershell");
        Assert.True(args.Contains("-File \"" + Layout.ScriptPath + "\""), "scriptpad tussen aanhalingstekens (spatie in pad)");
        Assert.True(args.Contains("-ExecutionPolicy RemoteSigned"), "RemoteSigned");
        Assert.False(args.Contains("Bypass", StringComparison.OrdinalIgnoreCase), "geen Bypass");
        Assert.True(args.Contains("-Mode Scheduled"), "modus");
        Assert.False(args.Contains("ConfirmedByUser"), "nooit bevestiging in geplande taak");
        Assert.Equal("false", doc.Descendants(Ns + "Settings").Single().Element(Ns + "Enabled")!.Value, "uitgeschakeld bij pauze");
    }

    [Test]
    public void TaaknaamPerGebruikerEnZonderVerbodenTekens()
    {
        Assert.Equal("PraktijkClean - assistent", TaskXmlBuilder.TaskNameFor(@"PRAKTIJK\assistent"), "naam");
        Assert.Equal("PraktijkClean - a_b_c", TaskXmlBuilder.TaskNameFor(@"DOM\a:b*c"), "verboden tekens");
    }

    [Test]
    public void AanhalingstekenInPadWordtGeweigerd()
    {
        Assert.Throws<ArgumentException>(() => TaskXmlBuilder.Quote("C:\\a\"b"), "aanhalingsteken");
    }
}

public sealed class InstallerTests
{
    private static readonly TaskEnvironment Env = new(@"PRAKTIJK\assistent", "S-1-5-21-1-2-3-1001", "powershell.exe", "conhost.exe");
    private const string Script = "# testscript\r\nWrite-Output 'x'\r\n";

    private static AppConfig Config(string dir) => ConfigStore.CreateDefault(Path.Combine(dir, "Bureaublad"), Path.Combine(dir, "Downloads"));

    [Test]
    public void InstallerenSchrijftScriptConfigEnTaak()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        Assert.True(File.Exists(inst.Layout.ScriptPath), "script");
        Assert.True(File.Exists(inst.Layout.ConfigPath), "config");
        Assert.True(tasks.Tasks.ContainsKey(inst.TaskName), "taak");
        var bytes = File.ReadAllBytes(inst.Layout.ScriptPath);
        Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "script met UTF-8 BOM voor PowerShell 5.1");
        var state = inst.GetState();
        Assert.True(state.ScriptInstalled && state.ScriptIsCurrent && state.ConfigInstalled && state.Task is not null, "status");
    }

    [Test]
    public void InstallerenVerwijdertNooitGebruikersbestanden()
    {
        using var tmp = new TempDir();
        string desk = tmp.Sub("Bureaublad");
        string file = Path.Combine(desk, "brief.pdf");
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddYears(-1));
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), new FakeTaskService(), Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        inst.Repair(Config(tmp.Path));
        inst.Uninstall();
        Assert.True(File.Exists(file), "gebruikersbestand bestaat nog");
    }

    [Test]
    public void ZonderMomentGeenTaakEnPauzeZetTaakUit()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        var c = Config(tmp.Path);
        inst.SaveAndApply(c);
        inst.SetPaused(c, true);
        Assert.True(tasks.Tasks[inst.TaskName].Contains("<Enabled>false</Enabled>"), "taak uitgeschakeld");
        Assert.True(ConfigStore.TryLoad(inst.Layout.ConfigPath, out _)!.Paused, "pauze in config");
        inst.SetPaused(c, false);
        Assert.True(inst.GetState().Task!.Enabled, "taak weer aan");
        c.Schedule.Daily = false; c.Schedule.AtLogon = false;
        inst.SaveAndApply(c);
        Assert.False(tasks.Tasks.ContainsKey(inst.TaskName), "geen taak zonder moment");
    }

    [Test]
    public void ActiverenAlleenViaExplicieteAanroep()
    {
        using var tmp = new TempDir();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), new FakeTaskService(), Env, Script);
        var c = Config(tmp.Path);
        inst.SaveAndApply(c);
        var saved = ConfigStore.TryLoad(inst.Layout.ConfigPath, out _)!;
        Assert.True(saved.SimulationMode && !saved.AutomaticCleanupConfirmed, "na installatie simulatie");
        inst.ActivateAutomaticCleanup(c);
        saved = ConfigStore.TryLoad(inst.Layout.ConfigPath, out _)!;
        Assert.True(!saved.SimulationMode && saved.AutomaticCleanupConfirmed, "na activeren");
        inst.ReturnToSimulation(c);
        saved = ConfigStore.TryLoad(inst.Layout.ConfigPath, out _)!;
        Assert.True(saved.SimulationMode && !saved.AutomaticCleanupConfirmed, "terug naar simulatie trekt bevestiging in");
    }

    [Test]
    public void HerhaaldInstallerenEnDeinstalleren()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        for (int i = 0; i < 3; i++)
        {
            inst.SaveAndApply(Config(tmp.Path));
            inst.SaveAndApply(Config(tmp.Path));
            var r = inst.Uninstall();
            Assert.True(r.TaskRemoved, "taak verwijderd");
            Assert.Equal(0, r.RemainingItems.Count, "niets achtergebleven");
            Assert.False(Directory.Exists(inst.Layout.BaseDirectory), "map leeg en weg");
            Assert.Equal(0, tasks.Tasks.Count, "geen taken");
            var again = inst.Uninstall();
            Assert.True(again.TaskRemoved, "nogmaals deïnstalleren geeft geen fout");
        }
    }

    [Test]
    public void DeinstallerenLaatOnbekendeBestandenStaan()
    {
        using var tmp = new TempDir();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), new FakeTaskService(), Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        string foreign = Path.Combine(inst.Layout.BaseDirectory, "eigen-notities.txt");
        File.WriteAllText(foreign, "niet van PraktijkClean");
        var r = inst.Uninstall();
        Assert.True(File.Exists(foreign), "onbekend bestand blijft");
        Assert.Equal(1, r.RemainingItems.Count, "gemeld als achtergebleven");
        Assert.False(File.Exists(inst.Layout.ScriptPath), "script weg");
        Assert.False(File.Exists(inst.Layout.ConfigPath), "config weg");
    }

    [Test]
    public void TaakKanNietWordenAangemaakt()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService { FailRegister = true };
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        var ex = Assert.Throws<ScheduledTaskException>(() => inst.SaveAndApply(Config(tmp.Path)), "registratie mislukt");
        Assert.True(ex.Message.Length > 0, "foutmelding");
        Assert.True(File.Exists(inst.Layout.ConfigPath), "instellingen wel opgeslagen (in simulatiemodus)");
        Assert.True(inst.GetState().Task is null, "geen taak");
        tasks.FailRegister = false;
        inst.Repair(Config(tmp.Path));
        Assert.True(inst.GetState().Task is not null, "herstel maakt taak alsnog aan");
    }

    [Test]
    public void TaakVerwijderenMisluktBijDeinstallatie()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        tasks.FailDelete = true;
        var r = inst.Uninstall();
        Assert.False(r.TaskRemoved, "taak niet verwijderd");
        Assert.True(r.TaskError is not null, "foutmelding voor gebruiker");
        Assert.False(File.Exists(inst.Layout.ScriptPath), "bestanden toch opgeruimd");
    }

    [Test]
    public void OngeldigeInstellingenWijzigenNiets()
    {
        using var tmp = new TempDir();
        var tasks = new FakeTaskService();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), tasks, Env, Script);
        var c = Config(tmp.Path);
        c.Folders[0].RetentionDays = 99999;
        Assert.Throws<InvalidOperationException>(() => inst.SaveAndApply(c), "ongeldig");
        Assert.False(File.Exists(inst.Layout.ConfigPath), "geen config");
        Assert.Equal(0, tasks.RegisterCalls, "geen taak");
    }

    [Test]
    public void GewijzigdScriptWordtHerkendEnHersteld()
    {
        using var tmp = new TempDir();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), new FakeTaskService(), Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        File.WriteAllText(inst.Layout.ScriptPath, "# aangepast");
        Assert.False(inst.GetState().ScriptIsCurrent, "afwijking herkend");
        inst.Repair(Config(tmp.Path));
        Assert.True(inst.GetState().ScriptIsCurrent, "hersteld");
    }

    [Test]
    public void DeinstallatieVolgtGeenKoppelingen()
    {
        using var tmp = new TempDir();
        var inst = new Installer(new InstallLayout(tmp.Sub("app")), new FakeTaskService(), Env, Script);
        inst.SaveAndApply(Config(tmp.Path));
        string elsewhere = tmp.Sub("elders");
        string precious = Path.Combine(elsewhere, "belangrijk.txt");
        File.WriteAllText(precious, "x");
        Directory.Delete(inst.Layout.LogDirectory, true);
        // Map-koppeling (junction); kan zonder beheerdersrechten worden gemaakt.
        if (!Platform.CreateJunction(inst.Layout.LogDirectory, elsewhere)) Assert.Skip("map-koppeling maken lukte niet");
        inst.Uninstall();
        Assert.True(File.Exists(precious), "bestand achter koppeling blijft");
    }
}
