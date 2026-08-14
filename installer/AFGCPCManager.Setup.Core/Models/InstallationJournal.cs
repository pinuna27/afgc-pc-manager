namespace AFGCPCManager.Setup.Core.Models;

public sealed record InstallationJournal
{
    public int SchemaVersion { get; init; } = 2;
    public required string InstallDirectory { get; init; }
    public required string Version { get; init; }
    public List<InstalledFile> Files { get; init; } = [];
    public HashSet<string> DependenciesInstalledBySetup { get; init; } = [];
    public HashSet<string> DependenciesPresentBeforeSetup { get; init; } = [];
    public PendingDependencyOperation? PendingDependencyOperation { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record InstalledFile(string RelativePath, string Sha256);

public sealed record PendingDependencyOperation(
    string Dependency,
    string TargetVersion,
    string InstallerPath,
    DependencyOperationPhase Phase,
    bool RestartRequired,
    DateTimeOffset? BootStartedAtUtc = null);

public enum DependencyOperationPhase { Prepared, InstallerStarted, RestartRequired, DeferredUntilRestart }
