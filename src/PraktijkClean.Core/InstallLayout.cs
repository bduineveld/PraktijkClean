using System;
using System.IO;

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
    };
}
