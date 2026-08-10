using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class ControllerReconnectGateTests
{
    [Fact]
    public void ConnectedControllerStaysPendingAcrossRepeatedObservations()
    {
        var gate = new ControllerReconnectGate();
        gate.Require("controller");

        Assert.Empty(gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
        Assert.Empty(gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
        Assert.True(gate.IsPending("controller"));
    }

    [Fact]
    public void CompleteDisconnectThenReconnectMakesControllerReadyOnce()
    {
        var gate = new ControllerReconnectGate();
        gate.Require("controller");

        Assert.Empty(gate.Observe(new HashSet<string>()).NewlyDisconnectedControllerIds);
        Assert.Equal(["controller"], gate.Observe(new HashSet<string>()).NewlyDisconnectedControllerIds);
        Assert.Equal(["controller"], gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
        Assert.True(gate.IsPending("controller"));

        gate.Complete("controller");

        Assert.False(gate.IsPending("controller"));
        Assert.Empty(gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
    }

    [Fact]
    public void StartupPendingControllerThatIsInitiallyAbsentCompletesOnFirstConnection()
    {
        var gate = new ControllerReconnectGate();
        gate.Require("controller");

        gate.Observe(new HashSet<string>());
        gate.Observe(new HashSet<string>());

        Assert.Equal(["controller"], gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
    }

    [Fact]
    public void SingleTransientMissingSnapshotDoesNotSatisfyReset()
    {
        var gate = new ControllerReconnectGate();
        gate.Require("controller");

        gate.Observe(new HashSet<string>());

        Assert.Empty(gate.Observe(new HashSet<string> { "controller" }).ReadyControllerIds);
        Assert.True(gate.IsPending("controller"));
    }

    [Fact]
    public void PersistedDisconnectedPhaseIsReadyImmediatelyWhenControllerIsPresent()
    {
        var gate = new ControllerReconnectGate();
        gate.Require("controller", disconnectedObserved: true);

        ControllerReconnectObservation observation = gate.Observe(
            new HashSet<string> { "controller" });

        Assert.Equal(["controller"], observation.ReadyControllerIds);
        Assert.Empty(observation.NewlyDisconnectedControllerIds);
    }
}
