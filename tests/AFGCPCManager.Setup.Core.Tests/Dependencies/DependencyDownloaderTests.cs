using System.Net;
using System.Text;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-dependency-download-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FailedVerificationDeletesPartialDownloadAndPreservesPreviousPackage()
    {
        Directory.CreateDirectory(_root);
        string destination = Path.Combine(_root, "driver.exe");
        await File.WriteAllTextAsync(destination, "previous", TestContext.Current.CancellationToken);
        using var http = new HttpClient(new ContentHandler("download"));
        var downloader = new DependencyDownloader(http, new FakeVerifier(new(false, "bad signature")));

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadVerifiedAsync(
            Entry("driver.exe"), _root, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("previous", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(destination + ".download"));
    }

    [Fact]
    public async Task VerifierExceptionAlsoDeletesPartialDownload()
    {
        using var http = new HttpClient(new ContentHandler("download"));
        var downloader = new DependencyDownloader(http, new ThrowingVerifier());

        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadVerifiedAsync(
            Entry("driver.exe"), _root, cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(_root, "driver.exe.download")));
    }

    [Fact]
    public async Task SuccessfulVerificationAtomicallyReplacesPreviousPackage()
    {
        Directory.CreateDirectory(_root);
        string destination = Path.Combine(_root, "driver.exe");
        await File.WriteAllTextAsync(destination, "previous", TestContext.Current.CancellationToken);
        using var http = new HttpClient(new ContentHandler("download"));
        var downloader = new DependencyDownloader(http, new FakeVerifier(new(true, null)));

        string result = await downloader.DownloadVerifiedAsync(
            Entry("driver.exe"), _root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(destination, result);
        Assert.Equal("download", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(destination + ".download"));
    }

    [Theory]
    [InlineData("..\\driver.exe")]
    [InlineData("folder/driver.exe")]
    public async Task RejectsPackagePathTraversalBeforeDownloading(string fileName)
    {
        var handler = new ContentHandler("download");
        using var http = new HttpClient(handler);
        var downloader = new DependencyDownloader(http, new FakeVerifier(new(true, null)));

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadVerifiedAsync(
            Entry(fileName), _root, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.Calls);
    }

    private static DependencyManifestEntry Entry(string fileName) => new(
        "driver", new Version(1, 0), new Uri("https://example.invalid/driver.exe"),
        new string('A', 64), "Publisher", fileName, [], []);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeVerifier(VerificationResult result) : IPackageVerifier
    {
        public Task<VerificationResult> VerifyAsync(string path, DependencyManifestEntry expected,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ThrowingVerifier : IPackageVerifier
    {
        public Task<VerificationResult> VerifyAsync(string path, DependencyManifestEntry expected,
            CancellationToken cancellationToken = default) => throw new IOException("verification failed");
    }

    private sealed class ContentHandler(string content) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/octet-stream")
            });
        }
    }
}
