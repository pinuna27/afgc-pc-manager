using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AFGCPCManager.Core.Updates;

public sealed class GitHubReleaseChecker(HttpClient client)
{
    public async Task<UpdateCheckResult> CheckAsync(ReleaseSource source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases/latest");
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue("AFGC-PC-Manager", "1.0"));
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult.Failed(source.Component,
                    $"GitHub returned {(int)response.StatusCode}.");
            ReleaseResponse? release = await response.Content.ReadFromJsonAsync<ReleaseResponse>(cancellationToken: cancellationToken);
            if (release is null || release.Draft || release.Prerelease)
                return new UpdateCheckResult.Failed(source.Component,
                    "The latest response was not a stable release.");
            if (!TryParseVersion(release.TagName, out Version? latest))
                return new UpdateCheckResult.Failed(source.Component,
                    "The latest release tag is not a valid stable version.");
            if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? page)
                || page.Scheme != Uri.UriSchemeHttps
                || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckResult.Failed(source.Component,
                    "The release page URL is invalid.");
            return latest > source.InstalledVersion
                ? new UpdateCheckResult.Available(source.Component, source.InstalledVersion, latest, page, release.PublishedAt)
                : new UpdateCheckResult.UpToDate(source.Component, source.InstalledVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or System.Text.Json.JsonException)
        {
            return new UpdateCheckResult.Failed(source.Component, ex.Message);
        }
    }

    public static bool TryParseVersion(string? tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        string value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        if (value.Any(character => !(char.IsDigit(character) || character == '.')))
            return false;
        return Version.TryParse(value, out version);
    }
    private sealed record ReleaseResponse([property: JsonPropertyName("tag_name")] string TagName, [property: JsonPropertyName("html_url")] string HtmlUrl, [property: JsonPropertyName("draft")] bool Draft, [property: JsonPropertyName("prerelease")] bool Prerelease, [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);
}
