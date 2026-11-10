using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace PraktijkClean.Core.Windows;

/// <summary>Gegevens over de huidige Windows-gebruiker en -omgeving.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsSystem
{
    public static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string ConhostPath => Path.Combine(Environment.SystemDirectory, "conhost.exe");

    public static TaskEnvironment CreateTaskEnvironment()
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("Gebruikers-SID onbekend.");
        return new TaskEnvironment(identity.Name, sid, PowerShellPath, ConhostPath);
    }

    /// <summary>Waar als de app met verhoogde (administrator)rechten draait. Dat is niet nodig en wordt afgeraden.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Bekende synchronisatiemappen (OneDrive en andere Cloud Files-providers).</summary>
    public static List<string> GetSyncRoots()
    {
        var roots = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v)) roots.Add(v);
        }
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            if (accounts is not null)
                foreach (var sub in accounts.GetSubKeyNames())
                {
                    using var k = accounts.OpenSubKey(sub);
                    if (k?.GetValue("UserFolder") is string uf && !string.IsNullOrWhiteSpace(uf)) roots.Add(uf);
                }
            using var mgr = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
            if (mgr is not null)
                foreach (var provider in mgr.GetSubKeyNames())
                {
                    using var userRoots = mgr.OpenSubKey(provider + @"\UserSyncRoots");
                    if (userRoots is null) continue;
                    foreach (var valueName in userRoots.GetValueNames())
                        if (userRoots.GetValue(valueName) is string p && !string.IsNullOrWhiteSpace(p)) roots.Add(p);
                }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return roots;
    }

    /// <summary>Netwerkstation, USB-stick of cd: alles wat geen vaste schijf is.</summary>
    public static bool IsNonFixedDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return true;
            return new DriveInfo(root).DriveType != DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return true;
        }
    }
}
