using System;
using System.IO;
using System.Linq;

namespace PraktijkClean.Core;

/// <summary>
/// Waar PraktijkClean zijn bestanden neerzet. Standaard per gebruiker in
/// %LOCALAPPDATA%\PraktijkClean (geen administratorrechten nodig).
/// </summary>
public sealed class InstallLayout
{
    public InstallLayout(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
    }

    public static InstallLayout ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PraktijkClean"));

    public string BaseDirectory { get; }
    public string ScriptPath => Path.Combine(BaseDirectory, "cleanup.ps1");
    public string ConfigPath => Path.Combine(BaseDirectory, "config.json");
    public string StatusPath => Path.Combine(BaseDirectory, "status.json");
    public string LogDirectory => Path.Combine(BaseDirectory, "logs");
    public string LogPath => Path.Combine(LogDirectory, "praktijkclean.log");

    /// <summary>Lijst met namen van verwijderde bestanden (alleen als die is ingesteld; wordt automatisch ingekort).</summary>
    public string DeletedListPath => Path.Combine(LogDirectory, "verwijderde-bestanden.log");

    /// <summary>
    /// De geïnstalleerde app (kopie van het gedownloade bestand). Hiernaar wijzen het Startmenu, de lijst met
    /// geïnstalleerde apps van Windows en het icoon in het systeemvak.
    /// </summary>
    public string AppExePath => Path.Combine(BaseDirectory, "PraktijkClean.exe");

    /// <summary>Tijdelijke map voor een gedownloade nieuwe versie (zie UpdateCheck); wordt na bijwerken leeggemaakt.</summary>
    public string UpdateDirectory => Path.Combine(BaseDirectory, "update");

    /// <summary>Bestanden die PraktijkClean zelf in de updatemap kan hebben neergezet (alleen deze worden opgeruimd).</summary>
    public string[] OwnedUpdateFiles()
    {
        try
        {
            var dir = new DirectoryInfo(UpdateDirectory);
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0) return Array.Empty<string>();
            return dir.GetFiles("PraktijkClean-*")
                .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0 &&
                            (f.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                             f.Name.EndsWith(".exe.part", StringComparison.OrdinalIgnoreCase) ||
                             f.Name.EndsWith(".exe.sig", StringComparison.OrdinalIgnoreCase)))
                .Select(f => f.FullName).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// De enige bestanden die PraktijkClean ooit zelf aanmaakt. Bij deïnstallatie worden
    /// uitsluitend deze bestanden verwijderd; nooit een complete map.
    /// </summary>
    public string[] OwnedFiles => new[]
    {
        ScriptPath, ScriptPath + ".tmp",
        ConfigPath, ConfigPath + ".tmp",
        StatusPath, StatusPath + ".tmp",
        LogPath, Path.Combine(LogDirectory, "praktijkclean.1.log"),
        DeletedListPath, DeletedListPath + ".tmp",
        AppExePath, AppExePath + ".tmp",
    };
}
