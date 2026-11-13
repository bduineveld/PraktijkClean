using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Aan- en uitzetten van het icoon in het systeemvak.
///
/// Aan: de app kopieert zichzelf naar %LOCALAPPDATA%\PraktijkClean\PraktijkClean.exe (de map die PraktijkClean nooit
/// opschoont; het gedownloade exe-bestand zelf staat vaak in Downloads) en zet die kopie met "/tray" in
/// HKCU\...\Run, zodat het icoon bij elke aanmelding start. Er zijn geen administratorrechten nodig.
/// Uit: Run-waarde weg, icoon afsluiten, kopie verwijderen.
/// </summary>
internal static class TrayIntegration
{
    public const string Argument = "/tray";
    public const string MutexName = @"Local\PraktijkClean-Tray";
    public const string ExitEventName = @"Local\PraktijkClean-TrayExit";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "PraktijkClean";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(RunValue) is string;
    }

    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    /// <summary>Zet het icoon aan en start het meteen.</summary>
    public static void Enable(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.BaseDirectory);
        Stop();
        CopySelf(layout);
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(RunValue, "\"" + layout.TrayExePath + "\" " + Argument, RegistryValueKind.String);
        Start(layout);
    }

    /// <summary>Zet het icoon uit. Fouten bij het opruimen van de kopie worden genegeerd.</summary>
    public static void Disable(InstallLayout layout)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        Stop();
        if (!SamePath(layout.TrayExePath, Application.ExecutablePath))
        {
            try { File.Delete(layout.TrayExePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Bij het openen van de app: staat het icoon aan, zorg dan dat de kopie dezelfde versie is als deze app
    /// en dat het icoon draait.
    /// </summary>
    public static void EnsureCurrent(InstallLayout layout)
    {
        if (!IsEnabled())
        {
            // Uitgezet via 'Icoon verbergen': het icoon kon zijn eigen kopie toen niet wissen; dat gebeurt nu.
            if (!IsRunning() && !SamePath(layout.TrayExePath, Application.ExecutablePath) && File.Exists(layout.TrayExePath))
            {
                try { File.Delete(layout.TrayExePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return;
        }
        if (!SamePath(layout.TrayExePath, Application.ExecutablePath) && !SameContent(layout.TrayExePath, Application.ExecutablePath))
        {
            Stop();
            CopySelf(layout);
        }
        if (!IsRunning()) Start(layout);
    }

    /// <summary>Vraagt een draaiend icoon om af te sluiten en wacht daar kort op.</summary>
    public static void Stop()
    {
        if (!IsRunning()) return;
        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var ev))
        {
            using (ev) ev.Set();
        }
        for (int i = 0; i < 30 && IsRunning(); i++) Thread.Sleep(100);
    }

    private static void Start(InstallLayout layout)
    {
        if (!File.Exists(layout.TrayExePath) || IsRunning()) return;
        Process.Start(new ProcessStartInfo(layout.TrayExePath, Argument) { UseShellExecute = false, WorkingDirectory = layout.BaseDirectory })?.Dispose();
    }

    private static void CopySelf(InstallLayout layout)
    {
        string self = Application.ExecutablePath;
        if (SamePath(self, layout.TrayExePath)) return;
        string tmp = layout.TrayExePath + ".tmp";
        File.Copy(self, tmp, overwrite: true);
        FileCompat.MoveOverwrite(tmp, layout.TrayExePath);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameContent(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
