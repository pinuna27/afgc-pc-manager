using System.Security.Cryptography;
using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Tests.Security;

public sealed class LocalReleaseBundleVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-local-release-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AcceptsSignedManifestAndMatchingArchive()
    {
        (LocalReleaseBundleVerifier verifier, string manifest, string signature, string archive) = await CreateBundleAsync();
        ReleaseManifest result = await verifier.VerifyAsync(manifest, signature, archive, new(1, 2, 3), TestContext.Current.CancellationToken);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task RejectsArchiveChangedAfterSigning()
    {
        (LocalReleaseBundleVerifier verifier, string manifest, string signature, string archive) = await CreateBundleAsync();
        await File.AppendAllTextAsync(archive, "tampered", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(manifest, signature, archive, new(1, 2, 3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcceptsEquivalentShortExpectedVersion()
    {
        (LocalReleaseBundleVerifier verifier, string manifest, string signature, string archive) = await CreateBundleAsync();

        ReleaseManifest result = await verifier.VerifyAsync(
            manifest, signature, archive, new(1, 2, 3, 0), TestContext.Current.CancellationToken);

        Assert.Equal("1.2.3", result.Version);
    }

    private async Task<(LocalReleaseBundleVerifier, string, string, string)> CreateBundleAsync()
    {
        Directory.CreateDirectory(_root);
        string archive = Path.Combine(_root, "AFGCPCManager-x64.zip");
        await File.WriteAllTextAsync(archive, "payload", TestContext.Current.CancellationToken);
        byte[] bytes = await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken);
        var release = new ReleaseManifest
        {
            Version = "1.2.3",
            Architecture = "x64",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Assets = [new(Path.GetFileName(archive), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length)]
        };
        string manifest = Path.Combine(_root, "release-manifest.json"), signature = Path.Combine(_root, "release-manifest.sig");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(release);
        await File.WriteAllBytesAsync(manifest, json, TestContext.Current.CancellationToken);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await File.WriteAllBytesAsync(signature, key.SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), TestContext.Current.CancellationToken);
        return (new(new ReleaseManifestVerifier(key.ExportSubjectPublicKeyInfoPem())), manifest, signature, archive);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
