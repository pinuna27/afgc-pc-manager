using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;
using AFGCPCManager.Setup.Core.Updates;

namespace AFGCPCManager.Setup.Core.Tests.Updates;

public sealed class GitHubSignedReleaseClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-release-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task VerifiesReleaseAndDownloadsSignedAsset()
    {
        byte[] asset = Encoding.UTF8.GetBytes("setup payload"); using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bundle = Bundle(key, asset, "1.2.0");
        using var http = new HttpClient(new ReleaseHandler(bundle.ReleaseJson, bundle.Manifest, bundle.Signature, asset)); var client = new GitHubSignedReleaseClient(http, new(key.ExportSubjectPublicKeyInfoPem()));
        VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager", TestContext.Current.CancellationToken);
        string path = await client.DownloadAssetAsync(release, "AFGCPCManager-Setup-x64.exe", _root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(asset, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(bundle.Manifest, release.ManifestBytes.ToArray());
        Assert.Equal(bundle.Signature, release.SignatureBytes.ToArray());
    }
    [Fact]
    public async Task RejectsManifestWhoseVersionDiffersFromTag()
    {
        byte[] asset = [1, 2, 3]; using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bundle = Bundle(key, asset, "1.3.0", "v1.2.0");
        using var http = new HttpClient(new ReleaseHandler(bundle.ReleaseJson, bundle.Manifest, bundle.Signature, asset)); var client = new GitHubSignedReleaseClient(http, new(key.ExportSubjectPublicKeyInfoPem()));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestAsync("pinuna27", "afgc-pc-manager", TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task AcceptsEquivalentShortManifestVersion()
    {
        byte[] asset = [1, 2, 3]; using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bundle = Bundle(key, asset, "1.2", "v1.2.0");
        using var http = new HttpClient(new ReleaseHandler(bundle.ReleaseJson, bundle.Manifest, bundle.Signature, asset)); var client = new GitHubSignedReleaseClient(http, new(key.ExportSubjectPublicKeyInfoPem()));

        VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager", TestContext.Current.CancellationToken);

        Assert.Equal(new Version(1, 2, 0), release.Version);
    }
    [Fact]
    public async Task RejectsNullGitHubAssetList()
    {
        byte[] asset = [1, 2, 3]; using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bundle = Bundle(key, asset, "1.2.0");
        string invalidRelease = JsonSerializer.Serialize(new { tag_name = "v1.2.0", html_url = "https://github.com/pinuna27/afgc-pc-manager/releases/tag/v1.2.0", draft = false, prerelease = false, assets = (object?)null });
        using var http = new HttpClient(new ReleaseHandler(invalidRelease, bundle.Manifest, bundle.Signature, asset)); var client = new GitHubSignedReleaseClient(http, new(key.ExportSubjectPublicKeyInfoPem()));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetLatestAsync("pinuna27", "afgc-pc-manager", TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task DeletesDownloadWhenHashDoesNotMatch()
    {
        byte[] expected = Encoding.UTF8.GetBytes("expected"), corrupt = Encoding.UTF8.GetBytes("corrupt!"); using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bundle = Bundle(key, expected, "1.2.0");
        using var http = new HttpClient(new ReleaseHandler(bundle.ReleaseJson, bundle.Manifest, bundle.Signature, corrupt)); var client = new GitHubSignedReleaseClient(http, new(key.ExportSubjectPublicKeyInfoPem()));
        VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAssetAsync(release, "AFGCPCManager-Setup-x64.exe", _root, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(_root, "AFGCPCManager-Setup-x64.exe.download")));
    }
    private static BundleData Bundle(ECDsa key, byte[] asset, string manifestVersion, string tag = "v1.2.0")
    {
        string hash = Convert.ToHexString(SHA256.HashData(asset)); byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new ReleaseManifest { Version = manifestVersion, Architecture = "x64", PublishedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), Assets = [new("AFGCPCManager-Setup-x64.exe", hash, asset.Length)] });
        byte[] signature = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string release = JsonSerializer.Serialize(new { tag_name = tag, html_url = $"https://github.com/pinuna27/afgc-pc-manager/releases/tag/{tag}", draft = false, prerelease = false, assets = new[] { Asset("release-manifest.json"), Asset("release-manifest.sig"), Asset("AFGCPCManager-Setup-x64.exe") } }); return new(release, manifest, signature);
        static object Asset(string name) => new { name, browser_download_url = $"https://github.com/pinuna27/afgc-pc-manager/releases/download/v1.2.0/{name}" };
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed record BundleData(string ReleaseJson, byte[] Manifest, byte[] Signature);
    private sealed class ReleaseHandler(string release, byte[] manifest, byte[] signature, byte[] asset) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath; HttpContent content = path.EndsWith("/releases/latest") ? new StringContent(release, Encoding.UTF8, "application/json") : path.EndsWith("release-manifest.json") ? new ByteArrayContent(manifest) : path.EndsWith("release-manifest.sig") ? new ByteArrayContent(signature) : new ByteArrayContent(asset);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
