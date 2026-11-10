using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace PraktijkClean.Core;

/// <summary>Gegevens over de huidige gebruiker en Windows die nodig zijn voor de geplande taak.</summary>
public sealed record TaskEnvironment(
    string UserName,        // DOMEIN\gebruiker
    string UserSid,         // S-1-5-21-...
    string PowerShellPath,  // C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
    string ConhostPath);    // C:\Windows\System32\conhost.exe

/// <summary>
/// Bouwt de XML-definitie voor Windows Taakplanner.
///
/// Belangrijke keuzes (zie docs/ARCHITECTUUR.md):
///  - Draait als de huidige gebruiker, alleen wanneer die is aangemeld (InteractiveToken):
///    er wordt geen wachtwoord opgeslagen.
///  - RunLevel LeastPrivilege: NOOIT verhoogd. Script en config staan in een map die de
///    gebruiker zelf kan wijzigen; verhoogd draaien zou dan een beveiligingsrisico zijn.
///  - StartWhenAvailable: een gemiste dagelijkse uitvoering (pc stond uit) wordt ingehaald.
///  - conhost.exe --headless: geen zichtbaar venster.
///  - -ExecutionPolicy RemoteSigned geldt alleen voor dit proces. Een beleid dat via
///    groepsbeleid is ingesteld (bijv. AllSigned) gaat altijd voor en wordt gerespecteerd.
/// </summary>
public static class TaskXmlBuilder
{
    public const string DefaultTaskNamePrefix = "PraktijkClean";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>Taaknaam per gebruiker, zodat meerdere gebruikers op één pc elk hun eigen taak hebben.</summary>
    public static string TaskNameFor(string userName)
    {
        string shortName = userName.Contains('\\') ? userName.Substring(userName.LastIndexOf('\\') + 1) : userName;
        var safe = new string(shortName.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' ? c : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "gebruiker";
        return $"{DefaultTaskNamePrefix} - {safe}";
    }

    public static bool HasAnyTrigger(ScheduleSettings schedule) => schedule.Daily || schedule.AtLogon;

    /// <summary>De opdrachtregel waarmee de taak het script start (zonder het conhost-gedeelte).</summary>
    public static string BuildPowerShellArguments(InstallLayout layout, string trigger) =>
        "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden " +
        $"-File {Quote(layout.ScriptPath)} -ConfigPath {Quote(layout.ConfigPath)} -Mode Scheduled -Trigger {trigger}";

    public static string Build(ScheduleSettings schedule, bool enabled, InstallLayout layout, TaskEnvironment env)
    {
        if (!HasAnyTrigger(schedule))
            throw new InvalidOperationException("Er is geen moment van opschonen gekozen.");

        var triggers = new XElement(Ns + "Triggers");
        if (schedule.Daily)
        {
            if (!ConfigValidator.TryParseTime(schedule.DailyTime, out var time))
                throw new InvalidOperationException("Ongeldig tijdstip voor dagelijkse uitvoering.");
            // Tijd zonder tijdzone = lokale tijd van de pc.
            string start = new DateTime(2026, 1, 1).Add(time).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            triggers.Add(new XElement(Ns + "CalendarTrigger",
                new XElement(Ns + "StartBoundary", start),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", "1"))));
        }
        if (schedule.AtLogon)
        {
            triggers.Add(new XElement(Ns + "LogonTrigger",
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "UserId", env.UserName),
                // Even wachten zodat bureaublad en eventuele synchronisatie zijn opgestart.
                new XElement(Ns + "Delay", "PT2M")));
        }

        string arguments = $"--headless {Quote(env.PowerShellPath)} {BuildPowerShellArguments(layout, "Taakplanner")}";

        var doc = new XDocument(
            new XElement(Ns + "Task", new XAttribute("version", "1.4"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Author", "PraktijkClean"),
                    new XElement(Ns + "Description",
                        "Ruimt oude bestanden op uit de in PraktijkClean gekozen mappen. Beheer via PraktijkClean.exe.")),
                triggers,
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", env.UserSid),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", enabled ? "true" : "false"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT30M"),
                    new XElement(Ns + "Priority", "7")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", env.ConhostPath),
                        new XElement(Ns + "Arguments", arguments),
                        new XElement(Ns + "WorkingDirectory", layout.BaseDirectory)))));

        return doc.Declaration + doc.ToString();
    }

    /// <summary>Zet een pad tussen aanhalingstekens. Windows-paden kunnen zelf geen " bevatten.</summary>
    public static string Quote(string path)
    {
        if (path.Contains('"')) throw new ArgumentException("Pad bevat een ongeldig teken.", nameof(path));
        return "\"" + path + "\"";
    }
}
