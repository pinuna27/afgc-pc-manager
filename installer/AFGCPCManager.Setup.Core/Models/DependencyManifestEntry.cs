namespace AFGCPCManager.Setup.Core.Models;

public sealed record DependencyManifestEntry(
    string Name,
    Version Version,
    Uri DownloadUri,
    string Sha256,
    string ExpectedPublisher,
    string FileName,
    string[] SilentInstallArguments,
    string[] DetectionPaths);
