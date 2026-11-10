using System;
using System.Threading;
using System.Windows.Forms;

namespace PraktijkClean;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Eerst de Windows-stijl aanzetten, zodat ook de melding hieronder er normaal uitziet.
        // (Schermschaal/DPI staat in app.manifest.)
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Slechts één beheervenster tegelijk per gebruiker.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\PraktijkClean-App", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PraktijkClean is al geopend.", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowUnexpected(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowUnexpected(e.ExceptionObject as Exception);

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
            "Blijft het probleem bestaan, gebruik dan 'Installatie herstellen'.",
            Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
