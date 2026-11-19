using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>Een update kon niet worden gedownload of goedgekeurd; de tekst is voor de gebruiker.</summary>
internal sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}

/// <summary>
/// Bijwerken met één klik:
/// <list type="number">
/// <item>Hooguit eens per 20 uur vraagt de app of het icoon aan GitHub wat de nieuwste release is. Er wordt niets
/// over de pc meegestuurd (alleen "PraktijkClean/versie" als programmanaam).</item>
/// <item>Pas na een klik op "Bijwerken" wordt de nieuwe .exe gedownload naar %LOCALAPPDATA%\PraktijkClean\update.</item>
/// <item>De download wordt alleen gebruikt als de handtekening klopt met de releasesleutel die in deze app zit, en
/// als naam en versie in het bestand kloppen. Anders wordt hij weggegooid.</item>
/// <item>De nieuwe versie start met "/update-install", wacht tot deze app dicht is, zet zichzelf op de plek van de
/// geïnstalleerde app (instellingen, taak en logboek blijven), werkt het opschoonscript bij en opent PraktijkClean.</item>
/// </list>
/// Uitzetten: in de app (Meer), of voor een hele organisatie met de registerwaarde
/// HKLM\SOFTWARE\Policies\PraktijkClean\DisableUpdates = 1 (DWORD).
/// </summary>
internal static class Updater
{
    public const string InstallArgument = "/update-install";
    public const string UpdatedArgument = "/updated";

    private const string Key = @"Software\PraktijkClean";
    private const string PolicyKey = @"SOFTWARE\Policies\PraktijkClean";

    private static readonly Lazy<HttpClient> Http = new(CreateClient);

    public static Version Current => UpdateCheck.ParseVersion(Program.Version) ?? new Version(0, 0, 0);

