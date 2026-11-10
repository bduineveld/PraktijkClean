using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PraktijkClean.Core.Windows;

/// <summary>
/// Bepaalt de werkelijke locatie van Bureaublad en Downloads via de Windows Known Folders API
/// (SHGetKnownFolderPath). Werkt ook als de map is verplaatst, omgeleid of door OneDrive wordt beheerd.
/// </summary>
[SupportedOSPlatform("windows")]
public static class KnownFolders
{
    private static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string GetPath(string knownFolderName) => knownFolderName switch
    {
        KnownFolderNames.Desktop => Get(Desktop),
        KnownFolderNames.Downloads => Get(Downloads),
        _ => throw new ArgumentException("Onbekende standaardmap: " + knownFolderName, nameof(knownFolderName)),
    };

    private static string Get(Guid id)
    {
        int hr = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr pszPath);
        try
        {
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            return Marshal.PtrToStringUni(pszPath) ?? throw new InvalidOperationException("Leeg pad.");
        }
        finally
        {
            if (pszPath != IntPtr.Zero) Marshal.FreeCoTaskMem(pszPath);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
