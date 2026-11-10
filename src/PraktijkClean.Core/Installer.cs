using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PraktijkClean.Core;

/// <summary>Huidige installatietoestand, voor de statusweergave.</summary>
public sealed record InstallState(
    bool ScriptInstalled,
    bool ScriptIsCurrent,
    bool ConfigInstalled,
    string? ConfigError,
    ScheduledTaskInfo? Task,
    string? TaskError);

public sealed record UninstallResult(bool TaskRemoved, string? TaskError, IReadOnlyList<string> RemainingItems);

/// <summary>
/// Installeert, herstelt en verwijdert PraktijkClean: script, configuratie en geplande taak.
///
/// Deze klasse verwijdert NOOIT gebruikersbestanden. Bij deïnstallatie worden alleen de
/// bestanden uit <see cref="InstallLayout.OwnedFiles"/> verwijderd en lege mappen opgeruimd.
/// Tijdens installatie of configuratie wordt nooit iets opgeschoond.
/// </summary>
public sealed class Installer
{
    private readonly InstallLayout _layout;
    private readonly IScheduledTaskService _tasks;
    private readonly TaskEnvironment _env;
    private readonly string _scriptContent;

    public Installer(InstallLayout layout, IScheduledTaskService tasks, TaskEnvironment env, string scriptContent)
    {
        _layout = layout;
        _tasks = tasks;
        _env = env;
        _scriptContent = scriptContent;
    }

    public InstallLayout Layout => _layout;
    public string TaskName => TaskXmlBuilder.TaskNameFor(_env.UserName);

    /// <summary>
    /// Slaat instellingen op en zorgt dat script en geplande taak aanwezig en actueel zijn.
    /// Gooit <see cref="InvalidOperationException"/> bij ongeldige instellingen (er wordt dan niets gewijzigd)
    /// en <see cref="ScheduledTaskException"/> als alleen de taak niet kon worden ingesteld.
    /// </summary>
    public void SaveAndApply(AppConfig config)
    {
        var problems = ConfigValidator.Validate(config);
        if (problems.Count > 0)
            throw new InvalidOperationException("De instellingen zijn niet opgeslagen:\n- " + string.Join("\n- ", problems));

        Directory.CreateDirectory(_layout.BaseDirectory);
        Directory.CreateDirectory(_layout.LogDirectory);
        WriteScriptIfChanged(force: false);
        ConfigStore.Save(_layout.ConfigPath, config);
        ApplyTask(config);
    }

    /// <summary>Herstel: script opnieuw wegschrijven, configuratie opnieuw opslaan en taak opnieuw aanmaken.</summary>
    public void Repair(AppConfig config)
    {
        var problems = ConfigValidator.Validate(config);
        if (problems.Count > 0)
            throw new InvalidOperationException("Herstel niet mogelijk; de instellingen zijn ongeldig:\n- " + string.Join("\n- ", problems));
        Directory.CreateDirectory(_layout.BaseDirectory);
        Directory.CreateDirectory(_layout.LogDirectory);
        WriteScriptIfChanged(force: true);
        ConfigStore.Save(_layout.ConfigPath, config);
        _tasks.Delete(TaskName);
        ApplyTask(config);
    }

    /// <summary>Pauzeren of hervatten: zet de vlag in config.json én de geplande taak aan/uit.</summary>
    public void SetPaused(AppConfig config, bool paused)
    {
        config.Paused = paused;
        SaveAndApply(config);
    }

    /// <summary>
    /// Activeert daadwerkelijke automatische verwijdering. Mag alleen worden aangeroepen
    /// nadat de gebruiker dit expliciet heeft bevestigd.
    /// </summary>
    public void ActivateAutomaticCleanup(AppConfig config)
    {
        config.SimulationMode = false;
        config.AutomaticCleanupConfirmed = true;
        SaveAndApply(config);
    }

    /// <summary>Terug naar simulatiemodus; opnieuw activeren vraagt weer om bevestiging.</summary>
    public void ReturnToSimulation(AppConfig config)
    {
        config.SimulationMode = true;
        config.AutomaticCleanupConfirmed = false;
        SaveAndApply(config);
    }

    public InstallState GetState()
    {
        bool scriptInstalled = File.Exists(_layout.ScriptPath);
        bool scriptCurrent = false;
        if (scriptInstalled)
        {
            try { scriptCurrent = File.ReadAllText(_layout.ScriptPath, Encoding.UTF8) == _scriptContent; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        bool configInstalled = File.Exists(_layout.ConfigPath);
        string? configError = null;
        if (configInstalled) ConfigStore.TryLoad(_layout.ConfigPath, out configError);

        ScheduledTaskInfo? task = null;
        string? taskError = null;
        try { task = _tasks.GetInfo(TaskName); }
        catch (ScheduledTaskException ex) { taskError = ex.Message; }

        return new InstallState(scriptInstalled, scriptCurrent, configInstalled, configError, task, taskError);
    }

    /// <summary>
    /// Verwijdert de geplande taak en uitsluitend de eigen bestanden van PraktijkClean.
    /// Gebruikersbestanden en onbekende bestanden worden niet aangeraakt.
    /// </summary>
    public UninstallResult Uninstall()
    {
        bool taskRemoved = true;
        string? taskError = null;
        try { _tasks.Delete(TaskName); }
        catch (ScheduledTaskException ex) { taskRemoved = false; taskError = ex.Message; }

        foreach (var file in _layout.OwnedFiles)
        {
            try
            {
                var info = new FileInfo(file);
                // Een koppeling met dezelfde naam wordt niet gevolgd of verwijderd.
                if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0) info.Delete();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        TryRemoveEmptyDirectory(_layout.LogDirectory);
        TryRemoveEmptyDirectory(_layout.BaseDirectory);

        var remaining = new List<string>();
        if (Directory.Exists(_layout.BaseDirectory))
            remaining.AddRange(Directory.EnumerateFileSystemEntries(_layout.BaseDirectory, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(_layout.BaseDirectory.Length).TrimStart('\\', '/')));
        return new UninstallResult(taskRemoved, taskError, remaining);
    }

    private void WriteScriptIfChanged(bool force)
    {
        if (!force && File.Exists(_layout.ScriptPath))
        {
            try
            {
                if (File.ReadAllText(_layout.ScriptPath, Encoding.UTF8) == _scriptContent) return;
            }
            catch (IOException) { }
        }
        // UTF-8 met BOM: Windows PowerShell 5.1 leest het script dan altijd correct.
        ConfigStore.WriteAtomic(_layout.ScriptPath, _scriptContent, new UTF8Encoding(true));
    }

    private void ApplyTask(AppConfig config)
    {
        if (!TaskXmlBuilder.HasAnyTrigger(config.Schedule))
        {
            // Geen automatisch moment gekozen: alleen handmatig gebruik.
            _tasks.Delete(TaskName);
            return;
        }
        string xml = TaskXmlBuilder.Build(config.Schedule, enabled: !config.Paused, _layout, _env);
        _tasks.Register(TaskName, xml);
    }

    private static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) == 0 && !dir.EnumerateFileSystemInfos().Any())
                dir.Delete(recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
