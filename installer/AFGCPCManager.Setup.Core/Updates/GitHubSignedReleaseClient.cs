using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Updates;

public sealed class GitHubSignedReleaseClient(
    HttpClient client, ReleaseManifestVerifier verifier)
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumSignatureBytes = 4096;

    public async Task<VerifiedRelease> GetLatestAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        ValidateRepositoryPart(owner);
        ValidateRepositoryPart(repository);
        using var request = Request(
            $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        using HttpResponseMessage response = await client.SendAsync(
            request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ReleaseResponse release = await response.Content
            .ReadFromJsonAsync<ReleaseResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");
        if (release.Draft || release.Prerelease)
            throw new InvalidDataException("GitHub returned a non-stable release.");
        if (release.Assets is null
            || release.Assets.Any(asset => asset is null
                || string.IsNullOrWhiteSpace(asset.Name)
                || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            || release.Assets.GroupBy(asset => asset.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
            throw new InvalidDataException("GitHub returned an invalid release asset list.");
        if (!TryVersion(release.TagName, out Version? releaseVersion))
            throw new InvalidDataException("The GitHub release tag is invalid.");

        Uri releasePage = RequireGitHubUri(release.HtmlUrl);
        Dictionary<string, Uri> assets = release.Assets.ToDictionary(
            asset => asset.Name,
            asset => RequireGitHubUri(asset.BrowserDownloadUrl),
            StringComparer.OrdinalIgnoreCase);
        if (!assets.TryGetValue("release-manifest.json", out Uri? manifestUri)
            || !assets.TryGetValue("release-manifest.sig", out Uri? signatureUri))
            throw new InvalidDataException("The release is missing its signed manifest.");

        byte[] manifestBytes = await DownloadSmallAsync(
            manifestUri, MaximumManifestBytes, cancellationToken);
        byte[] signature = await DownloadSmallAsync(
            signatureUri, MaximumSignatureBytes, cancellationToken);
        ManifestVerificationResult verified = verifier.Verify(manifestBytes, signature);
        if (!verified.IsValid)
            throw new InvalidDataException(verified.FailureReason);
        if (!TryVersion(verified.Manifest!.Version, out Version? manifestVersion)
            || DependencyPlanBuilder.IsOlder(manifestVersion!, releaseVersion!)
            || DependencyPlanBuilder.IsOlder(releaseVersion!, manifestVersion!))
            throw new InvalidDataException(
                "The signed manifest version does not match the GitHub release tag.");
        foreach (ReleaseAsset asset in verified.Manifest.Assets)
        {
            if (!assets.ContainsKey(asset.Name))
                throw new InvalidDataException(
                    $"The signed asset '{asset.Name}' is missing from the GitHub release.");
        }
        return new(releaseVersion!, releasePage, verified.Manifest,
            assets, manifestBytes, signature);
    }

    public async Task<string> DownloadAssetAsync(
        VerifiedRelease release,
        string assetName,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseAsset expected = release.Manifest.Assets.SingleOrDefault(asset =>
                asset.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "The requested asset is not in the signed manifest.");
        if (!release.AssetDownloads.TryGetValue(expected.Name, out Uri? uri))
            throw new InvalidDataException("The requested release asset is unavailable.");

        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, expected.Name);
        string temporary = destination + ".download";
        using var request = Request(uri.AbsoluteUri);
        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long declared
            && declared != expected.Size)
            throw new InvalidDataException(
                "The release asset size does not match its signed manifest.");

        await using Stream source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using FileStream target = new(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long total = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > expected.Size)
                    throw new InvalidDataException(
                        "The release asset exceeded its signed size.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress?.Report((double)total / expected.Size);
            }
            await target.FlushAsync(cancellationToken);
            target.Close();
            if (total != expected.Size)
                throw new InvalidDataException("The release asset was truncated.");

            string hash = await Hashing.Sha256Async(temporary, cancellationToken);
            if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The release asset hash does not match its signed manifest.");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch
        {
            target.Close();
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public async Task<string> DownloadDependencyAsync(
        DependencyRelease dependency,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string[] repository = dependency.Repository.Split('/');
        if (repository.Length != 2)
            throw new InvalidDataException("The signed dependency repository is invalid.");
        ValidateRepositoryPart(repository[0]);
        ValidateRepositoryPart(repository[1]);
        ValidateRepositoryPart(dependency.ReleaseTag);
        if (Path.GetFileName(dependency.AssetName) != dependency.AssetName)
            throw new InvalidDataException("The signed dependency asset name is invalid.");
        if (!Version.TryParse(dependency.Version.TrimStart('v', 'V'), out Version? version))
            throw new InvalidDataException("The signed dependency version is invalid.");

        Uri uri = new($"https://github.com/{repository[0]}/{repository[1]}/releases/"
            + $"download/{dependency.ReleaseTag}/{dependency.AssetName}");
        var entry = new DependencyManifestEntry(
            repository[1], version, uri, dependency.Sha256,
            dependency.ExpectedPublisher, dependency.AssetName, [], []);
        return await new DependencyDownloader(client, new PackageVerifier())
            .DownloadVerifiedAsync(entry, destinationDirectory, progress, cancellationToken);
    }

    private async Task<byte[]> DownloadSmallAsync(
        Uri uri, int maximum, CancellationToken cancellationToken)
    {
        using var request = Request(uri.AbsoluteUri);
        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum)
            throw new InvalidDataException("A release metadata asset is too large.");

        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maximum)
                throw new InvalidDataException("A release metadata asset is too large.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static HttpRequestMessage Request(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("AFGC-PC-Manager-Setup", "1.0"));
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        return request;
    }

    private static Uri RequireGitHubUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned an untrusted release URL.");
        return uri;
    }

    private static void ValidateRepositoryPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.')))
            throw new ArgumentException("The repository identity is invalid.");
    }

    private static bool TryVersion(string value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string clean = value.Trim();
        if (clean.StartsWith('v') || clean.StartsWith('V'))
            clean = clean[1..];
        return clean.All(character => char.IsDigit(character) || character == '.')
            && Version.TryParse(clean, out version);
    }

    private sealed record ReleaseResponse(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<AssetResponse> Assets);

    private sealed record AssetResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
