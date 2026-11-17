using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Aan- en uitzetten van het icoon in het systeemvak.
///
/// Aan: de geïnstalleerde app (%LOCALAPPDATA%\PraktijkClean\PraktijkClean.exe, zie <see cref="AppRegistration"/>)
/// komt met "/tray" in HKCU\...\Run, zodat het icoon bij elke aanmelding start. Er zijn geen administratorrechten nodig.
/// Uit: Run-waarde weg en icoon afsluiten. De geïnstalleerde app zelf blijft staan.
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

    /// <summary>Zet het icoon aan en start het meteen. Kan even duren (virusscanner): niet op de UI-thread aanroepen.</summary>
    public static void Enable(InstallLayout layout)
    {
        AppRegistration.Install(layout);
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(RunValue, "\"" + layout.AppExePath + "\" " + Argument, RegistryValueKind.String);
        Start(layout);
    }

    /// <summary>Zet het icoon uit.</summary>
    public static void Disable()
    {
        RemoveAutostart();
        Stop();
    }

    /// <summary>Alleen de opstartregel weghalen (vanuit het icoon zelf, dat daarna zelf afsluit).</summary>
    public static void RemoveAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    /// <summary>Staat het icoon aan maar draait het niet (bijv. na bijwerken), start het dan.</summary>
    public static void EnsureRunning(InstallLayout layout)
    {
        if (IsEnabled() && !IsRunning()) Start(layout);
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

    public static void Start(InstallLayout layout)
    {
        if (!File.Exists(layout.AppExePath) || IsRunning()) return;
        Process.Start(new ProcessStartInfo(layout.AppExePath, Argument) { UseShellExecute = false, WorkingDirectory = layout.BaseDirectory })?.Dispose();
    }
}
