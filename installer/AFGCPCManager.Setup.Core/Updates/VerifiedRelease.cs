using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Updates;

public sealed record VerifiedRelease(Version Version, Uri ReleasePage, ReleaseManifest Manifest, IReadOnlyDictionary<string, Uri> AssetDownloads);
