using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PraktijkClean.Core;

namespace PraktijkClean.Tests;

public sealed class UpdateTests
{
    private static string ReleaseJson(string tag, bool withSig = true, bool draft = false, bool prerelease = false, long size = 300000) =>
        "{\"tag_name\":\"" + tag + "\",\"html_url\":\"https://github.com/bduineveld/PraktijkClean/releases/tag/" + tag + "\"," +
        "\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (prerelease ? "true" : "false") + "," +
        "\"body\":\"## Nieuw\\r\\n- **Bijwerken** met één klik\\r\\n- `fix`\",\"assets\":[" +
        "{\"name\":\"PraktijkClean-" + tag.TrimStart('v') + ".exe\",\"size\":" + size + ",\"browser_download_url\":\"https://github.com/bduineveld/PraktijkClean/releases/download/" + tag + "/PraktijkClean-" + tag.TrimStart('v') + ".exe\"}," +
        "{\"name\":\"PraktijkClean-" + tag.TrimStart('v') + ".exe.sha256\",\"size\":91,\"browser_download_url\":\"https://github.com/x/y\"}" +
        (withSig ? ",{\"name\":\"PraktijkClean-" + tag.TrimStart('v') + ".exe.sig\",\"size\":512,\"browser_download_url\":\"https://github.com/bduineveld/PraktijkClean/releases/download/" + tag + "/PraktijkClean-" + tag.TrimStart('v') + ".exe.sig\"}" : "") +
        "],\"extra\":{\"onbekend\":[1,2,3]}}";

    [Test]
    public void ReleaseVanGitHubWordtGelezen()
    {
        var r = UpdateCheck.ParseLatestRelease(ReleaseJson("v1.4.1"));
        Assert.True(r is not null, "release gelezen");
        Assert.Equal(new Version(1, 4, 1), r!.Version, "versie");
        Assert.Equal("PraktijkClean-1.4.1.exe", r.ExeName, "naam");
        Assert.Equal(300000L, r.ExeSize, "grootte");
        Assert.True(r.SignatureUrl.EndsWith(".exe.sig"), "handtekening");
        Assert.True(UpdateCheck.IsTrustedDownloadUrl(r.ExeUrl) && UpdateCheck.IsTrustedDownloadUrl(r.SignatureUrl), "github.com via https");
        Assert.True(UpdateCheck.ShortNotes(r.Notes).Contains("• Bijwerken met één klik"), "opmaak verwijderd: " + UpdateCheck.ShortNotes(r.Notes));
    }

    [Test]
    public void OnbruikbareReleasesWordenGenegeerd()
    {
        Assert.True(UpdateCheck.ParseLatestRelease(ReleaseJson("v1.4.1", withSig: false)) is null, "zonder handtekening");
        Assert.True(UpdateCheck.ParseLatestRelease(ReleaseJson("v1.4.1", draft: true)) is null, "concept");
        Assert.True(UpdateCheck.ParseLatestRelease(ReleaseJson("v1.4.1", prerelease: true)) is null, "testversie");
        Assert.True(UpdateCheck.ParseLatestRelease(ReleaseJson("versie-twee")) is null, "onbekende versie");
        Assert.True(UpdateCheck.ParseLatestRelease(ReleaseJson("v1.4.1", size: 900L * 1024 * 1024)) is null, "veel te groot");
        Assert.True(UpdateCheck.ParseLatestRelease("{ kapot") is null, "kapotte JSON");
        Assert.True(UpdateCheck.ParseLatestRelease("[]") is null, "verkeerde vorm");
    }

    [Test]
    public void VersiesVergelijken()
    {
        Assert.Equal(new Version(1, 4, 0), UpdateCheck.ParseVersion("v1.4.0"), "met v");
        Assert.Equal(new Version(1, 4, 0), UpdateCheck.ParseVersion("1.4.0"), "zonder v");
        Assert.True(UpdateCheck.ParseVersion("1.4.0.7") is null, "vier delen");
        Assert.True(UpdateCheck.ParseVersion("") is null, "leeg");
        Assert.True(UpdateCheck.IsNewer(new Version(1, 4, 1), new Version(1, 4, 0)), "nieuwer");
        Assert.True(UpdateCheck.IsNewer(new Version(1, 10, 0), new Version(1, 9, 9)), "numeriek, niet als tekst");
        Assert.False(UpdateCheck.IsNewer(new Version(1, 4, 0), new Version(1, 4, 0)), "gelijk");
        Assert.False(UpdateCheck.IsNewer(new Version(1, 3, 9), new Version(1, 4, 0)), "ouder (nooit terug)");
    }

    [Test]
    public void AlleenGitHubViaHttps()
    {
        Assert.False(UpdateCheck.IsTrustedDownloadUrl("http://github.com/a"), "http");
        Assert.False(UpdateCheck.IsTrustedDownloadUrl("https://github.com.evil.example/a"), "lijkt op github");
        Assert.False(UpdateCheck.IsTrustedDownloadUrl("https://example.com/PraktijkClean.exe"), "ander domein");
        Assert.False(UpdateCheck.IsTrustedDownloadUrl("geen url"), "geen url");
    }

    [Test]
    public void HandtekeningWordtGecontroleerd()
    {
        using var rsa = new RSACryptoServiceProvider(3072);
        var pub = rsa.ExportParameters(includePrivateParameters: false);
        byte[] data = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
        string sig = Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        Assert.True(UpdateSignature.Verify(data, sig, pub), "juiste handtekening");
        Assert.True(UpdateSignature.Verify(data, sig + "\n", pub), "regeleinde maakt niet uit");
        var changed = (byte[])data.Clone(); changed[1234] ^= 1;
        Assert.False(UpdateSignature.Verify(changed, sig, pub), "één bit veranderd");
        Assert.False(UpdateSignature.Verify(data, "geen base64!", pub), "onzin");
        Assert.False(UpdateSignature.Verify(data, Convert.ToBase64String(new byte[10]), pub), "te kort");
        Assert.False(UpdateSignature.Verify(data, sig, UpdateSignature.ReleaseKey), "andere sleutel (de echte releasesleutel)");
    }

    [Test]
    public void EchteReleasesleutelIsGeldig()
    {
        var key = UpdateSignature.ReleaseKey;
        Assert.Equal(384, key.Modulus!.Length, "RSA-3072");
        using var rsa = new RSACryptoServiceProvider();
        rsa.ImportParameters(key);
        Assert.Equal(3072, rsa.KeySize, "sleutellengte");
    }

    [Test]
    public void UpdatemapWordtBijVerwijderenOpgeruimd()
    {
        string root = Path.Combine(Path.GetTempPath(), "pc-upd-" + Guid.NewGuid().ToString("N"));
        var layout = new InstallLayout(root);
        try
        {
            Directory.CreateDirectory(layout.UpdateDirectory);
            foreach (var n in new[] { "PraktijkClean-1.4.1.exe", "PraktijkClean-1.4.1.exe.part", "PraktijkClean-1.4.1.exe.sig", "notities.txt" })
                File.WriteAllText(Path.Combine(layout.UpdateDirectory, n), "x");
            var owned = layout.OwnedUpdateFiles().Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal("PraktijkClean-1.4.1.exe|PraktijkClean-1.4.1.exe.part|PraktijkClean-1.4.1.exe.sig", string.Join("|", owned), "alleen eigen bestanden");
            Assert.Equal(0, new InstallLayout(Path.Combine(root, "bestaat-niet")).OwnedUpdateFiles().Length, "geen map");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }
}
