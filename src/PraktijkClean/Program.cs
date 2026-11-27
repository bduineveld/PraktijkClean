using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

internal static class Program
{
    /// <summary>Versie uit PraktijkClean.csproj (&lt;Version&gt;), bijvoorbeeld "1.3.0".</summary>
    public static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var v = typeof(Program).Assembly.GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Signaal waarmee een tweede start het al geopende venster naar voren haalt.</summary>
    public const string ShowEventName = @"Local\PraktijkClean-Show";

    private const string AppMutexName = @"Local\PraktijkClean-App";

    [STAThread]
    private static int Main(string[] args)
    {
        // Vóór het eerste venster: dezelfde AppUserModelID als de snelkoppelingen, zodat venster, Startmenu en een
        // vastgemaakt taakbalk-icoon bij elkaar horen (en vastmaken via Windows kan).
        Shortcuts.SetProcessAppId();

        // Eerst de Windows-stijl aanzetten, zodat ook de melding hieronder er normaal uitziet.
        // (Schermschaal/DPI staat in app.manifest.)
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowUnexpected(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowUnexpected(e.ExceptionObject as Exception);

        // "/tray": alleen het icoon in het systeemvak (gestart bij aanmelden), zonder venster.
        if (args.Any(a => string.Equals(a, TrayIntegration.Argument, StringComparison.OrdinalIgnoreCase)))
        {
            using var trayMutex = new Mutex(initiallyOwned: true, TrayIntegration.MutexName, out bool firstTray);
            if (!firstTray) return 0;
            Application.Run(new TrayContext());
            return 0;
        }

        var layout = InstallLayout.ForCurrentUser();

        // "/update-install": een gedownloade en goedgekeurde nieuwe versie maakt het bijwerken af (zie Updater).
        if (args.Any(a => string.Equals(a, Updater.InstallArgument, StringComparison.OrdinalIgnoreCase)))
            return FinishUpdate(layout);

        // Slechts één beheervenster tegelijk per gebruiker; een tweede start haalt het open venster naar voren.
        using var mutex = new Mutex(initiallyOwned: true, AppMutexName, out bool isFirst);

        // "/uninstall": vanuit Instellingen > Apps > Geïnstalleerde apps > Verwijderen.
        if (args.Any(a => string.Equals(a, AppRegistration.UninstallArgument, StringComparison.OrdinalIgnoreCase)))
        {
            if (!isFirst)
            {
                MessageBox.Show("PraktijkClean is nog geopend. Sluit eerst het venster van PraktijkClean en probeer het daarna opnieuw.",
                    Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            if (!ConfirmDialog.Ask(null, "PraktijkClean verwijderen?", Texts.UninstallConfirm, "PraktijkClean verwijderen", ConfirmIcon.Warning))
                return 0;
            MessageBox.Show(AppUninstall.Run(layout), Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show)) { using (show) show.Set(); }
            else MessageBox.Show("PraktijkClean is al geopend.", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // Een oudere download geopend terwijl er al een nieuwere versie is geïnstalleerd: open die.
        if (!AppRegistration.IsRunningInstalledCopy(layout) && AppRegistration.InstalledVersion(layout) is System.Version installed &&
            installed > new System.Version(Version))
        {
            mutex.ReleaseMutex();
            try
            {
                Process.Start(new ProcessStartInfo(layout.AppExePath) { UseShellExecute = false, WorkingDirectory = layout.BaseDirectory })?.Dispose();
                return 0;
            }
            catch (System.ComponentModel.Win32Exception) { }
            if (!mutex.WaitOne(0)) return 0;
        }

        // Dankzij app.manifest geeft Environment.OSVersion de echte Windows-versie (Windows 11 = build 22000+).
        var os = Environment.OSVersion.Version;
        bool isWindows11 = os.Major > 10 || (os.Major == 10 && os.Build >= 22000);
        if (!isWindows11 &&
            !ConfirmDialog.Ask(null, "Niet getest op deze Windows-versie", Texts.NotWindows11, "Toch doorgaan", ConfirmIcon.Warning, cancel: "Afsluiten"))
        {
            return 0;
        }

        bool justUpdated = args.Any(a => string.Equals(a, Updater.UpdatedArgument, StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(justUpdated));
        return 0;
    }

    /// <summary>
    /// Draait vanuit %LOCALAPPDATA%\PraktijkClean\update. Wacht tot de oude app dicht is, zet zichzelf op de plek van
    /// de geïnstalleerde app, werkt script en geplande taak bij (instellingen blijven) en opent PraktijkClean weer.
    /// </summary>
    private static int FinishUpdate(InstallLayout layout)
    {
        using var mutex = new Mutex(initiallyOwned: false, AppMutexName);
        bool owned;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(60)); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            MessageBox.Show("PraktijkClean kon niet worden bijgewerkt omdat het venster nog geopend is. Sluit PraktijkClean en probeer het opnieuw.",
                Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        string? problem = null;
        try
        {
            // De oude app is net gesloten; Windows laat het bestand soms nog heel even vast.
            for (int attempt = 1; ; attempt++)
            {
                try { AppRegistration.Install(layout); break; }
                catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
            }
            // Staat er inmiddels een nóg nieuwere versie (Install overschrijft die nooit), dan het script van die versie laten.
            bool installedIsSelf = AppRegistration.InstalledVersion(layout) is not System.Version installed ||
                                   installed.CompareTo(AppRegistration.SelfVersion) <= 0;
            if (installedIsSelf && File.Exists(layout.ConfigPath) && ConfigStore.TryLoad(layout.ConfigPath, out _) is AppConfig config)
            {
                var installer = new Installer(layout, new WindowsTaskSchedulerService(), WindowsSystem.CreateTaskEnvironment(), MainForm.ReadEmbeddedScript());
                installer.SaveAndApply(config);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ScheduledTaskException or InvalidOperationException or System.Security.SecurityException)
        {
            problem = ex.GetType().Name;
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        if (problem is not null)
            MessageBox.Show("Bijwerken is niet helemaal gelukt (" + problem + "). Er is niets verwijderd.\n\n" +
                            "PraktijkClean wordt nu geopend. Gebruik eventueel 'Meer > Problemen oplossen', of download de nieuwe versie zelf van GitHub.",
                            Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        try
        {
            string exe = File.Exists(layout.AppExePath) ? layout.AppExePath : Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exe, problem is null ? Updater.UpdatedArgument : "")
            { UseShellExecute = false, WorkingDirectory = layout.BaseDirectory })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { }
        return problem is null ? 0 : 1;
    }

    private static void ShowUnexpected(Exception? ex)
    {
        // Geen paden of bestandsnamen tonen; alleen het soort fout.
        MessageBox.Show(
            "Er is een onverwachte fout opgetreden (" + (ex?.GetType().Name ?? "onbekend") + ").\n\n" +
            "Er is niets verwijderd door deze fout. Sluit PraktijkClean en probeer het opnieuw. " +
            "Blijft het probleem bestaan, gebruik dan 'Meer > Problemen oplossen'.",
            Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
