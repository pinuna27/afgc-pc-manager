using AFGCPCManager.Setup.Core.Dependencies;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyInstallerTests
{
    [Theory]
    [InlineData(0, true, false)]
    [InlineData(8, true, true)]
    [InlineData(1641, true, true)]
    [InlineData(3010, true, true)]
    [InlineData(1, false, false)]
    public void InterpretsDocumentedInstallerExitCodes(int code, bool succeeded, bool restartRequired)
    {
        DependencyInstallResult result = DependencyInstaller.InterpretExitCode(code);

        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(restartRequired, result.RestartRequired);
        Assert.Equal(code, result.ExitCode);
    }

    [Fact]
    public void RecognizesWindowsShutdownTerminationWithoutClaimingInstallSuccess()
    {
        DependencyInstallResult result = DependencyInstaller.InterpretExitCode(0x40010004);

        Assert.False(result.Succeeded);
        Assert.True(result.RestartRequired);
        Assert.True(result.RestartInitiated);
    }
}
