namespace AFGCPCManager.Setup.Core.Dependencies;

public enum DependencyId { VJoy, ViGEmBus, HidHide }

public static class DependencyNames
{
    public static string DisplayName(DependencyId dependency) => dependency switch
    {
        DependencyId.VJoy => "vJoy",
        DependencyId.ViGEmBus => "ViGEmBus",
        DependencyId.HidHide => "HidHide",
        _ => dependency.ToString()
    };

    public static string RemovalArgument(DependencyId dependency) => dependency switch
    {
        DependencyId.VJoy => "--remove-vjoy",
        DependencyId.ViGEmBus => "--remove-vigembus",
        DependencyId.HidHide => "--remove-hidhide",
        _ => throw new ArgumentOutOfRangeException(nameof(dependency))
    };
}

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
