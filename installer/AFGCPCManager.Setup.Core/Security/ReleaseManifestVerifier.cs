using System.Security.Cryptography;
using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Security;

public sealed record ManifestVerificationResult(bool IsValid, ReleaseManifest? Manifest, string? FailureReason);

public sealed class ReleaseManifestVerifier(string publicKeyPem)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public ManifestVerificationResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (!key.VerifyData(manifestBytes, signatureBytes,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return new(false, null, "The release manifest signature is invalid.");
            ReleaseManifest? manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions);
            string? error = Validate(manifest);
            return error is null
                ? new(true, manifest, null)
                : new(false, null, error);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException
                                   or ArgumentException)
        {
            return new(false, null,
                "The release manifest could not be verified or parsed.");
        }
    }
    private static string? Validate(ReleaseManifest? value)
    {
        if (value is null || value.SchemaVersion != 1)
            return "The release manifest schema is unsupported.";
        if (string.IsNullOrWhiteSpace(value.Version)
            || !Version.TryParse(value.Version.TrimStart('v', 'V'), out _))
            return "The release version is invalid.";
        if (!string.Equals(value.Architecture, "x64", StringComparison.OrdinalIgnoreCase))
            return "The release architecture is unsupported.";
        if (value.PublishedAtUtc == default)
            return "The release publication time is invalid.";
        if (value.Assets is null || value.Assets.Count == 0
            || value.Assets.Any(asset => asset is null || !IsSafeName(asset.Name)
                || !IsHash(asset.Sha256) || asset.Size <= 0))
            return "The release asset list is invalid.";
        if (value.Assets.GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            return "The release contains duplicate asset names.";
        foreach (DependencyRelease dependency in new[]
                 {
                     value.VJoy, value.ViGEmBus, value.HidHide
                 }.OfType<DependencyRelease>())
        {
            if (!IsRepository(dependency.Repository) || !IsRepositoryPart(dependency.ReleaseTag)
                || string.IsNullOrWhiteSpace(dependency.Version)
                || !Version.TryParse(dependency.Version.TrimStart('v', 'V'), out _)
                || !IsSafeName(dependency.AssetName) || !IsHash(dependency.Sha256)
                || string.IsNullOrWhiteSpace(dependency.ExpectedPublisher))
                return "A dependency release entry is invalid.";
        }
        return null;
    }
    private static bool IsSafeName(string name) => !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    private static bool IsHash(string value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool IsRepository(string value)
    {
        if (value is null)
            return false;
        string[] parts = value.Split('/');
        return parts.Length == 2 && parts.All(IsRepositoryPart);
    }
    private static bool IsRepositoryPart(string value) => !string.IsNullOrWhiteSpace(value)
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
}
