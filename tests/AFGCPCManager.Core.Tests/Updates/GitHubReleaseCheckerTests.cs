using System.Net;
using System.Text;
using AFGCPCManager.Core.Updates;

namespace AFGCPCManager.Core.Tests.Updates;

public sealed class GitHubReleaseCheckerTests
{
    [Fact]
    public async Task AcceptsNewerStableRelease()
    {
        using var client = new HttpClient(new Handler("""{"tag_name":"v1.2.0","html_url":"https://github.com/pinuna27/afgc-pc-manager/releases/tag/v1.2.0","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z"}"""));
        var result = await new GitHubReleaseChecker(client).CheckAsync(new(ReleaseComponent.AfgcPcManager, "pinuna27", "afgc-pc-manager", new(1, 1, 0)));
        Assert.IsType<UpdateCheckResult.Available>(result);
    }
    [Theory] [InlineData("v1.2.0-beta")] [InlineData("nightly")] [InlineData("")]
    public void RejectsNonStableVersionTags(string tag) => Assert.False(GitHubReleaseChecker.TryParseVersion(tag, out _));
    [Fact]
    public async Task RejectsPrereleaseResponse()
    {
        using var client = new HttpClient(new Handler("""{"tag_name":"v2.0.0","html_url":"https://github.com/a/b/releases/tag/v2","draft":false,"prerelease":true,"published_at":"2026-01-01T00:00:00Z"}"""));
        var result = await new GitHubReleaseChecker(client).CheckAsync(new(ReleaseComponent.VJoy, "a", "b", new(1, 0)));
        Assert.IsType<UpdateCheckResult.Failed>(result);
    }
    private sealed class Handler(string json) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }); }
}
