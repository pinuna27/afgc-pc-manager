using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyManifestPolicyTests
{
    [Fact]
    public void ReadyDependenciesWithoutPendingOperationDoNotNeedManifest()
    {
        var states = new[]
        {
            new DependencyState(DependencyId.VJoy, true,
                Readiness: DependencyReadiness.Ready),
            new DependencyState(DependencyId.ViGEmBus, true,
                Readiness: DependencyReadiness.Ready),
            new DependencyState(DependencyId.HidHide, true,
                Readiness: DependencyReadiness.Ready)
        };

        Assert.True(DependencyManifestPolicy.CanUseInstalledDependenciesWithoutManifest(
            states, Journal()));
    }

    [Theory]
    [InlineData(DependencyReadiness.Absent)]
    [InlineData(DependencyReadiness.Unhealthy)]
    [InlineData(DependencyReadiness.Unknown)]
    [InlineData(DependencyReadiness.PendingRestart)]
    public void AnyUnreadyDependencyStillRequiresManifest(DependencyReadiness readiness)
    {
        var states = new[]
        {
            new DependencyState(DependencyId.VJoy, readiness != DependencyReadiness.Absent,
                Readiness: readiness),
            new DependencyState(DependencyId.ViGEmBus, true,
                Readiness: DependencyReadiness.Ready),
            new DependencyState(DependencyId.HidHide, true,
                Readiness: DependencyReadiness.Ready)
        };

        Assert.False(DependencyManifestPolicy.CanUseInstalledDependenciesWithoutManifest(
            states, Journal()));
    }

    [Fact]
    public void PendingOperationStillRequiresManifest()
    {
        var states = Enum.GetValues<DependencyId>().Select(id =>
            new DependencyState(id, true, Readiness: DependencyReadiness.Ready));
        var journal = Journal() with
        {
            PendingDependencyOperation = new PendingDependencyOperation(
                DependencyId.HidHide.ToString(), "1.0", "installer.exe",
                DependencyOperationPhase.RestartRequired, true)
        };

        Assert.False(DependencyManifestPolicy.CanUseInstalledDependenciesWithoutManifest(
            states, journal));
    }

    private static InstallationJournal Journal() => new()
    {
        InstallDirectory = @"C:\Program Files\AFGC PC Manager",
        Version = "1.0.0"
    };
}
