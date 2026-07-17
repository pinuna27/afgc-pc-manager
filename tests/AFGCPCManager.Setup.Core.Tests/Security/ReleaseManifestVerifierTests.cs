using System.Security.Cryptography;
using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Tests.Security;

public sealed class ReleaseManifestVerifierTests
{
    [Fact]
    public void AcceptsAuthenticValidManifest()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); byte[] json = Manifest(); byte[] signature = Sign(key, json);
        var result = new ReleaseManifestVerifier(key.ExportSubjectPublicKeyInfoPem()).Verify(json, signature);
        Assert.True(result.IsValid); Assert.Equal("1.2.0", result.Manifest!.Version);
    }
    [Fact]
    public void RejectsTamperedManifest()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); byte[] json = Manifest(); byte[] signature = Sign(key, json); json[^2] ^= 1;
        Assert.False(new ReleaseManifestVerifier(key.ExportSubjectPublicKeyInfoPem()).Verify(json, signature).IsValid);
    }
    [Fact]
    public void RejectsSignatureFromDifferentKey()
    {
        using ECDsa trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256); using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256); byte[] json = Manifest();
        Assert.False(new ReleaseManifestVerifier(trusted.ExportSubjectPublicKeyInfoPem()).Verify(json, Sign(attacker, json)).IsValid);
    }
    [Fact]
    public void RejectsTraversalAssetName()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); byte[] json = Manifest("../setup.exe");
        var result = new ReleaseManifestVerifier(key.ExportSubjectPublicKeyInfoPem()).Verify(json, Sign(key, json)); Assert.False(result.IsValid);
    }
    private static byte[] Manifest(string name = "AFGCPCManager-Setup-x64.exe") => JsonSerializer.SerializeToUtf8Bytes(new ReleaseManifest { Version = "1.2.0", Architecture = "x64", PublishedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), Assets = [new(name, new string('A', 64), 1234)], VJoy = new("BrunnerInnovation/vJoy", "v2.2.2.0", "2.2.2.0", "vJoySetup.exe", new string('B', 64), "Brunner", false) });
    private static byte[] Sign(ECDsa key, byte[] data) => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}
