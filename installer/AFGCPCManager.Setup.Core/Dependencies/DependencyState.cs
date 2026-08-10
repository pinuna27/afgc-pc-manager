namespace AFGCPCManager.Setup.Core.Dependencies;

public enum DependencyId { VJoy, HidHide }

public enum DependencyReadiness { Absent, Ready, PendingRestart, Unhealthy, Unknown }

public sealed record DependencyEvidence(string Source, bool? Present, Version? Version = null, string? Detail = null);

public sealed record DependencyState(
    DependencyId Id,
    bool IsInstalled,
    Version? Version = null,
    string? InstallLocation = null,
    DependencyReadiness? Readiness = null,
    IReadOnlyList<DependencyEvidence>? Evidence = null)
{
    public DependencyReadiness EffectiveReadiness => Readiness
        ?? (IsInstalled ? DependencyReadiness.Ready : DependencyReadiness.Absent);
}

public interface IDependencyDetector
{
    DependencyState Detect(DependencyId dependency);
}
