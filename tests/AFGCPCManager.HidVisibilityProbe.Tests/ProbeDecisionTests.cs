namespace AFGCPCManager.HidVisibilityProbe.Tests;

using Xunit;

public sealed class ProbeDecisionTests
{
    private const string Expected =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void ExactVisibleIdentityIsVisible()
    {
        ProbeDecisionResult result = ProbeDecision.Evaluate(Expected,
            [new(Expected, 2, true)]);

        Assert.Equal("visible", result.Status);
        Assert.Equal(10, result.ExitCode);
    }

    [Fact]
    public void AbsentIdentityWithOnlyTrustedOtherControllersIsHidden()
    {
        ProbeDecisionResult result = ProbeDecision.Evaluate(Expected,
            [new(new string('B', 64), 2, true)]);

        Assert.Equal("hidden", result.Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void UnidentifiedVisibleControllerMakesAbsenceIndeterminate()
    {
        ProbeDecisionResult result = ProbeDecision.Evaluate(Expected,
            [new(new string('C', 64), 2, false)]);

        Assert.Equal("indeterminate", result.Status);
        Assert.Equal(20, result.ExitCode);
    }

    [Fact]
    public void NoVisibleControllersConfirmsHidden()
    {
        ProbeDecisionResult result = ProbeDecision.Evaluate(Expected, []);

        Assert.Equal("hidden", result.Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AssertNoneVisibleRejectsAnyPhysicalController()
    {
        ProbeDecisionResult result = ProbeDecision.EvaluateNoneVisible(
            [new(Expected, 2, true)]);

        Assert.Equal("visible", result.Status);
        Assert.Equal(10, result.ExitCode);
    }

    [Fact]
    public void OnlyAccessDeniedCountsAsHidHideIsolation()
    {
        Assert.True(RawInputVisibility.IsHidHideAccessDenial(5));
        Assert.False(RawInputVisibility.IsHidHideAccessDenial(2));
        Assert.False(RawInputVisibility.IsHidHideAccessDenial(32));
    }
}
