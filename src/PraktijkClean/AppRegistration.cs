using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

/// <summary>
/// Maakt van PraktijkClean een gewone Windows-app voor deze gebruiker, zonder administratorrechten:
/// <list type="bullet">
/// <item>de app staat in %LOCALAPPDATA%\PraktijkClean\PraktijkClean.exe (een kopie van het gedownloade bestand;
/// Downloads wordt door PraktijkClean zelf opgeschoond, deze map nooit);</item>
/// <item>een snelkoppeling in het Startmenu;</item>
/// <item>een vermelding in Instellingen &gt; Apps &gt; Geïnstalleerde apps (HKCU\...\Uninstall), met een knop
/// Verwijderen die "PraktijkClean.exe /uninstall" start.</item>
/// </list>
/// </summary>
internal static class AppRegistration
{
    public const string UninstallArgument = "/uninstall";
    public const string WebsiteUrl = "https://github.com/bduineveld/PraktijkClean";

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PraktijkClean";

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PraktijkClean.lnk");

    public static bool IsRunningInstalledCopy(InstallLayout layout) => SamePath(Application.ExecutablePath, layout.AppExePath);

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: false);
        return key is not null;
    }

    /// <summary>
    /// Installeert of werkt bij: kopieert deze exe naar de installatiemap (als die ontbreekt of ouder is),
    /// en zet de Startmenu-snelkoppeling en de vermelding bij Geïnstalleerde apps neer.
    /// Stopt zo nodig even het icoon in het systeemvak, omdat dat de geïnstalleerde exe in gebruik heeft.
    /// </summary>
    public static void Install(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.BaseDirectory);
        if (NeedsCopy(layout))
        {
            bool trayWasRunning = TrayIntegration.IsRunning();
            TrayIntegration.Stop();
            string tmp = layout.AppExePath + ".tmp";
            File.Copy(Application.ExecutablePath, tmp, overwrite: true);
            FileCompat.MoveOverwrite(tmp, layout.AppExePath);
            if (trayWasRunning || TrayIntegration.IsEnabled()) TrayIntegration.Start(layout);
        }
        WriteUninstallEntry(layout);
        TryCreateShortcut(layout);
    }

    /// <summary>Haalt de Startmenu-snelkoppeling en de vermelding bij Geïnstalleerde apps weg.</summary>
    public static void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    /// <summary>
    /// Een draaiende exe kan zichzelf niet verwijderen. Daarom wacht een verborgen PowerShell-proces tot deze
    /// PraktijkClean is afgesloten en ruimt dan de geïnstalleerde exe op (en de map, als die dan leeg is).
    /// </summary>
    public static void ScheduleSelfDelete(InstallLayout layout)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        int pid = Process.GetCurrentProcess().Id;
        string ps =
            $"Wait-Process -Id {pid} -Timeout 600 -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; " +
            $"Remove-Item -LiteralPath {Q(layout.AppExePath)} -Force -ErrorAction SilentlyContinue; " +
            $"foreach ($d in @({Q(layout.LogDirectory)}, {Q(layout.BaseDirectory)})) {{ " +
            "if ((Test-Path -LiteralPath $d) -and -not (Get-ChildItem -LiteralPath $d -Force)) { Remove-Item -LiteralPath $d -ErrorAction SilentlyContinue } }";
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
        try
        {
            Process.Start(new ProcessStartInfo(WindowsSystem.PowerShellPath, "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Versie van de geïnstalleerde exe, of null als die er niet is.</summary>
    public static Version? InstalledVersion(InstallLayout layout)
    {
        try
        {
            if (!File.Exists(layout.AppExePath)) return null;
            var info = FileVersionInfo.GetVersionInfo(layout.AppExePath);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static Version SelfVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Kopiëren als de geïnstalleerde exe ontbreekt, ouder is, of bij dezelfde versie afwijkt.</summary>
    private static bool NeedsCopy(InstallLayout layout)
    {
        if (IsRunningInstalledCopy(layout)) return false;
        var installed = InstalledVersion(layout);
        if (installed is null) return true;
        int cmp = SelfVersion.CompareTo(installed);
        if (cmp != 0) return cmp > 0;          // nooit een nieuwere installatie overschrijven met een oudere download
        return !SameContent(Application.ExecutablePath, layout.AppExePath);
    }

    private static void WriteUninstallEntry(InstallLayout layout)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        string exe = layout.AppExePath;
        key.SetValue("DisplayName", "PraktijkClean");
        key.SetValue("DisplayVersion", (InstalledVersion(layout) ?? SelfVersion).ToString(3));
        key.SetValue("Publisher", "PraktijkClean");
        key.SetValue("DisplayIcon", "\"" + exe + "\",0");
        key.SetValue("InstallLocation", layout.BaseDirectory);
        key.SetValue("UninstallString", "\"" + exe + "\" " + UninstallArgument);
        key.SetValue("URLInfoAbout", WebsiteUrl);
        key.SetValue("HelpLink", WebsiteUrl);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try { key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord); }
        catch (IOException) { }
        if (key.GetValue("InstallDate") is null) key.SetValue("InstallDate", DateTime.Today.ToString("yyyyMMdd"));
    }

    /// <summary>Startmenu-snelkoppeling via de standaard Windows Script Host-component; mislukt dit, dan is dat niet erg.</summary>
    private static void TryCreateShortcut(InstallLayout layout)
    {
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
            if (link is null) return;
            var lt = link.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { layout.AppExePath });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { layout.BaseDirectory });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Bureaublad en Downloads automatisch opruimen" });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { layout.AppExePath + ",0" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception ex) when (ex is TargetInvocationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException or MissingMethodException) { }
        finally
        {
            if (link is not null && System.Runtime.InteropServices.Marshal.IsComObject(link)) System.Runtime.InteropServices.Marshal.ReleaseComObject(link);
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    internal static bool SamePath(string a, string b) =>
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
