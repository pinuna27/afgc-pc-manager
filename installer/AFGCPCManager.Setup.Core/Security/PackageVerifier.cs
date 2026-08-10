using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Security;

public sealed record VerificationResult(bool IsValid, string? FailureReason);

public interface IPackageVerifier
{
    Task<VerificationResult> VerifyAsync(string path, DependencyManifestEntry expected,
        CancellationToken cancellationToken = default);
}

public sealed class PackageVerifier : IPackageVerifier
{
    public async Task<VerificationResult> VerifyAsync(string path, DependencyManifestEntry expected, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new(false, "Downloaded package is missing.");
        if (expected.Sha256.Length != 64 || expected.Sha256.Any(character => !Uri.IsHexDigit(character)))
            return new(false, "The pinned package hash is invalid.");
        string hash = await Hashing.Sha256Async(path, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(expected.Sha256))) return new(false, "SHA-256 hash does not match the pinned manifest.");
        if (!AuthenticodeTrust.IsTrusted(path)) return new(false, "Authenticode signature is missing or untrusted.");
        try
        {
#pragma warning disable SYSLIB0057 // This API extracts the signer from an Authenticode-signed PE; X509CertificateLoader does not provide an equivalent.
            using X509Certificate2 certificate = new(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!certificate.Subject.Contains(expected.ExpectedPublisher, StringComparison.OrdinalIgnoreCase)) return new(false, $"Unexpected signer: {certificate.Subject}");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException) { return new(false, "Could not read the package signer."); }
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
        if (Version.TryParse(info.FileVersion, out Version? actual) && DependencyPlanBuilder.IsOlder(actual, expected.Version))
            return new(false, $"Package version {actual} is older than required {expected.Version}.");
        return new(true, null);
    }
}
