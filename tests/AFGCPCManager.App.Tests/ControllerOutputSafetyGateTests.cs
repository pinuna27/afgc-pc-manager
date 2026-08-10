using AFGCPCManager.Core.Output;
using AFGCPCManager.HidHide;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class ControllerOutputSafetyGateTests
{
    [Theory]
    [InlineData(HidHideVisibilityStatus.Visible)]
    [InlineData(HidHideVisibilityStatus.Indeterminate)]
    public async Task UnverifiedHidingWithholdsAndReleasesVirtualOutput(
        HidHideVisibilityStatus status)
    {
        var output = new FakeOutput();
        var hidden = new List<string>();
        var gate = new ControllerOutputSafetyGate(
            (_, _, _, _) => Task.FromResult(new HidHideVisibilityResult(status, "not hidden")),
            (_, _) => Task.CompletedTask,
            hidden.Add);

        OutputSafetyAuthorization result = await gate.AuthorizeAsync(true, "controller",
            ["endpoint"], "app.exe", output, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthorized);
        Assert.True(output.Disposed);
        Assert.Equal(["controller"], hidden);
    }

    [Fact]
    public async Task ConfirmedHidingKeepsOutputForBridgeActivation()
    {
        var output = new FakeOutput();
        var hidden = new List<string>();
        var gate = new ControllerOutputSafetyGate(
            (_, _, _, _) => Task.FromResult(new HidHideVisibilityResult(
                HidHideVisibilityStatus.Hidden, "confirmed")),
            (_, _) => Task.CompletedTask,
            hidden.Add);

        OutputSafetyAuthorization result = await gate.AuthorizeAsync(true, "controller",
            ["endpoint"], "app.exe", output, TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthorized);
        Assert.False(output.Disposed);
        Assert.Equal(["controller"], hidden);
    }

    [Fact]
    public async Task NewlyChangedHidingWithholdsOutputUntilHandlesAreReset()
    {
        var output = new FakeOutput();
        var gate = new ControllerOutputSafetyGate(
            (_, _, _, _) => Task.FromResult(new HidHideVisibilityResult(
                HidHideVisibilityStatus.Hidden, "new processes are hidden", true)),
            (_, _) => Task.CompletedTask,
            _ => { });

        OutputSafetyAuthorization result = await gate.AuthorizeAsync(true, "controller",
            ["endpoint"], "app.exe", output, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthorized);
        Assert.True(result.HandleResetRequired);
        Assert.True(output.Disposed);
        Assert.Contains("off and back on once", result.Detail);
    }

    [Fact]
    public async Task ConfigurationFailureRestoresVisibilityAndReleasesOutput()
    {
        var output = new FakeOutput();
        var unhidden = new List<string>();
        var gate = new ControllerOutputSafetyGate(
            (_, _, _, _) => throw new IOException("driver failure"),
            (id, _) => { unhidden.Add(id); return Task.CompletedTask; },
            _ => throw new Xunit.Sdk.XunitException("failed setup must not be marked hidden"));

        OutputSafetyAuthorization result = await gate.AuthorizeAsync(true, "controller",
            ["endpoint"], "app.exe", output, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthorized);
        Assert.True(output.Disposed);
        Assert.Equal(["controller"], unhidden);
        Assert.Contains("driver failure", result.Detail);
    }

    [Fact]
    public async Task DisabledHidingDoesNotCallHidHide()
    {
        var output = new FakeOutput();
        var gate = new ControllerOutputSafetyGate(
            (_, _, _, _) => throw new Xunit.Sdk.XunitException("unexpected HidHide call"),
            (_, _) => throw new Xunit.Sdk.XunitException("unexpected unhide call"),
            _ => throw new Xunit.Sdk.XunitException("unexpected hidden marker"));

        OutputSafetyAuthorization result = await gate.AuthorizeAsync(false, "controller",
            ["endpoint"], "app.exe", output, TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthorized);
        Assert.False(output.Disposed);
    }

    private sealed class FakeOutput : IGamepadOutputSession
    {
        public uint DeviceId => 1;
        public bool Disposed { get; private set; }
        public ValueTask WriteAsync(VirtualGamepadState state,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void Dispose() => Disposed = true;
    }
}
