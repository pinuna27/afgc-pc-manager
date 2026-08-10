using AFGCPCManager.Setup.Core.Dependencies;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class VJoyProbeProtocolTests
{
    [Theory]
    [InlineData(VJoyProbeProtocol.ReadyExitCode, true, true)]
    [InlineData(VJoyProbeProtocol.UnhealthyExitCode, true, false)]
    [InlineData(VJoyProbeProtocol.UnavailableExitCode, false, false)]
    public void MapsExpectedExitCodes(int exitCode, bool installed, bool operational)
    {
        DependencyProbeResult result = VJoyProbeProtocol.Interpret(
            exitCode, "probe detail\r\ncontinued");

        Assert.Equal(installed, result.Installed);
        Assert.Equal(operational, result.Operational);
        Assert.Equal("probe detail continued", result.Detail);
    }

    [Fact]
    public void RejectsUnexpectedExitCodeWithBoundedDiagnostic()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            VJoyProbeProtocol.Interpret(99, new string('x', 600)));

        Assert.Contains("code 99", error.Message);
        Assert.True(error.Message.Length < 600);
    }
}
