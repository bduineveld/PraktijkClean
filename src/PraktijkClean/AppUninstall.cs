using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

/// <summary>
/// Volledig verwijderen, vanuit de app (menu) of vanuit Instellingen &gt; Apps ("/uninstall").
/// Raakt nooit gebruikersbestanden aan: alleen de geplande taak, de eigen bestanden van PraktijkClean,
/// de opstartregel van het icoon, de Startmenu-snelkoppeling en de vermelding bij Geïnstalleerde apps.
/// </summary>
internal static class AppUninstall
{
    /// <summary>Voert de verwijdering uit en geeft de tekst voor de gebruiker terug.</summary>
    public static string Run(InstallLayout layout)
    {
        try { TrayIntegration.Disable(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        var installer = new Installer(layout, new WindowsTaskSchedulerService(), WindowsSystem.CreateTaskEnvironment(), MainForm.ReadEmbeddedScript());
        var r = installer.Uninstall();
        AppRegistration.Unregister();
        Updater.RemoveSettings();

        // De draaiende exe kan zichzelf niet verwijderen; dat gebeurt een paar seconden na het sluiten.
        string exeName = Path.GetFileName(layout.AppExePath);
        bool selfPending = r.RemainingItems.Any(i => string.Equals(i, exeName, StringComparison.OrdinalIgnoreCase)) &&
                           AppRegistration.IsRunningInstalledCopy(layout);
        if (selfPending) AppRegistration.ScheduleSelfDelete(layout);

        var sb = new StringBuilder("PraktijkClean is verwijderd voor deze gebruiker. Uw eigen bestanden zijn niet aangeraakt.");
        if (!r.TaskRemoved)
            sb.Append("\n\nDe geplande taak kon niet worden verwijderd: ").Append(r.TaskError)
              .Append("\nVerwijder de taak '").Append(installer.TaskName).Append("' eventueel handmatig in Taakplanner.");
        var foreign = r.RemainingItems
            .Where(i => !(selfPending && string.Equals(i, exeName, StringComparison.OrdinalIgnoreCase)))
            .Where(i => !string.Equals(i, "logs", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (foreign.Count > 0)
            sb.Append("\n\nIn ").Append(layout.BaseDirectory).Append(" staan nog bestanden die niet van PraktijkClean zijn of in gebruik waren; die zijn blijven staan.");
        if (!AppRegistration.IsRunningInstalledCopy(layout))
            sb.Append("\n\nHet bestand dat u nu heeft geopend (").Append(Path.GetFileName(Application.ExecutablePath))
              .Append(") kunt u daarna zelf verwijderen.");
        return sb.ToString();
    }
}
