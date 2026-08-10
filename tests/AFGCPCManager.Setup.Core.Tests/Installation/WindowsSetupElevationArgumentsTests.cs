using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class WindowsSetupElevationArgumentsTests
{
    [Fact]
    public void CliModeSurvivesElevationRelaunch()
    {
        string[] result = WindowsSetupElevationArguments.Prepare(
            ["--payload", @"C:\payload"], cliMode: true);

        Assert.Equal(["--cli", "--payload", @"C:\payload"], result);
    }

    [Fact]
    public void WizardModeIsAddedExactlyOnce()
    {
        string[] first = WindowsSetupElevationArguments.Prepare(
            ["--payload", @"C:\payload"], cliMode: false);
        string[] second = WindowsSetupElevationArguments.Prepare(first, cliMode: false);

        Assert.Equal(first, second);
        Assert.Single(second, argument => argument.Equals(
            "--wizard-run", StringComparison.OrdinalIgnoreCase));
    }
}
