using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Dependencies;

namespace AFGCPCManager.Setup.Core.Security;

public sealed class LocalReleaseBundleVerifier(ReleaseManifestVerifier manifestVerifier)
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumSignatureBytes = 4096;

    public async Task<ReleaseManifest> VerifyAsync(
        string manifestPath,
        string signaturePath,
        string archivePath,
        Version expectedVersion,
        CancellationToken cancellationToken = default)
    {
        byte[] manifest = await ReadLimitedAsync(manifestPath, MaximumManifestBytes, cancellationToken);
        byte[] signature = await ReadLimitedAsync(signaturePath, MaximumSignatureBytes, cancellationToken);
        ManifestVerificationResult result = manifestVerifier.Verify(manifest, signature);
        if (!result.IsValid) throw new InvalidDataException(result.FailureReason);
        ReleaseManifest verified = result.Manifest!;
        if (!Version.TryParse(verified.Version.TrimStart('v', 'V'), out Version? version)
            || DependencyPlanBuilder.IsOlder(version, expectedVersion)
            || DependencyPlanBuilder.IsOlder(expectedVersion, version))
            throw new InvalidDataException("The local release manifest version does not match setup.");

        string archiveName = Path.GetFileName(archivePath);
        ReleaseAsset expected = verified.Assets.SingleOrDefault(x => x.Name.Equals(archiveName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The application archive is not listed in the signed manifest.");
        var file = new FileInfo(archivePath);
        if (!file.Exists || file.Length != expected.Size)
            throw new InvalidDataException("The application archive size does not match the signed manifest.");
        string hash = await Hashing.Sha256Async(archivePath, cancellationToken);
        if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The application archive hash does not match the signed manifest.");
        return verified;
    }

    private static async Task<byte[]> ReadLimitedAsync(string path, int maximum, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > maximum)
            throw new InvalidDataException("Local release metadata is missing or too large.");
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
