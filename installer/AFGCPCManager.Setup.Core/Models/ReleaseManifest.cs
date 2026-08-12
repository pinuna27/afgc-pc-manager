namespace AFGCPCManager.Setup.Core.Models;

public sealed record ReleaseManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Version { get; init; }
    public required string Architecture { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public required List<ReleaseAsset> Assets { get; init; }
    public DependencyRelease? VJoy { get; init; }
    public DependencyRelease? ViGEmBus { get; init; }
    public DependencyRelease? HidHide { get; init; }
}
public sealed record ReleaseAsset(string Name, string Sha256, long Size);
public sealed record DependencyRelease(string Repository, string ReleaseTag, string Version, string AssetName, string Sha256, string ExpectedPublisher, bool MayRequireRestart);