    /// <summary>Uitgezet door de systeembeheerder (groepsbeleid/register); dan ook niet aan te zetten in de app.</summary>
    public static bool BlockedByPolicy
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: false);
                return key?.GetValue("DisableUpdates") is int v && v != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
    }

    /// <summary>Automatisch controleren op nieuwe versies (standaard aan).</summary>
    public static bool Enabled
    {
        get => !BlockedByPolicy && (Read("CheckForUpdates") is not int v || v != 0);
        set => Write("CheckForUpdates", value ? 1 : 0);
    }

    /// <summary>Een nieuwere versie die bij een eerdere controle is gevonden (of null).</summary>
    public static Version? Available =>
        UpdateCheck.ParseVersion(Read("UpdateLatest") as string) is { } v && UpdateCheck.IsNewer(v, Current) ? v : null;

    /// <summary>Of het icoon voor deze versie al een melding heeft gegeven.</summary>
    public static bool AlreadyNotified(Version v) => Read("UpdateNotified") as string == v.ToString(3);
    public static void MarkNotified(Version v) => Write("UpdateNotified", v.ToString(3));

    public static bool CheckDue
    {
        get
        {
            if (Read("UpdateLastCheck") is not string s ||
                !DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)) return true;
            var age = DateTime.UtcNow - last.ToUniversalTime();
            return age < TimeSpan.Zero || age >= UpdateCheck.CheckInterval;
        }
    }

    /// <summary>Alleen voor testen: ander adres voor "releases/latest" (de handtekening blijft verplicht).</summary>
    private static string? SourceOverride => Read("UpdateSource") is string s && s.Length > 0 ? s : null;

    /// <summary>Vraagt GitHub naar de nieuwste release. Geeft null als die niet bruikbaar is.</summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct = default)
    {
        Write("UpdateLastCheck", DateTime.UtcNow.ToString("o"));
        string json = await GetStringAsync(SourceOverride ?? UpdateCheck.LatestReleaseApi, 1024 * 1024, ct).ConfigureAwait(false);
        var info = UpdateCheck.ParseLatestRelease(json);
        if (info is null) return null;
        if (SourceOverride is null && (!UpdateCheck.IsTrustedDownloadUrl(info.ExeUrl) || !UpdateCheck.IsTrustedDownloadUrl(info.SignatureUrl)))
            return null;
        Write("UpdateLatest", info.Version.ToString(3));
        return info;
    }

    /// <summary>Controle op de achtergrond (als die aan staat en weer aan de beurt is). Netwerkfouten worden genegeerd.</summary>
    public static async Task<Version?> CheckIfDueAsync()
    {
        if (!Enabled) return null;
        if (CheckDue)
        {
            try { await FetchLatestAsync().ConfigureAwait(false); }
            catch (Exception ex) when (IsNetworkError(ex)) { }
        }
        return Available;
    }

    public static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or WebException or TaskCanceledException or IOException or UpdateException;

    /// <summary>
    /// Downloadt en controleert de nieuwe versie. Geeft het pad van de goedgekeurde .exe; gooit
    /// <see cref="UpdateException"/> (met uitleg) als er iets niet klopt. Een afgekeurde download wordt verwijderd.
    /// </summary>
    public static async Task<string> DownloadAsync(InstallLayout layout, ReleaseInfo info, IProgress<int> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(layout.UpdateDirectory);
        foreach (var old in layout.OwnedUpdateFiles()) TryDelete(old);

        string signature = await GetStringAsync(info.SignatureUrl, 4096, ct).ConfigureAwait(false);
        string part = Path.Combine(layout.UpdateDirectory, info.ExeName + ".part");
        try
        {
            using (var response = await Http.Value.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read, lastPercent = -1;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > UpdateCheck.MaxExeSize) throw new UpdateException("De download is veel groter dan verwacht en is daarom afgebroken.");
                    await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    int percent = (int)(total * 100 / Math.Max(1, info.ExeSize));
                    if (percent != lastPercent) { lastPercent = percent; progress.Report(Math.Min(100, percent)); }
                }
            }

            byte[] bytes = File.ReadAllBytes(part);
            if (bytes.Length != info.ExeSize)
                throw new UpdateException("De download is onvolledig. Probeer het later opnieuw.");
            if (!UpdateSignature.Verify(bytes, signature, UpdateSignature.ReleaseKey))
                throw new UpdateException("De handtekening van de download klopt niet. Uit voorzorg wordt deze versie NIET geïnstalleerd.\n\n" +
                                          "Meld dit bij de maker van PraktijkClean (zie 'Over PraktijkClean').");
            var fvi = FileVersionInfo.GetVersionInfo(part);
            var fileVersion = new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart);
            if (fvi.ProductName != "PraktijkClean" || fileVersion != info.Version)
                throw new UpdateException("Het gedownloade bestand is niet de verwachte versie van PraktijkClean en wordt niet geïnstalleerd.");

            string exe = Path.Combine(layout.UpdateDirectory, info.ExeName);
            TryDelete(exe);
            File.Move(part, exe);
            return exe;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    /// <summary>Start de goedgekeurde nieuwe versie, die het bijwerken afmaakt zodra deze app is gesloten.</summary>
    public static void StartInstall(string exe) =>
        Process.Start(new ProcessStartInfo(exe, InstallArgument) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();

    /// <summary>Gooit oude downloads weg (niet als deze app zelf vanuit de updatemap draait).</summary>
    public static void CleanUpDownloads(InstallLayout layout)
    {
        string self = System.Windows.Forms.Application.ExecutablePath;
        foreach (var f in layout.OwnedUpdateFiles())
            if (!AppRegistration.SamePath(f, self)) TryDelete(f);
        try
        {
            var dir = new DirectoryInfo(layout.UpdateDirectory);
            if (dir.Exists && dir.GetFileSystemInfos().Length == 0) dir.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Instellingen van het bijwerken weghalen (bij verwijderen van PraktijkClean).</summary>
    public static void RemoveSettings()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    // ------------------------------------------------------------------------------------

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseProxy = true,
            // Proxyservers in praktijken vragen vaak om de Windows-aanmelding van de gebruiker.
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PraktijkClean/" + Program.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<string> GetStringAsync(string url, long maxBytes, CancellationToken ct)
    {
        using var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long len && len > maxBytes)
            throw new UpdateException("Onverwacht antwoord van GitHub.");
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (text.Length > maxBytes) throw new UpdateException("Onverwacht antwoord van GitHub.");
        return text;
    }

    private static object? Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key, writable: false);
            return key?.GetValue(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static void Write(string name, object value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
