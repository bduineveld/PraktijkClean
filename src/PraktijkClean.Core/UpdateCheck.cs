using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace PraktijkClean.Core;

/// <summary>Een nieuwe versie op GitHub, met de twee bestanden die nodig zijn om bij te werken.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string Notes,
                                 string ExeName, string ExeUrl, long ExeSize, string SignatureUrl);

/// <summary>
/// Controleren op een nieuwe versie (zonder netwerk; dat doet de app). Een update wordt alleen geïnstalleerd als
/// de .exe is ondertekend met de releasesleutel van PraktijkClean (<see cref="UpdateSignature"/>): een download
/// van GitHub alleen is niet genoeg, want wie de GitHub-release kan vervangen, kan ook het .sha256-bestand vervangen.
/// </summary>
public static class UpdateCheck
{
    public const string Repository = "bduineveld/PraktijkClean";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Niet vaker dan dit bij GitHub vragen (app en icoon delen het tijdstip).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);

    /// <summary>Bovengrens voor de download; PraktijkClean.exe is enkele honderden kB.</summary>
    public const long MaxExeSize = 50L * 1024 * 1024;

    public static string ExeNameFor(Version v) => $"PraktijkClean-{v.ToString(3)}.exe";
    public static string SignatureNameFor(Version v) => ExeNameFor(v) + ".sig";

    /// <summary>"v1.4.0" of "1.4.0" → 1.4.0; anders null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag!.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
        if (!Version.TryParse(t, out var v) || v.Major < 0 || v.Minor < 0 || v.Build < 0 || v.Revision > 0) return null;
        return new Version(v.Major, v.Minor, v.Build);
    }

    public static bool IsNewer(Version candidate, Version current) =>
        candidate.CompareTo(new Version(current.Major, current.Minor, Math.Max(current.Build, 0))) > 0;

    /// <summary>
    /// Leest het antwoord van GitHub (releases/latest). Geeft null als er geen bruikbare release is (concept,
    /// testversie, onbekende versie, of de .exe of de handtekening ontbreekt).
    /// </summary>
    public static ReleaseInfo? ParseLatestRelease(string json)
    {
        GhRelease? r;
        try
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            r = new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms) as GhRelease;
        }
        catch (Exception ex) when (ex is SerializationException or ArgumentException or InvalidCastException) { return null; }
        if (r is null || r.Draft || r.Prerelease) return null;
        var version = ParseVersion(r.TagName);
        if (version is null) return null;

        string exeName = ExeNameFor(version), sigName = SignatureNameFor(version);
        var exe = r.Assets?.FirstOrDefault(a => string.Equals(a.Name, exeName, StringComparison.OrdinalIgnoreCase));
        var sig = r.Assets?.FirstOrDefault(a => string.Equals(a.Name, sigName, StringComparison.OrdinalIgnoreCase));
        if (exe?.Url is null || sig?.Url is null || exe.Size <= 0 || exe.Size > MaxExeSize) return null;

        return new ReleaseInfo(version, r.TagName!, r.PageUrl ?? "", (r.Body ?? "").Trim(), exeName, exe.Url, exe.Size, sig.Url);
    }

    /// <summary>Alleen downloads via https van github.com (die stuurt door naar zijn eigen downloadserver).</summary>
    public static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(u.Host, "api.github.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Korte versie van de releasetekst voor in een vraagvenster (zonder Markdown-opmaak).</summary>
    public static string ShortNotes(string notes, int max = 700)
    {
        var lines = notes.Replace("\r", "").Split('\n')
            .Select(l => l.Trim().TrimStart('#').Trim().Replace("**", "").Replace("`", ""))
            .Select(l => l.StartsWith("- ") || l.StartsWith("* ") ? "• " + l.Substring(2) : l)
            .Where(l => l.Length > 0);
        string text = string.Join("\n", lines);
        return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
    }

    [DataContract]
    private sealed class GhRelease
    {
        [DataMember(Name = "tag_name")] public string? TagName { get; set; }
        [DataMember(Name = "html_url")] public string? PageUrl { get; set; }
        [DataMember(Name = "body")] public string? Body { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public GhAsset[]? Assets { get; set; }
    }

    [DataContract]
    private sealed class GhAsset
    {
        [DataMember(Name = "name")] public string? Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string? Url { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }
}

/// <summary>
/// Handtekening van een release: RSA-3072, SHA-256, PKCS#1 v1.5, als base64-tekst in PraktijkClean-x.y.z.exe.sig.
/// De geheime sleutel staat alleen op de computer van de ontwikkelaar (niet op GitHub); zie docs/BIJWERKEN.md.
/// </summary>
public static class UpdateSignature
{
    /// <summary>Openbare sleutel van PraktijkClean-releases (modulus, base64; exponent 65537).</summary>
    public const string ReleaseKeyModulus =
        "rzcc5KlzD6fxvyQstZN9UVPR7V5tpffXrHTzywzpTgNgih1fO1rFo9pKbF10bRV8/V0/NufQKrotwOuzjkmP37b9N4fbHecs8MdyHBhCrfj7BOdf" +
        "+6jZXLyFAU78uqDdvVs4bgZdNEjNSAJ4Enkb4i4hagpk+5QuP4kxofuxMYubyL83UMropcYw/UBNKIFU6D1cNuSn2XFxcRWnrZN33A8wrO3ubgCL" +
        "vjGFJHzvcJRry3/pBYT/yWNFOjDAtt41jIs/WWiI8yb+x42wAtUfk7Uc5xVT4ek9na+hGzceWiB+GVIy/hNxY6krTXRBY6e3dnKy1eZBwgbw55B2" +
        "fo5KIwduDQxuCmSqmpO3MkgBMFX+wC4/jtfaQ3Bs6ufI9fgO5ZLbcYhSL4NVUFBvGHk7js5+aBQYRO1hfpfZI4eCy6qoXD3N2HzStJIUNpQVy+VH" +
        "ZvaB2378F62FzPqm7yMtTigZiLDAI92reh+zT4HKitjQRagS7gUuRuncXGyVu/oV";

    public static RSAParameters ReleaseKey => new()
    {
        Modulus = Convert.FromBase64String(ReleaseKeyModulus),
        Exponent = new byte[] { 1, 0, 1 },
    };

    /// <summary>Klopt de handtekening (base64-tekst) bij deze bytes en deze openbare sleutel?</summary>
    public static bool Verify(byte[] data, string signatureText, RSAParameters publicKey)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText.Trim()); }
        catch (FormatException) { return false; }
        if (publicKey.Modulus is null || signature.Length != publicKey.Modulus.Length) return false;
        using var rsa = new RSACryptoServiceProvider();
        rsa.ImportParameters(publicKey);
        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
