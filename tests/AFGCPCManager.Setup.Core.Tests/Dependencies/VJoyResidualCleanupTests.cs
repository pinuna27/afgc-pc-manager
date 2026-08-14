using AFGCPCManager.Setup.Core.Dependencies;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class VJoyResidualCleanupTests
{
    [Fact]
    public void AcceptsExactPostVendorUninstallResidue()
    {
        DependencyState state = State(
            new("registered application", false),
            new("driver service", true, Detail: "vjoy"),
            new("runtime library", false),
            new("operational API", false));

        Assert.True(VJoyResidualCleanup.CanClean(state));
    }

    [Fact]
    public void AcceptsResidueWhenNoOperationalProbeWasUsed()
    {
        DependencyState state = State(
            new("registered application", false),
            new("driver service", true, Detail: "vjoy"),
            new("runtime library", false));

        Assert.True(VJoyResidualCleanup.CanClean(state));
    }

    [Theory]
    [InlineData("registered application")]
    [InlineData("runtime library")]
    [InlineData("operational API")]
    public void RejectsResidueWhileAnyUserModeInstallEvidenceRemains(string presentSource)
    {
        var evidence = new List<DependencyEvidence>
        {
            new("registered application", false),
            new("driver service", true, Detail: "vjoy"),
            new("runtime library", false),
            new("operational API", false)
        };
        int index = evidence.FindIndex(item => item.Source == presentSource);
        evidence[index] = evidence[index] with { Present = true };

        Assert.False(VJoyResidualCleanup.CanClean(State(evidence.ToArray())));
    }

    [Fact]
    public void RejectsAnotherDependencyOrUnverifiableState()
    {
        DependencyEvidence[] evidence =
        [
            new("registered application", false),
            new("driver service", true),
            new("runtime library", false)
        ];
        DependencyState other = State(evidence) with { Id = DependencyId.HidHide };
        DependencyState unknown = State(evidence) with { Readiness = DependencyReadiness.Unknown };

        Assert.False(VJoyResidualCleanup.CanClean(other));
        Assert.False(VJoyResidualCleanup.CanClean(unknown));
    }

    private static DependencyState State(params DependencyEvidence[] evidence) =>
        new(DependencyId.VJoy, true, Readiness: DependencyReadiness.Unhealthy, Evidence: evidence);
}
