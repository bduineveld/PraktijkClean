using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PraktijkClean.Core;

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
            if (MessageBox.Show(Texts.UninstallConfirm, Texts.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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
            MessageBox.Show(Texts.NotWindows11, Texts.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return 0;
        }

        Application.Run(new MainForm());
        return 0;
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
