namespace AFGCPCManager.Setup.Core.Dependencies;

public enum DependencyId { VJoy, HidHide }

public sealed record DependencyState(
    DependencyId Id,
    bool IsInstalled,
    Version? Version = null,
    string? InstallLocation = null);

public interface IDependencyDetector
{
    DependencyState Detect(DependencyId dependency);
}
